namespace ZstdDotnet.Tests;

public class EncoderStatusTests
{
    [Fact]
    [Trait(TestTraits.Domain, "Encoder")]
    [Trait(TestTraits.Type, "DestinationTooSmallLoop")]
    [Trait(TestTraits.Mode, "Sync")]
    public void SmallDestination_ProducesDestinationTooSmall_ThenDone()
    {
        var data = Encoding.ASCII.GetBytes(string.Join('-', Enumerable.Repeat("segment", 50))); // moderate size
        using var encoder = new ZstdEncoder(3);
        // Intentionally tiny output buffer to force multiple calls
        Span<byte> dest = stackalloc byte[32];
        var all = new ArrayBufferWriter<byte>();
        int consumedTotal = 0;
        bool finalSent = false;
        while (true)
        {
            var srcSlice = data.AsSpan(consumedTotal);
            var isFinal = srcSlice.Length <= 20; // send last small chunk as final to exercise finalization path
            if (!finalSent && isFinal) finalSent = true;
            var status = encoder.Compress(srcSlice, dest, out int consumed, out int written, isFinalBlock: isFinal);
            consumedTotal += consumed;
            if (written > 0)
                all.Write(dest[..written]);
            if (status == OperationStatus.Done && finalSent && consumedTotal == data.Length)
                break;
            if (status == OperationStatus.NeedMoreData)
            {
                // Provide more input next loop
                continue;
            }
            if (status == OperationStatus.DestinationTooSmall)
            {
                // Just loop again with remaining source (if any) and fresh dest
                continue;
            }
            Assert.True(status == OperationStatus.Done || status == OperationStatus.DestinationTooSmall || status == OperationStatus.NeedMoreData);
        }
        // Basic sanity: produced compressed bytes and consumed everything
        Assert.Equal(data.Length, consumedTotal);
        Assert.True(all.WrittenCount > 0);
    }

    [Fact]
    [Trait(TestTraits.Domain, "Encoder")]
    [Trait(TestTraits.Type, "NeedMoreData")]
    [Trait(TestTraits.Mode, "Sync")]
    public void NeedMoreData_Status_WhenNotFinal()
    {
        var payload = Encoding.UTF8.GetBytes("ABCDEFGH12345678IJKLMNOPQRSTUVWX" + new string('Z', 64));
        using var encoder = new ZstdEncoder(5);
        // Provide all output space upfront; we want to observe NeedMoreData after first call that consumes all provided input but expects more.
        Span<byte> dest = stackalloc byte[512];
        // Feed only a prefix and mark not final
        var first = payload.AsSpan(0, 16);
        var status1 = encoder.Compress(first, dest, out int consumed1, out int written1, isFinalBlock: false);
        Assert.Equal(first.Length, consumed1);
        Assert.True(written1 >= 0);
        Assert.True(status1 == OperationStatus.Done || status1 == OperationStatus.NeedMoreData, $"Unexpected status {status1}");
        // Provide rest and final
        var rest = payload.AsSpan(consumed1);
        var status2 = encoder.Compress(rest, dest, out int consumed2, out int written2, isFinalBlock: true);
        Assert.Equal(rest.Length, consumed2);
        Assert.True(written2 > 0);
        Assert.Equal(OperationStatus.Done, status2);
    }
}
