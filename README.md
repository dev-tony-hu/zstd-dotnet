# ZstdDotnet

ZstdDotnet is a high-performance, streaming-friendly .NET wrapper for the Zstandard (ZSTD) compression library. It builds on the official native `libzstd` implementation and exposes modern .NET APIs that work seamlessly with `Span<byte>` and `Memory<byte>`.

> The managed `ZstdDotnet` package delivers the .NET API surface, while `ZstdDotnet.NativeAssets` ships the cross-platform native binaries. This README covers both.

## Table of contents
- [ZstdDotnet](#zstddotnet)
	- [Table of contents](#table-of-contents)
	- [Packages](#packages)
	- [Features](#features)
	- [Installation](#installation)
	- [Quick start](#quick-start)
		- [Asynchronous usage](#asynchronous-usage)
		- [Span/Memory helpers](#spanmemory-helpers)
	- [API at a glance](#api-at-a-glance)
	- [Flush API cheat sheet](#flush-api-cheat-sheet)
	- [Design \& performance notes](#design--performance-notes)
	- [Building \& testing](#building--testing)
	- [Native assets package](#native-assets-package)
		- [Goals \& layout](#goals--layout)
		- [Building libzstd](#building-libzstd)
		- [Packing \& publishing](#packing--publishing)
		- [Upgrade checklist](#upgrade-checklist)
	- [Benchmarks](#benchmarks)
	- [Additional documentation](#additional-documentation)
	- [License](#license)
	- [Acknowledgements](#acknowledgements)

## Packages

| Package | Version source | Notes |
|---------|----------------|-------|
| `ZstdDotnet` | `<PackageVersion>` in `src/ZstdDotnet/ZstdDotnet.csproj` (must be four-part, e.g. `1.5.7.0`) | Managed compression/decompression API that consumes the native package |
| `ZstdDotnet.NativeAssets` | `<PackageVersion>` in `src/Zstdotnet.NativeAssets/ZstdDotnet.NativeAssets.csproj` | Bundles `libzstd` (`dll` / `so`) for runtime consumption |

> CI workflows read the version directly from the respective project files. Keep the managed package version in four-part `Major.Minor.Patch.Revision` format.

## Features
- Powered by the official C implementation of Zstandard, matching native compression quality and performance.
- Full streaming support: chunked writes/reads and transparent multi-frame decoding.
- True async APIs (`ReadAsync`, `WriteAsync`, `FlushAsync`, `DisposeAsync`) with no sync blocking shims.
- `Span<byte>`/`Memory<byte>` overloads to minimize allocations and copies.
- Configurable compression level via precise integers or the built-in `CompressionLevel` enum (default 5, range `ZstdProperties.MinCompressionLevel`..`ZstdProperties.MaxCompressionLevel`).
- Concurrency guards: prevents simultaneous read/write/flush/dispose on the same instance.
- Frame tooling: `ZstdFrameDecoder` and `ZstdFrameInspector` expose incremental frame metadata and async iteration.
- Hardened with 60+ unit tests covering edge cases, fuzz inputs, concurrency, cancellation, pooling, and huge frames.
- Requires native libzstd >= 1.5.0 (the library now exclusively uses the unified `ZSTD_compressStream2()` API; legacy `compress/flush/end` trio removed internally).
 - Decoder uses DCtx (modern context); optional `SetMaxWindow(log)` to cap memory usage.
 - Reusable decoder instances via `ZstdDecoderPool` reduce allocation and native context churn.
 - Optional raw content prefix via `ZstdEncoder.SetPrefix(memory)` to boost ratio when many frames share an initial header-like segment.


## Installation

```xml
<ItemGroup>
	<PackageReference Include="ZstdDotnet" Version="1.5.7.1" />
</ItemGroup>
```

Referencing `ZstdDotnet` automatically pulls in `ZstdDotnet.NativeAssets`. At runtime the appropriate native binary is loaded based on the current RID. If you only need the native library, reference `ZstdDotnet.NativeAssets` directly.

## Quick start

```csharp
// Compress
var data = File.ReadAllBytes("input.bin");
using var outStream = new MemoryStream();
using (var zs = new ZstdStream(outStream, CompressionMode.Compress, leaveOpen: true))
{
		int offset = 0;
		while (offset < data.Length)
		{
				int chunk = Math.Min(8192, data.Length - offset);
				zs.Write(data, offset, chunk); // or zs.Write(data.AsSpan(offset, chunk));
				offset += chunk;
		}
}
File.WriteAllBytes("output.zst", outStream.ToArray());

// Decompress
using var compressed = File.OpenRead("output.zst");
using var zsDec = new ZstdStream(compressed, CompressionMode.Decompress);
using var restored = new MemoryStream();
var buffer = new byte[8192];
int read;
while ((read = zsDec.Read(buffer, 0, buffer.Length)) > 0)
{
		restored.Write(buffer, 0, read);
}
```

### Asynchronous usage

```csharp
byte[] payload = GetLargeBuffer();
await using var stream = new MemoryStream();
await using (var encoder = new ZstdStream(stream, CompressionMode.Compress, leaveOpen: true))
{
		int offset = 0;
		var random = new Random();
		while (offset < payload.Length)
		{
				int chunk = Math.Min(random.Next(1024, 16_384), payload.Length - offset);
				await encoder.WriteAsync(payload.AsMemory(offset, chunk));
				offset += chunk;
		}
		await encoder.FlushAsync();
}
stream.Position = 0;
await using (var decoder = new ZstdStream(stream, CompressionMode.Decompress, leaveOpen: true))
{
		var scratch = new byte[4096];
		int n;
		while ((n = await decoder.ReadAsync(scratch)) > 0)
		{
				// consume bytes
		}
}
```

### Span/Memory helpers
- Sync: `Write(ReadOnlySpan<byte>)`, `Read(Span<byte>)`
- Async: `WriteAsync(ReadOnlyMemory<byte>, CancellationToken)`, `ReadAsync(Memory<byte>, CancellationToken)`

See [docs/LowLevel.md](docs/LowLevel.md) for incrementally streaming the low-level encoder/decoder, and [docs/Advanced.md](docs/Advanced.md) for advanced tuning guidance.

## API at a glance

| Member | Description |
|--------|-------------|
| `ZstdStream(Stream inner, CompressionMode mode, bool leaveOpen = false)` | Create a compression or decompression stream |
| `CompressionLevel` | Sets compression level when writing |
| `Flush()` / `FlushAsync()` | Drain pending output without finalizing a frame |
| `Flush(Span<byte>, out int)` | Low-level flush returning an `OperationStatus` |
| `FlushFrame()` | Terminates the current frame and starts a new one |
| `Dispose()` / `DisposeAsync()` | Finalizes the frame and releases resources |
| `Reset()` | Reset decoder state and continue with subsequent frames |
| `ZstdFrameInspector.EnumerateFrames(...)` | Inspect frame metadata without decompressing |
| `ZstdFrameDecoder.DecodeFramesAsync(...)` | Async frame iterator returning content + metadata |
| `ZstdProperties.LibraryVersion` | Reports the native library version |
| `ZstdProperties.ZstdVersion` / `ZstdProperties.ZstdVersionString` | Alternate strongly typed / string version forms |
| `ZstdProperties.MaxCompressionLevel` | Reports the maximum available level |

## Flush API cheat sheet

| Method | Writes frame terminator? | Continue same frame? | Starts new frame? | Primary use |
|--------|--------------------------|----------------------|------------------|-------------|
| `Flush()` / `FlushAsync()` | No | Yes | No | Drain the encoder buffer to lower latency |
| `Flush(Span<byte>, out int)` | No | Yes | No | Manual buffer control and `DestinationTooSmall` loops |
| `FlushFrame()` | Yes | No | Yes | Logical segmentation / multi-frame output |
| `Dispose()` / `DisposeAsync()` | Yes | No | No | Finalize the stream |

```csharp
Span<byte> scratch = stackalloc byte[1024];
while (true)
{
		var status = zs.Flush(scratch, out int written);
		if (written > 0)
				downstream.Write(scratch[..written]);
		if (status == OperationStatus.Done)
				break;
		// status == DestinationTooSmall -> loop again
}
```

## Design & performance notes
- Partial layout: `Streams/ZstdStream.cs` provides shared state, `enc/` and `dec/` contain compression/decompression logic, and `Streams/ZstdStream.Async.*.cs` adds async entry points.
- Buffer management: relies on `ArrayPool<byte>.Shared` to limit GC pressure.
- Compression pipeline: repeatedly call `encoder.Compress`, using empty input to trigger flushes.
- Concurrency guard: CAS on `activeOperation` prevents simultaneous operations on a single instance.
- Unsafe confinement: `unsafe` usage is isolated to interop; public APIs remain safe.
- Performance guidance: prefer chunk sizes above 8 KB and avoid sharing a single stream instance across threads.

Example compression level adjustment:

```csharp
using var zs = new ZstdStream(output, CompressionMode.Compress)
{
		CompressionLevel = 10
};
```

Higher levels cost more CPU—pick the right trade-off for your workload.

## Building & testing

```pwsh
pwsh> dotnet test
```

The suite currently covers flush semantics, async streaming, multi-frame behavior, edge cases, fuzz input, concurrency guards, and frame inspection (49 tests total).

## Native assets package

### Goals & layout

`ZstdDotnet.NativeAssets` publishes cross-platform `libzstd` binaries for consumption by the managed package.

```
root
 ├─ src/ZstdDotnet.NativeAssets/  # Packing project
 ├─ bin/                          # Drop native artifacts here
 │   ├─ libzstd.dll (win-x64)
 │   └─ libzstd.so  (linux-x64)
 └─ licenses/                     # MIT + upstream licenses
```

Pack the binaries into the NuGet package with:

```xml
<None Include="../../bin/libzstd.dll" Pack="true" PackagePath="runtimes/win-x64/native" />
<None Include="../../bin/libzstd.so"  Pack="true" PackagePath="runtimes/linux-x64/native" />
```

### Building libzstd

1. Clone the upstream source:
	 ```pwsh
	 git clone https://github.com/facebook/zstd.git
	 cd zstd
	 git checkout v$(xmlstarlet sel -t -v '/Project/PropertyGroup/PackageVersion' ..\src\ZstdDotnet.NativeAssets\ZstdDotnet.NativeAssets.csproj)
	 ```
2. Build with CMake (recommended):
	 ```pwsh
	 mkdir build
	 cd build
	 cmake -DZSTD_BUILD_SHARED=ON -DZSTD_BUILD_STATIC=OFF -DCMAKE_BUILD_TYPE=Release ..
	 cmake --build . --config Release --target zstd
	 ```
	 Copy the resulting `zstd.dll`/`libzstd.so` into the repository `bin/` directory.
3. On Linux you may also run `make -C build/cmake install` to produce `libzstd.so`.

Optional validation:

```pwsh
dumpbin /exports bin\libzstd.dll | Select-String ZSTD_version
nm -D bin/libzstd.so | grep ZSTD_version
```

### Packing & publishing

```pwsh
dotnet pack src/ZstdDotnet.NativeAssets/ZstdDotnet.NativeAssets.csproj -c Release -o out
dotnet nuget push out/ZstdDotnet.NativeAssets.<version>.nupkg -k <API_KEY> -s https://api.nuget.org/v3/index.json
```

Workflow **Build Native Package**:

1. Reads `<PackageVersion>` from `src/ZstdDotnet.NativeAssets/ZstdDotnet.NativeAssets.csproj`.
2. Downloads the matching upstream release, compiles/extracts the native binaries.
3. Runs `dotnet pack` to produce the NuGet package.
4. Pushes the package to NuGet using the `NUGET_DEPLOY_KEY` secret (skipping duplicates).

Managed package publishing is handled by **Publish ZstdDotnet Package**:

1. Reads `<PackageVersion>` from `src/ZstdDotnet/ZstdDotnet.csproj` (must already be four-part).
2. Synchronizes the native dependency version inside the same project file.
3. Optionally runs tests (toggle via the `skip-tests` workflow input).
4. Runs `dotnet pack` and pushes to NuGet with `--skip-duplicate`.

### Upgrade checklist

1. Update `<PackageVersion>` in both `src/ZstdDotnet/ZstdDotnet.csproj` and `src/ZstdDotnet.NativeAssets/ZstdDotnet.NativeAssets.csproj`.
2. Trigger the native and/or managed publishing workflows as appropriate.
3. Refresh version numbers in this README and docs if needed.
4. Verify the newly published packages on NuGet.org.

## Benchmarks

Run the BenchmarkDotNet project in `benchmark/ZstdDotnet.Benchmark`:

```pwsh
dotnet run -c Release --project benchmark/ZstdDotnet.Benchmark -- --filter *LibraryCompressionBench*
```

Filter specific cases:

```pwsh
dotnet run -c Release --project benchmark/ZstdDotnet.Benchmark -- --filter *Zstd_Compress_LibCompare_Async*
```

Find results under `BenchmarkDotNet.Artifacts/results/`. Use `--job Dry` for a quick sanity pass.

## Additional documentation
- [docs/LowLevel.md](docs/LowLevel.md): Low-level encoder/decoder and static helper examples
- [docs/Advanced.md](docs/Advanced.md): Advanced parameters, dictionary roadmap, tuning hints
- [docs/FAQ.md](docs/FAQ.md): Frequently asked questions
- [docs/Benchmark.md](docs/Benchmark.md): Extended benchmarking guide
- [docs/TestMatrix.md](docs/TestMatrix.md): Test trait reference

## License

Distributed under the MIT License. Upstream Zstandard library code used here (only the `lib` directory, built as a dynamically linked native library) is under the 3-clause BSD license. See `licenses/THIRD-PARTY-NOTICES.txt` for architectural usage notes and `licenses/zstdlicense.txt` for the full upstream BSD text.

## Acknowledgements

- Facebook/Meta Zstandard maintainers for the reference implementation
- The .NET team and community for Span/Stream innovations

Questions or ideas? Open an issue or pull request.


