namespace ZstdDotnet;

internal sealed class ZstdDCtxHandle : SafeHandle
{
    public ZstdDCtxHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(ZstdInterop.ZSTD_createDCtx());
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        if (!IsInvalid)
        {
            ZstdInterop.ZSTD_freeDCtx(handle);
        }
        return true;
    }
}
