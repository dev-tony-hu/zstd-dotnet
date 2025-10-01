using System.IO.Compression;

namespace ZstdDotnet.Tests;

public class ZstdCompressionLevelTests
{
    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    [Trait(TestTraits.Domain, "Encoder")]
    [Trait(TestTraits.Type, "Unit")]
    public void Mapping_MatchesExpectedLevel(CompressionLevel level)
    {
        var actual = ZstdCompressionLevelHelper.GetLevelFromCompressionLevel(level);
        var expected = level switch
        {
            CompressionLevel.NoCompression => ZstdProperties.MinCompressionLevel,
            CompressionLevel.Fastest => ZstdProperties.MinCompressionLevel,
            CompressionLevel.Optimal => ZstdCompressionLevelHelper.DefaultLevel,
            CompressionLevel.SmallestSize => ZstdProperties.MaxCompressionLevel,
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "RoundTrip")]
    public void Static_Compress_CompressionLevel_RoundTrips(CompressionLevel level)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join('-', Enumerable.Repeat("compression-level", 64)));
        var compressed = ZstdEncoder.Compress(payload, level);
        var roundTrip = ZstdDecoder.Decompress(compressed, payload.Length);
        Assert.Equal(payload.Length, roundTrip.Length);
        Assert.True(payload.AsSpan().SequenceEqual(roundTrip));
    }

    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "RoundTrip")]
    public void Static_Compress_NoCompression_PreservesPayload()
    {
        var payload = Encoding.UTF8.GetBytes(string.Join(',', Enumerable.Repeat("no-compress", 32)));
        var compressed = ZstdEncoder.Compress(payload, CompressionLevel.NoCompression);
        var roundTrip = ZstdDecoder.Decompress(compressed, payload.Length);
        Assert.Equal(payload.Length, roundTrip.Length);
        Assert.True(payload.AsSpan().SequenceEqual(roundTrip));
    }

    [Fact]
    [Trait(TestTraits.Domain, "Encoder")]
    [Trait(TestTraits.Type, "Construction")]
    public void Constructor_CompressionLevel_UsesValidLevel()
    {
        using var encoder = new ZstdEncoder(CompressionLevel.Optimal);
        Span<byte> destination = stackalloc byte[512];
        var source = Encoding.ASCII.GetBytes("level-mapping-test");
        var status = encoder.Compress(source, destination, out int consumed, out int written, isFinalBlock: true);
        Assert.Equal(System.Buffers.OperationStatus.Done, status);
        Assert.Equal(source.Length, consumed);
        Assert.True(written > 0);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Encoder")]
    [Trait(TestTraits.Type, "Unit")]
    public void Mapping_InvalidLevel_Throws()
    {
        var invalid = (CompressionLevel)42;
        Assert.Throws<ArgumentOutOfRangeException>(() => ZstdCompressionLevelHelper.GetLevelFromCompressionLevel(invalid));
    }
}
