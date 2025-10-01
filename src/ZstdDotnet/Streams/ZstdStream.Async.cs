namespace ZstdDotnet;

public partial class ZstdStream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<int>(cancellationToken);
        return ReadInternalAsync(buffer, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);
        return WriteInternalAsync(buffer, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        if (!isClosed)
        {
            if (System.Threading.Interlocked.Exchange(ref activeOperation, 1) == 1) throw new InvalidOperationException("Concurrent operation not allowed during dispose");
            try
            {
                if (mode == CompressionMode.Compress && encoder != null && data != null)
                {
                    bool done = false; int safety = 0;
                    while (!done && safety++ < 64)
                    {
                        int consumed; int written;
                        var status = encoder.Compress(ReadOnlySpan<byte>.Empty, data, out consumed, out written, isFinalBlock: true);
                        if (written > 0)
                            await stream.WriteAsync(data.AsMemory(0, written)).ConfigureAwait(false);
                        if (status == System.Buffers.OperationStatus.Done)
                            done = true;
                        else if (status == System.Buffers.OperationStatus.DestinationTooSmall)
                            continue; // drain further
                        else if (status == System.Buffers.OperationStatus.NeedMoreData)
                            done = true;
                    }
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                if (!leaveOpen)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                encoder?.Dispose();
                decoder?.Dispose();
                isClosed = true;
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref activeOperation, 0);
            }
        }
        if (data != null) arrayPool.Return(data, clearArray: false);
        data = null; isDisposed = true;
        GC.SuppressFinalize(this);
    }

}
