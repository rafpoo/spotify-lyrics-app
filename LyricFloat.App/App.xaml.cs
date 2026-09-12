using System.Windows;
using LyricFloat.App.Services;
using LyricFloat.App.Views;
using LyricFloat.App.ViewModels;
using System.Windows.Media;

namespace LyricFloat.App;

public partial class App : Application
{
    private OverlayController? _overlay;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;
    private SpotifySessionController? _spotify;
    private bool _exiting;
    private SettingsService? _settings;
    private SettingsViewModel? _settingsModel;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().ToArray();
        _settings = new SettingsService(fonts: fonts);
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable) || System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            executable = System.IO.Path.Combine(AppContext.BaseDirectory, "LyricFloat.exe");
        var startup = new StartupService(executable);
        try { _settings.Update(s => s.StartWithWindows = startup.IsEnabled()); }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { AppLog.Write("Could not read Windows startup configuration."); }
        var cache = new LyricsCache();
        var window = new OverlayWindow();
        MainWindow = window;
        _overlay = new OverlayController(window, _settings);
        _spotify = new SpotifySessionController((OverlayViewModel)window.DataContext, Dispatcher, cache);
        _settings.Changed += OnPreferencesChanged;
        _settingsModel = new SettingsViewModel(_settings, startup, _overlay, cache, fonts);
        _tray = new TrayIconService(_overlay, ExitApplication, _spotify, OpenSettings);
        _hotkeys = new HotkeyService(_overlay.ToggleVisibility, _overlay.ToggleLock);
        _overlay.InitializeFromSettings();
        OnPreferencesChanged(this, EventArgs.Empty);
        AppLog.Write("Application started.");
        _spotify.Initialize();
    }

    private void OnPreferencesChanged(object? sender, EventArgs e) => _spotify?.SetTimingOffset(_settings!.Current.LyricsTimingOffsetMs);

    private void OpenSettings()
    {
        if (_exiting || _settingsModel is null) return;
        _settingsModel.RefreshStartup();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsModel);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private async void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _settingsWindow?.Close();
        try { if (_spotify is not null) await _spotify.StopAsync(); }
        catch (Exception e) { AppLog.Write($"Spotify shutdown failed ({e.GetType().Name}); closing application."); }
        finally
        {
            if (_settingsModel is not null) await _settingsModel.WaitForMaintenanceAsync();
            if (_settings is not null) await _settings.FlushAsync();
            Cleanup();
            Shutdown();
        }
    }

    private void Cleanup()
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _spotify?.Dispose();
        _settingsModel?.Dispose();
        if (_settings is not null) _settings.Changed -= OnPreferencesChanged;
        _overlay?.CloseForExit();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }
}
