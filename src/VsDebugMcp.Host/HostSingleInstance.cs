using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public sealed class HostSingleInstance : IDisposable
{
    private readonly Mutex? _mutex;

    private HostSingleInstance(Mutex? mutex, bool acquired)
    {
        _mutex = mutex;
        Acquired = acquired;
    }

    public bool Acquired { get; }

    public static HostSingleInstance Acquire()
    {
        var mutex = new Mutex(true, $@"Local\{PipeNames.ForHostControl()}.Mutex", out var createdNew);
        return createdNew
            ? new HostSingleInstance(mutex, true)
            : new HostSingleInstance(mutex, false);
    }

    public void Dispose()
    {
        if (Acquired)
        {
            _mutex?.ReleaseMutex();
        }

        _mutex?.Dispose();
    }
}