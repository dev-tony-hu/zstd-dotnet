namespace ZstdDotnet.Tests;

public class ConcurrencyTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Concurrency")]
    [Trait(TestTraits.Type, "ExclusiveOperation")]
    [Trait(TestTraits.Mode, "Sync")]
    public void Write_WhenActiveOperationSet_ShouldThrow()
    {
        using var ms = new MemoryStream();
        using var zs = new ZstdStream(ms, CompressionMode.Compress);
        Assert.True(zs.TestTryAcquireOperation());
        Assert.Throws<InvalidOperationException>(() => zs.Write(new byte[] { 1, 2, 3 }, 0, 3));
        zs.TestReleaseOperation();
    }

    [Fact]
    [Trait(TestTraits.Domain, "Concurrency")]
    [Trait(TestTraits.Type, "ExclusiveOperation")]
    [Trait(TestTraits.Mode, "Sync")]
    public void Read_WhenActiveOperationSet_ShouldThrow()
    {
        // prepare minimal compressed data (empty frame)
        using var comp = new MemoryStream();
        using (var enc = new ZstdStream(comp, CompressionMode.Compress, leaveOpen: true)) { }
        comp.Position = 0;
        using var ds = new ZstdStream(comp, CompressionMode.Decompress);
        Assert.True(ds.TestTryAcquireOperation());
        Assert.Throws<InvalidOperationException>(() => ds.Read(new byte[16], 0, 16));
        ds.TestReleaseOperation();
    }

    [Fact]
    [Trait(TestTraits.Domain, "Concurrency")]
    [Trait(TestTraits.Type, "ExclusiveOperation")]
    [Trait(TestTraits.Mode, "Async")]
    public async Task WriteAsync_WhenActiveOperationSet_ShouldThrow()
    {
        using var ms = new MemoryStream();
        await using var zs = new ZstdStream(ms, CompressionMode.Compress);
        Assert.True(zs.TestTryAcquireOperation());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await zs.WriteAsync(new byte[8]));
        zs.TestReleaseOperation();
    }

    [Fact]
    [Trait(TestTraits.Domain, "Concurrency")]
    [Trait(TestTraits.Type, "ExclusiveOperation")]
    [Trait(TestTraits.Mode, "Async")]
    public async Task ReadAsync_WhenActiveOperationSet_ShouldThrow()
    {
        using var comp = new MemoryStream();
        using (var enc = new ZstdStream(comp, CompressionMode.Compress, leaveOpen: true)) { enc.Write(new byte[] { 5, 6, 7 }, 0, 3); }
        comp.Position = 0;
        await using var ds = new ZstdStream(comp, CompressionMode.Decompress);
        Assert.True(ds.TestTryAcquireOperation());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await ds.ReadAsync(new byte[32]));
        ds.TestReleaseOperation();
    }
}
