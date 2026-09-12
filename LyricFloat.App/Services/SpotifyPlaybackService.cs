using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using LyricFloat.App.Models;

namespace LyricFloat.App.Services;

internal sealed class SpotifyPlaybackService(HttpClient http, SpotifyAuthService auth)
{
    public async Task<PlaybackResult> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var access = await auth.GetAccessTokenAsync(cancellationToken);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/currently-playing");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AppLog.Write("Spotify API returned 401.");
                if (attempt == 1) throw new SpotifyAuthException();
                // A transport failure while refreshing is not proof of revoked authorization.
                // Preserve credentials and let the polling loop retry after network recovery.
                access = await auth.GetAccessTokenAsync(cancellationToken, access);
                continue;
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                AppLog.Write("Spotify API returned 429.");
                return new(null, "Spotify rate limit — retrying later", GetRetryDelay(response));
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                AppLog.Write("Spotify API returned 403.");
                return new(null, "Spotify access denied — check app access in Dashboard", TimeSpan.FromSeconds(30));
            }
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Write($"Spotify API returned {(int)response.StatusCode}.");
                return new(null, "Spotify unavailable — retrying", TimeSpan.FromSeconds(10));
            }
            return ParseResponse(response.StatusCode, response.StatusCode == HttpStatusCode.NoContent
                ? null : await response.Content.ReadAsStringAsync(cancellationToken));
        }
        throw new SpotifyAuthException();
    }

    internal static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var delay = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
            ?? TimeSpan.FromSeconds(30);
        return delay < TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : delay;
    }

    internal static PlaybackResult ParseResponse(HttpStatusCode status, string? content)
    {
        if (status == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(content)) return new(null, "Nothing playing");
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return new(null, "Nothing playing");
        var type = Text(root, "currently_playing_type");
        if (type != "track" && type != "") return new(null, "Unsupported playback item");
        if (!root.TryGetProperty("item", out var item) || item.ValueKind == JsonValueKind.Null) return new(null, "Nothing playing");
        if (type != "track" || item.ValueKind != JsonValueKind.Object) return new(null, "Unsupported playback item");
        var artists = item.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Select(x => Text(x, "name")).Where(x => x.Length > 0)) : "";
        var album = item.TryGetProperty("album", out var b) ? Text(b, "name") : "";
        return new(new SpotifyTrack
        {
            Id = Text(item, "id"), Name = Text(item, "name"), Artist = artists, Album = album,
            DurationMs = Number(item, "duration_ms"), ProgressMs = Number(root, "progress_ms"),
            IsPlaying = root.TryGetProperty("is_playing", out var playing) && playing.ValueKind == JsonValueKind.True
        }, "");
    }

    private static string Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int Number(JsonElement element, string key) => element.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? Math.Max(0, number) : 0;
}
