namespace ZstdDotnet;

public partial class ZstdStream
{
    internal bool TestTryAcquireOperation()
    {
        return Interlocked.Exchange(ref activeOperation, 1) == 0;
    }

    internal void TestReleaseOperation()
    {
        Interlocked.Exchange(ref activeOperation, 0);
    }
}