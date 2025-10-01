namespace ZstdDotnet;

public static class ZstdProperties
{
    public static Version LibraryVersion => _version.Value;
    public static int MaxCompressionLevel => _maxCompressionLevel.Value;
    public static int MinCompressionLevel => 1; // ZSTD 通常定义最小等级为 1
    public static bool SupportsReset => ZstdInterop.SupportsCStreamReset; // >= 1.4.0

    private static readonly Lazy<Version> _version = new(() =>
    {
        var version = (int)ZstdInterop.ZSTD_versionNumber();
        return new Version((version / 10000) % 100, (version / 100) % 100, version % 100);
    });

    private static readonly Lazy<int> _maxCompressionLevel = new(() => ZstdInterop.ZSTD_maxCLevel());
}
