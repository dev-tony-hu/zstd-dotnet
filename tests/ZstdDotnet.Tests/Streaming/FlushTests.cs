namespace ZstdDotnet.Tests;

public class FlushTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Flush")]
    [Trait(TestTraits.Mode, "Sync")]
    public void PartialFlush_DoesNotRequireFinalFrame()
    {
        var part1 = Encoding.UTF8.GetBytes("Hello ");
        var part2 = Encoding.UTF8.GetBytes("World And Some More Data");
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(part1, 0, part1.Length);
            // Flush pending part1 without ending frame
            zs.Flush();
            zs.Write(part2, 0, part2.Length);
        }
        // Now decompress and ensure full message intact
        ms.Position = 0;
        using var ds = new ZstdStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        var buf = new byte[128]; int r; while ((r = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, r);
        var result = Encoding.UTF8.GetString(outMs.ToArray());
        Assert.Equal("Hello World And Some More Data", result);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Flush")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "FlushFrameBoundary")]
    public void FlushFrame_ProducesTwoFrames()
    {
        var first = Enumerable.Range(0, 5000).Select(i => (byte)(i % 256)).ToArray();
        var second = Enumerable.Range(0, 3000).Select(i => (byte)((i * 7) % 256)).ToArray();
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(first, 0, first.Length);
            zs.FlushFrame(); // end frame 1
            zs.Write(second, 0, second.Length);
        }
        ms.Position = 0;
        // Decompress sequentially; consumer sees concatenated payloads
        using var ds = new ZstdStream(ms, CompressionMode.Decompress);
        var restored = new byte[first.Length + second.Length];
        int total = 0; var tmp = new byte[4096]; int read;
        while ((read = ds.Read(tmp, 0, tmp.Length)) > 0)
        { Array.Copy(tmp, 0, restored, total, read); total += read; }
        Assert.Equal(restored.Length, total);
        Assert.True(first.AsSpan().SequenceEqual(restored.AsSpan(0, first.Length)));
        Assert.True(second.AsSpan().SequenceEqual(restored.AsSpan(first.Length, second.Length)));
    }

    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Flush")]
    [Trait(TestTraits.Mode, "Async")]
    public async Task FlushAsync_BehavesLikeSync()
    {
        var text = string.Join('-', Enumerable.Range(0, 1000).Select(i => $"L{i:000}"));
        var bytes = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        await using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            await zs.WriteAsync(bytes);
            await zs.FlushAsync();
        }
        ms.Position = 0;
        using var outMs = new MemoryStream();
        await using (var ds = new ZstdStream(ms, CompressionMode.Decompress, leaveOpen: true))
        {
            var buf = new byte[2048]; int r; while ((r = await ds.ReadAsync(buf)) > 0) outMs.Write(buf, 0, r);
        }
        var roundTrip = Encoding.UTF8.GetString(outMs.ToArray());
        Assert.Equal(text, roundTrip);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Streaming")]
    [Trait(TestTraits.Type, "Flush")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "SpanFlushLoop")]
    public void FlushSpan_SmallBuffer_LoopsUntilDone()
    {
        var data = new byte[50_000]; new Random(123).NextBytes(data);
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(data, 0, data.Length);
            // Use small destination buffers to drain flush output
            Span<byte> scratch = stackalloc byte[256];
            while (true)
            {
                var status = zs.Flush(scratch, out int written);
                if (written > 0)
                    ms.Write(scratch[..written]);
                if (status == OperationStatus.Done)
                    break;
                Assert.Equal(OperationStatus.DestinationTooSmall, status);
            }
        }
        ms.Position = 0;
        using var ds = new ZstdStream(ms, CompressionMode.Decompress);
        var restored = new byte[data.Length]; int total = 0; var tmpBuf = new byte[4096]; int rr;
        while ((rr = ds.Read(tmpBuf, 0, tmpBuf.Length)) > 0) { Array.Copy(tmpBuf, 0, restored, total, rr); total += rr; }
        Assert.Equal(data.Length, total);
        Assert.True(data.AsSpan().SequenceEqual(restored));
    }
}
