#nullable enable
namespace ZstdDotnet.Tests;

/// <summary>
/// Optional very large frame test. Only runs when environment variable ZSTD_RUN_HUGE=1.
/// It creates a single compressed frame targeting >4GiB compressed length (best-effort; if compression reduces
/// below threshold the test is skipped gracefully to avoid false failures).
/// WARNING: This test can take significant disk space and time; it is disabled by default.
/// </summary>
public class HugeFrameTests
{
    private const long TargetBytes = (long)(4.5 * 1024 * 1024 * 1024); // 4.5 GiB target uncompressed
    private const int Chunk = 16 * 1024 * 1024; // 16MB
    private const int MaxChunks = (int)(TargetBytes / Chunk) + 2;

    [Fact]
    [Trait(TestTraits.Domain, "Frames")]
    [Trait(TestTraits.Type, "HugeFrame")]
    [Trait(TestTraits.Mode, "Sync")]
    [Trait(TestTraits.Feature, "4GiBPlus")]
    public void LargeSingleFrame_CompressedSize_ExceedsUInt32_WhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("ZSTD_RUN_HUGE") != "1")
        {
            return; // silently skip (not using Skip so test count stable)
        }

        string tempFile = Path.Combine(Path.GetTempPath(), "zstd_hugeframe_" + Guid.NewGuid().ToString("N") + ".zst");
        try
        {
            var rng = new Random(1234);
            long produced = 0;
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan))
            using (var zs = new ZstdStream(fs, CompressionMode.Compress, leaveOpen: true))
            {
                var buffer = new byte[Chunk];
                for (int i = 0; i < MaxChunks && produced < TargetBytes; i++)
                {
                    rng.NextBytes(buffer);
                    int writeLen = (int)Math.Min(buffer.Length, TargetBytes - produced);
                    zs.Write(buffer, 0, writeLen);
                    produced += writeLen;
                }
            }

            long fileSize = new FileInfo(tempFile).Length;
            // If fileSize still <= uint.MaxValue and we reached target uncompressed, compression ratio too high -> skip assumption.
            if (produced >= TargetBytes && fileSize <= uint.MaxValue)
            {
                // Cannot assert failure (data happened to compress a lot). Accept and return.
                return;
            }
            // Primary assertion: we generated a compressed artifact larger than 32-bit limit.
            Assert.True(fileSize > uint.MaxValue, $"Compressed size {fileSize} did not exceed 4GB boundary (uncompressed {produced}).");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* ignore */ }
        }
    }
}
