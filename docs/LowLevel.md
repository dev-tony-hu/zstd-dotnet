# Low-level API: ZstdEncoder / ZstdDecoder and static helpers

This document provides examples for incremental encoder/decoder usage and the one-shot static helper methods.

## Incremental compression (ZstdEncoder)
Internally the encoder now exclusively uses the unified `ZSTD_compressStream2()` API (library requires libzstd >= 1.5.0). The former legacy `compress / flush / end` triple-call path has been removed for simpler state handling.
```csharp
var encoder = new ZstdEncoder(CompressionLevel.Optimal); // or new ZstdEncoder(level: 5)
var input = GetLargeBytes();
var outputBuffer = new byte[64 * 1024];
int offset = 0;
using var outMs = new MemoryStream();
while (offset < input.Length)
{
    int chunk = Math.Min(8192, input.Length - offset);
    var src = input.AsSpan(offset, chunk);
    bool finished = encoder.Compress(src, out int consumed, outputBuffer, out int written, isFinalBlock: offset + chunk == input.Length);
    if (written > 0) outMs.Write(outputBuffer, 0, written);
    offset += consumed;
    if (finished) break; // frame complete
}
encoder.Dispose();
var compressed = outMs.ToArray();
```

### Using a raw content prefix
If many payloads begin with the same header-like bytes, you can provide a raw prefix to improve compression ratio without training a dictionary:
```csharp
var encoder = new ZstdEncoder();
encoder.SetPrefix(headerBytes); // must be called before the first Compress (or after Reset before reuse)
// now call Compress(...) normally
```
Notes:
- This is not a trained dictionary; it's only the raw bytes expected at the start of the content.
- Call `SetPrefix(ReadOnlyMemory<byte>.Empty)` (or just create a new encoder / Reset) to clear it.
- Prefix is copied defensively; prefer reusing the same encoder for multiple related frames to amortize the cost.
- A prefix never appears in the compressed output by itself; only matches influence tokenization.

## Incremental decompression (ZstdDecoder)
```csharp
var decoder = new ZstdDecoder();
var compressed = GetCompressedBytes();
var outBuf = new byte[128 * 1024];
int pos = 0; using var plain = new MemoryStream();
while (pos < compressed.Length)
{
    var src = compressed.AsSpan(pos, compressed.Length - pos);
    bool frameDone = decoder.Decompress(src, out int consumed, outBuf, out int written, isFinalBlock: pos + consumed == compressed.Length);
    if (written > 0) plain.Write(outBuf, 0, written);
    pos += consumed;
    if (frameDone && pos == compressed.Length) break; // all frames consumed
}
decoder.Dispose();
var decompressed = plain.ToArray();
```

### Limiting decoder window size
For untrusted or memory-sensitive scenarios you can cap the maximum allowed window (2^log bytes) prior to first use or after a `Reset()`:
```csharp
var d = new ZstdDecoder();
d.SetMaxWindow(20); // ~1MB max window
// now call Decompress...
```
If an input frame requires a larger window than allowed, native zstd will signal an error which is surfaced as an exception.

### Decoder pooling
Reuse decoder contexts to avoid repeated native allocations:
```csharp
var decoder = ZstdDecoderPool.Rent();
try
{
    // use decoder
}
finally
{
    ZstdDecoderPool.Return(decoder);
}
```
Returned decoders are `Reset()` and safe for reconfiguration (e.g. calling `SetMaxWindow` again) before the next use.

## Static convenience methods
Use these when you already hold the full buffer in memory.

### TryCompress / Compress
```csharp
byte[] raw = File.ReadAllBytes("big.dat");
int maxGuess = raw.Length + 512 + raw.Length / 20;
byte[] tmp = new byte[maxGuess];
if (ZstdEncoder.TryCompress(raw, tmp, out int written, CompressionLevel.SmallestSize))
{
    File.WriteAllBytes("big.dat.zst", tmp.AsSpan(0, written).ToArray());
}
else
{
    var compressed = ZstdEncoder.Compress(raw, CompressionLevel.SmallestSize);
    File.WriteAllBytes("big.dat.zst", compressed);
}
```

`CompressionLevel` values map to concrete zstd levels: `NoCompression` / `Fastest` → `ZstdProperties.MinCompressionLevel` (native `ZSTD_minCLevel()`), `Optimal` → default (`5`), `SmallestSize` → `ZstdProperties.MaxCompressionLevel`. Use the integer overloads when you need fine-grained control over intermediate levels.

### TryDecompress / Decompress
```csharp
byte[] comp = File.ReadAllBytes("big.dat.zst");
int expectedSize = GetExpectedPlainSize();
byte[] dest = new byte[expectedSize];
if (ZstdDecoder.TryDecompress(comp, dest, out int plainWritten))
{
    if (plainWritten != dest.Length) Array.Resize(ref dest, plainWritten);
}
else
{
    dest = ZstdDecoder.Decompress(comp, expectedSize);
}
```

If the original size is unknown, allocate 4–8× the compressed length or use the expanding `Decompress` helper that resizes as needed.

## API comparison
| Method | Best for | Failure handling | Extra allocation |
|--------|----------|------------------|------------------|
| `TryCompress` | Preallocated destination buffers, zero extra allocation | Returns `false` | Caller-owned |
| `Compress` | Simple one-shot compression | Throws on failure | May allocate 1–2 temporary buffers |
| `TryDecompress` | Known uncompressed size | Returns `false` | Caller-owned |
| `Decompress` | Unknown/estimated size | Throws when the buffer is still too small or data is invalid | Repeated growth attempts |

## Recommendations
- Performance-sensitive scenarios with controlled memory → prefer the `Try*` methods.
- Rapid prototyping → use the throwing versions for brevity.
- Untrusted input → cap the maximum allowed output size to avoid amplification attacks.
 - The encoder exposes identical semantics regardless of whether `ZSTD_compressStream2` is available; you don't need to feature-detect. The unified path can reduce native call count during flush/finalization phases.
