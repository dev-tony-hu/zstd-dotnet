namespace ZstdDotnet;

public partial class ZstdStream
{
    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0) return 0;
        byte[] rented = arrayPool.Rent(buffer.Length);
        try
        {
            int read = ReadInternal(rented, 0, buffer.Length);
            if (read > 0)
            {
                new ReadOnlySpan<byte>(rented, 0, read).CopyTo(buffer);
            }
            return read;
        }
        finally
        {
            arrayPool.Return(rented, clearArray: false);
        }
    }

    private partial int ReadInternal(byte[] buffer, int offset, int count)
    {
        if (System.Threading.Interlocked.Exchange(ref activeOperation, 1) == 1) throw new InvalidOperationException("Concurrent read/write not allowed");
        try
        {
            ArgumentNullException.ThrowIfNull(data);
            if (!CanRead) throw new NotSupportedException();
            if (decoder == null) throw new InvalidOperationException("Decoder not initialized");
            int totalWritten = 0;
            while (count > 0)
            {
                if (dataPosition >= dataSize && !dataDepleted)
                {
                    dataSize = stream.Read(data!, 0, data!.Length);
                    dataDepleted = dataSize == 0; dataPosition = 0;
                }
                int availableCompressed = dataSize - dataPosition;
                if (availableCompressed <= 0 && dataDepleted) break;
                var compressedSpan = new ReadOnlySpan<byte>(data!, dataPosition, availableCompressed);
                var outputSpan = new Span<byte>(buffer, offset, count);
                bool finalBlock = dataDepleted;
                var status = decoder.Decompress(compressedSpan, outputSpan, out int consumed, out int written, finalBlock, out bool frameFinished);
                if (consumed > 0) dataPosition += consumed;
                if (written > 0)
                {
                    totalWritten += written; offset += written; count -= written;
                }
                if (frameFinished)
                {
                    if (count == 0) break; // caller buffer full
                    // If more compressed remains or more frames can follow, continue loop naturally
                }
                switch (status)
                {
                    case System.Buffers.OperationStatus.DestinationTooSmall:
                        // Need caller to read accumulated output; stop now if we've produced something.
                        if (written > 0) return totalWritten;
                        break; // else loop again (should not happen normally without progress)
                    case System.Buffers.OperationStatus.NeedMoreData:
                        if (dataDepleted)
                            return totalWritten; // no more compressed input available
                        break; // read more compressed next iteration
                    case System.Buffers.OperationStatus.Done:
                        // Continue unless no more compressed and nothing written
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
}
