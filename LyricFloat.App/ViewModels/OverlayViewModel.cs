using System.ComponentModel;
using LyricFloat.App.Models;

namespace LyricFloat.App.ViewModels;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    public string Title { get; private set; } = "Spotify not connected";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public string PlaybackStatus { get; private set; } = "";
    public string InteractionHint => $"Unlocked — drag to move · Controls in the system tray\n{Title}\n{Artist}\n{Album}\n{PlaybackStatus}";
    private LyricFrame _frame = new("", "Spotify not connected", "", "", false);
    public string PreviousLyric => _frame.Previous;
    public string CurrentLyric => _frame.Current;
    public string NextLyric => _frame.Next;
    public string SongInfo => _frame.SongInfo;
    public string PauseIndicator => _frame.IsPaused ? "PAUSED" : "";
    public event PropertyChangedEventHandler? PropertyChanged;

    public void ShowLyrics(LyricFrame frame)
    {
        if (_frame == frame) return;
        var previous = _frame;
        _frame = frame;
        if (previous.Previous != frame.Previous) Changed(nameof(PreviousLyric));
        if (previous.Current != frame.Current) Changed(nameof(CurrentLyric));
        if (previous.Next != frame.Next) Changed(nameof(NextLyric));
        if (previous.SongInfo != frame.SongInfo) Changed(nameof(SongInfo));
        if (previous.IsPaused != frame.IsPaused) Changed(nameof(PauseIndicator));
    }

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public void ShowMessage(string message)
    {
        Title = message;
        Artist = Album = PlaybackStatus = "";
        Changed(nameof(Title)); Changed(nameof(Artist)); Changed(nameof(Album)); Changed(nameof(PlaybackStatus));
        Changed(nameof(InteractionHint));
    }

    public void ShowPlayback(PlaybackResult result)
    {
        if (result.Track is not { } track) { ShowMessage(result.Message); return; }
        Title = track.Name;
        Artist = track.Artist;
        Album = track.Album;
        PlaybackStatus = $"{(track.IsPlaying ? "" : "PAUSED · ")}{FormatTime(track.ProgressMs)} / {FormatTime(track.DurationMs)}";
        Changed(nameof(Title)); Changed(nameof(Artist)); Changed(nameof(Album)); Changed(nameof(PlaybackStatus));
        Changed(nameof(InteractionHint));
    }

    private static string FormatTime(int milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }
}
