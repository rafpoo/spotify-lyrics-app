namespace LyricFloat.App.Models;

public sealed class SpotifyTrack
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public int DurationMs { get; init; }
    public int ProgressMs { get; init; }
    public bool IsPlaying { get; init; }
}

public sealed record PlaybackResult(SpotifyTrack? Track, string Message, TimeSpan? RetryAfter = null);
