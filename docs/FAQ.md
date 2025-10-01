# FAQ

## How should I pick a compression level?
Range: `ZstdProperties.MinCompressionLevel` (=1) through `ZstdProperties.MaxCompressionLevel` (read at runtime and depends on the native library, e.g., 19 or 22).

Suggested ranges:
- 1–3: speed first (low-latency, real-time scenarios)
- 4–7: balanced profile (default is 6)
- 8–12: higher compression ratio with more CPU cost
- >12: maximum compression for archival or one-off packaging

Example:
```csharp
Console.WriteLine($"Max={ZstdProperties.MaxCompressionLevel}, Min={ZstdProperties.MinCompressionLevel}");
using var ms = new MemoryStream();
using var zs = new ZstdStream(ms, CompressionMode.Compress) { CompressionLevel = 9 }; // Throws ArgumentOutOfRangeException if out of range
zs.Write(data, 0, data.Length);
```

Adjusting the level during writing:
```csharp
zs.CompressionLevel = 3; // Triggers encoder.Reset(...) and takes effect on the next Write
```

Notes:
- Requires libzstd ≥ 1.4.0 (`ZstdProperties.SupportsReset == true`) to efficiently reuse state after a reset; older versions throw `NotSupportedException`.
- Frequent level changes incur additional resets, so avoid toggling on tiny segments.

## Why isn’t seeking supported?
Zstandard streams are not random-access by default; an index format would be required (not yet implemented).

## What happens with truncated or corrupt data?
The decoder returns the bytes that were successfully recovered and stops. Validate integrity on the caller side (length, checksum, etc.).

## When should I use the `Try*` APIs instead of the throwing versions?
Choose `Try*` when you can preallocate target buffers and want to avoid extra allocations. Use the throwing versions for convenience when allocation costs are acceptable.

## Can I call into the same `ZstdStream` concurrently?
No. The implementation uses CAS guards; concurrent access throws `InvalidOperationException`.

## How different is async performance from sync?
True async adds a minimal Task state-machine overhead. When working entirely in-memory, stick with sync for best results.

## Are dictionaries supported?
Not yet. The planned roadmap is outlined in [docs/Advanced.md](Advanced.md).

## Is multi-threaded compression (`nbWorkers`) supported?
Currently no. Future iterations may expose it via a parameter-setting API.

## How do I estimate the compressed size?
As a rule of thumb: `original length + original length / 20 + 512` bytes.

## Will unmanaged memory leak?
Encoder, decoder, and stream instances release native handles in `Dispose`. Finalizers (if added) or process shutdown also clean up, but you should dispose explicitly.

## How do I publish new NuGet packages?
1. Update `<PackageVersion>` in `src/ZstdDotnet/ZstdDotnet.csproj` and `src/ZstdDotnet.NativeAssets/ZstdDotnet.NativeAssets.csproj` (managed version must be four-part, e.g., `1.5.7.0`).
2. Trigger GitHub Actions:
   - **Publish ZstdDotnet Package** reads the managed csproj version and pushes to NuGet.
   - **Build Native Package** (when the native library changes) builds/extracts artifacts and publishes `ZstdDotnet.NativeAssets`.
3. Verify the published packages and dependencies on NuGet.org.
