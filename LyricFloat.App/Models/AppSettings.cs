namespace LyricFloat.App.Models;

public enum OverlayDisplayMode { Minimal, ThreeLines }
public enum OverlayBackgroundMode { Transparent, Subtle }
public enum LyricFontWeight { Normal, SemiBold, Bold }
public enum LyricAlignment { Left, Center, Right }

public sealed record AppSettings
{
    public int Version { get; set; } = 1;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public bool IsOverlayLocked { get; set; }
    public bool IsOverlayVisible { get; set; } = true;
    public string FontFamily { get; set; } = "Segoe UI";
    public double CurrentFontSize { get; set; } = 32;
    public double SecondaryFontSize { get; set; } = 22;
    public LyricFontWeight FontWeight { get; set; } = LyricFontWeight.SemiBold;
    public LyricAlignment TextAlignment { get; set; } = LyricAlignment.Center;
    public double TextOpacity { get; set; } = 1;
    public double SecondaryTextOpacity { get; set; } = 0.5;
    public double BackgroundOpacity { get; set; }
    public OverlayBackgroundMode BackgroundMode { get; set; }
    public OverlayDisplayMode DisplayMode { get; set; } = OverlayDisplayMode.ThreeLines;
    public int LyricsTimingOffsetMs { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; }
}
