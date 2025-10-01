# Benchmark Guide

The repository ships with a first-class BenchmarkDotNet project, `benchmark/ZstdDotnet.Benchmark`, used to compare Zstd / Brotli / Gzip across compression levels and datasets for both throughput and ratio.

## What’s Included
* Custom columns:
    * `CompRatio` compression ratio (compressed size / original size)
    * `CompSize` compressed byte size
    * `MB/s` estimated throughput computed from original size / mean time
* Built-in datasets: `sml_text` (small repetitive text), `med_binary` (patterned binary ~2MB), `rnd` (random ~2MB)
* Configuration de-duplication so repeated configuration builds don’t emit duplicate exporter/column warnings
* Optional environment flags:
    * `BMARK_INPROC=1` adds an InProcess emit job (great for quick debugging; results don’t fully reflect real JIT/runtime behavior)
    * `BMARK_COUNTERS=1` toggles hardware counter diagnostics via reflection when BenchmarkDotNet.Diagnostics.Windows is installed; otherwise fails silently

## Quick Run
```pwsh
pwsh> dotnet run -c Release --project benchmark/ZstdDotnet.Benchmark/ZstdDotnet.Benchmark.csproj -- -f CompressionBench.Zstd_Compress -j Short
```
This command runs every `Zstd_Compress` variant (all levels × all datasets × both jobs).

## Fine-Grained Filters
Benchmark names include parameters, for example:
```
CompressionBench.Zstd_Compress(Level: 6, Dataset: "sml_text")
```
List every benchmark:
```pwsh
pwsh> dotnet run -c Release --project benchmark/ZstdDotnet.Benchmark/ZstdDotnet.Benchmark.csproj -- --list flat | Select-String Zstd_Compress
```
Run a single instance (mind the escaping—wrap the entire filter in quotes for PowerShell):
```pwsh
pwsh> dotnet run -c Release --project benchmark/ZstdDotnet.Benchmark/ZstdDotnet.Benchmark.csproj -- -f "CompressionBench.Zstd_Compress(Level: 6, Dataset: \"sml_text\")" -j Short
```
If the parameter match fails, fall back to method-level filtering with `-f CompressionBench.Zstd_Compress` and post-process the CSV/Markdown output.

## Environment Variable Example
```pwsh
pwsh> $env:BMARK_INPROC=1; $env:BMARK_COUNTERS=1; dotnet run -c Release --project benchmark/ZstdDotnet.Benchmark/ZstdDotnet.Benchmark.csproj -- -f CompressionBench.Zstd_Compress
```

## Output
Artifacts land in `BenchmarkDotNet.Artifacts/results/`:
* `*-report-github.md`
* `*-report.csv`
* `*-report.html`

## Metric Cheat Sheet
| Metric | Meaning | Notes |
|--------|---------|-------|
| Mean | Average elapsed time | Controlled by the job’s iteration strategy |
| MB/s | Original size / Mean | Rough throughput estimate; ignores flush-frequency differences |
| CompRatio | Compressed / Original | Lower is better |
| CompSize | Compressed bytes | Calculated once during warmup and cached |
| Allocated | Heap allocations (BDN builtin) | Influenced by GC & JIT optimizations |

## Expected Trends
* Higher compression levels → lower throughput / smaller output
* Random data (`rnd`) yields ratios near 1; throughput reflects pure algorithm overhead
* Text/structured datasets benefit more from compression
* Unified streaming path (post-removal of legacy `compress/flush/end`) reduces native call count during flush/final phases – marginally improving throughput for flush-heavy scenarios.

## Future Enhancements
* Add async benchmarks (WriteAsync / ReadAsync)
* Explore threaded dictionaries or long-distance matching once APIs surface
* Compare dictionary build/load flows
* Include larger datasets (> 64MB) to explore window limits
* Record decompression parity with extended DecompressionBench columns

---
The following original skeleton remains for reference; trim if you no longer need it:

## Suggested Metrics
- Compression throughput (MB/s)
- Decompression throughput (MB/s)
- Compression ratio (compressed_size / original_size)
- Allocations (Allocated / Gen0/1/2 collections)

## Sample Project Layout
```
benchmarks/
  ZstdBenchmarks.csproj
  CompressionBench.cs
  DecompressionBench.cs
  DataGenerator.cs
```

## Sample csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
    <ProjectReference Include="..\..\src\ZstdDotnet\ZstdDotnet.csproj" />
  </ItemGroup>
</Project>
```

## CompressionBench Example
```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ZstdDotnet;

[MemoryDiagnoser]
public class CompressionBench
{
    [Params(1024, 1024*128, 1024*1024)] public int Size;
    [Params(3,6,9)] public int Level;
    private byte[] _data = default!;
    private MemoryStream _sink = default!;

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Size];
        new Random(42).NextBytes(_data);
        _sink = new MemoryStream();
    }

    [Benchmark]
    public long Stream_Compress()
    {
        _sink.Position = 0; _sink.SetLength(0);
        using (var zs = new ZstdStream(_sink, CompressionMode.Compress, leaveOpen:true) { CompressionLevel = Level })
        {
            zs.Write(_data, 0, _data.Length);
        }
        return _sink.Length;
    }

    [Benchmark]
    public int Static_Compress()
    {
        var c = ZstdEncoder.Compress(_data, Level);
        return c.Length;
    }

    public static void Main() => BenchmarkRunner.Run<CompressionBench>();
}
```

## DecompressionBench Example
```csharp
using BenchmarkDotNet.Attributes;
using ZstdDotnet;

[MemoryDiagnoser]
public class DecompressionBench
{
    [Params(1024, 1024*128, 1024*1024)] public int Size;
    private byte[] _data = default!;
    private byte[] _compressed = default!;
    private MemoryStream _sink = default!;

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Size]; new Random(123).NextBytes(_data);
        _compressed = ZstdEncoder.Compress(_data, 6);
        _sink = new MemoryStream(_data.Length);
    }

    [Benchmark]
    public int Stream_Decompress()
    {
        using var ms = new MemoryStream(_compressed);
        using var zs = new ZstdStream(ms, CompressionMode.Decompress);
        _sink.Position = 0; int read; var buf = new byte[8192]; int total=0;
        while ((read = zs.Read(buf,0,buf.Length))>0) { total += read; }
        return total;
    }

    [Benchmark]
    public int Static_Decompress()
    {
        var plain = ZstdDecoder.Decompress(_compressed, _data.Length);
        return plain.Length;
    }
}
```

(The production benchmarks already cover a richer feature set.)
