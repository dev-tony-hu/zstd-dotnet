namespace ZstdDotnet.Tests;

public class StreamBehaviorTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Lifecycle")]
    [Trait(TestTraits.Mode, "Sync")]
    public void LeaveOpen_DoesNotCloseUnderlying()
    {
        var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(new byte[] { 1, 2, 3 }, 0, 3);
        }
        Assert.True(ms.CanWrite); // still open
    }

    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Lifecycle")]
    [Trait(TestTraits.Mode, "Sync")]
    public void Dispose_ClosesUnderlyingWhenNotLeaveOpen()
    {
        var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: false))
        {
            zs.Write(new byte[] { 1, 2, 3 }, 0, 3);
        }
        Assert.False(ms.CanWrite);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Reset")]
    [Trait(TestTraits.Mode, "Sync")]
    public void Reset_Decoder()
    {
        // Compress two payloads separately
        byte[] payload1 = new byte[100]; new Random(1).NextBytes(payload1);
        byte[] payload2 = new byte[200]; new Random(2).NextBytes(payload2);
        using var ms1 = new MemoryStream();
        using (var zs1 = new ZstdStream(ms1, CompressionMode.Compress, leaveOpen: true))
            zs1.Write(payload1, 0, payload1.Length);
        using (var zs2 = new ZstdStream(ms1, CompressionMode.Compress, leaveOpen: true))
            zs2.Write(payload2, 0, payload2.Length);
        ms1.Position = 0;
        using var ds = new ZstdStream(ms1, CompressionMode.Decompress, leaveOpen: true);
        var buf = new byte[500];
        int r = ds.Read(buf, 0, buf.Length);
        Assert.True(r > 0);
        ds.Reset();
        // After reset we should still be able to continue reading (remaining frames)
        int r2 = ds.Read(buf, 0, buf.Length);
        Assert.True(r2 >= 0);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Cancellation")]
    [Trait(TestTraits.Mode, "Async")]
    public async Task CancellationDuringReadAsync()
    {
        // Create large compressed data
        var data = new byte[200_000]; new Random(42).NextBytes(data);
        using var comp = new MemoryStream();
        using (var zs = new ZstdStream(comp, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(data, 0, data.Length);
        }
        comp.Position = 0;
        await using var ds = new ZstdStream(comp, CompressionMode.Decompress, leaveOpen: true);
        var buf = new byte[4096];
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ds.ReadAsync(buf, cts.Token));
    }
}
