namespace ZstdDotnet;

public partial class ZstdStream : Stream, IAsyncDisposable
{
    private const int DEFAULT_BUFFER_SIZE = 65536; // same as System.IO.Stream default
    private readonly Stream stream;
    private readonly CompressionMode mode;
    private readonly bool leaveOpen;
    private bool isClosed = false;
    private bool isDisposed = false;

    private byte[]? data;               // shared buffer
    private bool dataDepleted = false;  // underlying compressed stream EOF (decode)
    private int dataPosition = 0;       // read pointer (decode)
    private int dataSize = 0;           // valid compressed bytes (decode)
    private ZstdEncoder? encoder;       // active encoder
    private ZstdDecoder? decoder;       // active decoder
    private readonly ArrayPool<byte> arrayPool = ArrayPool<byte>.Shared;
    private int activeOperation = 0; // 0 = free, 1 = in-use (disallow overlapping read/write)

    public ZstdStream(Stream stream, CompressionMode mode = CompressionMode.Compress, bool leaveOpen = false)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.mode = mode;
        this.leaveOpen = leaveOpen;
        if (mode == CompressionMode.Compress)
        {
            encoder = new ZstdEncoder(CompressionLevel);
        }
        else
        {
            decoder = new ZstdDecoder();
        }
        data = arrayPool.Rent(DEFAULT_BUFFER_SIZE);
    }

    public ZstdStream(Stream stream, int compressionLevel, bool leaveOpen = false) : this(stream, CompressionMode.Compress, leaveOpen)
    {
        CompressionLevel = compressionLevel;
    }

    private int compressionLevel = 3;
    public int CompressionLevel
    {
        get => compressionLevel;
        set
        {
            if (value < ZstdProperties.MinCompressionLevel || value > ZstdProperties.MaxCompressionLevel)
                throw new ArgumentOutOfRangeException(nameof(CompressionLevel), $"CompressionLevel must be between {ZstdProperties.MinCompressionLevel} and {ZstdProperties.MaxCompressionLevel}.");
            if (compressionLevel != value)
            {
                compressionLevel = value;
                if (mode == CompressionMode.Compress && encoder != null)
                {
                    // Reset encoder to apply new level on next write
                    encoder.Reset();
                    encoder.SetCompressionLevel(value);
                }
            }
        }
    }
    public override bool CanRead => stream.CanRead && mode == CompressionMode.Decompress;
    public override bool CanWrite => stream.CanWrite && mode == CompressionMode.Compress;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!isDisposed)
        {
            if (!isClosed) ReleaseResources(flushStream: true); // ensure final frame markers emitted
            if (data != null) arrayPool.Return(data, clearArray: false);
            isDisposed = true;
            data = null;
        }
    }

    public override void Close()
    {
        if (isClosed) return;
        try { ReleaseResources(flushStream: true); }
        finally { isClosed = true; base.Close(); }
    }

    // Async dispose implemented in Async partial

    private void ReleaseResources(bool flushStream)
    {
        if (mode == CompressionMode.Compress)
        {
            try
            {
                if (flushStream && encoder != null)
                {
                    // Detect if the last operation already finalized the frame (FlushFrame) to avoid
                    // emitting an extra empty frame footer on dispose.
                    bool skipFinalize = false;
                    var field = typeof(ZstdStream).GetField("pendingFrameReset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        skipFinalize = field.GetValue(this) is bool b && b;
                    }
                    if (skipFinalize)
                    {
                        stream.Flush();
                    }
                    else
                    {
                        bool done = false; int safety = 0;
                        while (!done && safety < 64)
                        {
                            int consumed; int written;
                            var status = encoder.Compress(ReadOnlySpan<byte>.Empty, data!, out consumed, out written, isFinalBlock: true);
                            if (written > 0) stream.Write(data!, 0, written);
                            if (status == System.Buffers.OperationStatus.Done)
                                done = true;
                            else if (status == System.Buffers.OperationStatus.DestinationTooSmall)
                            {
                                // continue draining
                            }
                            else if (status == System.Buffers.OperationStatus.NeedMoreData)
                                done = true; // treat as finished safeguard
                            safety++;
                        }
                        stream.Flush();
                    }
                }
            }
            finally
            {
                encoder?.Dispose();
                if (!leaveOpen) stream.Close();
            }
        }
        else
        {
            decoder?.Dispose();
            if (!leaveOpen) stream.Close();
        }
    }

    public override void Flush() => FlushInternal();
    public override int Read(byte[] buffer, int offset, int count) => ReadInternal(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => WriteInternal(buffer, offset, count);
    // Span/Memory sync & async overrides implemented in separate partials

    // Rely on base Stream's virtual Task-based overloads automatically delegating to our Memory-based overrides in newer frameworks.
    public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
    public override void SetLength(long value) => throw new NotImplementedException();

    public void Reset()
    {
        if (mode == CompressionMode.Compress)
        {
            encoder?.Reset();
        }
        else
        {
            decoder?.Reset();
            dataPosition = 0; dataSize = 0; dataDepleted = false;
        }
    }

    partial void FlushInternal();
    private partial int ReadInternal(byte[] buffer, int offset, int count);
    private partial void WriteInternal(byte[] buffer, int offset, int count);
}
