using System.ComponentModel;
using System.Diagnostics;
using LyricFloat.App.Helpers;
using LyricFloat.App.Views;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LyricFloat.App.Services;

public sealed class OverlayController
{
    private readonly OverlayWindow _window;
    private bool _closing;
    private readonly SettingsService? _settings;
    private bool _applying;
    public bool IsOverlayVisible => _window.IsVisible;
    public bool IsOverlayLocked => _window.IsLocked;
    public event EventHandler? StateChanged;

    public OverlayController(OverlayWindow window)
        : this(window, null) { }

    internal OverlayController(OverlayWindow window, SettingsService? settings)
    {
        _window = window;
        _settings = settings;
        _window.Closing += OnClosing;
        _window.IsVisibleChanged += (_, _) =>
        {
            if (!_applying && !_closing) _settings?.Update(s => s.IsOverlayVisible = IsOverlayVisible);
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        _window.DragCompleted += (_, _) => SavePosition();
        if (_settings is not null) _settings.Changed += OnSettingsChanged;
    }

    public void InitializeFromSettings()
    {
        if (_settings is null) { ShowOverlay(); return; }
        _applying = true;
        try
        {
            new WindowInteropHelper(_window).EnsureHandle();
            _window.ApplyAppearance(_settings.Current);
            RestorePosition();
            SetLocked(_settings.Current.IsOverlayLocked);
            if (!_settings.Current.StartMinimizedToTray && _settings.Current.IsOverlayVisible) ShowOverlay();
        }
        finally { _applying = false; }
        SavePosition();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_applying || _closing || _settings is null) return;
        _applying = true;
        try
        {
            _window.ApplyAppearance(_settings.Current);
            RestorePosition();
            SavePosition();
        }
        finally { _applying = false; }
    }

    private (WorkingArea[] Areas, WorkingArea Primary) Screens()
    {
        var transform = HwndSource.FromHwnd(new WindowInteropHelper(_window).EnsureHandle())?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        WorkingArea Convert(System.Windows.Forms.Screen screen)
        {
            var bounds = screen.WorkingArea;
            var start = transform.Transform(new Point(bounds.Left, bounds.Top));
            var size = transform.Transform(new Point(bounds.Width, bounds.Height));
            return new(start.X, start.Y, size.X, size.Y);
        }
        var screens = System.Windows.Forms.Screen.AllScreens;
        return (screens.Select(Convert).ToArray(), Convert(screens.FirstOrDefault(s => s.Primary) ?? screens[0]));
    }

    private void RestorePosition()
    {
        var screens = Screens();
        var position = OverlayPosition.Validate(_settings?.Current.OverlayLeft, _settings?.Current.OverlayTop,
            _window.Width, _window.Height, screens.Areas, screens.Primary);
        _window.SetPosition(position.Left, position.Top);
    }

    public void ResetPosition()
    {
        var position = OverlayPosition.Default(Screens().Primary, _window.Width, _window.Height);
        _window.SetPosition(position.Left, position.Top);
        SavePosition();
    }

    private void SavePosition() => _settings?.Update(s => { s.OverlayLeft = _window.Left; s.OverlayTop = _window.Top; });

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
        SetLocked(!IsOverlayLocked);
    }

    public void SetLocked(bool locked)
    {
        try
        {
            OverlayWindowInterop.SetClickThrough(_window, locked);
            _window.SetLocked(locked);
            _window.Topmost = true;
            if (!_applying) _settings?.Update(s => s.IsOverlayLocked = locked);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Win32Exception exception)
        {
            AppLog.Exception("OverlayLock", exception);
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
        if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        _window.Close();
    }
}
