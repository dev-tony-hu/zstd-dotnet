namespace ZstdDotnet.Tests;

/// <summary>
/// Centralized trait keys to avoid typos when categorizing tests.
/// </summary>
public static class TestTraits
{
    public const string Domain = "Domain";      // Compression, Decompression, Frames, Streaming, Lifecycle, Fuzz, Metadata
    public const string Type = "Type";          // Happy, Edge, Error, Fuzz, Stress, Config, Metadata, Truncation
    public const string Mode = "Mode";          // Sync, Async, MultiFrame, SingleFrame
    public const string Size = "Size";          // Empty, Tiny, Small, Medium, Large, Huge
    public const string Feature = "Feature";    // Skippable, FlushFrame, Level, Truncated, RandomChunks, ManualHeader
}
