namespace LyricFloat.App.Models;

public sealed record LyricLine(TimeSpan Timestamp, string Text);
public enum LyricsStatus { Synced, PlainOnly, Instrumental, NotFound, Error }
public sealed record LyricsResult(LyricsStatus Status, IReadOnlyList<LyricLine> Lines);
public sealed record LyricFrame(string Previous, string Current, string Next, string SongInfo, bool IsPaused);

internal sealed class LrclibLyricsResponse
{
    public long Id { get; init; }
    public bool Instrumental { get; init; }
    public string? PlainLyrics { get; init; }
    public string? SyncedLyrics { get; init; }
}
