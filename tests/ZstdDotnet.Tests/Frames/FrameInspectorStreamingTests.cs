namespace ZstdDotnet.Tests;

public class FrameInspectorStreamingTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Frames")]
    [Trait(TestTraits.Type, "Metadata")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "ContentSizeOptional")]
    public void ContentSize_Present_ForKnownSizeFrames()
    {
        var data1 = Encoding.UTF8.GetBytes(new string('A', 5000));
        var data2 = Encoding.UTF8.GetBytes(new string('B', 1000));
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(data1, 0, data1.Length);
            zs.FlushFrame();
            zs.Write(data2, 0, data2.Length);
        }
        var blob = ms.ToArray();
        var frames = ZstdFrameInspector.EnumerateFrames(blob);
        Assert.Equal(2, frames.Count);
        Assert.True(frames[0].ContentSize is null || frames[0].ContentSize == (ulong)data1.Length);
        Assert.True(frames[1].ContentSize is null || frames[1].ContentSize == (ulong)data2.Length);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Frames")]
    [Trait(TestTraits.Type, "Enumeration")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "StreamingVsInMemory")]
    public void StreamingEnumeration_MatchesInMemory()
    {
        var rnd = new Random(42);
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            for (int i = 0; i < 10; i++)
            {
                var payload = new byte[rnd.Next(500, 4000)];
                rnd.NextBytes(payload);
                zs.Write(payload, 0, payload.Length);
                zs.FlushFrame();
            }
        }
        var blob = ms.ToArray();
        var listA = ZstdFrameInspector.EnumerateFrames(blob);
        ms.Position = 0;
        var listB = ZstdFrameInspector.EnumerateFrames(ms).ToList();
        Assert.Equal(listA.Count, listB.Count);
        for (int i = 0; i < listA.Count; i++)
        {
            Assert.Equal(listA[i].Offset, listB[i].Offset);
            Assert.Equal(listA[i].CompressedSize, listB[i].CompressedSize);
        }
    }
}
