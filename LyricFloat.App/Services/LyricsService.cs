using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using LyricFloat.App.Helpers;
using LyricFloat.App.Models;

namespace LyricFloat.App.Services;

internal sealed class LyricsService(HttpClient http, LyricsCache cache)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static Uri BuildRequestUri(SpotifyTrack track)
    {
        var parameters = new Dictionary<string, string>
        {
            ["track_name"] = track.Name, ["artist_name"] = track.Artist, ["album_name"] = track.Album,
            ["duration"] = (track.DurationMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture)
        };
        return new Uri("https://lrclib.net/api/get?" + string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")));
    }

    public async Task<LyricsResult> GetAsync(SpotifyTrack track, CancellationToken cancellationToken)
    {
        AppLog.Write("Lyrics lookup started.");
        try
        {
            var generation = cache.Generation;
            var cached = await cache.LoadAsync(track, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                AppLog.Write("Lyrics cache hit.");
                return ConvertResult(cached);
            }
            AppLog.Write("Lyrics cache miss.");
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(track));
            request.Headers.UserAgent.ParseAdd("LyricFloat/0.4");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AppLog.Write("Lyrics not found.");
                return new(LyricsStatus.NotFound, []);
            }
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Write($"Lyrics request failed (HTTP {(int)response.StatusCode}).");
                return new(LyricsStatus.Error, []);
            }
            var data = JsonSerializer.Deserialize<LrclibLyricsResponse>(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), JsonOptions);
            if (data is null) return new(LyricsStatus.Error, []);
            var result = ConvertResult(data);
            if (result.Status is LyricsStatus.Synced or LyricsStatus.PlainOnly or LyricsStatus.Instrumental)
                await cache.SaveAsync(track, data, cancellationToken, generation).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { AppLog.Write("Lyrics request cancelled."); throw; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException)
        {
            AppLog.Write($"Lyrics request failed ({e.GetType().Name}).");
            return new(LyricsStatus.Error, []);
        }
    }

    internal static LyricsResult ConvertResult(LrclibLyricsResponse data)
    {
        if (data.Instrumental) { AppLog.Write("Instrumental track."); return new(LyricsStatus.Instrumental, []); }
        var lines = LrcParser.Parse(data.SyncedLyrics);
        if (lines.Count > 0)
        {
            AppLog.Write($"Synced lyrics found. LRC parsed: {lines.Count} lines.");
            return new(LyricsStatus.Synced, lines);
        }
        if (!string.IsNullOrWhiteSpace(data.PlainLyrics))
        { AppLog.Write("Plain lyrics only."); return new(LyricsStatus.PlainOnly, []); }
        AppLog.Write("Lyrics not found.");
        return new(LyricsStatus.NotFound, []);
    }
}
