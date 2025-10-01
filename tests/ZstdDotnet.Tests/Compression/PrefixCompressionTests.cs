using System.Globalization;

namespace ZstdDotnet.Tests;

public class PrefixCompressionTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "Prefix")]
    [Trait(TestTraits.Mode, "Sync")]
    public void PrefixImprovesOrMatchesSize()
    {
        // Construct a payload whose first N bytes match the prefix and then diverges with repeating pattern
        var prefix = Encoding.ASCII.GetBytes("HEADER-1234567890-ABCDEFG");
        var builder = new ArrayBufferWriter<byte>();
        builder.Write(prefix);
        // Append many blocks that partially repeat prefix segments to allow zstd to leverage prefix literals
        for (int i = 0; i < 200; i++)
        {
            builder.Write(Encoding.ASCII.GetBytes("HEADER-1234-"));
            builder.Write(Encoding.ASCII.GetBytes(i.ToString(CultureInfo.InvariantCulture)));
            builder.Write(Encoding.ASCII.GetBytes("-XYZ-"));
        }
        var payload = builder.WrittenSpan.ToArray();

        // Baseline compression
        byte[] baseline;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                zs.Write(payload, 0, payload.Length);
            }
            baseline = ms.ToArray();
        }

        // Prefix compression
        byte[] withPrefix;
        using (var ms = new MemoryStream())
        {
            using (var encoder = new ZstdEncoder())
            {
                encoder.SetPrefix(prefix.AsMemory());
                // Stream manually using encoder to mirror low-level path
                byte[] outBufArray = new byte[1024];
                Span<byte> outBuf = outBufArray;
                int consumed = 0;
                while (consumed < payload.Length)
                {
                    var src = payload.AsSpan(consumed, Math.Min(64, payload.Length - consumed));
                    bool final = consumed + src.Length == payload.Length;
                    var status = encoder.Compress(src, outBuf, out int c, out int w, final);
                    consumed += c;
                    if (w > 0) ms.Write(outBuf[..w]);
                    if (status == OperationStatus.DestinationTooSmall)
                    {
                        // enlarge temp buffer (rare for our chosen size) and continue same loop iteration
                        outBufArray = new byte[2048];
                        outBuf = outBufArray;
                    }
                }
            }
            withPrefix = ms.ToArray();
        }

        // Prefix should not worsen compression; often improves
        Assert.True(withPrefix.Length <= baseline.Length, $"Prefix produced larger output: baseline={baseline.Length} prefix={withPrefix.Length}");
    }

    [Fact]
    [Trait(TestTraits.Domain, "Compression")]
    [Trait(TestTraits.Type, "Prefix")]
    public void SetPrefix_AfterInit_Throws()
    {
        using var encoder = new ZstdEncoder();
        // Perform an initial compression to trigger initialization
        Span<byte> dst = stackalloc byte[128];
        var src = Encoding.ASCII.GetBytes("hello world");
        encoder.Compress(src, dst, out var c, out var w, isFinalBlock: false);
    Assert.Throws<InvalidOperationException>(() => encoder.SetPrefix(src.AsMemory()));
    }
}
