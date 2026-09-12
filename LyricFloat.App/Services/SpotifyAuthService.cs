using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using LyricFloat.App.Helpers;
using LyricFloat.App.Models;

namespace LyricFloat.App.Services;

internal sealed class SpotifyAuthException : Exception
{
    public SpotifyAuthException() : base("Spotify authorization expired or was rejected. Connect Spotify again.") { }
}

internal sealed class SpotifyRateLimitException(TimeSpan delay) : Exception
{
    public TimeSpan Delay { get; } = delay;
}

internal sealed class SpotifyAuthService(HttpClient http, SecureTokenStore store,
    Func<SpotifyConfiguration>? loadConfiguration = null, Action<string>? openBrowser = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SpotifyToken? _token;
    private SpotifyConfiguration? _configuration;
    private DateTimeOffset _refreshNotBefore;
    public bool IsConnected => _token is not null;
    private const string Scopes = "user-read-currently-playing user-read-playback-state";

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var saved = await store.LoadAsync(cancellationToken);
            if (saved is null) return;
            _configuration = (loadConfiguration ?? SpotifyConfiguration.Load)();
            if (saved is not null && saved.ClientId == _configuration.ClientId && !string.IsNullOrWhiteSpace(saved.RefreshToken))
                _token = saved;
        }
        finally { _gate.Release(); }
    }

    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            _configuration = (loadConfiguration ?? SpotifyConfiguration.Load)();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            using var listener = new LoopbackCallbackListener();
            listener.Start();
            var verifier = PkceHelper.CreateVerifier();
            var state = PkceHelper.CreateState();
            var parameters = new Dictionary<string, string>
            {
                ["response_type"] = "code", ["client_id"] = _configuration.ClientId,
                ["redirect_uri"] = SpotifyConfiguration.CallbackUri, ["scope"] = Scopes,
                ["state"] = state, ["code_challenge"] = PkceHelper.CreateChallenge(verifier),
                ["code_challenge_method"] = "S256"
            };
            var url = "https://accounts.spotify.com/authorize?" + string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
            AppLog.Write("Spotify authentication started.");
            if (openBrowser is not null) openBrowser(url);
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            await listener.ReceiveAsync(async query =>
            {
                if (!query.TryGetValue("state", out var actualState) || !PkceHelper.ValidateState(state, actualState)
                    || query.ContainsKey("error") || !query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                {
                    AppLog.Write("Spotify authentication rejected (state mismatch or consent denied).");
                    throw new SpotifyAuthException();
                }
                var token = await RequestTokenAsync(new()
                {
                    ["grant_type"] = "authorization_code", ["code"] = code,
                    ["redirect_uri"] = SpotifyConfiguration.CallbackUri,
                    ["client_id"] = _configuration.ClientId, ["code_verifier"] = verifier
                }, null, timeout.Token);
                await store.SaveAsync(token, timeout.Token);
                _token = token;
                AppLog.Write("Spotify authentication succeeded.");
            }, timeout.Token);
        }
        catch (Exception e)
        {
            AppLog.Write($"Spotify authentication failed ({e.GetType().Name}).");
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, string? rejectedAccessToken = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is null) throw new SpotifyAuthException();
            var force = rejectedAccessToken is not null && rejectedAccessToken == _token.AccessToken;
            if (!force && _token.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60)) return _token.AccessToken;
            if (_refreshNotBefore > DateTimeOffset.UtcNow)
                throw new SpotifyRateLimitException(_refreshNotBefore - DateTimeOffset.UtcNow);
            try
            {
                var refreshed = await RequestTokenAsync(new()
                {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = _token.RefreshToken,
                    ["client_id"] = _token.ClientId
                }, _token, cancellationToken);
                await store.SaveAsync(refreshed, cancellationToken);
                _token = refreshed;
                AppLog.Write("Spotify token refreshed.");
                return _token.AccessToken;
            }
            catch (SpotifyAuthException)
            {
                _token = null;
                await store.DeleteAsync();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<SpotifyToken> RequestTokenAsync(Dictionary<string, string> values, SpotifyToken? previous, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(values);
        using var response = await http.PostAsync("https://accounts.spotify.com/api/token", form, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = SpotifyPlaybackService.GetRetryDelay(response);
            _refreshNotBefore = DateTimeOffset.UtcNow + delay;
            AppLog.Write("Spotify token endpoint returned 429.");
            throw new SpotifyRateLimitException(delay);
        }
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new SpotifyAuthException();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Spotify token endpoint unavailable.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        var access = root.GetProperty("access_token").GetString();
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : previous?.RefreshToken;
        var type = root.GetProperty("token_type").GetString();
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) || !string.Equals(type, "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new SpotifyAuthException();
        return new SpotifyToken
        {
            AccessToken = access, RefreshToken = refresh, TokenType = "Bearer", ClientId = values["client_id"],
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
            Scope = root.TryGetProperty("scope", out var s) ? s.GetString() ?? Scopes : previous?.Scope ?? Scopes
        };
    }

    public async Task LogoutAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _token = null;
            await store.DeleteAsync();
            AppLog.Write("Spotify disconnected.");
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
