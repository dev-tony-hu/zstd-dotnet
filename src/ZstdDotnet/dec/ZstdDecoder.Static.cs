namespace ZstdDotnet;

public sealed partial class ZstdDecoder : IDisposable
{
    public static bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        using var dec = new ZstdDecoder();
        bool final = true;
        var status = dec.Decompress(source, destination, out int consumed, out int written, final, out bool frameFinished);
        bytesWritten = written;
        if (status == System.Buffers.OperationStatus.DestinationTooSmall) return false;
        return frameFinished && consumed == source.Length && status == System.Buffers.OperationStatus.Done;
    }

    public static byte[] Decompress(ReadOnlySpan<byte> source, int expectedSize)
    {
        byte[] dest = new byte[expectedSize];
        if (TryDecompress(source, dest, out int written))
        {
            if (written != dest.Length) Array.Resize(ref dest, written);
            return dest;
        }
        int size = expectedSize;
        for (int i = 0; i < 6; i++)
        {
            size = checked(size * 2);
            dest = new byte[size];
            if (TryDecompress(source, dest, out written))
            {
                if (written != dest.Length) Array.Resize(ref dest, written);
                return dest;
            }
        }
        throw new InvalidOperationException("Decompression failed: destination too small or data invalid.");
    }
}