using LyricFloat.App.Helpers;
using LyricFloat.App.Models;
using LyricFloat.App.Services;

namespace LyricFloat.Tests;

internal static class Phase5Tests
{
    private static int _count;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("Phase 5: " + name); _count++; }

    public static async Task<int> RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LyricFloat.SettingsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await TestSettingsAsync(directory);
            await TestOffsetAsync();
            TestStartup();
            TestPositions();
            await TestCacheClearAsync(directory);
        }
        finally { Directory.Delete(directory, true); }
        return _count;
    }

    private static async Task TestSettingsAsync(string directory)
    {
        var path = Path.Combine(directory, "settings.json");
        var settings = new SettingsService(path, ["Segoe UI", "Arial"]);
        Check(settings.Current.Version == 1 && settings.Current.CurrentFontSize == 32 && settings.Current.SecondaryFontSize == 22, "defaults and schema");
        Check(settings.Current.IsOverlayVisible && !settings.Current.IsOverlayLocked && settings.Current.DisplayMode == OverlayDisplayMode.ThreeLines, "default overlay state");
        settings.Update(s =>
        {
            s.FontFamily = "Arial"; s.CurrentFontSize = 40; s.DisplayMode = OverlayDisplayMode.Minimal;
            s.LyricsTimingOffsetMs = 300; s.OverlayLeft = -1500; s.OverlayTop = 220;
            s.IsOverlayLocked = true; s.IsOverlayVisible = false; s.StartMinimizedToTray = true;
            s.StartWithWindows = true; s.TextOpacity = 0.8; s.BackgroundMode = OverlayBackgroundMode.Subtle;
        });
        await settings.FlushAsync();
        Check(new SettingsService(path, ["Arial", "Segoe UI"]).Current == settings.Current, "full preference round-trip");
        Check(!File.Exists(path + ".tmp"), "atomic-save temporary file cleaned");
        var text = await File.ReadAllTextAsync(path);
        Check(!text.Contains("accessToken") && !text.Contains("refreshToken"), "settings contain no credentials");
        await File.WriteAllTextAsync(path, "{broken");
        Check(new SettingsService(path).Current.CurrentFontSize == 32, "corrupt JSON defaults");
        await File.WriteAllTextAsync(path, "{\"version\":1,\"currentFontSize\":-500,\"secondaryFontSize\":999,\"textOpacity\":12,\"backgroundOpacity\":-1,\"lyricsTimingOffsetMs\":900000,\"fontFamily\":\"Nonexistent Font\"}");
        var invalid = new SettingsService(path).Current;
        Check(invalid.CurrentFontSize == 16 && invalid.SecondaryFontSize == 48 && invalid.TextOpacity == 1 && invalid.BackgroundOpacity == 0 && invalid.LyricsTimingOffsetMs == 3000, "invalid values clamped");
        Check(invalid.FontFamily == "Segoe UI", "missing font fallback");
        var nonfinite = settings.Validate(new() { CurrentFontSize = double.NaN, SecondaryTextOpacity = double.PositiveInfinity, OverlayLeft = double.NaN, DisplayMode = (OverlayDisplayMode)77 });
        Check(nonfinite.CurrentFontSize == 32 && nonfinite.SecondaryTextOpacity == 0.5 && nonfinite.OverlayLeft is null && nonfinite.DisplayMode == OverlayDisplayMode.ThreeLines, "nonfinite and invalid enum validation");
        await File.WriteAllTextAsync(path, "{\"version\":1,\"currentFontSize\":41,\"futureField\":{\"anything\":true}}");
        Check(new SettingsService(path).Current.CurrentFontSize == 41, "unknown fields ignored");
        await File.WriteAllTextAsync(path, "{\"version\":999,\"currentFontSize\":41}");
        Check(new SettingsService(path).Current == new AppSettings(), "future schema defaults");
        settings.Reset();
        await settings.FlushAsync();
        Check(settings.Current == new AppSettings(), "reset ordinary preferences");
        var changes = 0;
        settings.Changed += (_, _) => changes++;
        settings.Update(s => s.CurrentFontSize = 32);
        Check(changes == 0, "unchanged preferences do not schedule updates");
        for (var i = 33; i < 50; i++) settings.Update(s => s.CurrentFontSize = i);
        await settings.FlushAsync();
        Check(new SettingsService(path).Current.CurrentFontSize == 49, "debounced final value flushed on exit");
    }

    private static async Task TestOffsetAsync()
    {
        double now = 0;
        var requests = 0;
        var lines = LrcParser.Parse("[00:10.000]Hello");
        var sync = new LyricsSyncService((_, _) => { requests++; return Task.FromResult(new LyricsResult(LyricsStatus.Synced, lines)); }, new PlaybackClock(() => now));
        sync.UpdateTrack(new() { Id = "offset", DurationMs = 30000, ProgressMs = 10000, IsPlaying = true });
        await sync.WaitForPendingLookupsAsync();
        Check(sync.GetFrame().Current == "Hello", "zero offset at timestamp");
        sync.SetTimingOffset(500);
        Check(sync.GetFrame().Current == "", "positive offset delays lyric immediately");
        now = 500;
        Check(sync.GetFrame().Current == "Hello", "positive 500 appears at 10.500");
        sync.UpdateTrack(new() { Id = "offset", DurationMs = 30000, ProgressMs = 9500, IsPlaying = true });
        sync.SetTimingOffset(-500);
        Check(sync.GetFrame().Current == "Hello", "negative 500 appears at 9.500");
        sync.SetTimingOffset(0);
        Check(sync.GetFrame().Current == "", "live offset recomputes current line");
        Check(requests == 1 && lines[0].Timestamp.TotalMilliseconds == 10000, "offset never refetches or changes cached timestamps");
        await sync.StopAsync();
    }

    private static void TestStartup()
    {
        var registry = new FakeRegistry();
        const string executable = @"C:\Users\Example User\Music Tools\LyricFloat.exe";
        var service = new StartupService(executable, registry);
        registry.Values["OtherApp"] = "keep me";
        Check(service.Command == "\"" + executable + "\"", "quoted executable with spaces");
        Check(!service.IsEnabled(), "missing startup entry disabled");
        service.SetEnabled(true);
        Check(service.IsEnabled() && registry.Values["LyricFloat"] == service.Command, "enable startup");
        service.SetEnabled(false);
        Check(!service.IsEnabled() && registry.Values["OtherApp"] == "keep me" && registry.Values.Count == 1, "disable isolates value name");
        registry.Values["LyricFloat"] = "\"C:\\Old\\LyricFloat.exe\"";
        Check(!service.IsEnabled(), "stale executable not effective startup");
        service.SetEnabled(true);
        Check(service.IsEnabled(), "enable updates actual path");
    }

    private static void TestPositions()
    {
        var primary = new WorkingArea(0, 0, 1920, 1040);
        var secondary = new WorkingArea(-1920, 0, 1920, 1080);
        WorkingArea[] screens = [primary, secondary];
        Check(OverlayPosition.Validate(100, 200, 760, 320, screens, primary) == (100, 200), "valid position preserved");
        Check(OverlayPosition.Validate(-1500, 200, 760, 320, screens, primary) == (-1500, 200), "negative multi-monitor position preserved");
        var expected = OverlayPosition.Default(primary, 760, 320);
        Check(OverlayPosition.Validate(9000, 9000, 760, 320, screens, primary) == expected, "disconnected monitor recovery");
        Check(OverlayPosition.Validate(-1500, 200, 760, 320, [primary], primary) == expected, "removed secondary monitor");
        var corrected = OverlayPosition.Validate(1910, 1030, 760, 320, screens, primary);
        Check(corrected.Left <= 1160 && corrected.Top <= 720, "tiny visible portion corrected");
        Check(OverlayPosition.Validate(200, 800, 760, 544, screens, primary).Top <= 496, "resized lyric center remains reachable");
        Check(OverlayPosition.Validate(double.NaN, null, 760, 320, screens, primary) == expected, "invalid coordinates default");
    }

    private static async Task TestCacheClearAsync(string directory)
    {
        var cacheDirectory = Path.Combine(directory, "cache");
        var cache = new LyricsCache(cacheDirectory);
        var track = new SpotifyTrack { Id = "clear-test" };
        var generation = cache.Generation;
        await cache.SaveAsync(track, new() { SyncedLyrics = "[00:01]Fixture" }, default);
        var unrelated = Path.Combine(cacheDirectory, "settings.json");
        await File.WriteAllTextAsync(unrelated, "keep");
        Check(await cache.ClearAsync(), "cache clear succeeds");
        Check(await cache.LoadAsync(track, default) is null && File.Exists(unrelated), "clear deletes only cache-shaped filenames");
        await cache.SaveAsync(track, new() { Instrumental = true }, default, generation);
        Check(await cache.LoadAsync(track, default) is null, "in-flight old save cannot repopulate cleared cache");
        await cache.SaveAsync(track, new() { Instrumental = true }, default, cache.Generation);
        Check((await cache.LoadAsync(track, default))!.Instrumental, "new lookup can cache again");
    }

    private sealed class FakeRegistry : IStartupRegistry
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? Read(string name) => Values.GetValueOrDefault(name);
        public void Write(string name, string value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
    }
}
