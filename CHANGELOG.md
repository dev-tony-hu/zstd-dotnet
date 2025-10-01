# Changelog

All notable changes to this project will be documented in this file.

## [1.5.7.1] - 2025-10-01
### Added
- Decoder migrated to DCtx (safe handle) matching encoder CCtx modernization.
- Raw content prefix support via `ZstdEncoder.SetPrefix(ReadOnlyMemory<byte>)` (internally pinned until `Reset()`), using `ZSTD_CCtx_refPrefix` to improve ratio for shared leading headers.
- `ZstdDecoder.SetMaxWindow(int log)` to cap maximum decoding window size for untrusted input scenarios.
- `ZstdDecoderPool` for simple thread-safe decoder reuse.
- `ZstdEncoder.SetCompressionLevel(int)` to change level after a `Reset()` but before new compression work.
- Interop additions: `ZSTD_DCtx_setParameter`, `ZSTD_d_windowLogMax`.
- Tests expanded (pooling, window limits, prefix, encoder reset/config) – total test count now 68.

### Removed
- Legacy CStream / DStream APIs (create/init/free/size/reset); encoder uses only `ZSTD_compressStream2`, decoder uses DCtx + `ZSTD_decompressStream`.

### Changed
- Encoder `Reset` simplified to parameterless; reconfiguration now done via `SetCompressionLevel` & `SetPrefix` prior to first write.
- `ZstdEncoder.SetPrefix` signature from `ReadOnlySpan<byte>` → `ReadOnlyMemory<byte>` (safer lifetimes & async scenarios).
- Unified buffer sizing heuristics (removed dependency on `ZSTD_DStreamInSize`; fixed 64KiB default).
- Documentation updated to reflect DCtx migration, pooling, window limit parameter, prefix support and encoder API simplifications.

### Internal
- Lazy native initialization paths for encoder & decoder (removed explicit `ZSTD_initDStream`).
- SafeHandle adoption across compression & decompression contexts.

### Potential Next Steps
- Dictionary (DDict/CDict) support.
- Additional decoder parameters (`ZSTD_d_format`, `ZSTD_d_stableOutBuffer`).
- Advanced benchmarking: pooled vs non-pooled contexts.

## [1.5.7]
- Baseline version prior to DCtx decoder migration and prefix removal.

---
Format: Keep future entries newest on top. Version numbers follow the managed package's four-part scheme; native assets version may remain aligned to upstream libzstd where applicable.
