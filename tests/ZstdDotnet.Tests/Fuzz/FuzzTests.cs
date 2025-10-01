namespace ZstdDotnet.Tests;

public class FuzzTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(32, 5)]
    [InlineData(1024, 20)]
    [InlineData(64_000, 10)]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Domain, "Decompression")]
    [Trait(TestTraits.Type, "Fuzz")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "RandomChunks")]
    public void Sync_Fuzz_RoundTrip(int maxSize, int rounds)
    {
        var rnd = new Random(1234 + maxSize);
        for (int i = 0; i < rounds; i++)
        {
            int len = maxSize == 0 ? 0 : rnd.Next(0, maxSize + 1);
            var data = new byte[len]; rnd.NextBytes(data);
            using var ms = new MemoryStream();
            using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                // random chunk writes
                int off = 0; while (off < data.Length) { int chunk = Math.Min(rnd.Next(1, 4096), data.Length - off); zs.Write(data, off, chunk); off += chunk; }
            }
            ms.Position = 0;
            using var ds = new ZstdStream(ms, CompressionMode.Decompress);
            var restored = new byte[data.Length];
            int total = 0; var temp = new byte[8192];
            int r; while ((r = ds.Read(temp, 0, temp.Length)) > 0) { Array.Copy(temp, 0, restored, total, r); total += r; }
            Assert.Equal(data.Length, total);
            Assert.True(data.AsSpan().SequenceEqual(restored));
        }
    }

    [Theory]
    [InlineData(16384, 5)]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Domain, "Decompression")]
    [Trait(TestTraits.Type, "Fuzz")]
    [Trait(TestTraits.Mode, "Async")]
    [Trait(TestTraits.Feature, "RandomChunks")]
    public async Task Async_Fuzz_RoundTrip(int maxSize, int rounds)
    {
        var rnd = new Random(777);
        for (int i = 0; i < rounds; i++)
        {
            int len = rnd.Next(0, maxSize + 1);
            var data = new byte[len]; rnd.NextBytes(data);
            using var ms = new MemoryStream();
            await using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                int off = 0; while (off < data.Length) { int chunk = Math.Min(rnd.Next(1, 5000), data.Length - off); await zs.WriteAsync(data.AsMemory(off, chunk)); off += chunk; }
                await zs.FlushAsync();
            }
            ms.Position = 0;
            using var outMs = new MemoryStream();
            await using (var ds = new ZstdStream(ms, CompressionMode.Decompress, leaveOpen: true))
            {
                var buf = new byte[4096]; int r; while ((r = await ds.ReadAsync(buf)) > 0) outMs.Write(buf, 0, r);
            }
            var restored = outMs.ToArray();
            Assert.Equal(data.Length, restored.Length);
            Assert.True(data.SequenceEqual(restored));
        }
    }
}
