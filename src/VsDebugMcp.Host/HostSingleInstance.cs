using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public sealed class HostSingleInstance : IDisposable
{
    private readonly Semaphore? _semaphore;

    private HostSingleInstance(Semaphore semaphore, bool acquired)
    {
        _semaphore = semaphore;
        Acquired = acquired;
    }

    public bool Acquired { get; }

    public static HostSingleInstance Acquire()
    {
        var semaphore = new Semaphore(
            1,
            1,
            $@"Local\{PipeNames.ForHostControl()}.Semaphore");
        return new HostSingleInstance(semaphore, semaphore.WaitOne(0));
    }

    public void Dispose()
    {
        if (Acquired)
        {
            _semaphore?.Release();
        }

        _semaphore?.Dispose();
    }
}