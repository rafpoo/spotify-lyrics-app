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

    public SettingsViewModel(SettingsService settings, StartupService startup, OverlayController overlay, LyricsCache cache, IEnumerable<string> fonts)
    {
        _settings = settings; _startup = startup; _overlay = overlay; _cache = cache;
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

    public Task ClearCacheAsync() => _maintenance = ClearCacheCoreAsync();

    private async Task ClearCacheCoreAsync()
    {
        Status = await _cache.ClearAsync() ? "Lyrics cache cleared." : "Some lyrics cache files could not be deleted. Check permissions and try again.";
        OnChanged(this, EventArgs.Empty);
    }

    private void OnChanged(object? sender, EventArgs e) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        _settings.Changed -= OnChanged; _settings.SaveStatusChanged -= OnChanged; _overlay.StateChanged -= OnChanged;
    }
}
