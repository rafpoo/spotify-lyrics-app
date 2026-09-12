using System.Globalization;
using System.Net;
using System.Text.Json;
using LyricFloat.App.Helpers;
using LyricFloat.App.Models;
using LyricFloat.App.Services;
using LyricFloat.App.ViewModels;

namespace LyricFloat.Tests;

internal static class Phase4Tests
{
    private static int _count;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Phase 4: " + message);
        _count++;
    }

    public static async Task<int> RunAsync()
    {
        TestParserAndLookup();
        TestClock();
        await TestCacheAndHttpAsync();
        await TestTrackLifecycleAsync();
        TestBindings();
        return _count;
    }

    private static void TestParserAndLookup()
    {
        var lines = LrcParser.Parse("[00:10.00]Line A\n[00:15.50]Line B");
        Check(lines.Count == 2 && lines[1].Timestamp.TotalMilliseconds == 15500, "normal hundredths");
        Check(LrcParser.Parse("[00:10.123]Line")[0].Timestamp.TotalMilliseconds == 10123, "milliseconds");
        Check(LrcParser.Parse("[00:10]Line")[0].Timestamp.TotalSeconds == 10, "whole seconds");
        Check(LrcParser.Parse("[00:10.1]Line")[0].Timestamp.TotalMilliseconds == 10100, "tenths");
        Check(LrcParser.Parse("[ar:Artist]\n[ti:Title]\n[al:Album]\n[by:Me]\n[offset:500]\n[00:10]Line").Single().Timestamp.TotalSeconds == 10, "metadata and offset explicitly ignored");
        Check(LrcParser.Parse("garbage\n[broken]Text\n[99:notvalid]foo\n[00:61]bad\n[00:10.1234]bad\n[9999999999999:00]bad").Count == 0, "malformed lines ignored");
        var duplicates = LrcParser.Parse("[00:15]Hello\n[00:15]World");
        Check(duplicates.Select(l => l.Text).SequenceEqual(new[] { "Hello", "World" }), "duplicate stable source order");
        Check(LyricLookup.Select(duplicates, TimeSpan.FromSeconds(15)).Current == "World", "last duplicate wins lookup");
        var gap = LrcParser.Parse("[00:10]Text\n[00:15]\n[00:20]Again");
        Check(gap.Count == 3 && LyricLookup.Select(gap, TimeSpan.FromSeconds(17)).Current == "", "empty timing boundary preserved");
        Check(LrcParser.Parse(null).Count == 0 && LrcParser.Parse("").Count == 0, "null/empty input");
        Check(LrcParser.Parse("[65:10]Long song")[0].Timestamp.TotalSeconds == 3910, "large minute values");
        var multiple = LrcParser.Parse("[00:20][00:10.25]Repeated");
        Check(multiple.Count == 2 && multiple[0].Timestamp.TotalMilliseconds == 10250 && multiple[1].Text == "Repeated", "multiple timestamps and sorting");
        var lookup = LrcParser.Parse("[00:20]Line C\n[00:10]Line A\n[00:15]Line B");
        Check(LyricLookup.Select(lookup, TimeSpan.FromSeconds(9)) == ("", "", "Line A"), "before first lyric");
        Check(LyricLookup.Select(lookup, TimeSpan.FromSeconds(10)) == ("", "Line A", "Line B"), "exact timestamp");
        Check(LyricLookup.Select(lookup, TimeSpan.FromSeconds(17)) == ("Line A", "Line B", "Line C"), "middle lookup");
        Check(LyricLookup.Select(lookup, TimeSpan.FromSeconds(25)) == ("Line B", "Line C", ""), "after final lyric");
        Check(LyricLookup.Select([], TimeSpan.Zero) == ("", "", ""), "empty lookup");
    }

    private static SpotifyTrack Track(string id = "A", int progress = 10000, bool playing = true) => new()
    { Id = id, Name = "Title & /?", Artist = "Artist A, Artist B", Album = "Album #1", DurationMs = 289533, ProgressMs = progress, IsPlaying = playing };

    private static void TestClock()
    {
        double now = 0;
        var clock = new PlaybackClock(() => now);
        Check(!clock.Synchronize(Track()), "first anchor is not seek");
        now = 500;
        Check(clock.PositionMs == 10500, "playing advances");
        clock.Synchronize(Track(progress: 10500, playing: false));
        now = 5000;
        Check(clock.PositionMs == 10500, "pause freezes");
        clock.Synchronize(Track(progress: 10500));
        now += 500;
        Check(clock.PositionMs == 11000, "resume advances");
        Check(!clock.Synchronize(Track(progress: 11200)) && clock.PositionMs == 11200, "small remote correction anchors normally");
        Check(clock.Synchronize(Track(progress: 90000)) && clock.PositionMs == 90000, "seek forward");
        Check(clock.Synchronize(Track(progress: 15000)) && clock.PositionMs == 15000, "seek backward");
        clock.Freeze(); now += 9999;
        Check(clock.PositionMs == 15000, "network freeze");
        clock.Reset();
        Check(clock.PositionMs == 0 && !clock.IsPlaying, "track reset");
        clock.Synchronize(Track(progress: 289000)); now += 20000;
        Check(clock.PositionMs == 289533, "duration clamps local clock");
    }

    private static async Task TestCacheAndHttpAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LyricFloat.LyricsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cache = new LyricsCache(directory);
            Check(await cache.LoadAsync(Track(), default) is null, "cache miss");
            var source = new LrclibLyricsResponse { Id = 12, SyncedLyrics = "[00:10]Fixture" };
            await cache.SaveAsync(Track(), source, default);
            Check((await cache.LoadAsync(Track(), default))?.SyncedLyrics == source.SyncedLyrics, "synced cache save/load");
            await cache.SaveAsync(Track("I"), new() { Instrumental = true }, default);
            Check((await cache.LoadAsync(Track("I"), default))!.Instrumental, "instrumental cache");
            await cache.SaveAsync(Track("P"), new() { PlainLyrics = "Plain fixture" }, default);
            Check((await cache.LoadAsync(Track("P"), default))!.PlainLyrics == "Plain fixture", "plain cache");
            var unsafeKey = LyricsCache.KeyFor(Track("../../unsafe:id?"));
            Check(unsafeKey.Length == 64 && unsafeKey.All(Uri.IsHexDigit), "hashed safe filename");
            Check(LyricsCache.KeyFor(Track()) == LyricsCache.KeyFor(Track(progress: 50000)), "stable track-ID key");
            Check(LyricsCache.KeyFor(Track("")) != LyricsCache.KeyFor(new() { Name = "different" }), "metadata fallback key");
            var path = Path.Combine(directory, LyricsCache.KeyFor(Track()) + ".json");
            await File.WriteAllTextAsync(path, "{invalid");
            Check(await cache.LoadAsync(Track(), default) is null, "corrupted cache ignored");
            var count = 0;
            using var handler = new Handler(request =>
            {
                count++;
                Check(request.RequestUri!.Host == "lrclib.net" && request.RequestUri.AbsolutePath == "/api/get", "official LRCLIB endpoint");
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(source)) };
            });
            using var http = new HttpClient(handler);
            var service = new LyricsService(http, cache);
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new("fr-FR");
                var parameters = LoopbackCallbackListener.ParseQuery(LyricsService.BuildRequestUri(Track()).Query[1..]);
                Check(parameters["track_name"] == Track().Name && parameters["artist_name"] == Track().Artist && parameters["album_name"] == Track().Album, "escaped metadata preserved");
                Check(parameters["duration"] == "289.533", "invariant seconds");
            }
            finally { CultureInfo.CurrentCulture = culture; }
            Check((await service.GetAsync(Track(), default)).Status == LyricsStatus.Synced, "corrupt cache replaced by download");
            Check((await service.GetAsync(Track(), default)).Status == LyricsStatus.Synced && count == 1, "cache avoids second HTTP request");
            Check((await service.GetAsync(Track("I"), default)).Status == LyricsStatus.Instrumental, "instrumental result");
            Check((await service.GetAsync(Track("P"), default)).Status == LyricsStatus.PlainOnly, "plain-only result");
            handler.Respond = _ => new(HttpStatusCode.NotFound);
            Check((await service.GetAsync(Track("404"), default)).Status == LyricsStatus.NotFound, "404 ordinary state");
            handler.Respond = _ => new(HttpStatusCode.TooManyRequests);
            Check((await service.GetAsync(Track("429"), default)).Status == LyricsStatus.Error, "429 no immediate retry");
            handler.Respond = _ => throw new HttpRequestException();
            Check((await service.GetAsync(Track("offline"), default)).Status == LyricsStatus.Error, "network error state");
            Check((await service.GetAsync(Track(), default)).Status == LyricsStatus.Synced, "offline cache hit");
            handler.Respond = _ => new(HttpStatusCode.OK) { Content = new StringContent("not-json") };
            Check((await service.GetAsync(Track("bad-json"), default)).Status == LyricsStatus.Error, "invalid API response handled");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task TestTrackLifecycleAsync()
    {
        double now = 0;
        var startedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = new TaskCompletionSource<LyricsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = new TaskCompletionSource<LyricsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken tokenA = default;
        var calls = 0;
        var sync = new LyricsSyncService((track, token) =>
        {
            Interlocked.Increment(ref calls);
            if (track.Id == "A") { tokenA = token; startedA.SetResult(); return a.Task; }
            startedB.SetResult(); return b.Task;
        }, new PlaybackClock(() => now));
        sync.UpdateTrack(Track());
        await startedA.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pendingA = sync.WaitForPendingLookupsAsync();
        sync.UpdateTrack(Track("B"));
        await startedB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(tokenA.IsCancellationRequested, "track change cancels previous request");
        b.SetResult(new(LyricsStatus.Synced, LrcParser.Parse("[00:10]B first\n[00:15]B second\n[00:20]B last")));
        await sync.WaitForCurrentLookupAsync();
        Check(sync.GetFrame().Current == "B first", "B displayed before A completes");
        a.SetResult(new(LyricsStatus.Synced, LrcParser.Parse("[00:00]STALE A")));
        await sync.WaitForPendingLookupsAsync();
        await pendingA;
        Check(sync.GetFrame().Current == "B first", "late A cannot replace B");
        now = 6000;
        Check(sync.GetFrame().Current == "B second", "local timer advances without network");
        sync.UpdateTrack(Track("B", 16000, false)); now = 12000;
        Check(sync.GetFrame().Current == "B second" && sync.GetFrame().IsPaused, "paused frame freezes");
        sync.UpdateTrack(Track("B", 16000)); now += 5000;
        Check(sync.GetFrame().Current == "B last", "resumed lyrics advance");
        sync.UpdateTrack(Track("B", 11000));
        Check(sync.GetFrame().Current == "B first", "backward seek rewinds lookup");
        Check(calls == 2, "same track checkpoints do not fetch lyrics");
        sync.Suspend("Network issue"); now += 5000;
        Check(sync.GetFrame().Current == "", "network message expires");
        sync.UpdateTrack(Track("B", 22000));
        Check(sync.GetFrame().Current == "B last" && calls == 2, "network recovery reuses current lyrics");
        sync.Clear("Spotify not connected");
        Check(sync.GetFrame().Current == "Spotify not connected" && sync.GetFrame().Next == "", "disconnect clears lyric state");
        await sync.StopAsync();

        foreach (var status in new[] { LyricsStatus.NotFound, LyricsStatus.PlainOnly, LyricsStatus.Error, LyricsStatus.Instrumental })
        {
            var requests = 0;
            var state = new LyricsSyncService((_, _) => { requests++; return Task.FromResult(new LyricsResult(status, [])); }, new PlaybackClock(() => now));
            state.UpdateTrack(Track());
            await state.WaitForPendingLookupsAsync();
            Check(state.GetFrame().Current.Length > 0, $"{status} message");
            now += 5000;
            state.UpdateTrack(Track());
            Check(requests == 1, $"{status} no repeated request");
            Check((state.GetFrame().Current.Length > 0) == (status == LyricsStatus.Instrumental), $"{status} temporary/permanent duration");
            await state.StopAsync();
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var shutdown = new LyricsSyncService(async (_, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return new(LyricsStatus.Error, []);
        });
        shutdown.UpdateTrack(Track());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check(cancelled, "shutdown cancels and awaits lookup");
    }

    private static void TestBindings()
    {
        var model = new OverlayViewModel();
        var notifications = new List<string?>();
        model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var frame = new LyricFrame("previous", "current", "next", "song", false);
        model.ShowLyrics(frame);
        notifications.Clear();
        for (var i = 0; i < 100; i++) model.ShowLyrics(frame);
        Check(notifications.Count == 0, "unchanged timer frames do not notify WPF");
        model.ShowPlayback(new(Track(), ""));
        Check(!notifications.Any(n => n is null or nameof(model.CurrentLyric) or nameof(model.PreviousLyric) or nameof(model.NextLyric)), "Spotify checkpoints do not rebind lyric text");
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Respond(request));
    }
}
