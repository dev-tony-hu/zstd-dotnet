namespace ZstdDotnet;

/// <summary>
/// Provides async enumeration of decompressed frames from a concatenated zstd stream.
/// Each yielded item represents one full frame's decompressed payload plus metadata.
/// </summary>
public static class ZstdFrameDecoder
{
    /// <summary>
    /// Represents a fully decoded frame.
    /// </summary>
    public readonly record struct ZstdDecodedFrame(ZstdFrameInspector.ZstdFrameInfo Info, byte[] Data);

    /// <summary>
    /// Asynchronously enumerates frames from <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">Readable stream positioned at the start of the first frame.</param>
    /// <param name="leaveOpen">If true, underlying stream is left open after enumeration completes.</param>
    /// <param name="compressedBufferSize">Buffer size used for reading compressed data chunks.</param>
    /// <param name="initialFrameBufferSize">Initial allocation size for each frame's decompressed buffer (auto grows).</param>
    /// <param name="maxFrameSize">Optional safety limit; if a single frame's decompressed size exceeds this, an exception is thrown.</param>
    public static async IAsyncEnumerable<ZstdDecodedFrame> DecodeFramesAsync(
        Stream stream,
        bool leaveOpen = true,
        int compressedBufferSize = 128 * 1024,
        int initialFrameBufferSize = 256 * 1024,
        long? maxFrameSize = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new NotSupportedException("Stream must be readable");
        if (compressedBufferSize < 4096) throw new ArgumentOutOfRangeException(nameof(compressedBufferSize));
        if (initialFrameBufferSize < 1024) throw new ArgumentOutOfRangeException(nameof(initialFrameBufferSize));

        byte[] compBuf = ArrayPool<byte>.Shared.Rent(compressedBufferSize);
        byte[] frameBuf = ArrayPool<byte>.Shared.Rent(initialFrameBufferSize);
        var decoder = new ZstdDecoder();
        long globalOffset = 0; // compressed offset progression
        int compLen = 0; int compPos = 0;
        bool streamDepleted = false;
        try
        {
            while (true)
            {
                // Ensure we have compressed bytes to feed
                if (compPos >= compLen && !streamDepleted)
                {
                    compLen = await stream.ReadAsync(compBuf, 0, compBuf.Length, cancellationToken).ConfigureAwait(false);
                    compPos = 0;
                    streamDepleted = compLen == 0;
                }
                if (compPos >= compLen && streamDepleted) break; // end

                // Start decoding one frame
                int frameWrite = 0;
                ulong? headerContentSize = null;
                ulong frameCompressedConsumed = 0;
                ulong? windowSize = null; uint? dictId = null; bool checksum = false; string ftype = "unknown";
                bool frameDone = false;
                while (!frameDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (compPos >= compLen && !streamDepleted)
                    {
                        compLen = await stream.ReadAsync(compBuf, 0, compBuf.Length, cancellationToken).ConfigureAwait(false);
                        compPos = 0;
                        streamDepleted = compLen == 0;
                        if (compLen == 0 && frameWrite == 0) break;
                    }
                    int available = compLen - compPos;
                    if (available <= 0 && streamDepleted)
                        break;

                    // Perform one decode step (no span locals in async state machine)
                    var step = DecodeStep(decoder, compBuf, compPos, available, frameBuf, frameWrite, streamDepleted, maxFrameSize, compLen, frameCompressedConsumed, headerContentSize, windowSize, dictId, checksum, ftype);
                    compPos = step.compPos;
                    frameBuf = step.frameBuf;
                    frameWrite = step.frameWrite;
                    frameCompressedConsumed = step.frameCompressedConsumed;
                    headerContentSize = step.headerContentSize;
                    windowSize = step.windowSize;
                    dictId = step.dictId;
                    checksum = step.checksum;
                    ftype = step.ftype;
                    frameDone = step.frameDone;
                }
                if (frameWrite == 0 && frameDone == false) break; // nothing produced
                // Skip spurious empty frames (can arise from encoder finalization patterns producing
                // an empty member). We consider a frame spurious if no decompressed bytes were produced
                // AND either no compressed bytes were consumed or only a minimal header (< 8 bytes) was consumed.
                if (frameWrite == 0 && frameCompressedConsumed <= 8)
                {
                    globalOffset += (long)frameCompressedConsumed;
                    decoder.Reset();
                    continue; // do not yield
                }
                // Build frame info (compressed size unknown exactly unless we track end-of-frame offset = consumed). We tracked via frameCompressedConsumed.
                var info = new ZstdFrameInspector.ZstdFrameInfo(
                    Offset: (ulong)globalOffset,
                    CompressedSize: (ulong)frameCompressedConsumed,
                    ContentSize: headerContentSize,
                    WindowSize: windowSize,
                    DictionaryId: dictId,
                    HasChecksum: checksum,
                    FrameType: ftype);
                // Slice exact size
                byte[] exact = new byte[frameWrite];
                Buffer.BlockCopy(frameBuf, 0, exact, 0, frameWrite);
                globalOffset += (long)frameCompressedConsumed;
                decoder.Reset();
                yield return new ZstdDecodedFrame(info, exact);
            }
        }
        finally
        {
            if (!leaveOpen)
                await stream.DisposeAsync().ConfigureAwait(false);
            decoder.Dispose();
            ArrayPool<byte>.Shared.Return(compBuf);
            ArrayPool<byte>.Shared.Return(frameBuf);
        }
    }

    // Returns updated state after one decode iteration
    private static (int compPos, byte[] frameBuf, int frameWrite, ulong frameCompressedConsumed, ulong? headerContentSize, ulong? windowSize, uint? dictId, bool checksum, string ftype, bool frameDone) DecodeStep(
        ZstdDecoder decoder,
        byte[] compBuf,
        int compPos,
        int available,
        byte[] frameBuf,
        int frameWrite,
        bool finalBlock,
        long? maxFrameSize,
        int compLen,
        ulong frameCompressedConsumed,
        ulong? headerContentSize,
        ulong? windowSize,
        uint? dictId,
        bool checksum,
        string ftype)
    {
        // Create spans only inside this sync helper
        ReadOnlySpan<byte> inputSpan = new ReadOnlySpan<byte>(compBuf, compPos, available);
        if (frameWrite >= frameBuf.Length)
        {
            int newSize = frameBuf.Length * 2;
            if (maxFrameSize.HasValue && newSize > maxFrameSize.Value) throw new InvalidOperationException("Frame exceeds maxFrameSize");
            var newBuf = ArrayPool<byte>.Shared.Rent(newSize);
            Buffer.BlockCopy(frameBuf, 0, newBuf, 0, frameWrite);
            ArrayPool<byte>.Shared.Return(frameBuf);
            frameBuf = newBuf;
        }
        Span<byte> outSpan = frameBuf.AsSpan(frameWrite);
        bool frameFinished;
        var status = decoder.Decompress(inputSpan, outSpan, out int consumed, out int written, finalBlock, out frameFinished);
        if (consumed > 0)
        {
            compPos += consumed;
            frameCompressedConsumed += (ulong)consumed;
        }
        if (written > 0)
        {
            frameWrite += written;
            if (maxFrameSize.HasValue && frameWrite > maxFrameSize.Value)
                throw new InvalidOperationException("Frame decompressed size exceeded maxFrameSize");
        }
        if (headerContentSize is null)
        {
            int frameCompOffset = (int)(compPos - (long)frameCompressedConsumed);
            var window = new ReadOnlySpan<byte>(compBuf, frameCompOffset, Math.Min(consumed + 16, compLen - frameCompOffset));
            if (ZstdInterop.TryGetFrameHeader(window, out var hdr))
            {
                headerContentSize = hdr.frameContentSize == 0 ? null : hdr.frameContentSize;
                windowSize = hdr.windowSize == 0 ? null : hdr.windowSize;
                dictId = hdr.dictID == 0 ? null : hdr.dictID;
                checksum = hdr.checksumFlag != 0;
                ftype = hdr.frameType == ZSTD_frameType_e.ZSTD_skippableFrame ? "skippable" : "frame";
            }
        }
        // If destination too small we treat it similarly to Done for frame accumulation; caller loop will continue.
        bool fd = frameFinished;
        return (compPos, frameBuf, frameWrite, frameCompressedConsumed, headerContentSize, windowSize, dictId, checksum, ftype, fd);
    }
}
