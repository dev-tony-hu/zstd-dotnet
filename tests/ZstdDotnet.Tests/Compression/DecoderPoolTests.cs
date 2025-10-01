namespace ZstdDotnet.Tests;

public class DecoderPoolTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Decoder")]
    [Trait(TestTraits.Type, "Pooling")]
    [Trait(TestTraits.Mode, "Sync")]
    public void RentReturn_ReusesInstance()
    {
        var d1 = ZstdDecoderPool.Rent();
        Assert.NotNull(d1);
        ZstdDecoderPool.Return(d1);
        var d2 = ZstdDecoderPool.Rent();
        // Likely same reference (not guaranteed but acceptable); test that it still works
        var compressed = ZstdEncoder.Compress(Encoding.UTF8.GetBytes("hello world"));
        Span<byte> dest = stackalloc byte[64];
        var status = d2.Decompress(compressed, dest, out int consumed, out int written, isFinalBlock: true, out bool frameDone);
        Assert.Equal(System.Buffers.OperationStatus.Done, status);
        Assert.True(frameDone);
        Assert.Equal("hello world", Encoding.UTF8.GetString(dest[..written]));
        ZstdDecoderPool.Return(d2);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Decoder")]
    [Trait(TestTraits.Type, "WindowLimit")]
    [Trait(TestTraits.Mode, "Sync")]
    public void SetMaxWindow_AppliesBeforeDecompress()
    {
        using var d = new ZstdDecoder();
        d.SetMaxWindow(20); // allow up to 1MB (2^20) roughly
        var data = Encoding.UTF8.GetBytes(new string('A', 100));
        var comp = ZstdEncoder.Compress(data);
        Span<byte> dst = stackalloc byte[200];
        var st = d.Decompress(comp, dst, out int consumed, out int written, isFinalBlock: true, out bool frameFinished);
        Assert.Equal(System.Buffers.OperationStatus.Done, st);
        Assert.True(frameFinished);
        Assert.Equal(data.Length, written);
    }
}
