using System.Diagnostics;
using LyricFloat.App.Models;

namespace LyricFloat.App.Services;

internal sealed class PlaybackClock(Func<double>? monotonicMilliseconds = null)
{
    private readonly Func<double> _now = monotonicMilliseconds ?? (() => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);
    private double _anchorTime;
    private double _anchorProgress;
    private int _duration;
    private bool _anchored;
    public bool IsPlaying { get; private set; }
    public double Now => _now();
    public double PositionMs => !_anchored ? 0 : Math.Clamp(_anchorProgress + (IsPlaying ? Math.Max(0, Now - _anchorTime) : 0), 0, _duration);

    public bool Synchronize(SpotifyTrack track)
    {
        var seek = _anchored && Math.Abs(PositionMs - track.ProgressMs) > 1000;
        _duration = Math.Max(0, track.DurationMs);
        _anchorProgress = Math.Clamp(track.ProgressMs, 0, _duration);
        _anchorTime = Now;
        IsPlaying = track.IsPlaying;
        _anchored = true;
        return seek;
    }

    public void Freeze()
    {
        _anchorProgress = PositionMs;
        _anchorTime = Now;
        IsPlaying = false;
    }

    public void Reset()
    {
        _anchored = false;
        IsPlaying = false;
        _anchorProgress = _duration = 0;
    }
}
