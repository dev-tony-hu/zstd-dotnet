namespace ZstdDotnet;

public partial class ZstdStream
{
    private async ValueTask<int> ReadInternalAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (System.Threading.Interlocked.Exchange(ref activeOperation, 1) == 1) throw new InvalidOperationException("Concurrent read/write not allowed");
        try
        {
            if (!CanRead) throw new NotSupportedException();
            ArgumentNullException.ThrowIfNull(data);
            if (decoder == null) throw new InvalidOperationException("Decoder not initialized");
            int totalWritten = 0;
            while (buffer.Length > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (dataPosition >= dataSize && !dataDepleted)
                {
                    dataSize = await stream.ReadAsync(data.AsMemory(0, data.Length), ct).ConfigureAwait(false);
                    dataDepleted = dataSize == 0; dataPosition = 0;
                }
                int availableCompressed = dataSize - dataPosition;
                if (availableCompressed <= 0)
                {
                    if (dataDepleted) break; // no more compressed data
                    continue; // need to read more
                }
                bool finalBlock = dataDepleted;
                // Avoid declaring a local Span<T> inside async method (CS4012). Inline the span expression in the call.
                var status = decoder.Decompress(
                    data.AsSpan(dataPosition, availableCompressed),
                    buffer.Span,
                    out int consumed,
                    out int written,
                    finalBlock,
                    out bool frameFinished);
                if (consumed > 0) dataPosition += consumed;
                if (written > 0)
                {
                    totalWritten += written;
                    buffer = buffer[written..];
                }
                if (frameFinished && buffer.Length == 0)
                    break; // caller buffer full at frame boundary

                switch (status)
                {
                    case System.Buffers.OperationStatus.DestinationTooSmall:
                        if (written > 0)
                            return totalWritten; // allow caller to consume
                        break;
                    case System.Buffers.OperationStatus.NeedMoreData:
                        if (dataDepleted) return totalWritten; // truncated or finished
                        break; // trigger another compressed read
                    case System.Buffers.OperationStatus.Done:
                        if (written == 0 && consumed == 0 && dataDepleted)
                            return totalWritten;
                        break;
                }
            }
            return totalWritten;
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref activeOperation, 0);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<int>(cancellationToken);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
