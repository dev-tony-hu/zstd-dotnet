using System.IO.Compression;

namespace ZstdDotnet;

public sealed partial class ZstdEncoder : IDisposable
{
    public static bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, int level = ZstdCompressionLevelHelper.DefaultLevel)
    {
        bytesWritten = 0;
        using var enc = new ZstdEncoder(level);
        int consumed;
        var status = enc.Compress(source, destination, out consumed, out int written, isFinalBlock: true);
        bytesWritten = written;
        if (status == System.Buffers.OperationStatus.DestinationTooSmall) return false;
        return status == System.Buffers.OperationStatus.Done && consumed == source.Length;
    }

    public static bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, CompressionLevel compressionLevel)
    {
        int level = ZstdCompressionLevelHelper.GetLevelFromCompressionLevel(compressionLevel);
        return TryCompress(source, destination, out bytesWritten, level);
    }

    public static byte[] Compress(ReadOnlySpan<byte> source, int level = ZstdCompressionLevelHelper.DefaultLevel)
    {
        int max = checked(source.Length + 128 + source.Length / 20);
        byte[] dest = new byte[max];
        if (!TryCompress(source, dest, out int written, level))
        {
            dest = new byte[max * 2];
            if (!TryCompress(source, dest, out written, level))
                throw new InvalidOperationException("Compression failed: destination buffer too small.");
        }
        if (written == dest.Length) return dest;
        Array.Resize(ref dest, written);
        return dest;
    }

    public static byte[] Compress(ReadOnlySpan<byte> source, CompressionLevel compressionLevel)
    {
        int level = ZstdCompressionLevelHelper.GetLevelFromCompressionLevel(compressionLevel);
        return Compress(source, level);
    }
}