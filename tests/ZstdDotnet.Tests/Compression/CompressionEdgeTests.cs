namespace ZstdDotnet.Tests;

public class CompressionEdgeTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "Edge")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Size, "Empty")]
    public void EmptyInput_CompressDecompress()
    {
        var src = Array.Empty<byte>();
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true)) { zs.Flush(); }
        ms.Position = 0;
        using var dz = new ZstdStream(ms, CompressionMode.Decompress);
        var buf = new byte[1];
        int r = dz.Read(buf, 0, 1);
        Assert.Equal(0, r);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "Edge")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "ManySmallWrites")]
    public void SingleByteWrites()
    {
        var text = "SingleByteWrites-Test-Data";
        var bytes = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            foreach (var b in bytes)
            {
                var one = new byte[] { b };
                zs.Write(one, 0, 1);
            }
        }
        ms.Position = 0;
        using var ds = new ZstdStream(ms, CompressionMode.Decompress);
        var outBuf = new byte[bytes.Length];
        int read = ds.Read(outBuf, 0, outBuf.Length);
        Assert.Equal(bytes.Length, read);
        Assert.True(bytes.AsSpan().SequenceEqual(outBuf));
    }

    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "Stress")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Size, "Large")]
    [Trait(TestTraits.Feature, "IrregularChunks")]
    public void LargeData_MultiChunk()
    {
        int size = 2 * 1024 * 1024 + 123; // > 2MB non aligned
        var data = new byte[size];
        new Random(123).NextBytes(data);
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int chunk = Math.Min(13_117, data.Length - offset); // odd chunk
                zs.Write(data, offset, chunk);
                offset += chunk;
            }
        }
        ms.Position = 0;
        using var ds = new ZstdStream(ms, CompressionMode.Decompress);
        var restored = new byte[data.Length];
        int total = 0; var tmp = new byte[8192];
        int r; while ((r = ds.Read(tmp, 0, tmp.Length)) > 0) { Array.Copy(tmp, 0, restored, total, r); total += r; }
        Assert.Equal(data.Length, total);
        Assert.True(data.AsSpan().SequenceEqual(restored));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "Config")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "Level")]
    public void CompressionLevel_SetBeforeUse(int level)
    {
        var payload = Encoding.ASCII.GetBytes("level-payload");
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.CompressionLevel = level;
            zs.Write(payload, 0, payload.Length);
        }
        Assert.True(ms.Length > 0);
    }
}
