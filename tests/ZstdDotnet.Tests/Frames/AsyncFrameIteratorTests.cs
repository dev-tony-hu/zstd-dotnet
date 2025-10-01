namespace ZstdDotnet.Tests;

public class AsyncFrameIteratorTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Frames")]
    [Trait(TestTraits.Type, "Enumeration")]
    [Trait(TestTraits.Mode, "Async")]
    [Trait(TestTraits.Feature, "IAsyncEnumerableDecoder")]
    public async Task DecodeFramesAsync_MultiFrame_RoundTrip()
    {
        var frames = new List<byte[]>();
        for (int i = 0; i < 5; i++)
        {
            var buf = new byte[10_000 + i * 1234];
            new Random(100 + i).NextBytes(buf);
            frames.Add(buf);
        }
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            foreach (var f in frames)
            {
                zs.Write(f, 0, f.Length);
                zs.FlushFrame();
            }
        }
        ms.Position = 0;
        var decoded = new List<byte[]>();
        await foreach (var df in ZstdFrameDecoder.DecodeFramesAsync(ms, leaveOpen: true, compressedBufferSize: 32 * 1024, initialFrameBufferSize: 8 * 1024))
        {
            decoded.Add(df.Data);
        }
        Assert.Equal(frames.Count, decoded.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            Assert.True(frames[i].AsSpan().SequenceEqual(decoded[i]));
        }
    }
}
