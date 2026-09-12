using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LyricFloat.App.Helpers;
using LyricFloat.App.Models;
using LyricFloat.App.Services;

namespace LyricFloat.Tests;

internal static class Program
{
    private static int _passed;
    private static readonly string TestDirectory = Path.Combine(Path.GetTempPath(), "LyricFloat.Tests-" + Guid.NewGuid().ToString("N"));
    private const string ClientId = "0123456789abcdef0123456789abcdef";
    private const string TrackJson = """
        {"progress_ms":84321,"is_playing":true,"currently_playing_type":"track","item":{
          "id":"abc123","name":"Iris","duration_ms":289533,"artists":[{"name":"Goo Goo Dolls"}],
          "album":{"name":"Dizzy Up the Girl"}}}
        """;

    private static async Task<int> Main()
    {
        Directory.CreateDirectory(TestDirectory);
        try
        {
            TestPkce();
            TestPlaybackParser();
            await TestSecureStorageAsync();
            await TestRefreshAsync();
            await TestPlaybackHttpAsync();
            await TestLoginAsync();
            await TestLoopbackAsync();
            _passed += await Phase4Tests.RunAsync();
            _passed += await Phase5Tests.RunAsync();
            _passed += await Phase5WindowTests.RunAsync();
            Console.WriteLine($"PASS: {_passed} assertions; no Spotify server requests.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally { Directory.Delete(TestDirectory, true); }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _passed++;
    }

    private static async Task ThrowsAsync<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); }
        catch (T) { _passed++; return; }
        throw new Exception(name);
    }

    private static void TestPkce()
    {
        // RFC 7636 Appendix B.
        Check(PkceHelper.CreateChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") ==
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "RFC S256 vector");
        var verifiers = Enumerable.Range(0, 128).Select(_ => PkceHelper.CreateVerifier()).ToArray();
        Check(verifiers.Distinct().Count() == verifiers.Length, "independent verifier samples");
        Check(verifiers.All(v => v.Length == 86 && v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')), "512-bit verifier format");
        var challenge = PkceHelper.CreateChallenge(verifiers[0]);
        Check(challenge.Length == 43 && !challenge.Contains('=') && !challenge.Contains('+') && !challenge.Contains('/'), "unpadded Base64URL challenge");
        var state = PkceHelper.CreateState();
        Check(state.Length == 43 && state != PkceHelper.CreateState(), "256-bit random state");
        Check(PkceHelper.ValidateState(state, state), "matching state");
        Check(!PkceHelper.ValidateState(state, null) && !PkceHelper.ValidateState(state, state + "x"), "mismatching/missing state rejected");
        var query = LoopbackCallbackListener.ParseQuery("code=a%2Bb&state=xyz");
        Check(query["code"] == "a+b" && query["state"] == "xyz", "callback decoding");
    }

    private static void TestPlaybackParser()
    {
        var track = SpotifyPlaybackService.ParseResponse(HttpStatusCode.OK, TrackJson).Track!;
        Check(track.Id == "abc123" && track.Name == "Iris", "track identity");
        Check(track.Artist == "Goo Goo Dolls" && track.Album == "Dizzy Up the Girl", "artist and album");
        Check(track.ProgressMs == 84321 && track.DurationMs == 289533 && track.IsPlaying, "playback numbers");
        Check(SpotifyPlaybackService.ParseResponse(HttpStatusCode.NoContent, null).Message == "Nothing playing", "204");
        Check(SpotifyPlaybackService.ParseResponse(HttpStatusCode.OK, "{\"item\":null,\"currently_playing_type\":\"track\"}").Track is null, "null item");
        Check(SpotifyPlaybackService.ParseResponse(HttpStatusCode.OK, "{}").Message == "Nothing playing", "missing item");
        Check(SpotifyPlaybackService.ParseResponse(HttpStatusCode.OK, TrackJson.Replace("[{\"name\":\"Goo Goo Dolls\"}]", "[{\"name\":\"A\"},{\"name\":\"B\"}]")).Track!.Artist == "A, B", "multiple artists");
        foreach (var type in new[] { "episode", "ad", "unknown" })
            Check(SpotifyPlaybackService.ParseResponse(HttpStatusCode.OK, TrackJson.Replace("\"track\"", $"\"{type}\"")).Message == "Unsupported playback item", type);
        Check(!SpotifyPlaybackService.ParseResponse(HttpStatusCode.OK, TrackJson.Replace("true", "false")).Track!.IsPlaying, "paused");
    }

    private static SpotifyToken Token(bool expired = false) => new()
    {
        ClientId = ClientId, AccessToken = "synthetic-access", RefreshToken = "synthetic-refresh",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 30), Scope = "user-read-currently-playing"
    };

    private static async Task TestSecureStorageAsync()
    {
        var path = Path.Combine(TestDirectory, "storage.dpapi");
        var store = new SecureTokenStore(path);
        Check(await store.LoadAsync(default) is null, "missing token store");
        await store.SaveAsync(Token(), default);
        Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)).Contains("synthetic-refresh"), "token file encrypted");
        Check((await store.LoadAsync(default))!.RefreshToken == "synthetic-refresh", "DPAPI round trip");
        await store.DeleteAsync();
        Check(!File.Exists(path), "token deletion");
        await File.WriteAllTextAsync(path, "corrupted token file");
        await ThrowsAsync<CryptographicException>(() => store.LoadAsync(default), "corrupt ciphertext rejected");
    }

    private static HttpResponseMessage TokenResponse(string? refresh = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["access_token"] = "replacement-access", ["token_type"] = "Bearer", ["expires_in"] = 3600
        }.Concat(refresh is null ? [] : new[] { new KeyValuePair<string, object>("refresh_token", refresh) }).ToDictionary()))
    };

    private static async Task TestRefreshAsync()
    {
        var store = new SecureTokenStore(Path.Combine(TestDirectory, "refresh.dpapi"));
        await store.SaveAsync(Token(true), default);
        var calls = 0;
        using var handler = new FakeHandler(async request =>
        {
            Interlocked.Increment(ref calls);
            var form = await request.Content!.ReadAsStringAsync();
            Check(form.Contains("grant_type=refresh_token") && !form.Contains("client_secret") && request.Headers.Authorization is null, "PKCE refresh form without secret/basic auth");
            await Task.Delay(20);
            return TokenResponse();
        });
        using var http = new HttpClient(handler);
        using var auth = new SpotifyAuthService(http, store, () => new() { ClientId = ClientId });
        await auth.RestoreAsync(default);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => auth.GetAccessTokenAsync(default)));
        Check(calls == 1, "concurrent refresh serialized");
        Check((await store.LoadAsync(default))!.RefreshToken == "synthetic-refresh", "missing replacement refresh preserved");
        handler.Respond = _ => Task.FromResult(TokenResponse("rotated-refresh"));
        await auth.GetAccessTokenAsync(default, "replacement-access");
        Check((await store.LoadAsync(default))!.RefreshToken == "rotated-refresh", "refresh token rotation saved");
        handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        await ThrowsAsync<SpotifyAuthException>(() => auth.GetAccessTokenAsync(default, "replacement-access"), "rejected refresh requires login");
        Check(!auth.IsConnected && await store.LoadAsync(default) is null, "invalid grant clears stored session");
    }

    private static async Task TestPlaybackHttpAsync()
    {
        var store = new SecureTokenStore(Path.Combine(TestDirectory, "playback.dpapi"));
        await store.SaveAsync(Token(), default);
        var calls = 0;
        using var handler = new FakeHandler(request =>
        {
            calls++;
            Check(request.RequestUri!.Host is "api.spotify.com" or "accounts.spotify.com", "official endpoint");
            return Task.FromResult(calls switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                2 => TokenResponse(),
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TrackJson) }
            });
        });
        using var http = new HttpClient(handler);
        using var auth = new SpotifyAuthService(http, store, () => new() { ClientId = ClientId });
        await auth.RestoreAsync(default);
        var playback = new SpotifyPlaybackService(http, auth);
        Check((await playback.GetCurrentAsync(default)).Track!.Name == "Iris" && calls == 3, "401 refresh and one retry");
        calls = 0;
        handler.Respond = request =>
        {
            calls++;
            return Task.FromResult(request.RequestUri!.Host == "accounts.spotify.com" ? TokenResponse() : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        };
        await ThrowsAsync<SpotifyAuthException>(() => playback.GetCurrentAsync(default), "second 401 fails");
        Check(calls == 3, "no infinite 401 retry");
        handler.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return Task.FromResult(response);
        };
        Check((await playback.GetCurrentAsync(default)).RetryAfter == TimeSpan.FromSeconds(45), "Retry-After respected");
        handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        Check((await playback.GetCurrentAsync(default)).Message.Contains("access denied"), "403 handled");
        handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        Check((await playback.GetCurrentAsync(default)).Message == "Nothing playing", "HTTP 204 handled");
        handler.Respond = _ => throw new HttpRequestException("synthetic network failure");
        await ThrowsAsync<HttpRequestException>(() => playback.GetCurrentAsync(default), "network error reaches retry loop");
        await auth.LogoutAsync();
        Check(!auth.IsConnected && await store.LoadAsync(default) is null, "logout clears session");
    }

    private static async Task TestLoginAsync()
    {
        var store = new SecureTokenStore(Path.Combine(TestDirectory, "login.dpapi"));
        using var browser = new HttpClient(new HttpClientHandler { UseProxy = false });
        Task<HttpResponseMessage>? callback = null;
        Dictionary<string, string>? authorization = null;
        var browserLaunches = 0;
        var exchanges = 0;
        var wrongState = false;
        using var handler = new FakeHandler(async request =>
        {
            exchanges++;
            var form = LoopbackCallbackListener.ParseQuery(await request.Content!.ReadAsStringAsync());
            Check(form["grant_type"] == "authorization_code" && form["code"] == "synthetic-code", "authorization code exchange form");
            Check(form["redirect_uri"] == SpotifyConfiguration.CallbackUri && form["client_id"] == ClientId, "exact token redirect and client ID");
            Check(PkceHelper.CreateChallenge(form["code_verifier"]) == authorization!["code_challenge"], "original verifier matches challenge");
            Check(!form.ContainsKey("client_secret") && request.Headers.Authorization is null, "no client secret in login exchange");
            return TokenResponse("login-refresh");
        });
        using var http = new HttpClient(handler);
        using var auth = new SpotifyAuthService(http, store, () => new() { ClientId = ClientId }, url =>
        {
            browserLaunches++;
            var uri = new Uri(url);
            authorization = LoopbackCallbackListener.ParseQuery(uri.Query[1..]);
            Check(uri.Host == "accounts.spotify.com" && authorization["code_challenge_method"] == "S256", "official S256 authorization URL");
            Check(authorization["response_type"] == "code" && authorization["scope"] == "user-read-currently-playing user-read-playback-state", "read-only authorization scopes");
            callback = browser.GetAsync(SpotifyConfiguration.CallbackUri + "?code=synthetic-code&state=" + (wrongState ? "wrong" : authorization["state"]));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var login = auth.LoginAsync(timeout.Token);
        await auth.LoginAsync(timeout.Token);
        await login;
        using (var response = await callback!)
            Check(response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("connected successfully"), "success browser response after exchange");
        Check(browserLaunches == 1 && exchanges == 1 && auth.IsConnected, "duplicate login ignored");
        Check((await store.LoadAsync(default))!.RefreshToken == "login-refresh", "login persisted securely");
        await auth.LogoutAsync();
        wrongState = true;
        await ThrowsAsync<SpotifyAuthException>(() => auth.LoginAsync(timeout.Token), "OAuth state mismatch rejects login");
        using (var response = await callback!)
            Check(!response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("connection failed"), "failed browser response");
        Check(exchanges == 1 && !auth.IsConnected && await store.LoadAsync(default) is null, "state mismatch never exchanges code");
    }

    private static async Task TestLoopbackAsync()
    {
        using (var listener = new LoopbackCallbackListener())
        {
            listener.Start();
            using var second = new LoopbackCallbackListener();
            await ThrowsAsync<InvalidOperationException>(() => { second.Start(); return Task.CompletedTask; }, "occupied callback port handled");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await ThrowsAsync<OperationCanceledException>(() => listener.ReceiveAsync(_ => Task.CompletedTask, cancellation.Token), "callback cancellation");
        }
        using var next = new LoopbackCallbackListener();
        next.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accepted = false;
        var pending = next.ReceiveAsync(query => { accepted = query["state"] == "test-state"; return Task.CompletedTask; }, timeout.Token);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        var text = await http.GetStringAsync("http://127.0.0.1:43821/callback?code=test-code&state=test-state", timeout.Token);
        await pending;
        Check(accepted && text.Contains("connected successfully"), "loopback callback and browser success response");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Respond(request);
    }
}

