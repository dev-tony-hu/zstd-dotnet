namespace ZstdDotnet;

public sealed partial class ZstdDecoder : IDisposable
{
    private IntPtr dstream;
    private bool disposed;
    private bool initialized;
    private ZstdBuffer inBuffer = new();
    private ZstdBuffer outBuffer = new();

    public ZstdDecoder()
    {
        dstream = ZstdInterop.ZSTD_createDStream();
    }

    public void Reset()
    {
        ThrowIfDisposed();
        if (dstream == IntPtr.Zero)
        {
            dstream = ZstdInterop.ZSTD_createDStream();
            initialized = false;
            return;
        }
        ZstdInterop.ResetDStream(dstream);
        initialized = false; // re-init on next use
    }

    public void Dispose()
    {
        if (!disposed)
        {
            if (dstream != IntPtr.Zero)
            {
                ZstdInterop.ZSTD_freeDStream(dstream);
                dstream = IntPtr.Zero;
            }
            disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Decompresses a chunk of compressed <paramref name="source"/> bytes into <paramref name="destination"/>.
    /// Mirrors encoder side OperationStatus pattern.
    /// </summary>
    /// <param name="source">Compressed bytes (may contain part of one or more frames).</param>
    /// <param name="destination">Destination span for decompressed data.</param>
    /// <param name="bytesConsumed">Compressed bytes actually consumed.</param>
    /// <param name="bytesWritten">Decompressed bytes produced.</param>
    /// <param name="isFinalBlock">Caller indicates no more compressed data will follow.</param>
    /// <param name="frameFinished">True if a full frame boundary was completed during this call.</param>
    /// <returns>
    /// Done: Made forward progress; if <paramref name="frameFinished"/> is true a frame ended.
    /// DestinationTooSmall: Output buffer filled but more output for the current frame remains.
    /// NeedMoreData: All provided compressed data consumed and more is required to continue (no frame finished this call).
    /// InvalidData: Reserved (never returned unless native signals error, which throws instead).
    /// </returns>
    public System.Buffers.OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock, out bool frameFinished)
    {
        ThrowIfDisposed();
        EnsureInit();
        bytesConsumed = 0; bytesWritten = 0; frameFinished = false;
        unsafe
        {
            fixed (byte* inPtr = source)
            fixed (byte* outPtr = destination)
            {
                inBuffer.Data = (IntPtr)(inPtr + bytesConsumed);
                inBuffer.Size = (UIntPtr)(uint)(source.Length - bytesConsumed);
                inBuffer.Position = (UIntPtr)0;
                outBuffer.Data = (IntPtr)(outPtr + bytesWritten);
                outBuffer.Size = (UIntPtr)(uint)(destination.Length - bytesWritten);
                outBuffer.Position = (UIntPtr)0;
                var result = ZstdInterop.ZSTD_decompressStream(dstream, outBuffer, inBuffer);
                ZstdInterop.ThrowIfError(result);
                bytesConsumed += (int)inBuffer.Position.ToUInt32();
                bytesWritten += (int)outBuffer.Position.ToUInt32();
                frameFinished = result == UIntPtr.Zero; // native returns 0 when frame completed

                // If we filled destination exactly but still not at frame end (frameFinished false and result != 0)
                if (bytesWritten == destination.Length && !frameFinished)
                {
                    return System.Buffers.OperationStatus.DestinationTooSmall;
                }
                // If we consumed everything provided, produced nothing (or some) and need more compressed data
                if (bytesConsumed == source.Length && !frameFinished && result != UIntPtr.Zero && !isFinalBlock)
                {
                    return System.Buffers.OperationStatus.NeedMoreData;
                }
                // If caller says final block and we've consumed all compressed bytes but frame not finished: treat as NeedMoreData (truncated)
                if (isFinalBlock && !frameFinished && bytesConsumed == source.Length && result != UIntPtr.Zero)
                {
                    return System.Buffers.OperationStatus.NeedMoreData; // truncated input scenario
                }
                return System.Buffers.OperationStatus.Done;
            }
        }
    }

    private void EnsureInit()
    {
        if (!initialized)
        {
            ZstdInterop.ThrowIfError(ZstdInterop.ZSTD_initDStream(dstream));
            initialized = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ZstdDecoder));
    }
}
