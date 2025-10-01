namespace ZstdDotnet;

public sealed partial class ZstdEncoder : IDisposable
{
    public static bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, int quality = 6)
    {
        bytesWritten = 0;
        using var enc = new ZstdEncoder(quality);
        int consumed;
        var status = enc.Compress(source, destination, out consumed, out int written, isFinalBlock: true);
        bytesWritten = written;
        if (status == System.Buffers.OperationStatus.DestinationTooSmall) return false;
        return status == System.Buffers.OperationStatus.Done && consumed == source.Length;
    }

    public static byte[] Compress(ReadOnlySpan<byte> source, int quality = 6)
    {
        int max = checked(source.Length + 128 + source.Length / 20);
        byte[] dest = new byte[max];
        if (!TryCompress(source, dest, out int written, quality))
        {
            dest = new byte[max * 2];
            if (!TryCompress(source, dest, out written, quality))
                throw new InvalidOperationException("Compression failed: destination buffer too small.");
        }
        if (written == dest.Length) return dest;
        Array.Resize(ref dest, written);
        return dest;
    }
}