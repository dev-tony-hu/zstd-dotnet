namespace ZstdDotnet;

internal static class ZstdInterop
{
    // Version captured once; format: (major * 100 * 100 + minor * 100 + patch) as returned by ZSTD_versionNumber
    private static readonly uint VersionNumber = ZSTD_versionNumber();
    // ZSTD_resetCStream introduced in v1.4.0 (per zstd changelog). 1.4.0 -> 1*100*100 + 4*100 + 0 = 10400
    internal static bool SupportsCStreamReset => VersionNumber >= 10400u;
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZSTD_versionNumber();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern int ZSTD_maxCLevel();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern int ZSTD_minCLevel();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr ZSTD_createCStream();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_initCStream(IntPtr zcs, int compressionLevel);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_freeCStream(IntPtr zcs);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_CStreamInSize();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_CStreamOutSize();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_compressStream(IntPtr zcs, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer inputBuffer);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr ZSTD_createDStream();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_initDStream(IntPtr zds);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_freeDStream(IntPtr zds);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_DStreamInSize();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_DStreamOutSize();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_decompressStream(IntPtr zds, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer inputBuffer);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_flushStream(IntPtr zcs, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_endStream(IntPtr zcs, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer);
    // Frame inspection (size discovery)
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_findFrameCompressedSize(IntPtr src, UIntPtr srcSize);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern ulong ZSTD_getFrameContentSize(IntPtr src, UIntPtr srcSize);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_getFrameHeader(out ZSTD_frameHeader zfh, IntPtr src, UIntPtr srcSize);
    // Reset APIs (ZSTD v1.4.0+) - More efficient reuse than free/create. Mode constants: 0 = reset session only, 1 = reset parameters, 2 = reset session + params
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_resetCStream(IntPtr zcs, uint resetMode);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_resetDStream(IntPtr zds);
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

    public static void ResetCStream(IntPtr zcs, bool resetParameters)
    {
        if (zcs == IntPtr.Zero) throw new ArgumentNullException(nameof(zcs));
        if (!SupportsCStreamReset)
            throw new NotSupportedException("libzstd version does not support ZSTD_resetCStream (requires >= 1.4.0)");
        // 0 = session only, 1 = parameters only, 2 = both. We choose 0 or 2 so parameters optionally re-applied by re-init.
        uint mode = resetParameters ? 2u : 0u;
        ThrowIfError(ZSTD_resetCStream(zcs, mode));
    }

    public static void ResetDStream(IntPtr zds)
    {
        if (zds == IntPtr.Zero) throw new ArgumentNullException(nameof(zds));
        ThrowIfError(ZSTD_resetDStream(zds));
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
