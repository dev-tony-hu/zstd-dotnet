namespace ZstdDotnet.Tests;

public class DecoderStatusTests
{
    private static byte[] CompressSingle(byte[] input)
    {
        using var ms = new MemoryStream();
        using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(input, 0, input.Length);
        }
        return ms.ToArray();
    }

    [Fact]
    [Trait(TestTraits.Domain, "Decoder")]
    [Trait(TestTraits.Type, "DestinationTooSmallLoop")]
    [Trait(TestTraits.Mode, "Sync")]
    public void DestinationTooSmall_ThenDone()
    {
        var original = Enumerable.Range(0, 500).Select(i => (byte)(i % 256)).ToArray();
        var compressed = CompressSingle(original);
        using var decoder = new ZstdDecoder();
        var output = new byte[original.Length];
        int produced = 0; int consumed = 0; int srcOffset = 0;
        // Use a tiny destination so we force DestinationTooSmall
        Span<byte> tiny = output.AsSpan();
        var src = compressed.AsSpan();
        while (produced < original.Length)
        {
            var destSlice = tiny.Slice(produced, Math.Min(17, output.Length - produced));
            var srcSlice = src.Slice(srcOffset);
            var status = decoder.Decompress(srcSlice, destSlice, out int bytesConsumed, out int bytesWritten, isFinalBlock: true, out bool frameFinished);
            produced += bytesWritten;
            srcOffset += bytesConsumed;
            consumed += bytesConsumed;
            if (status == OperationStatus.DestinationTooSmall)
            {
                Assert.True(bytesWritten == destSlice.Length);
                continue; // loop for remaining output
            }
            if (status == OperationStatus.Done && frameFinished && produced == original.Length)
            {
                break;
            }
            if (status == OperationStatus.NeedMoreData)
            {
                // Should not generally happen here because we pass full compressed data as final block.
                continue;
            }
            Assert.True(status == OperationStatus.Done || status == OperationStatus.DestinationTooSmall);
        }
        Assert.Equal(original.Length, produced);
        Assert.True(original.AsSpan().SequenceEqual(output));
    }

    [Fact]
    [Trait(TestTraits.Domain, "Decoder")]
    [Trait(TestTraits.Type, "NeedMoreData")]
    [Trait(TestTraits.Mode, "Sync")]
    public void NeedMoreData_WhenNotFinal()
    {
        var text = Encoding.UTF8.GetBytes(string.Join(',', Enumerable.Range(0, 200).Select(i => i.ToString())));
        var compressed = CompressSingle(text);
        using var decoder = new ZstdDecoder();
        // Feed compressed in two pieces to elicit NeedMoreData after first part fully consumed.
        var firstHalf = compressed.AsSpan(0, compressed.Length / 2);
        var secondHalf = compressed.AsSpan(firstHalf.Length);
        Span<byte> outBuf = stackalloc byte[1024];
        var status1 = decoder.Decompress(firstHalf, outBuf, out int c1, out int w1, isFinalBlock: false, out bool frameFinished1);
        Assert.True(c1 == firstHalf.Length);
        Assert.False(frameFinished1);
        Assert.True(status1 == OperationStatus.Done || status1 == OperationStatus.NeedMoreData);
        var status2 = decoder.Decompress(secondHalf, outBuf, out int c2, out int w2, isFinalBlock: true, out bool frameFinished2);
        Assert.Equal(secondHalf.Length, c2);
        Assert.True(frameFinished2);
        Assert.Equal(OperationStatus.Done, status2);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Decoder")]
    [Trait(TestTraits.Type, "MultiFrameStatus")]
    [Trait(TestTraits.Mode, "Sync")]
    public void MultiFrame_StatusFrameFinishedTrue()
    {
        using var concat = new MemoryStream();
        for (int i = 0; i < 3; i++)
        {
            var payload = Encoding.ASCII.GetBytes(new string((char)('A' + i), 1000));
            using (var zs = new ZstdStream(concat, CompressionMode.Compress, leaveOpen: true))
            {
                zs.Write(payload, 0, payload.Length);
            }
        }
        var all = concat.ToArray();
        using var decoder = new ZstdDecoder();
        int consumed = 0;
        int frames = 0;
        Span<byte> outBuf = stackalloc byte[2048];
        while (consumed < all.Length)
        {
            var srcSlice = all.AsSpan(consumed, Math.Min(64, all.Length - consumed));
            var status = decoder.Decompress(srcSlice, outBuf, out int c, out int w, isFinalBlock: false, out bool frameFinished);
            consumed += c;
            if (frameFinished)
                frames++;
            if (status == OperationStatus.NeedMoreData && c == srcSlice.Length && consumed < all.Length)
                continue;
            if (status == OperationStatus.DestinationTooSmall)
                continue; // drain output next loop chunk
        }
        // Final flush with isFinalBlock to ensure last frame accounted if boundary ended exactly at chunk end
        if (consumed == all.Length)
        {
            var empty = ReadOnlySpan<byte>.Empty;
            var finalStatus = decoder.Decompress(empty, outBuf, out int c3, out int w3, isFinalBlock: true, out bool frameFinished3);
            if (frameFinished3) frames++;
            Assert.True(finalStatus == OperationStatus.Done || finalStatus == OperationStatus.NeedMoreData);
        }
        Assert.Equal(3, frames);
    }
    // Additional tests for static decoder APIs
    public class StaticDecoderTests
    {
        [Fact]
        [Trait(TestTraits.Domain, "Decoder")]
        [Trait(TestTraits.Type, "StaticAPI")]
        [Trait(TestTraits.Mode, "Sync")]
        public void TryDecompress_Succeeds_Roundtrip()
        {
            // Arrange
            var raw = System.Text.Encoding.UTF8.GetBytes("Hello Static Decoder ✅");
                var compressed = ZstdEncoder.Compress(raw, level: 3);
            var tmp = new byte[raw.Length];

            // Act
            bool ok = ZstdDecoder.TryDecompress(compressed, tmp, out int written);

            // Assert
            Assert.True(ok);
            Assert.Equal(raw.Length, written);
            Assert.Equal(raw, tmp);
        }

        [Fact]
        [Trait(TestTraits.Domain, "Decoder")]
        [Trait(TestTraits.Type, "StaticAPI")]
        [Trait(TestTraits.Mode, "Sync")]
        public void TryDecompress_DestinationTooSmall_ReturnsFalse()
        {
            var raw = System.Text.Encoding.UTF8.GetBytes("DataTooSmall");
                var compressed = ZstdEncoder.Compress(raw, level: 3);
            var tiny = new byte[2];
            bool ok = ZstdDecoder.TryDecompress(compressed, tiny, out int written);
            Assert.False(ok);
            // Allow partial write when destination too small
            Assert.InRange(written, 0, tiny.Length);
        }

        [Fact]
        [Trait(TestTraits.Domain, "Decoder")]
        [Trait(TestTraits.Type, "StaticAPI")]
        [Trait(TestTraits.Mode, "Sync")]
        public void Decompress_AutoGrowth_Works()
        {
            var raw = new byte[1024];
            new Random(123).NextBytes(raw);
                var compressed = ZstdEncoder.Compress(raw, level: 5);

            // Give a deliberately too-small expected size to force growth loop
            var result = ZstdDecoder.Decompress(compressed, expectedSize: 16);
            Assert.Equal(raw, result);
        }

        [Fact]
        [Trait(TestTraits.Domain, "Decoder")]
        [Trait(TestTraits.Type, "StaticAPI")]
        [Trait(TestTraits.Mode, "Sync")]
        public void Decompress_InvalidData_Throws()
        {
            var invalid = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            Assert.Throws<IOException>(() => ZstdDecoder.Decompress(invalid, expectedSize: 16));
        }
    }
}
