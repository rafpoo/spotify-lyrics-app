using LyricFloat.App.Models;

namespace LyricFloat.App.Helpers;

internal static class LyricLookup
{
    public static int FindCurrent(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        var low = 0;
        var high = lines.Count - 1;
        var current = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (lines[middle].Timestamp <= position) { current = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return current;
    }

    public static (string Previous, string Current, string Next) Select(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        var index = FindCurrent(lines, position);
        return (index > 0 ? lines[index - 1].Text : "", index >= 0 ? lines[index].Text : "",
            index + 1 < lines.Count ? lines[index + 1].Text : "");
    }
}
