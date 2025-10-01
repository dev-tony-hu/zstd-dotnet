namespace ZstdDotnet;

public sealed partial class ZstdEncoder : IDisposable
{
    private IntPtr _cstream;
    private bool _disposed;
    private bool _initialized;
    private int _level; // made non-readonly so Reset can change it

    private ZstdBuffer _in = new();
    private ZstdBuffer _out = new();

    public ZstdEncoder(int quality = 6)
    {
        if (quality < 1 || quality > ZstdProperties.MaxCompressionLevel)
            throw new ArgumentOutOfRangeException(nameof(quality));
        _level = quality;
        _cstream = ZstdInterop.ZSTD_createCStream();
    }

    public void Reset(int? newQuality = null, bool resetParameters = false)
    {
        ThrowIfDisposed();
        if (newQuality.HasValue)
        {
            int q = newQuality.Value;
            if (q < 1 || q > ZstdProperties.MaxCompressionLevel)
                throw new ArgumentOutOfRangeException(nameof(newQuality));
            if (q != _level)
            {
                _level = q; // update level; will be applied on next init
                resetParameters = true; // need params reapplied
            }
        }
        if (_cstream == IntPtr.Zero)
        {
            _cstream = ZstdInterop.ZSTD_createCStream();
            _initialized = false;
            return;
        }
        // Efficient reset via native API
        ZstdInterop.ResetCStream(_cstream, resetParameters);
        _initialized = false; // will re-init with (possibly new) level on next use
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_cstream != IntPtr.Zero)
            {
                ZstdInterop.ZSTD_freeCStream(_cstream);
                _cstream = IntPtr.Zero;
            }
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Compresses <paramref name="source"/> into <paramref name="destination"/> producing zero or more bytes.
    /// Parameter order and semantics mirror <c>BrotliEncoder.Compress</c> (Span-based) patterns.
    /// </summary>
    /// <param name="source">Uncompressed input bytes.</param>
    /// <param name="destination">Destination span for compressed output.</param>
    /// <param name="bytesConsumed">How many source bytes were consumed.</param>
    /// <param name="bytesWritten">How many compressed bytes were produced.</param>
    /// <param name="isFinalBlock">True if this call provides the final portion of the frame.</param>
    /// <returns>
    /// <see cref="System.Buffers.OperationStatus.Done"/> when all provided input was consumed and (if final) the frame is finalized;
    /// <see cref="System.Buffers.OperationStatus.DestinationTooSmall"/> when more destination space is required to flush pending state;
    /// <see cref="System.Buffers.OperationStatus.NeedMoreData"/> when the encoder consumed all available destination capacity for this step but expects more input (only possible when not final and all current input was consumed);
    /// <see cref="System.Buffers.OperationStatus.InvalidData"/> is never returned (reserved for decompression symmetry).
    /// </returns>
    public System.Buffers.OperationStatus Compress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = false)
    {
        ThrowIfDisposed();
        EnsureInit();
        bytesConsumed = 0; bytesWritten = 0;
        unsafe
        {
            fixed (byte* inPtr = source)
            fixed (byte* outPtr = destination)
            {
                // Feed input once per call – we trust caller to loop on statuses.
                _in.Data = (IntPtr)(inPtr + bytesConsumed);
                _in.Size = (UIntPtr)(uint)(source.Length - bytesConsumed);
                _in.Position = (UIntPtr)0;
                _out.Data = (IntPtr)(outPtr + bytesWritten);
                _out.Size = (UIntPtr)(uint)(destination.Length - bytesWritten);
                _out.Position = (UIntPtr)0;
                ZstdInterop.ThrowIfError(ZstdInterop.ZSTD_compressStream(_cstream, _out, _in));
                bytesConsumed += (int)_in.Position.ToUInt32();
                bytesWritten += (int)_out.Position.ToUInt32();

                // If final block and we've consumed all input, attempt to finish the frame.
                if (isFinalBlock && bytesConsumed == source.Length)
                {
                    int before = bytesWritten;
                    bool frameFinished = FinishFrame(ref bytesWritten, destination);
                    if (!frameFinished && bytesWritten == destination.Length)
                        return System.Buffers.OperationStatus.DestinationTooSmall; // need more space to finalize
                    if (frameFinished)
                        return System.Buffers.OperationStatus.Done;
                    // frame not finished but buffer filled -> DestinationTooSmall already handled above
                }

                // Not final block paths:
                if (!isFinalBlock)
                {
                    // If some destination produced but internal still has pending (rare) we signal DestTooSmall when buffer exhausted
                    if (bytesWritten == destination.Length)
                        return System.Buffers.OperationStatus.DestinationTooSmall;
                    // If we consumed all provided input and produced something (or nothing) but more input is needed to progress
                    if (bytesConsumed == source.Length && bytesWritten < destination.Length)
                        return System.Buffers.OperationStatus.NeedMoreData;
                }
                // Default: work for this slice finished – either partial progress or nothing pending.
                return System.Buffers.OperationStatus.Done;
            }
        }
    }

    private bool FinishFrame(ref int written, Span<byte> output)
    {
        // Loop flush/end until endStream reports completion (returns 0) or buffer fills.
        unsafe
        {
            fixed (byte* outPtr = output)
            {
                while (true)
                {
                    // Flush pending
                    _out.Data = (IntPtr)(outPtr + written);
                    _out.Size = (UIntPtr)(uint)(output.Length - written);
                    _out.Position = (UIntPtr)0;
                    ZstdInterop.ThrowIfError(ZstdInterop.ZSTD_flushStream(_cstream, _out));
                    written += (int)_out.Position.ToUInt32();
                    if (written == output.Length) return false; // caller must provide more space

                    // Attempt endStream
                    _out.Data = (IntPtr)(outPtr + written);
                    _out.Size = (UIntPtr)(uint)(output.Length - written);
                    _out.Position = (UIntPtr)0;
                    var remaining = ZstdInterop.ZSTD_endStream(_cstream, _out);
                    ZstdInterop.ThrowIfError(remaining);
                    written += (int)_out.Position.ToUInt32();
                    if (remaining == UIntPtr.Zero) return true; // frame fully finished
                    if (written == output.Length) return false;  // need more space to finalize
                }
            }
        }
    }

    private void EnsureInit()
    {
        if (!_initialized)
        {
            ZstdInterop.ThrowIfError(ZstdInterop.ZSTD_initCStream(_cstream, _level));
            _initialized = true;
        }
    }


    /// <summary>
    /// Flushes any pending compressed bytes from the internal zstd stream state into <paramref name="destination"/>
    /// without finishing (finalizing) the current frame.
    /// </summary>
    /// <remarks>
    /// <para>This mirrors the contract of <c>BrotliEncoder.Flush</c>: it makes buffered data available to the caller
    /// early (useful for latency-sensitive streaming) while keeping the frame open for subsequent <see cref="Compress"/> calls.</para>
    /// <para>No end-of-frame markers are emitted. The consumer of the compressed data MUST continue reading until the
    /// producer later finalizes the frame (e.g. via a final <see cref="Compress"/> call with <c>isFinalBlock=true</c> or via
    /// <see cref="FinishFrame"/> invoked internally by <see cref="ZstdStream.Dispose"/> / <see cref="ZstdStream.FlushFrame"/>).
    /// </para>
    /// <para>Callers should loop while the returned status is <see cref="OperationStatus.DestinationTooSmall"/> reusing or
    /// providing a new output buffer and accumulating the produced bytes.</para>
    /// <para>This method is idempotent when there is no pending data (it immediately returns <see cref="OperationStatus.Done"/>).</para>
    /// </remarks>
    /// <param name="destination">The span that receives flushed bytes.</param>
    /// <param name="bytesWritten">The number of bytes actually written into <paramref name="destination"/>.</param>
    /// <returns>
    /// <see cref="OperationStatus.Done"/> when all pending data has been emitted (or nothing was pending), or
    /// <see cref="OperationStatus.DestinationTooSmall"/> if more output remains and the caller must provide more space.
    /// </returns>
    public OperationStatus Flush(Span<byte> destination, out int bytesWritten)
    {
        ThrowIfDisposed();
        EnsureInit();
        bytesWritten = 0;
        unsafe
        {
            fixed (byte* outPtr = destination)
            {
                while (true)
                {
                    _out.Data = (IntPtr)(outPtr + bytesWritten);
                    _out.Size = (UIntPtr)(uint)(destination.Length - bytesWritten);
                    _out.Position = (UIntPtr)0;
                    var remaining = ZstdInterop.ZSTD_flushStream(_cstream, _out);
                    ZstdInterop.ThrowIfError(remaining);
                    bytesWritten += (int)_out.Position.ToUInt32();
                    if (remaining == UIntPtr.Zero) return OperationStatus.Done; // nothing more pending
                    if (bytesWritten == destination.Length) return OperationStatus.DestinationTooSmall; // caller needs to supply more space
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ZstdEncoder));
    }
}
