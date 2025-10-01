namespace ZstdDotnet;

internal static class ZstdInterop
{
    // Version captured once; format: (major * 100 * 100 + minor * 100 + patch) as returned by ZSTD_versionNumber
    private static readonly uint VersionNumber = ZSTD_versionNumber();
    // ZSTD_resetCStream introduced in v1.4.0 (per zstd changelog). 1.4.0 -> 1*100*100 + 4*100 + 0 = 10400
    internal static bool SupportsCStreamReset => VersionNumber >= 10400u;
    // ZSTD_compressStream2 introduced in v1.5.0 -> 1*100*100 + 5*100 + 0 = 10500
    internal static bool SupportsCompressStream2 => VersionNumber >= 10500u;
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZSTD_versionNumber();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern int ZSTD_maxCLevel();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern int ZSTD_minCLevel();
    // Legacy CStream APIs removed (create/init/free/size/compress). Encoder path uses CCtx + ZSTD_compressStream2 exclusively.
    // New unified streaming API (>= 1.5.0). Returns remaining to flush/end.
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_compressStream2(IntPtr cctx, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer inputBuffer, ZSTD_EndDirective endOp);
    // CCtx APIs (modern unified context)
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr ZSTD_createCCtx();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_freeCCtx(IntPtr cctx);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_CCtx_setParameter(IntPtr cctx, ZSTD_cParameter param, int value);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_CCtx_reset(IntPtr cctx, ZSTD_ResetDirective reset);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_CCtx_refPrefix(IntPtr cctx, IntPtr prefix, UIntPtr prefixSize);
    // DCtx APIs (decoder modern unified context) - parallels CCtx for decompression
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr ZSTD_createDCtx();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_freeDCtx(IntPtr dctx);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_DCtx_reset(IntPtr dctx, ZSTD_ResetDirective reset);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_DCtx_setParameter(IntPtr dctx, ZSTD_dParameter param, int value);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_decompressStream(IntPtr dctx, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer inputBuffer);
    // Legacy flush/end (CStream) removed; use ZSTD_compressStream2 with ZSTD_e_flush / ZSTD_e_end instead.
    // Frame inspection (size discovery)
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_findFrameCompressedSize(IntPtr src, UIntPtr srcSize);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern ulong ZSTD_getFrameContentSize(IntPtr src, UIntPtr srcSize);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_getFrameHeader(out ZSTD_frameHeader zfh, IntPtr src, UIntPtr srcSize);
    // Reset APIs (ZSTD v1.4.0+) - More efficient reuse than free/create. Mode constants: 0 = reset session only, 1 = reset parameters, 2 = reset session + params
    // Legacy resetCStream removed (CCtx reset used instead)
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] internal static extern bool ZSTD_isError(UIntPtr code);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ZSTD_getErrorName(UIntPtr code);
    public static void ThrowIfError(UIntPtr code)
    {
        if (ZSTD_isError(code))
        {
            var errorPtr = ZSTD_getErrorName(code);
            var errorMsg = Marshal.PtrToStringAnsi(errorPtr);
            throw new IOException(errorMsg);
        }
    }


    public static ulong FindFrameCompressedSize(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) throw new ArgumentException("Empty span", nameof(data));
        unsafe
        {
            fixed (byte* ptr = data)
            {
                UIntPtr size = ZSTD_findFrameCompressedSize((IntPtr)ptr, (UIntPtr)(uint)data.Length);
                ThrowIfError(size);
                return (ulong)size; // full width (platform dependent); caller treats as ulong
            }
        }
    }

    // Helpers for content size retrieval (may be unknown)
    internal const ulong ZSTD_CONTENTSIZE_UNKNOWN = ulong.MaxValue - 0UL; // per zstd docs: (unsigned long long)(-1)
    internal const ulong ZSTD_CONTENTSIZE_ERROR = ulong.MaxValue - 1UL;   // per zstd docs: (unsigned long long)(-2)

    public static ulong? GetFrameContentSize(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return null;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                ulong v = ZSTD_getFrameContentSize((IntPtr)ptr, (UIntPtr)(uint)data.Length);
                if (v == ZSTD_CONTENTSIZE_ERROR) return null;
                if (v == ZSTD_CONTENTSIZE_UNKNOWN) return null;
                return v; // exact decompressed size
            }
        }
    }

    public static bool TryGetFrameHeader(ReadOnlySpan<byte> data, out ZSTD_frameHeader header)
    {
        header = default;
        if (data.Length < 4) return false; // need at least magic
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var code = ZSTD_getFrameHeader(out header, (IntPtr)ptr, (UIntPtr)(uint)data.Length);
                if (ZSTD_isError(code)) return false; // treat as not ready / invalid
                return true;
            }
        }
    }
}

internal enum ZSTD_EndDirective : uint
{
    ZSTD_e_continue = 0,
    ZSTD_e_flush    = 1,
    ZSTD_e_end      = 2
}

// Subset of compression parameters we need now (can extend later)
internal enum ZSTD_cParameter : int
{
    ZSTD_c_compressionLevel = 100, // from zstd.h
}

internal enum ZSTD_ResetDirective : uint
{
    ZSTD_reset_session_only = 1,
    ZSTD_reset_parameters   = 2,
    ZSTD_reset_session_and_parameters = 3
}

// Decoder parameters subset
internal enum ZSTD_dParameter : int
{
    ZSTD_d_windowLogMax = 100
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZSTD_frameHeader
{
    public ulong frameContentSize; // 0 => unknown
    public ulong windowSize;       // 0 => not referenced
    public uint blockSizeMax;
    public ZSTD_frameType_e frameType;
    public uint headerSize;
    public uint dictID;
    public uint checksumFlag; // 1 if present
}

internal enum ZSTD_frameType_e : uint
{
    ZSTD_frame = 0,
    ZSTD_skippableFrame = 1
}

[StructLayout(LayoutKind.Sequential)]
internal class ZstdBuffer
{
    public IntPtr Data = IntPtr.Zero;
    public UIntPtr Size = UIntPtr.Zero;
    public UIntPtr Position = UIntPtr.Zero;
}
