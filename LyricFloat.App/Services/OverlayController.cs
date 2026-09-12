using System.ComponentModel;
using System.Diagnostics;
using LyricFloat.App.Helpers;
using LyricFloat.App.Views;

namespace LyricFloat.App.Services;

public sealed class OverlayController
{
    private readonly OverlayWindow _window;
    private bool _closing;
    public bool IsOverlayVisible => _window.IsVisible;
    public bool IsOverlayLocked => _window.IsLocked;
    public event EventHandler? StateChanged;

    public OverlayController(OverlayWindow window)
    {
        _window = window;
        _window.Closing += OnClosing;
        _window.IsVisibleChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowOverlay()
    {
        _window.Topmost = true;
        _window.Show();
    }

    public void HideOverlay() => _window.Hide();

    public void ToggleVisibility()
    {
        if (IsOverlayVisible) HideOverlay();
        else ShowOverlay();
    }

    public void ToggleLock()
    {
        var locked = !IsOverlayLocked;
        try
        {
            OverlayWindowInterop.SetClickThrough(_window, locked);
            _window.SetLocked(locked);
            _window.Topmost = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Win32Exception exception)
        {
            Trace.TraceWarning("Could not change overlay lock: {0}", exception.Message);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        HideOverlay();
    }

    public void CloseForExit()
    {
        if (_closing) return;
        _closing = true;
        _window.Close();
    }
}
