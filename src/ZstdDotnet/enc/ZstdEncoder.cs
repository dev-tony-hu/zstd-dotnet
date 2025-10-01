namespace ZstdDotnet;

public sealed partial class ZstdEncoder : IDisposable
{
    // Compression context (SafeHandle wrapper) – unified path only (requires libzstd >= 1.5.0)
    private ZstdCCtxHandle ctx;
    private bool disposed;
    private int level; // adjustable via Reset

    private ZstdBuffer inBuf = new();
    private ZstdBuffer outBuf = new();

    public ZstdEncoder(int level = ZstdCompressionLevelHelper.DefaultLevel)
    {
        if (!ZstdInterop.SupportsCompressStream2)
            throw new NotSupportedException("libzstd >= 1.5.0 required (ZSTD_compressStream2)");
        ZstdCompressionLevelHelper.ValidateLevel(level, nameof(level));
        this.level = level;
        ctx = new ZstdCCtxHandle();
    }


    public ZstdEncoder(CompressionLevel compressionLevel)
        : this(ZstdCompressionLevelHelper.GetLevelFromCompressionLevel(compressionLevel))
    {
    }

    
    public void Reset()
    {
        ThrowIfDisposed();
        if (ctx is null || ctx.IsInvalid)
            ctx = new ZstdCCtxHandle();
        // Session-only reset; parameters+pfx reapplied lazily on next Configure
        ctx.Reset();
        // After reset, worker threads can be reconfigured before next EnsureInit
    }

    public void SetCompressionLevel(int newLevel)
    {
        ThrowIfDisposed();
        ZstdCompressionLevelHelper.ValidateLevel(newLevel, nameof(newLevel));
        if (ctx.IsConfigured)
            throw new InvalidOperationException("SetCompressionLevel must be called before first compression or after a Reset before reuse.");
        level = newLevel;
    }

    /// <summary>
    /// Sets a raw content prefix (NOT a trained dictionary) expected to match the beginning of subsequently
    /// compressed data, potentially improving compression ratio for similar streams.
    /// Must be invoked before first compression after construction or a <see cref="Reset"/>.
    /// The provided span is defensively copied; for large prefixes prefer reusing the encoder instead of
    /// repeatedly calling SetPrefix. Passing an empty span clears any previously set prefix.
    /// </summary>
    public void SetPrefix(ReadOnlyMemory<byte> prefix)
    {
        ThrowIfDisposed();
        if (ctx.IsConfigured)
            throw new InvalidOperationException("SetPrefix must be called before starting compression (after Reset if reused).");
        ctx.SetPrefix(prefix);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            ctx?.Dispose();
            disposed = true;
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
                // Unified API only
                inBuf.Data = (IntPtr)(inPtr + bytesConsumed);
                inBuf.Size = (UIntPtr)(uint)(source.Length - bytesConsumed);
                inBuf.Position = (UIntPtr)0;
                outBuf.Data = (IntPtr)(outPtr + bytesWritten);
                outBuf.Size = (UIntPtr)(uint)(destination.Length - bytesWritten);
                outBuf.Position = (UIntPtr)0;
                var directive = isFinalBlock ? ZSTD_EndDirective.ZSTD_e_end : ZSTD_EndDirective.ZSTD_e_continue;
                var remaining = ZstdInterop.ZSTD_compressStream2(ctx.DangerousGetHandle(), outBuf, inBuf, directive);
                ZstdInterop.ThrowIfError(remaining);
                bytesConsumed += (int)inBuf.Position.ToUInt32();
                bytesWritten += (int)outBuf.Position.ToUInt32();

                if (directive == ZSTD_EndDirective.ZSTD_e_end)
                {
                    bool finished = remaining == UIntPtr.Zero;
                    if (finished && bytesConsumed == source.Length)
                        return System.Buffers.OperationStatus.Done;
                    if (!finished && bytesWritten == destination.Length)
                        return System.Buffers.OperationStatus.DestinationTooSmall;
                }

                if (!isFinalBlock)
                {
                    if (bytesWritten == destination.Length)
                        return System.Buffers.OperationStatus.DestinationTooSmall;
                    if (bytesConsumed == source.Length && bytesWritten < destination.Length)
                        return System.Buffers.OperationStatus.NeedMoreData;
                }
                return System.Buffers.OperationStatus.Done;
            }
        }
    }

    private void EnsureInit() => ctx.Configure(level); // idempotent


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
                    outBuf.Data = (IntPtr)(outPtr + bytesWritten);
                    outBuf.Size = (UIntPtr)(uint)(destination.Length - bytesWritten);
                    outBuf.Position = (UIntPtr)0;
                    // Provide an empty input buffer for pure flush
                    inBuf.Data = IntPtr.Zero;
                    inBuf.Size = UIntPtr.Zero;
                    inBuf.Position = UIntPtr.Zero;
                    var remaining = ZstdInterop.ZSTD_compressStream2(ctx.DangerousGetHandle(), outBuf, inBuf, ZSTD_EndDirective.ZSTD_e_flush);
                    ZstdInterop.ThrowIfError(remaining);
                    bytesWritten += (int)outBuf.Position.ToUInt32();
                    if (remaining == UIntPtr.Zero) return OperationStatus.Done;
                    if (bytesWritten == destination.Length) return OperationStatus.DestinationTooSmall;
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ZstdEncoder));
    }
}
