using System.Collections.Concurrent;

namespace ZstdDotnet;

/// <summary>
/// Simple thread-safe pool for ZstdDecoder instances to reduce allocation and native context churn.
/// </summary>
public static class ZstdDecoderPool
{
    private static readonly ConcurrentBag<ZstdDecoder> pool = new();
    private const int MaxRetained = 32;

    public static ZstdDecoder Rent()
    {
        if (pool.TryTake(out var d)) return d;
        return new ZstdDecoder();
    }

    public static void Return(ZstdDecoder decoder)
    {
        if (decoder == null) return;
        try
        {
            decoder.Reset();
            // Clear any user-set window limit so next renter can configure; internal field not accessible -> new instance if customization needed.
        }
        catch
        {
            // If reset fails, discard
            decoder.Dispose();
            return;
        }
        if (pool.Count >= MaxRetained)
        {
            decoder.Dispose();
            return;
        }
        pool.Add(decoder);
    }
}
