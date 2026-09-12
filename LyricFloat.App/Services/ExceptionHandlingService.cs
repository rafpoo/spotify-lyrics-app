using System.Windows;
using System.Windows.Threading;

namespace LyricFloat.App.Services;

internal sealed class ExceptionHandlingService(Application app, Action requestShutdown) : IDisposable
{
    public void Install()
    {
        app.DispatcherUnhandledException += OnDispatcher;
        TaskScheduler.UnobservedTaskException += OnTask;
        AppDomain.CurrentDomain.UnhandledException += OnDomain;
    }

    private void OnDispatcher(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Exception("Dispatcher", e.Exception, fatal: true);
        // Do not continue normal work after an unknown UI failure; run ordered shutdown.
        e.Handled = true;
        requestShutdown();
    }

    private static void OnTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Exception("UnobservedTask", e.Exception);
        e.SetObserved();
    }

    private static void OnDomain(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) AppLog.Exception("AppDomain", exception, fatal: e.IsTerminating);
        // The runtime is already terminating here. Logging is synchronous; OS releases handles.
    }

    public void Dispose()
    {
        app.DispatcherUnhandledException -= OnDispatcher;
        TaskScheduler.UnobservedTaskException -= OnTask;
        AppDomain.CurrentDomain.UnhandledException -= OnDomain;
    }
}
