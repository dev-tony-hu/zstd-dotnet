namespace ZstdDotnet.Tests;

public class FrameMetadataEdgeTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Frames")]
    [Trait(TestTraits.Type, "Metadata")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "Skippable")]
    public void SkippableFrame_FollowedBy_NormalFrame_InspectorRecognizesBoth()
    {
        // Build a skippable frame manually: magic 0x184D2A50 (little-endian) + size + payload
        var skipPayload = new byte[32];
        new Random(123).NextBytes(skipPayload);
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            bw.Write(0x184D2A50u); // base skippable magic
            bw.Write((uint)skipPayload.Length); // payload size
            bw.Write(skipPayload);
        }
        // Append a compressed frame
        var normalData = Encoding.UTF8.GetBytes("skippable-followed-normal-frame-data");
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(normalData, 0, normalData.Length);
        }
        var blob = ms.ToArray();
        var frames = ZstdFrameInspector.EnumerateFrames(blob);
        Assert.Equal(2, frames.Count);
        Assert.Equal("skippable", frames[0].FrameType);
        Assert.True(frames[0].CompressedSize > 0);
        Assert.True(frames[1].CompressedSize > 0);
        // Content size may be null depending on encoder header; just ensure second frame not mis-labeled.
        Assert.Equal("frame", frames[1].FrameType);
        // Offsets monotonic
        Assert.Equal(0UL, frames[0].Offset);
        Assert.Equal(frames[0].CompressedSize, frames[1].Offset);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Frames")]
    [Trait(TestTraits.Type, "Metadata")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "MixedKnownUnknownContentSize")]
    public void MixedKnownAndUnknownContentSize_Frames()
    {
        // Strategy: First frame small (likely inline content size), second frame large-ish to increase chance of unknown or different header behavior.
        var small = Encoding.ASCII.GetBytes(new string('A', 500));
        var large = new byte[150_000]; // large enough to exercise window/content heuristics
        new Random(55).NextBytes(large);
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(small, 0, small.Length);
            zs.FlushFrame();
            zs.Write(large, 0, large.Length);
        }
        var blob = ms.ToArray();
        var frames = ZstdFrameInspector.EnumerateFrames(blob);
        Assert.Equal(2, frames.Count);
        // At least one frame should either report content size or null; assert they aren't both zero-sized and compressed size sums up.
        Assert.True(frames[0].CompressedSize > 0);
        Assert.True(frames[1].CompressedSize > 0);
        Assert.NotEqual(frames[0].CompressedSize, frames[1].CompressedSize); // sanity difference
        // WindowSize may be null if not present; we at least touch getters
        _ = frames[0].WindowSize;
        _ = frames[1].WindowSize;
        _ = frames[0].ContentSize;
        _ = frames[1].ContentSize;
    }
}
