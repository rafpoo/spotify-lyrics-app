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
    private SingleInstanceService? _instance;
    private ExceptionHandlingService? _exceptions;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _exceptions = new ExceptionHandlingService(this, ExitApplication);
        _exceptions.Install();
        _instance = new SingleInstanceService();
        if (!_instance.IsPrimary)
        {
            if (e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase)) _instance.SignalExit();
            else _instance.SignalExisting();
            Shutdown();
            return;
        }
        if (e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase)) { Shutdown(); return; }
        _instance.Listen(() =>
        {
            if (!_exiting && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(OpenSettings));
        }, () =>
        {
            if (!_exiting && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(ExitApplication));
        });
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
        window.Icon = Helpers.AppIcon.Image();
        MainWindow = window;
        _overlay = new OverlayController(window, _settings);
        _spotify = new SpotifySessionController((OverlayViewModel)window.DataContext, Dispatcher, cache);
        _settings.Changed += OnPreferencesChanged;
        _settingsModel = new SettingsViewModel(_settings, startup, _overlay, cache, fonts, _spotify);
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
        try
        {
            var stopping = _spotify?.StopAsync() ?? Task.CompletedTask;
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _settingsWindow?.Close();
            await stopping.WaitAsync(TimeSpan.FromSeconds(8));
            if (_settingsModel is not null) await _settingsModel.WaitForMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(3));
            if (_settings is not null) await _settings.FlushAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception e) { AppLog.Exception("Shutdown", e); }
        finally
        {
            Cleanup();
            AppLog.Write("Application stopped.", component: "Lifecycle");
            Shutdown();
        }
    }

    private void Cleanup()
    {
        void Safely(Action action) { try { action(); } catch (Exception e) { AppLog.Exception("Cleanup", e); } }
        Safely(() => _hotkeys?.Dispose());
        Safely(() => _tray?.Dispose());
        Safely(() => _spotify?.Dispose());
        Safely(() => _settingsModel?.Dispose());
        if (_settings is not null) _settings.Changed -= OnPreferencesChanged;
        Safely(() => _overlay?.CloseForExit());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        _exceptions?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
