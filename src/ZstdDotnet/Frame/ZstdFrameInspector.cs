namespace ZstdDotnet;

/// <summary>
/// Utility for enumerating concatenated zstd frames (compressed members) without fully decompressing.
/// </summary>
public static class ZstdFrameInspector
{
    /// <summary>
    /// Metadata about a single frame inside a concatenated zstd stream.
    /// </summary>
    public readonly record struct ZstdFrameInfo(
        ulong Offset,
        ulong CompressedSize,
        ulong? ContentSize,
        ulong? WindowSize,
        uint? DictionaryId,
        bool HasChecksum,
        string FrameType);

    /// <summary>
    /// Enumerate frames in a concatenated zstd blob by walking successive compressed frame sizes.
    /// Does NOT validate full decompressed content; only size boundaries. Throws if any frame header is invalid.
    /// </summary>
    /// <param name="data">All concatenated frame bytes.</param>
    /// <returns>List of <see cref="ZstdFrameInfo"/> entries in order.</returns>
    public static IReadOnlyList<ZstdFrameInfo> EnumerateFrames(ReadOnlySpan<byte> data)
    {
        var list = new List<ZstdFrameInfo>(4);
        ulong offset = 0;
        while (offset < (ulong)data.Length)
        {
            var remaining = data[(int)offset..];
            ulong frameSize = ZstdInterop.FindFrameCompressedSize(remaining);
            // For content size we only need header prefix; pass the whole remaining span (cheap) for now.
            var contentSize = ZstdInterop.GetFrameContentSize(remaining);
            ulong? window = null; uint? dictId = null; bool checksum = false; string ftype = "unknown";
            if (ZstdInterop.TryGetFrameHeader(remaining, out var hdr))
            {
                window = hdr.windowSize == 0 ? null : hdr.windowSize;
                dictId = hdr.dictID == 0 ? null : hdr.dictID;
                checksum = hdr.checksumFlag != 0;
                ftype = hdr.frameType == ZSTD_frameType_e.ZSTD_skippableFrame ? "skippable" : "frame";
            }
            list.Add(new ZstdFrameInfo(offset, frameSize, contentSize, window, dictId, checksum, ftype));
            offset += frameSize;
        }
        return list;
    }

    /// <summary>
    /// Incrementally enumerate frames from a <see cref="Stream"/> without loading the entire payload in memory.
    /// The stream must be seekable OR fully readable; for non-seekable streams data is buffered internally.
    /// </summary>
    /// <param name="stream">Input stream positioned at start of first frame.</param>
    /// <param name="chunkSize">Read buffer size (default 64KB). Larger reduces boundary checks, smaller lowers memory usage.</param>
    /// <returns>Sequence of frame infos.</returns>
    public static IEnumerable<ZstdFrameInfo> EnumerateFrames(Stream stream, int chunkSize = 64 * 1024)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (chunkSize < 1024) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        // Strategy: read growing window; when we can determine compressed frame size, yield and discard prefix.
        var buffer = new List<byte>(chunkSize * 2);
        ulong globalOffset = 0;
        int read;
        Span<byte> temp = stackalloc byte[0]; // marker; dynamic list stores actual data
        while (true)
        {
            // Ensure we have at least some bytes to attempt size detection
            if (buffer.Count < 4)
            {
                read = Fill(stream, buffer, chunkSize);
                if (read == 0) break; // no more data
            }
            // Try to find full frame size given current buffer
            if (buffer.Count == 0) break;
            ulong frameSize;
            try
            {
                frameSize = ZstdInterop.FindFrameCompressedSize(GetBufferSpan(buffer));
            }
            catch (IOException)
            {
                // Need more data (incomplete frame header/body) -> attempt to read more
                read = Fill(stream, buffer, chunkSize);
                if (read == 0) throw; // no progress -> propagate
                continue;
            }
            if (frameSize > (ulong)buffer.Count)
            {
                // Not enough yet; read more
                read = Fill(stream, buffer, chunkSize);
                if (read == 0) throw new IOException("Unexpected EOF inside frame");
                continue;
            }
            // We have a full frame in first frameSize bytes
            var span = GetBufferSpan(buffer);
            var contentSize = ZstdInterop.GetFrameContentSize(span);
            ulong? window = null; uint? dictId = null; bool checksum = false; string ftype = "unknown";
            if (ZstdInterop.TryGetFrameHeader(span, out var hdr))
            {
                window = hdr.windowSize == 0 ? null : hdr.windowSize;
                dictId = hdr.dictID == 0 ? null : hdr.dictID;
                checksum = hdr.checksumFlag != 0;
                ftype = hdr.frameType == ZSTD_frameType_e.ZSTD_skippableFrame ? "skippable" : "frame";
            }
            yield return new ZstdFrameInfo(globalOffset, frameSize, contentSize, window, dictId, checksum, ftype);
            // Remove consumed bytes
            buffer.RemoveRange(0, (int)frameSize);
            globalOffset += frameSize;
            // loop continues for next frame
        }
    }

    private static int Fill(Stream stream, List<byte> buffer, int chunk)
    {
        byte[] renting = System.Buffers.ArrayPool<byte>.Shared.Rent(chunk);
        try
        {
            int r = stream.Read(renting, 0, chunk);
            if (r > 0)
            {
                buffer.AddRange(renting.AsSpan(0, r).ToArray());
            }
            return r;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(renting);
        }
    }

    private static ReadOnlySpan<byte> GetBufferSpan(List<byte> buffer)
    {
#if NET8_0_OR_GREATER
        return CollectionsMarshal.AsSpan(buffer);
#else
        return buffer.ToArray(); // fallback copy (older target frameworks)
#endif
    }
}
