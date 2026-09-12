namespace LyricFloat.App.Helpers;

internal readonly record struct WorkingArea(double Left, double Top, double Width, double Height);
internal static class OverlayPosition
{
    public static (double Left, double Top) Default(WorkingArea primary, double width, double height) =>
        (primary.Left + Math.Max(0, (primary.Width - width) / 2), primary.Top + Math.Max(0, primary.Height - height - 40));

    public static (double Left, double Top) Validate(double? left, double? top, double width, double height,
        IReadOnlyList<WorkingArea> monitors, WorkingArea primary)
    {
        if (left is not { } x || top is not { } y || !double.IsFinite(x) || !double.IsFinite(y)) return Default(primary, width, height);
        var best = monitors.Select(m => (Monitor: m, Overlap: Math.Max(0, Math.Min(x + width, m.Left + m.Width) - Math.Max(x, m.Left))
            * Math.Max(0, Math.Min(y + height, m.Top + m.Height) - Math.Max(y, m.Top)))).OrderByDescending(m => m.Overlap).FirstOrDefault();
        if (best.Overlap <= 0) return Default(primary, width, height);
        var area = best.Monitor;
        // Preserve valid multi-monitor placements; only correct nearly inaccessible edges.
        var visibleWidth = Math.Min(x + width, area.Left + area.Width) - Math.Max(x, area.Left);
        var visibleHeight = Math.Min(y + height, area.Top + area.Height) - Math.Max(y, area.Top);
        var centerVisible = x + width / 2 >= area.Left + 20 && x + width / 2 <= area.Left + area.Width - 20
            && y + height / 2 >= area.Top + 20 && y + height / 2 <= area.Top + area.Height - 20;
        if (visibleWidth >= Math.Min(width, 120) && visibleHeight >= Math.Min(height, 60) && centerVisible) return (x, y);
        return (Math.Clamp(x, area.Left, area.Left + Math.Max(0, area.Width - width)),
            Math.Clamp(y, area.Top, area.Top + Math.Max(0, area.Height - height)));
    }
}
