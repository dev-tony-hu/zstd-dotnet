# Advanced features and extensions

## Dictionaries
`CDict`/`DDict` handles are not currently exposed. A potential expansion plan:
1. Introduce a `ZstdDictionary` type that owns the native handle and reference counts usage.
2. Compression: pass the dictionary into the encoder constructor/factory and call `ZSTD_CCtx_refCDict`.
3. Decompression: call `ZSTD_DCtx_refDDict`.
4. Manage lifetime with `SafeHandle`, `Dispose`, and optional caching.

## Window and parameter tuning
Potentially expose parameters such as `WindowLog`, `HashLog`, `ChainLog`, `SearchLog`, `MinMatch`, `TargetLength`.

Conceptual API:
```csharp
public void SetParameter(ZstdEncoderParameter parameter, int value);
```
Internally call `ZSTD_CCtx_setParameter` and check for errors.

## Error-handling policy
| Scenario | Exception | Notes |
|----------|-----------|-------|
| Concurrent usage | `InvalidOperationException` | Protects `activeOperation` via CAS |
| Mode mismatch | `NotSupportedException` | e.g., calling `Write` while in decompress mode |
| Level out of range | `ArgumentOutOfRangeException` | Compression level invalid |
| Object disposed | `ObjectDisposedException` | Access after disposal |
| Native error code | `IOException` / `InvalidOperationException` | Raised from interop helpers |
| Cancellation | `OperationCanceledException` | Triggered by async tokens |

Truncated or incomplete streams: the implementation returns whatever was successfully decoded; callers should validate integrity on their own (length, checksum, etc.).

## Memory strategy
- Single pooled buffer for compression output / decompression input.
- Uses `ArrayPool<byte>.Shared` to minimize GC pressure.
- Buffers are returned to the pool without zeroing (performance first).

## Performance ideas
- Experiment with multi-threaded parameters such as `ZSTD_c_nbWorkers` when the native library supports them.(in the future)
- Cache dictionaries by identifier to avoid rebuilding them repeatedly.
- Adapt chunk size based on throughput feedback.

## Safety model
- All `unsafe` code lives inside the interop layer.
- No native pointers are exposed publicly.
- CAS guards block conflicting concurrent operations.

## Extension sketch
```csharp
public sealed class ZstdEncoder
{
    public void SetParameter(ZstdEncoderParameter parameter, int value)
    {
        int code = ZstdInterop.ZSTD_CCtx_setParameter(_cstream, (int)parameter, value);
        if (ZstdInterop.ZSTD_isError(code) != 0)
            throw new InvalidOperationException("SetParameter failed");
    }
}
```

## FAQ
**Why aren’t dictionaries exposed yet?**
The initial scope focuses on stabilizing the streaming pipeline. Efficient dictionary support requires additional lifetime management and caching decisions.

**What happens with truncated data?**
Decoded bytes are returned and the stream completes; callers should verify payload length or checksums as needed.

**How do I detect the end of a frame?**
`Decompress` reports `frameDone`. The stream wrapper automatically proceeds to the next frame.

## Multi-frame writing and decoding
Zstd allows multiple independent frames to be concatenated within a single file or stream. `ZstdStream` automatically iterates through all frames during decompression so callers do not need to manage boundaries manually.

### When to use multiple frames
* Logical segmentation (log shards, time slices, chunked uploads)
* “Write-and-seal” workflows that reduce downstream latency
* Resume support: locate completed frame boundaries for restart points

### Producing multiple frames
Use `FlushFrame()` to finalize the current frame and start the next:
```csharp
using var ms = new MemoryStream();
using (var zs = new ZstdStream(ms, CompressionMode.Compress, leaveOpen: true))
{
    zs.Write(chunkA);
    zs.FlushFrame(); // frame #1
    zs.Write(chunkB);
    zs.FlushFrame(); // frame #2
    zs.Write(chunkC);
} // Dispose => frame #3
```

### Decoding multiple frames (automatic stitching)
```csharp
ms.Position = 0;
using var decoder = new ZstdStream(ms, CompressionMode.Decompress);
using var output = new MemoryStream();
var buffer = new byte[8192];
int bytesRead;
while ((bytesRead = decoder.Read(buffer, 0, buffer.Length)) > 0)
    output.Write(buffer, 0, bytesRead);
// output now contains chunkA + chunkB + chunkC
```

### Per-frame processing (advanced pre-scan)
Sometimes you need frame offsets or sizes without fully decompressing. Iterate with `ZSTD_findFrameCompressedSize`:
```csharp
ulong offset = 0;
while (offset < (ulong)blob.Length)
{
    nuint frameSize = ZSTD_findFrameCompressedSize(blob[offset..]);
    if (ZSTD_isError(frameSize))
        throw new InvalidOperationException("Failed to read frame size");

    // Record offset/frameSize to build an index
    offset += (ulong)frameSize;
}
```

#### Built-in enumeration
```csharp
var frames = ZstdFrameInspector.EnumerateFrames(blob);
foreach (var frame in frames)
    Console.WriteLine($"Frame @ {frame.Offset} size={frame.CompressedSize}");
```
`ContentSize` may be `null` if it is omitted from the frame header or unknown in streaming mode.

#### Streaming enumeration (no full buffer load)
```csharp
using var fs = File.OpenRead(path);
foreach (var info in ZstdFrameInspector.EnumerateFrames(fs))
    Console.WriteLine($"Frame offset={info.Offset} comp={info.CompressedSize} content={info.ContentSize?.ToString() ?? "?"}");
```
Enumeration uses incremental buffering and frame-boundary inference to minimize peak memory, even for large concatenated files.

### Flush vs. FlushFrame recap
| Operation | Decodable immediately? | Writes frame terminator? | Starts new frame? | Use case |
|-----------|------------------------|--------------------------|------------------|----------|
| `Flush` / `FlushAsync` | Yes | No | No | Low-latency streaming |
| `Flush(Span)` | Yes | No | No | Fine-grained buffer control |
| `FlushFrame` | Yes | Yes | Yes | Segment boundaries / multi-member output |
| `Dispose` | Yes | Yes | No (closes) | Final completion |

### Performance notes
Many tiny frames add overhead (extra headers + end markers). If you only need lower latency without semantic splits, prefer `Flush()`.

### 64-bit sizes and huge frames
`ZstdFrameInspector` stores `CompressedSize` and `ContentSize` (when known) as 64-bit values, supporting >4GB frames.

`EnumerateFrames(ReadOnlySpan<byte>)` and `EnumerateFrames(Stream)` currently rely on knowing the compressed frame length. Single gigantic frames (>few GB) may require buffering their compressed payload in memory or repeatedly resizing collections. For TB-scale data consider:
1. Splitting the payload into multiple frames via `FlushFrame()`; or
2. Implementing an incremental block-header parser (on the roadmap).

To run the huge-frame test, set `ZSTD_RUN_HUGE=1`. By default it is skipped to avoid large time and disk usage.

