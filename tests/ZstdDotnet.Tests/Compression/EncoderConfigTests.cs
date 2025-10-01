namespace ZstdDotnet.Tests;

public class EncoderConfigTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Encoder")]
    [Trait(TestTraits.Type, "ResetLevel")]
    [Trait(TestTraits.Mode, "Sync")]
    public void Reset_ThenSetCompressionLevel_Works()
    {
        using var encoder = new ZstdEncoder(1);
        Span<byte> dst = stackalloc byte[256];
        var src = Encoding.ASCII.GetBytes("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        encoder.Compress(src, dst, out _, out _, isFinalBlock: true);
        encoder.Reset();
        encoder.SetCompressionLevel(5);
        var src2 = Encoding.ASCII.GetBytes("yyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy");
        var status = encoder.Compress(src2, dst, out int consumed2, out int written2, isFinalBlock: true);
        Assert.Equal(OperationStatus.Done, status);
        Assert.Equal(src2.Length, consumed2);
        Assert.True(written2 > 0);
    }
}
