using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using LyricFloat.App.ViewModels;

namespace LyricFloat.App.Services;

internal sealed class SpotifySessionController : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SpotifyAuthService _auth;
    private readonly SpotifyPlaybackService _playback;
    private readonly OverlayViewModel _viewModel;
    private readonly Dispatcher _dispatcher;
    private readonly LyricsSyncService _lyrics;
    private readonly DispatcherTimer _lyricTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pollCancellation;
    private Task _operation = Task.CompletedTask;
    private Task _polling = Task.CompletedTask;
    private bool _disposed;
    private bool _stopping;
    public bool IsConnected => _auth.IsConnected;
    public bool IsBusy { get; private set; }
    public event EventHandler? StateChanged;

    public SpotifySessionController(OverlayViewModel viewModel, Dispatcher dispatcher, LyricsCache? cache = null)
    {
        _viewModel = viewModel;
        _dispatcher = dispatcher;
        _auth = new SpotifyAuthService(_http, new SecureTokenStore());
        _playback = new SpotifyPlaybackService(_http, _auth);
        var lyricsService = new LyricsService(_http, cache ?? new LyricsCache());
        _lyrics = new LyricsSyncService(lyricsService.GetAsync);
        _lyricTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        { Interval = TimeSpan.FromMilliseconds(75) };
        _lyricTimer.Tick += OnLyricTick;
        _lyricTimer.Start();
    }

    private void OnLyricTick(object? sender, EventArgs e) => _viewModel.ShowLyrics(_lyrics.GetFrame());

    public void SetTimingOffset(int milliseconds)
    {
        _lyrics.SetTimingOffset(milliseconds);
        OnLyricTick(null, EventArgs.Empty);
    }

    public void Initialize() => BeginOperation(RestoreAsync);
    public void Connect() => BeginOperation(ConnectAsync);
    public void Disconnect() => BeginOperation(DisconnectAsync);

    private void BeginOperation(Func<Task> action)
    {
        if (IsBusy || _stopping) return;
        IsBusy = true;
        Notify();
        _operation = RunOperationAsync(action);
    }

    private async Task RunOperationAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException)
        {
            if (!_stopping) ShowMessage("Spotify connection timed out — try Connect Spotify again");
            AppLog.Write("Spotify authentication cancelled or timed out.");
        }
        catch (Exception e)
        {
            AppLog.Write($"Spotify operation failed ({e.GetType().Name}).");
            ShowMessage(e switch
            {
                SpotifyAuthException => "Spotify authorization rejected — connect again",
                SpotifyRateLimitException => "Spotify rate limit — try connecting later",
                InvalidOperationException => e.Message,
                CryptographicException => "Stored Spotify login cannot be read — connect again",
                IOException or UnauthorizedAccessException => "Cannot access Spotify token/config files — check permissions",
                _ => "Spotify connection failed — check your network and try again"
            });
        }
        finally { IsBusy = false; Notify(); }
    }

    private async Task RestoreAsync()
    {
        await _auth.RestoreAsync(_lifetime.Token);
        if (IsConnected) StartPolling();
    }

    private async Task ConnectAsync()
    {
        if (IsConnected) return;
        ShowMessage("Connecting Spotify — finish sign-in in your browser");
        await _auth.LoginAsync(_lifetime.Token);
        if (IsConnected) StartPolling();
    }

    private void StartPolling()
    {
        _pollCancellation?.Dispose();
        _pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _polling = PollAsync(_pollCancellation.Token);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        AppLog.Write("Spotify polling started.");
        string? previousTrackId = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(2);
                try
                {
                    var result = await _playback.GetCurrentAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (result.Track is { } track && track.Id != previousTrackId)
                        AppLog.Write($"Track changed: {track.Name} — {track.Artist}");
                    previousTrackId = result.Track?.Id;
                    await _dispatcher.InvokeAsync(() =>
                    {
                        _viewModel.ShowPlayback(result);
                        if (result.Track is { } current) _lyrics.UpdateTrack(current);
                        else if (result.RetryAfter is not null) _lyrics.Suspend(result.Message);
                        else _lyrics.Clear(result.Message);
                        OnLyricTick(null, EventArgs.Empty);
                    });
                    delay = result.RetryAfter ?? delay;
                }
                catch (SpotifyAuthException)
                {
                    try { await _auth.LogoutAsync(); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    { AppLog.Write("Could not delete rejected Spotify credentials."); }
                    ShowMessage("Spotify not connected — authorization expired; connect again");
                    Notify();
                    return;
                }
                catch (SpotifyRateLimitException e)
                {
                    delay = e.Delay;
                    _lyrics.Suspend("Spotify rate limit — retrying later");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException
                    or IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or KeyNotFoundException)
                {
                    AppLog.Write($"Network request or playback processing failed ({e.GetType().Name}).");
                    _lyrics.Suspend("Spotify temporarily unavailable — retrying");
                    delay = TimeSpan.FromSeconds(5);
                }
                // Sequential Spotify requests; lyric interpolation uses only the separate local clock.
                await Task.Delay(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { AppLog.Write("Spotify polling stopped."); }
    }

    private async Task DisconnectAsync()
    {
        _pollCancellation?.Cancel();
        await _polling;
        try { await _auth.LogoutAsync(); }
        finally { ShowMessage("Spotify not connected"); Notify(); }
    }

    private void ShowMessage(string message)
    {
        if (!_stopping) _dispatcher.Invoke(() =>
        {
            _lyrics.Clear(message);
            _viewModel.ShowMessage(message);
            OnLyricTick(null, EventArgs.Empty);
        });
    }

    private void Notify()
    {
        if (!_stopping) _dispatcher.Invoke(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    public async Task StopAsync()
    {
        _stopping = true;
        _lyricTimer.Stop();
        _lifetime.Cancel();
        _pollCancellation?.Cancel();
        var lyricsStopped = _lyrics.StopAsync();
        await _operation;
        await _polling;
        await lyricsStopped;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lyricTimer.Stop();
        _lyricTimer.Tick -= OnLyricTick;
        _ = _lyrics.StopAsync();
        _lifetime.Cancel();
        _pollCancellation?.Cancel();
        _http.Dispose();
        _pollCancellation?.Dispose();
        _lifetime.Dispose();
        _auth.Dispose();
    }
}
