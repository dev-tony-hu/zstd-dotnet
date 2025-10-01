namespace ZstdDotnet.Tests;

public class FrameInspectorTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Frames")]
    [Trait(TestTraits.Type, "Enumeration")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "MultiFrameOffsets")]
    public void EnumerateFrames_TwoFrames()
    {
        var part1 = Encoding.ASCII.GetBytes("AAAAAA-BBBBBB-CCCCCC");
        var part2 = Encoding.ASCII.GetBytes("DDD-EEEE-FFFF-GGGG");
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(part1, 0, part1.Length);
            zs.FlushFrame();
            zs.Write(part2, 0, part2.Length);
        }
        var blob = ms.ToArray();
        var frames = ZstdFrameInspector.EnumerateFrames(blob);
        Assert.Equal(2, frames.Count);
        Assert.True(frames[0].Offset == 0UL);
        Assert.True(frames[1].Offset == frames[0].CompressedSize);
        // Basic sanity: total size matches
        ulong sum = frames[0].CompressedSize + frames[1].CompressedSize;
        Assert.Equal((ulong)blob.Length, sum);
    }
}
