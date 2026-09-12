using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LyricFloat.App.Models;

namespace LyricFloat.App.Services;

internal sealed class SettingsService
{
    private readonly string _path;
    private readonly HashSet<string> _fonts;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private CancellationTokenSource? _debounce;
    private Task _pending = Task.CompletedTask;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public AppSettings Current { get; private set; }
    public string SaveStatus { get; private set; } = "Preferences save automatically.";
    public event EventHandler? Changed;
    public event EventHandler? SaveStatusChanged;

    public SettingsService(string? path = null, IEnumerable<string>? fonts = null)
    {
        _path = path ?? Path.Combine(AppLog.DataDirectory, "settings.json");
        _fonts = new(fonts ?? ["Segoe UI"], StringComparer.OrdinalIgnoreCase);
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return Validate(new());
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
            if (settings is null || settings.Version != 1)
            {
                AppLog.Write("Unsupported settings version; using defaults.");
                return Validate(new());
            }
            return Validate(settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write("Settings unreadable or corrupted; using defaults.");
            return Validate(new());
        }
    }

    internal AppSettings Validate(AppSettings input)
    {
        var result = input with { Version = 1 };
        result.FontFamily = !string.IsNullOrWhiteSpace(result.FontFamily) && _fonts.Contains(result.FontFamily)
            ? _fonts.First(f => f.Equals(result.FontFamily, StringComparison.OrdinalIgnoreCase))
            : _fonts.Contains("Segoe UI") ? "Segoe UI" : _fonts.Order().FirstOrDefault() ?? "Segoe UI";
        result.CurrentFontSize = Bound(result.CurrentFontSize, 16, 72, 32);
        result.SecondaryFontSize = Bound(result.SecondaryFontSize, 12, 48, 22);
        result.TextOpacity = Bound(result.TextOpacity, 0.1, 1, 1);
        result.SecondaryTextOpacity = Bound(result.SecondaryTextOpacity, 0.1, 1, 0.5);
        result.BackgroundOpacity = Bound(result.BackgroundOpacity, 0, 1, 0);
        result.LyricsTimingOffsetMs = Math.Clamp(result.LyricsTimingOffsetMs, -3000, 3000);
        result.OverlayLeft = ValidCoordinate(result.OverlayLeft);
        result.OverlayTop = ValidCoordinate(result.OverlayTop);
        if (!Enum.IsDefined(result.DisplayMode)) result.DisplayMode = OverlayDisplayMode.ThreeLines;
        if (!Enum.IsDefined(result.BackgroundMode)) result.BackgroundMode = OverlayBackgroundMode.Transparent;
        if (!Enum.IsDefined(result.FontWeight)) result.FontWeight = LyricFontWeight.SemiBold;
        if (!Enum.IsDefined(result.TextAlignment)) result.TextAlignment = LyricAlignment.Center;
        return result;
    }

    private static double Bound(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static double? ValidCoordinate(double? value) => value is { } number && double.IsFinite(number) && Math.Abs(number) <= 1_000_000 ? number : null;

    public void Update(Action<AppSettings> update)
    {
        var next = Current with { };
        update(next);
        Replace(next);
    }

    private void Replace(AppSettings next)
    {
        next = Validate(next);
        if (next == Current) return;
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
        _debounce?.Cancel();
        var cancellation = new CancellationTokenSource();
        _debounce = cancellation;
        _pending = SaveAfterDelayAsync(cancellation);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(400, cancellation.Token);
            await SaveAsync(Current, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_debounce, cancellation)) _debounce = null;
            cancellation.Dispose();
        }
    }

    private async Task SaveAsync(AppSettings snapshot, CancellationToken token)
    {
        await _writeGate.WaitAsync(token);
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, JsonOptions), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, _path, true);
            SaveStatus = "Preferences saved.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { SaveStatus = "Could not save preferences. Check folder permissions."; AppLog.Write(SaveStatus); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Write("Settings temporary file cleanup failed."); }
            _writeGate.Release();
            SaveStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task FlushAsync()
    {
        _debounce?.Cancel();
        await _pending;
        await SaveAsync(Current, CancellationToken.None);
    }

    public void Reset() => Replace(new AppSettings());
}
