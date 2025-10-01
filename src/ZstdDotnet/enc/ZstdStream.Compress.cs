namespace ZstdDotnet;

public partial class ZstdStream
{
    // Indicates that the previous frame was finalized via FlushFrame and the encoder
    // must be reset before accepting new input (to begin a fresh frame). We defer
    // the reset until the next write to avoid creating an empty trailing frame
    // when the user disposes immediately after FlushFrame.
    private bool pendingFrameReset = false;
    partial void FlushInternal()
    {
        if (mode != CompressionMode.Compress || encoder is null || data is null) return;
        int written;
        System.Buffers.OperationStatus status;
        do
        {
            status = encoder.Flush(data, out written);
            if (written > 0)
                stream.Write(data, 0, written);
        } while (status == System.Buffers.OperationStatus.DestinationTooSmall);
        stream.Flush();
    }

    /// <summary>
    /// Flushes pending compressed bytes into <paramref name="destination"/> without finalizing the current frame.
    /// </summary>
    /// <remarks>
    /// <para>The semantics mirror <c>BrotliEncoder.Flush</c>: data buffered inside the encoder becomes available to the caller
    /// early while keeping the frame open for more <see cref="Write(ReadOnlySpan{byte})"/> / <see cref="Stream.Write(byte[],int,int)"/> operations.</para>
    /// <para>No frame boundary markers are produced. A consumer reading the compressed stream cannot assume the frame has
    /// ended after a successful flush; only disposal / closing (or <see cref="FlushFrame"/>) guarantees a completed frame.</para>
    /// <para>Typical usage pattern:
    /// <code>
    /// Span<byte> scratch = stackalloc byte[8192];
    /// while (stream.Flush(scratch, out int written) == OperationStatus.DestinationTooSmall)
    ///     output.Write(scratch[..written]);
    /// if (written > 0) output.Write(scratch[..written]);
    /// </code></para>
    /// <para>This method is only valid when the stream is in <see cref="CompressionMode.Compress"/>.</para>
    /// </remarks>
    public System.Buffers.OperationStatus Flush(System.Span<byte> destination, out int bytesWritten)
    {
        if (mode != CompressionMode.Compress) throw new NotSupportedException("Flush(destination) only valid in compression mode");
        if (encoder is null) throw new ObjectDisposedException(nameof(ZstdStream));
        return encoder.Flush(destination, out bytesWritten);
    }

    /// <summary>
    /// Asynchronously flushes any pending compressed bytes to the underlying stream without finalizing the frame.
    /// </summary>
    /// <remarks>
    /// Equivalent to the synchronous <see cref="Stream.Flush"/> variant for this stream but performed asynchronously.
    /// Multiple awaits may occur internally until all buffered data has been drained from the encoder.
    /// </remarks>
    public override async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (mode != CompressionMode.Compress || encoder is null || data is null) return;
        if (System.Threading.Interlocked.Exchange(ref activeOperation, 1) == 1) throw new InvalidOperationException("Concurrent read/write not allowed");
        try
        {
            int written;
            System.Buffers.OperationStatus status;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                status = encoder.Flush(data, out written);
                if (written > 0)
                    await stream.WriteAsync(data.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
            } while (status == System.Buffers.OperationStatus.DestinationTooSmall);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref activeOperation, 0);
        }
    }

    /// <summary>
    /// Finalizes the current frame (emits all end-of-frame markers) and immediately starts a new frame for subsequent writes.
    /// </summary>
    /// <remarks>
    /// <para>This enables creation of multi-frame (multi-member) compressed streams without closing the <see cref="ZstdStream"/>.</para>
    /// <para><b>Difference from <see cref="Flush()"/> / <see cref="Flush(System.Span{byte}, out int)"/>:</b> ordinary flush operations do NOT end the frame; they only
    /// expose buffered data. <c>FlushFrame</c> guarantees the frame is fully terminated and decoders can begin a new frame afterwards.</para>
    /// <para>After calling <c>FlushFrame</c> you may continue writing more data which will belong to a new frame with identical compression level settings.</para>
    /// <para>This method is a no-op when invoked on a decompression stream.</para>
    /// </remarks>
    public void FlushFrame()
    {
        if (mode != CompressionMode.Compress || encoder is null || data is null) return;
        if (System.Threading.Interlocked.Exchange(ref activeOperation, 1) == 1) throw new InvalidOperationException("Concurrent read/write not allowed");
        try
        {
            // If a frame boundary has already been reached (FlushFrame called with no writes since), avoid
            // emitting an extra empty frame.
            if (pendingFrameReset)
            {
                return; // idempotent when no new data written
            }
            bool frameFinished = false; int safety = 0;
            while (!frameFinished && safety < 128)
            {
                int consumed; int written;
                var status = encoder.Compress(ReadOnlySpan<byte>.Empty, data, out consumed, out written, isFinalBlock: true);
                if (written > 0) stream.Write(data, 0, written);
                if (status == System.Buffers.OperationStatus.Done)
                    frameFinished = true;
                else if (status == System.Buffers.OperationStatus.DestinationTooSmall)
                {
                    // Continue loop with empty input to drain pending state
                }
                else if (status == System.Buffers.OperationStatus.NeedMoreData)
                {
                    // No more data forthcoming (empty input) yet encoder signals NeedMoreData -> treat as finished safeguard
                    frameFinished = true;
                }
                safety++;
            }
            stream.Flush();
            // Defer reset until next write to prevent creation of an empty trailing frame on Dispose.
            pendingFrameReset = true;
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref activeOperation, 0);
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0) return;
        byte[] rented = arrayPool.Rent(buffer.Length);
        buffer.CopyTo(rented);
        try
        {
            WriteInternal(rented, 0, buffer.Length);
        }
        finally
        {
            arrayPool.Return(rented, clearArray: false);
        }
    }

    private partial void WriteInternal(byte[] buffer, int offset, int count)
    {
        if (System.Threading.Interlocked.Exchange(ref activeOperation, 1) == 1) throw new InvalidOperationException("Concurrent read/write not allowed");
        try
        {
            ArgumentNullException.ThrowIfNull(data);
            if (!CanWrite) throw new NotSupportedException();
            if (encoder == null) throw new InvalidOperationException("Encoder not initialized");
            // If the previous frame was ended by FlushFrame, begin a new one lazily now.
            if (pendingFrameReset)
            {
                encoder.Reset();
                pendingFrameReset = false;
            }
            while (count > 0)
            {
                int chunk = Math.Min(count, buffer.Length - offset);
                var srcSpan = new ReadOnlySpan<byte>(buffer, offset, chunk);
                int consumed; int written;
                var status = encoder.Compress(srcSpan, data, out consumed, out written, isFinalBlock: false);
                if (written > 0) stream.Write(data, 0, written);
                offset += consumed; count -= consumed;
                if (status == System.Buffers.OperationStatus.DestinationTooSmall)
                {
                    // destination filled, continue without advancing input further than consumed
                    continue;
                }
                if (status == System.Buffers.OperationStatus.NeedMoreData && consumed == 0 && written == 0)
                {
                    // Nothing progressed; try to flush internal pending state with empty input
                    int flushedConsumed; int flushedWritten;
                    var flushStatus = encoder.Compress(ReadOnlySpan<byte>.Empty, data, out flushedConsumed, out flushedWritten, false);
                    if (flushedWritten > 0) stream.Write(data, 0, flushedWritten);
                    if (flushStatus == System.Buffers.OperationStatus.DestinationTooSmall)
                        continue; // still draining
                    if (flushedWritten == 0)
                        break; // no forward progress
                }
            }
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref activeOperation, 0);
        }
    }
}
