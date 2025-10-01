using System.Buffers;

namespace ZstdDotnet;

internal sealed class ZstdCCtxHandle : SafeHandle
{
    private bool initialized;

    internal bool IsConfigured => initialized;
    private MemoryHandle? prefixHandle; // pinned handle for native refPrefix

    public ZstdCCtxHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(ZstdInterop.ZSTD_createCCtx());
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Lazily configures the native context with compression level, worker threads, and optional prefix.
    /// Safe to call multiple times; only the first call performs native configuration until a Reset(true/false) occurs.
    /// </summary>
    public void Configure(int level)
    {
        if (initialized) return;
        if (IsInvalid) throw new ObjectDisposedException(nameof(ZstdCCtxHandle));
        ZstdInterop.ThrowIfError(ZstdInterop.ZSTD_CCtx_setParameter(handle, ZSTD_cParameter.ZSTD_c_compressionLevel, level));
        initialized = true;
    }

    /// <summary>
    /// Sets or clears a raw content prefix. Must be invoked before <see cref="Configure"/> first initializes the context.
    /// </summary>
    public unsafe void SetPrefix(ReadOnlyMemory<byte> prefix)
    {
        if (initialized)
            throw new InvalidOperationException("Prefix must be set before the context is initialized.");
        prefixHandle?.Dispose();
        prefixHandle = null;
        if (prefix.IsEmpty) return;
        var handle = prefix.Pin();
        UIntPtr code = ZstdInterop.ZSTD_CCtx_refPrefix(this.handle, (IntPtr)handle.Pointer, (UIntPtr)(uint)prefix.Length);
        try
        {
            ZstdInterop.ThrowIfError(code);
            prefixHandle = handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Resets only the active compression session; previously configured parameters and prefix remain.
    /// </summary>
    public void Reset()
    {
        if (IsInvalid) return;
        ZstdInterop.ThrowIfError(ZstdInterop.ZSTD_CCtx_reset(handle, ZSTD_ResetDirective.ZSTD_reset_session_only));
        initialized = false; // will re-apply parameters/prefix lazily on next Configure call
        // Clear prefix and pin; caller must SetPrefix again if desired
        prefixHandle?.Dispose();
        prefixHandle = null;
    }

    protected override bool ReleaseHandle()
    {
        if (IsInvalid) return true;
        try
        {
            ZstdInterop.ThrowIfError(ZstdInterop.ZSTD_freeCCtx(handle));
            return true;
        }
        catch
        {
            return false; // keep failure visible in finalizer logs if any
        }
        finally
        {
            handle = IntPtr.Zero;
            prefixHandle?.Dispose();
            prefixHandle = null;
            initialized = false;
        }
    }
}