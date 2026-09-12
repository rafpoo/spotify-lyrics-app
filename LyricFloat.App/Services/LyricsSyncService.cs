using LyricFloat.App.Helpers;
using LyricFloat.App.Models;

namespace LyricFloat.App.Services;

// All state is protected by one lock; the UI reads frames, while HTTP/parsing finishes off-thread.
internal sealed class LyricsSyncService(
    Func<SpotifyTrack, CancellationToken, Task<LyricsResult>> lookup,
    PlaybackClock? clock = null)
{
    private readonly object _gate = new();
    private readonly PlaybackClock _clock = clock ?? new PlaybackClock();
    private readonly List<Task> _requests = [];
    private CancellationTokenSource? _requestCancellation;
    private string? _trackKey;
    private SpotifyTrack? _track;
    private LyricsResult? _result;
    private long _generation;
    private double _songInfoUntil;
    private double _messageUntil = double.PositiveInfinity;
    private string _message = "Spotify not connected";
    private bool _suspended;
    private bool _stopped;
    private int _timingOffsetMs;

    public void SetTimingOffset(int milliseconds)
    {
        lock (_gate) _timingOffsetMs = Math.Clamp(milliseconds, -3000, 3000);
    }

    public void UpdateTrack(SpotifyTrack track)
    {
        lock (_gate)
        {
            if (_stopped) return;
            var key = LyricsCache.KeyFor(track);
            if (key != _trackKey)
            {
                CancelCurrent();
                _clock.Reset();
                _trackKey = key;
                _result = null;
                _message = "Loading lyrics…";
                _messageUntil = double.PositiveInfinity;
                _songInfoUntil = _clock.Now + 3000;
                AppLog.Write("Lyric sync reset.");
                var generation = _generation;
                var cancellation = new CancellationTokenSource();
                _requestCancellation = cancellation;
                _requests.RemoveAll(task => task.IsCompleted);
                _requests.Add(Task.Run(() => LoadAsync(track, generation, cancellation)));
            }
            _track = track;
            if (_suspended)
            {
                if (_result is not null) SetResultMessage(_result.Status);
                else { _message = "Loading lyrics…"; _messageUntil = double.PositiveInfinity; }
            }
            _suspended = false;
            if (_clock.Synchronize(track)) AppLog.Write("Playback seek detected (difference over 1000 ms).");
        }
    }

    private async Task LoadAsync(SpotifyTrack track, long generation, CancellationTokenSource cancellation)
    {
        try
        {
            var result = await lookup(track, cancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                // Identity/generation check is required even if a provider ignores cancellation.
                if (_stopped || generation != _generation || cancellation.IsCancellationRequested) return;
                _result = result;
                if (!_suspended) SetResultMessage(result.Status);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e)
        {
            AppLog.Write($"Lyrics lookup failed ({e.GetType().Name}).");
            lock (_gate)
            {
                if (!_stopped && generation == _generation)
                {
                    _result = new(LyricsStatus.Error, []);
                    if (!_suspended) SetResultMessage(LyricsStatus.Error);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_requestCancellation, cancellation)) _requestCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void SetResultMessage(LyricsStatus status)
    {
        _message = status switch
        {
            LyricsStatus.Instrumental => "♪ Instrumental ♪",
            LyricsStatus.PlainOnly => "Synced lyrics unavailable",
            LyricsStatus.NotFound => "Lyrics unavailable",
            LyricsStatus.Error => "Lyrics temporarily unavailable",
            _ => ""
        };
        _messageUntil = status == LyricsStatus.Instrumental ? double.PositiveInfinity : _clock.Now + 4000;
    }

    public void Suspend(string message)
    {
        lock (_gate)
        {
            _clock.Freeze();
            if (!_suspended) { _message = message; _messageUntil = _clock.Now + 4000; }
            _suspended = true;
        }
    }

    public void Clear(string message)
    {
        lock (_gate)
        {
            CancelCurrent();
            _clock.Reset();
            _trackKey = null;
            _track = null;
            _result = null;
            _suspended = false;
            _message = message;
            _messageUntil = double.PositiveInfinity;
            _songInfoUntil = 0;
        }
    }

    public LyricFrame GetFrame()
    {
        lock (_gate)
        {
            var info = _track is not null && _clock.Now < _songInfoUntil ? $"{_track.Name} — {_track.Artist}" : "";
            if (!_suspended && _result is { Status: LyricsStatus.Synced } synced)
            {
                var lines = LyricLookup.Select(synced.Lines, TimeSpan.FromMilliseconds(_clock.PositionMs - _timingOffsetMs));
                return new(lines.Previous, lines.Current, lines.Next, info, !_clock.IsPlaying);
            }
            return new("", _clock.Now < _messageUntil ? _message : "", "", info, _track is { IsPlaying: false });
        }
    }

    private void CancelCurrent()
    {
        _generation++;
        _requestCancellation?.Cancel();
        _requestCancellation = null;
    }

    internal Task WaitForPendingLookupsAsync()
    {
        lock (_gate) return Task.WhenAll(_requests.ToArray());
    }

    internal Task WaitForCurrentLookupAsync()
    {
        lock (_gate) return _requests.LastOrDefault() ?? Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            _stopped = true;
            CancelCurrent();
            _clock.Reset();
            tasks = _requests.ToArray();
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
