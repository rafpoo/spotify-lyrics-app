using System.Globalization;
using System.Text.RegularExpressions;
using LyricFloat.App.Models;

namespace LyricFloat.App.Helpers;

internal static partial class LrcParser
{
    [GeneratedRegex(@"\G\[(\d{1,8}):([0-5]\d)(?:\.(\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return [];
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var input = raw.Trim();
            var times = new List<TimeSpan>();
            var position = 0;
            while (true)
            {
                var match = TimestampPattern().Match(input, position);
                if (!match.Success) break;
                var minutes = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var fraction = match.Groups[3].Value;
                var milliseconds = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(3, '0'), CultureInfo.InvariantCulture);
                times.Add(TimeSpan.FromMilliseconds(minutes * 60000 + seconds * 1000 + milliseconds));
                position += match.Length;
            }
            // Metadata (including offset) and malformed timestamp lines are intentionally ignored.
            if (times.Count == 0) continue;
            var text = input[position..].Trim();
            if (text.StartsWith('[')) continue;
            foreach (var time in times) lines.Add(new(time, text));
        }
        // Stable sorting retains duplicate source order; lookup chooses the final duplicate.
        return lines.OrderBy(line => line.Timestamp).ToArray();
    }
}
