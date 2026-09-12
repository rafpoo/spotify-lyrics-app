using System.Security.Principal;

namespace LyricFloat.App.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly EventWaitHandle _exit;
    private RegisteredWaitHandle? _registration;
    private RegisteredWaitHandle? _exitRegistration;
    private bool _disposed;
    public bool IsPrimary { get; }

    public SingleInstanceService(string? identity = null)
    {
        var name = identity ?? "LyricFloat." + WindowsIdentity.GetCurrent().User!.Value;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Activate");
        _exit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Exit");
        _mutex = new Mutex(false, @"Local\" + name + ".Instance");
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public void Listen(Action activate, Action? exit = null)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance can listen.");
        _registration = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) =>
        {
            try { activate(); }
            catch (Exception e) { AppLog.Exception("InstanceActivation", e); }
        }, null, Timeout.Infinite, false);
        if (exit is not null) _exitRegistration = ThreadPool.RegisterWaitForSingleObject(_exit, (_, _) =>
        {
            try { exit(); }
            catch (Exception e) { AppLog.Exception("InstanceExit", e); }
        }, null, Timeout.Infinite, false);
    }

    public void SignalExisting() => _activation.Set();
    public void SignalExit() => _exit.Set();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registration?.Unregister(null);
        _exitRegistration?.Unregister(null);
        _activation.Dispose();
        _exit.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex(); // Created and disposed on the WPF dispatcher thread.
        _mutex.Dispose();
    }
}
