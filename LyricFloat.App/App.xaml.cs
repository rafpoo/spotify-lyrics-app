using System.Windows;
using LyricFloat.App.Services;
using LyricFloat.App.Views;

namespace LyricFloat.App;

public partial class App : Application
{
    private OverlayController? _overlay;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new OverlayWindow();
        MainWindow = window;
        _overlay = new OverlayController(window);
        _tray = new TrayIconService(_overlay, ExitApplication);
        _hotkeys = new HotkeyService(_overlay.ToggleVisibility, _overlay.ToggleLock);
        _overlay.ShowOverlay();
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        Cleanup();
        Shutdown();
    }

    private void Cleanup()
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _overlay?.CloseForExit();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }
}
