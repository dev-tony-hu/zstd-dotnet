namespace ZstdDotnet.Tests;

public class LargeDataTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "RoundTrip")]
    [Trait(TestTraits.Mode, "Sync")]
    public void Compress_Decompress_16MB()
    {
        // 16 MB payload with moderate repetition to allow compression but not trivial.
        const int size = 16 * 1024 * 1024; // 16MB
        var payload = new byte[size];
        var pattern = Encoding.ASCII.GetBytes("ZSTD-LARGE-DATA-PATTERN-");
        for (int i = 0; i < size; i += pattern.Length)
        {
            Buffer.BlockCopy(pattern, 0, payload, i, Math.Min(pattern.Length, size - i));
        }
        // Introduce some noise every ~4KB
        var rnd = new Random(1234);
        for (int off = 0; off < size; off += 4096)
        {
            payload[off] = (byte)rnd.Next(0, 256);
        }

        // Compress using static helper (one-shot)
        var compressed = ZstdEncoder.Compress(payload, level: 5);
        Assert.True(compressed.Length < payload.Length, "Expected compression to reduce size");

        // Decompress back
        var roundTrip = ZstdDecoder.Decompress(compressed, payload.Length);
        Assert.Equal(payload.Length, roundTrip.Length);
        Assert.True(payload.AsSpan().SequenceEqual(roundTrip));
    }
}
