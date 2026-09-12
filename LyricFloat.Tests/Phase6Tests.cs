using System.Xml.Linq;
using LyricFloat.App.Helpers;
using LyricFloat.App.Models;
using LyricFloat.App.Services;

namespace LyricFloat.Tests;

internal static class Phase6Tests
{
    private static int _count;
    private static void Check(bool value, string name) { if (!value) throw new Exception("Phase 6: " + name); _count++; }

    public static async Task<int> RunAsync()
    {
        TestSingleInstance();
        var directory = Path.Combine(Path.GetTempPath(), "LyricFloat.HardeningTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, "{\"version\":0,\"currentFontSize\":43,\"lyricsTimingOffsetMs\":200}");
            var settings = new SettingsService(path);
            Check(settings.Current.Version == 1 && settings.Current.CurrentFontSize == 43 && settings.Current.SecondaryFontSize == 22, "legacy migration preserves preferences and fills defaults");
            await settings.FlushAsync();
            Check(new SettingsService(path).Current.LyricsTimingOffsetMs == 200, "migrated settings round-trip");
            await File.WriteAllTextAsync(path, "{diagnostic-corruption");
            _ = new SettingsService(path);
            Check(await File.ReadAllTextAsync(path + ".invalid.bak") == "{diagnostic-corruption", "corruption preserved for diagnostics");
            for (var i = 0; i < 25; i++) AppLog.Append(directory, new string('x', 100), "INFO", "Test", 200);
            Check(Directory.GetFiles(directory, "app.log*").Length == 4, "bounded log file count");
            Check((await File.ReadAllTextAsync(Path.Combine(directory, "app.log"))).Contains("[INFO] [Test]"), "human-readable structured logs");
            var diagnostic = AppLog.ExceptionDetails(new InvalidOperationException("access_token=synthetic-secret&code=synthetic-code http://127.0.0.1/callback?state=secret"));
            Check(diagnostic.Contains("InvalidOperationException") && !diagnostic.Contains("synthetic") && !diagnostic.Contains("callback"), "exception messages never leak credentials/URLs");
            var cache = new LyricsCache(directory);
            var track = new SpotifyTrack { Id = "old-schema" };
            await File.WriteAllTextAsync(Path.Combine(directory, LyricsCache.KeyFor(track) + ".json"), "{\"Version\":999,\"Key\":\"old\",\"Data\":{}}");
            Check(await cache.LoadAsync(track, default) is null, "old cache schema ignored");
        }
        finally { Directory.Delete(directory, true); }
        using (var icon = AppIcon.Load()) Check(icon.Width > 0 && icon.Height > 0, "embedded icon loads");
        Check(typeof(AppSettings).Assembly.GetName().Version!.ToString(3) == "0.1.0", "release version");
        TestReleaseProfile();
        return _count;
    }

    private static void TestSingleInstance()
    {
        var name = "LyricFloat.Test." + Guid.NewGuid().ToString("N");
        using (var primary = new SingleInstanceService(name))
        {
            Check(primary.IsPrimary, "first instance owns mutex");
            using var signalled = new ManualResetEventSlim();
            using var exitSignalled = new ManualResetEventSlim();
            primary.Listen(() => signalled.Set(), () => exitSignalled.Set());
            var secondWasPrimary = true;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { using var second = new SingleInstanceService(name); secondWasPrimary = second.IsPrimary; second.SignalExisting(); second.SignalExit(); }
                catch (Exception e) { failure = e; }
            });
            thread.Start();
            Check(thread.Join(TimeSpan.FromSeconds(5)), "second instance completes");
            if (failure is not null) throw failure;
            Check(!secondWasPrimary && signalled.Wait(TimeSpan.FromSeconds(5)), "second signals existing primary without ownership");
            Check(exitSignalled.Wait(TimeSpan.FromSeconds(5)), "graceful shutdown signal delivered");
        }
        using var replacement = new SingleInstanceService(name);
        Check(replacement.IsPrimary, "mutex released on exit");
    }

    private static void TestReleaseProfile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LyricFloat.sln"))) directory = directory.Parent;
        if (directory is null) throw new Exception("Run release configuration tests from the source checkout.");
        var profile = XDocument.Load(Path.Combine(directory.FullName, "LyricFloat.App", "Properties", "PublishProfiles", "WindowsPortable.pubxml"));
        string? Value(string name) => profile.Descendants(name).SingleOrDefault()?.Value;
        Check(Value("RuntimeIdentifier") == "win-x64" && Value("SelfContained") == "true", "self-contained x64 publish profile");
        Check(Value("PublishTrimmed") == "false" && Value("PublishSingleFile") == "false", "conservative WPF publishing");
        var script = File.ReadAllText(Path.Combine(directory.FullName, "installer", "LyricFloat.iss"));
        Check(script.Contains("PrivilegesRequired=lowest") && script.Contains("{localappdata}\\Programs\\LyricFloat"), "per-user installer definition");
        Check(script.Contains("CompareText(Trim(Command)") && !script.Contains("[UninstallDelete]"), "uninstall guards startup path and preserves user data");
    }
}
