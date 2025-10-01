namespace ZstdDotnet.Tests;

public class DecompressionEdgeTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Decompression")]
    [Trait(TestTraits.Type, "Edge")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "SmallBufferLoop")]
    public void SmallReadBuffer_LoopsCorrectly()
    {
        var text = string.Join(',', Enumerable.Repeat("fragment", 200));
        var src = Encoding.UTF8.GetBytes(text);
        using var comp = new MemoryStream();
        using (var zs = new ZstdStream(comp, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(src, 0, src.Length);
        }
        comp.Position = 0;
        using var ds = new ZstdStream(comp, CompressionMode.Decompress);
        var outMs = new MemoryStream();
        var tiny = new byte[17];
        int r; while ((r = ds.Read(tiny, 0, tiny.Length)) > 0) outMs.Write(tiny, 0, r);
        var restored = outMs.ToArray();
        Assert.True(src.SequenceEqual(restored));
    }

    [Fact]
    [Trait(TestTraits.Domain, "Decompression")]
    [Trait(TestTraits.Type, "MultiFrame")]
    [Trait(TestTraits.Mode, "Sync")]
    public void MultipleFrames_SequentialDecode()
    {
        using var concat = new MemoryStream();
        for (int i = 0; i < 4; i++)
        {
            var bytes = Encoding.ASCII.GetBytes(new string((char)('A' + i), 1000));
            using (var zs = new ZstdStream(concat, CompressionMode.Compress, leaveOpen: true))
            {
                zs.Write(bytes, 0, bytes.Length);
            }
        }
        concat.Position = 0;
        using var ds = new ZstdStream(concat, CompressionMode.Decompress);
        var outBytes = new MemoryStream();
        var buf = new byte[256];
        int r2; while ((r2 = ds.Read(buf, 0, buf.Length)) > 0) outBytes.Write(buf, 0, r2);
        var data = outBytes.ToArray();
        Assert.Equal(4000, data.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.True(data.Skip(i * 1000).Take(1000).All(c => c == (byte)('A' + i)));
        }
    }

    [Fact]
    [Trait(TestTraits.Domain, "Decompression")]
    [Trait(TestTraits.Type, "Truncation")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "HalfFrame")]
    public void TruncatedData_ReadStopsGracefully()
    {
        using var full = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes(string.Join(' ', Enumerable.Range(0, 200).Select(i => i.ToString())));
        using (var zs = new ZstdStream(full, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(payload, 0, payload.Length);
        }
        var all = full.ToArray();
        var truncated = all.Take(all.Length / 2).ToArray();
        using var truncatedMs = new MemoryStream(truncated);
        using var ds2 = new ZstdStream(truncatedMs, CompressionMode.Decompress);
        var buf2 = new byte[4096];
        int r3 = ds2.Read(buf2, 0, buf2.Length);
        Assert.True(r3 >= 0);
    }
}
