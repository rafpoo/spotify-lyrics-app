using System.ComponentModel;
using LyricFloat.App.Models;
using LyricFloat.App.Services;

namespace LyricFloat.App.ViewModels;

internal sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly OverlayController _overlay;
    private readonly LyricsCache _cache;
    private readonly SpotifySessionController? _spotify;
    public string AppVersion => "Version " + (typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
    public string SpotifyStatus => _spotify?.IsBusy == true ? "Spotify: Connecting / updating…" : _spotify?.IsConnected == true ? "Spotify: Connected" : "Spotify: Not connected";
    public string SpotifyAction => _spotify?.IsConnected == true ? "Disconnect Spotify" : "Connect Spotify";
    public bool CanConnect => _spotify is not null && !_spotify.IsBusy;
    public string SpotifyClientId { get; set; } = "";
    public IReadOnlyList<string> Fonts { get; }
    public Array Weights => Enum.GetValues<LyricFontWeight>();
    public Array Alignments => Enum.GetValues<LyricAlignment>();
    public Array DisplayModes => Enum.GetValues<OverlayDisplayMode>();
    public Array BackgroundModes => Enum.GetValues<OverlayBackgroundMode>();
    public string Status { get; private set; } = "";
    public string SaveStatus => _settings.SaveStatus;
    private Task _maintenance = Task.CompletedTask;
    public Task WaitForMaintenanceAsync() => _maintenance;
    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(SettingsService settings, StartupService startup, OverlayController overlay, LyricsCache cache, IEnumerable<string> fonts, SpotifySessionController? spotify = null)
    {
        _settings = settings; _startup = startup; _overlay = overlay; _cache = cache;
        _spotify = spotify;
        if (spotify is not null)
        {
            spotify.StateChanged += OnChanged;
            try { SpotifyClientId = SpotifyConfiguration.Load().ClientId; }
            catch (Exception e) when (e is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
            { AppLog.Write("Spotify configuration missing or invalid; use Settings to configure.", component: "Configuration"); }
        }
        Fonts = fonts.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        settings.Changed += OnChanged;
        settings.SaveStatusChanged += OnChanged;
        overlay.StateChanged += OnChanged;
    }

    public string FontFamily { get => _settings.Current.FontFamily; set => _settings.Update(s => s.FontFamily = value); }
    public double CurrentFontSize { get => _settings.Current.CurrentFontSize; set => _settings.Update(s => s.CurrentFontSize = value); }
    public double SecondaryFontSize { get => _settings.Current.SecondaryFontSize; set => _settings.Update(s => s.SecondaryFontSize = value); }
    public LyricFontWeight FontWeight { get => _settings.Current.FontWeight; set => _settings.Update(s => s.FontWeight = value); }
    public LyricAlignment Alignment { get => _settings.Current.TextAlignment; set => _settings.Update(s => s.TextAlignment = value); }
    public double TextOpacity { get => _settings.Current.TextOpacity; set => _settings.Update(s => s.TextOpacity = value); }
    public double SecondaryOpacity { get => _settings.Current.SecondaryTextOpacity; set => _settings.Update(s => s.SecondaryTextOpacity = value); }
    public double BackgroundOpacity { get => _settings.Current.BackgroundOpacity; set => _settings.Update(s => s.BackgroundOpacity = value); }
    public OverlayBackgroundMode BackgroundMode
    {
        get => _settings.Current.BackgroundMode;
        set => _settings.Update(s => { s.BackgroundMode = value; if (value == OverlayBackgroundMode.Subtle && s.BackgroundOpacity == 0) s.BackgroundOpacity = 0.2; });
    }
    public OverlayDisplayMode DisplayMode { get => _settings.Current.DisplayMode; set => _settings.Update(s => s.DisplayMode = value); }
    public int TimingOffset { get => _settings.Current.LyricsTimingOffsetMs; set => _settings.Update(s => s.LyricsTimingOffsetMs = value); }
    public bool IsLocked { get => _overlay.IsOverlayLocked; set => _overlay.SetLocked(value); }
    public bool IsVisible
    {
        get => _overlay.IsOverlayVisible;
        set { if (value) _overlay.ShowOverlay(); else _overlay.HideOverlay(); }
    }
    public bool StartMinimized { get => _settings.Current.StartMinimizedToTray; set => _settings.Update(s => s.StartMinimizedToTray = value); }
    public bool StartWithWindows
    {
        get => _settings.Current.StartWithWindows;
        set
        {
            try { _startup.SetEnabled(value); _settings.Update(s => s.StartWithWindows = _startup.IsEnabled()); Status = "Windows startup preference updated."; }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            { Status = "Could not change Windows startup registration. Check permissions."; AppLog.Write(Status); }
            OnChanged(this, EventArgs.Empty);
        }
    }

    public void RefreshStartup()
    {
        try { _settings.Update(s => s.StartWithWindows = _startup.IsEnabled()); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { Status = "Could not read Windows startup registration."; AppLog.Write(Status); OnChanged(this, EventArgs.Empty); }
    }

    public void ResetPosition() { _overlay.ResetPosition(); Status = "Overlay moved to primary monitor, bottom-center."; OnChanged(this, EventArgs.Empty); }

    public void ToggleSpotify()
    {
        if (_spotify?.IsConnected == true) _spotify.Disconnect();
        else _spotify?.Connect();
    }

    public Task SaveClientIdAsync() => QueueMaintenance(SaveClientIdCoreAsync);
    private async Task SaveClientIdCoreAsync()
    {
        if (_spotify?.IsConnected == true || _spotify?.IsBusy == true)
        { Status = "Disconnect Spotify and finish any pending connection before changing Client ID."; OnChanged(this, EventArgs.Empty); return; }
        try { await SpotifyConfiguration.SaveClientIdAsync(SpotifyClientId); Status = "Client ID saved locally. You can now connect Spotify."; }
        catch (InvalidOperationException e) { Status = e.Message; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { Status = "Could not save Spotify configuration."; AppLog.Exception("Configuration", e); }
        OnChanged(this, EventArgs.Empty);
    }

    public void OpenLogs()
    {
        try
        {
            var directory = System.IO.Path.Combine(AppLog.DataDirectory, "logs");
            System.IO.Directory.CreateDirectory(directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Status = "Could not open the log folder."; AppLog.Exception("Settings", e); OnChanged(this, EventArgs.Empty); }
    }

    public void ResetDefaults()
    {
        try { _startup.SetEnabled(false); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { Status = "Reset cancelled: could not disable Windows startup."; AppLog.Write(Status); OnChanged(this, EventArgs.Empty); return; }
        _settings.Reset();
        _overlay.SetLocked(false);
        _overlay.ShowOverlay();
        _overlay.ResetPosition();
        Status = "Preferences reset. Spotify login and lyrics cache retained.";
        OnChanged(this, EventArgs.Empty);
    }

    public Task ClearCacheAsync() => QueueMaintenance(ClearCacheCoreAsync);

    private Task QueueMaintenance(Func<Task> operation)
    {
        var previous = _maintenance;
        return _maintenance = RunAsync();
        async Task RunAsync() { await previous; await operation(); }
    }

    private async Task ClearCacheCoreAsync()
    {
        Status = await _cache.ClearAsync() ? "Lyrics cache cleared." : "Some lyrics cache files could not be deleted. Check permissions and try again.";
        OnChanged(this, EventArgs.Empty);
    }

    private void OnChanged(object? sender, EventArgs e) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        _settings.Changed -= OnChanged; _settings.SaveStatusChanged -= OnChanged; _overlay.StateChanged -= OnChanged;
        if (_spotify is not null) _spotify.StateChanged -= OnChanged;
    }
}
