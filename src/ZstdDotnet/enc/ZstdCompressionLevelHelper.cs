namespace ZstdDotnet;

internal static class ZstdCompressionLevelHelper
{
    internal const int DefaultLevel = 5;

    internal static int GetLevelFromCompressionLevel(CompressionLevel compressionLevel) => compressionLevel switch
    {
        CompressionLevel.NoCompression => ZstdProperties.MinCompressionLevel,
        CompressionLevel.Fastest      => ZstdProperties.MinCompressionLevel,
        CompressionLevel.Optimal      => DefaultLevel,
        CompressionLevel.SmallestSize => ZstdProperties.MaxCompressionLevel,
        _ => throw new ArgumentOutOfRangeException(nameof(compressionLevel))
    };

    internal static void ValidateLevel(int level, string? paramName = null)
    {
        if (level < ZstdProperties.MinCompressionLevel || level > ZstdProperties.MaxCompressionLevel)
            throw new ArgumentOutOfRangeException(paramName ?? nameof(level), $"Level must be between {ZstdProperties.MinCompressionLevel} and {ZstdProperties.MaxCompressionLevel}.");
    }
}
