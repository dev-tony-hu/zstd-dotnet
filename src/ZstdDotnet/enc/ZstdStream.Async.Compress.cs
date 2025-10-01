namespace ZstdDotnet;

public partial class ZstdStream
{
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    private async ValueTask WriteInternalAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        if (System.Threading.Interlocked.Exchange(ref activeOperation, 1) == 1) throw new InvalidOperationException("Concurrent read/write not allowed");
        try
        {
            if (!CanWrite) throw new NotSupportedException();
            ArgumentNullException.ThrowIfNull(data);
            if (encoder == null) throw new InvalidOperationException("Encoder not initialized");
            while (buffer.Length > 0)
            {
                ct.ThrowIfCancellationRequested();
                int consumed; int written;
                var status = encoder.Compress(buffer.Span, data, out consumed, out written, isFinalBlock: false);
                if (written > 0)
                {
                    await stream.WriteAsync(data.AsMemory(0, written), ct).ConfigureAwait(false);
                }
                buffer = buffer.Slice(consumed);
                if (status == System.Buffers.OperationStatus.DestinationTooSmall)
                {
                    // Need to drain more pending output before feeding more input in next loop iteration
                    continue;
                }
                if (status == System.Buffers.OperationStatus.NeedMoreData && consumed == 0 && written == 0)
                {
                    int flushedConsumed; int flushedWritten;
                    var flushStatus = encoder.Compress(ReadOnlySpan<byte>.Empty, data, out flushedConsumed, out flushedWritten, false);
                    if (flushedWritten > 0)
                    {
                        await stream.WriteAsync(data.AsMemory(0, flushedWritten), ct).ConfigureAwait(false);
                    }
                    if (flushStatus == System.Buffers.OperationStatus.DestinationTooSmall)
                        continue; // still draining
                    if (flushedWritten == 0)
                        break; // no progress
                }
            }
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref activeOperation, 0);
        }
    }
}
