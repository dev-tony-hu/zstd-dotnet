namespace ZstdDotnet.Tests;

public class ZstdBasicTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "RoundTrip")]
    [Trait(TestTraits.Mode, "Sync")]
    public void RoundTrip_SingleFrame_VariousLevels(int level)
    {
        var original = Encoding.UTF8.GetBytes(string.Join(',', Enumerable.Repeat("hello zstd", 100)));
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.CompressionLevel = level;
            zs.Write(original, 0, original.Length);
            zs.Flush();
        }
        ms.Position = 0;
        using var ds = new ZstdStream(ms, CompressionMode.Decompress);
        var round = new byte[original.Length];
        int read = ds.Read(round, 0, round.Length);
        Assert.Equal(original.Length, read);
        Assert.True(original.AsSpan().SequenceEqual(round));
    }

    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "MultiFrame")]
    [Trait(TestTraits.Mode, "Sync")]
    public void MultiFrame_Decode_AllFrames()
    {
        var frames = Enumerable.Range(0, 3).Select(i => Encoding.UTF8.GetBytes(new string((char)('A' + i), 4096))).ToArray();
        using var concat = new MemoryStream();
        foreach (var f in frames)
        {
            using (var frameStream = new ZstdStream(concat, CompressionMode.Compress, leaveOpen: true))
            {
                frameStream.Write(f, 0, f.Length);
                // Rely on Dispose to finalize frame (endStream) ensuring clean frame boundary.
            }
        }
        concat.Position = 0;
        using var dec = new ZstdStream(concat, CompressionMode.Decompress);
        var output = new MemoryStream();
        var buffer = new byte[8192];
        int r;
        while ((r = dec.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, r);
        var combined = output.ToArray();
        var expected = frames.SelectMany(b => b).ToArray();
        Assert.Equal(expected.Length, combined.Length);
        Assert.True(expected.SequenceEqual(combined));
    }

    [Fact]
    [Trait(TestTraits.Domain, "Encoder")]
    [Trait(TestTraits.Type, "Reset")]
    [Trait(TestTraits.Mode, "Sync")]
    public void Encoder_Reset_ReusesContext()
    {
        var encoder = new ZstdEncoder(3);
        var src = Encoding.ASCII.GetBytes("payload-one-payload-one-payload-one");
        Span<byte> compressed = stackalloc byte[256];
        var status1 = encoder.Compress(src, compressed, out _, out int written, isFinalBlock: true);
        Assert.Equal(System.Buffers.OperationStatus.Done, status1);
        Assert.True(written > 0);
        encoder.Reset();
        encoder.SetCompressionLevel(4);
        var src2 = Encoding.ASCII.GetBytes("payload-two-payload-two-payload-two");
        Span<byte> compressed2 = stackalloc byte[256];
        var status2 = encoder.Compress(src2, compressed2, out _, out int written2, isFinalBlock: true);
        Assert.Equal(System.Buffers.OperationStatus.Done, status2);
        Assert.True(written2 > 0);
        encoder.Dispose();
    }

    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "RoundTrip")]
    [Trait(TestTraits.Mode, "Async")]
    public async Task Async_RoundTrip()
    {
        var original = Encoding.UTF8.GetBytes(string.Join('-', Enumerable.Repeat("asyncdata", 200)));
        using var ms = new MemoryStream();
        await using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            await zs.WriteAsync(original, 0, original.Length);
            await zs.FlushAsync();
        }
        ms.Position = 0;
        await using var ds = new ZstdStream(ms, CompressionMode.Decompress);
        var dest = new byte[original.Length];
        int total = 0;
        while (total < dest.Length)
        {
            int r = await ds.ReadAsync(dest, total, dest.Length - total);
            if (r == 0) break;
            total += r;
        }
        Assert.Equal(original.Length, total);
        Assert.True(original.AsSpan().SequenceEqual(dest));
    }
}