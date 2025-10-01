namespace ZstdDotnet.Tests;

public class StreamingAsyncTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "RoundTrip")]
    [Trait(TestTraits.Mode, "Async")]
    public async Task CompressDecompressAsync_RoundTrip()
    {
        var original = string.Join("-", Enumerable.Range(0, 1000).Select(i => $"line{i:0000}"));
        byte[] originalBytes = Encoding.UTF8.GetBytes(original);
        using var ms = new MemoryStream();
        await using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            await zs.WriteAsync(originalBytes);
            await zs.FlushAsync();
        }
        ms.Position = 0;
        using var outMs = new MemoryStream();
        await using (var dz = new ZstdStream(ms, CompressionMode.Decompress, leaveOpen: true))
        {
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await dz.ReadAsync(buffer)) > 0)
            {
                await outMs.WriteAsync(buffer.AsMemory(0, read));
            }
        }
        var roundTrip = Encoding.UTF8.GetString(outMs.ToArray());
        Assert.Equal(original, roundTrip);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Cancellation")]
    [Trait(TestTraits.Mode, "Async")]
    public async Task CancellationDuringWriteAsync()
    {
        var data = new byte[1024 * 64];
        new Random(42).NextBytes(data);
        using var ms = new MemoryStream();
        using var cts = new CancellationTokenSource();
        await using var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await zs.WriteAsync(data, cts.Token));
    }
}

