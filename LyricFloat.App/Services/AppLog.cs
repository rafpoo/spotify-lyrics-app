using System.Diagnostics;
using System.IO;

namespace LyricFloat.App.Services;

internal static class AppLog
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricFloat");
    private static readonly object Gate = new();

    // Callers supply event text, never exception messages or HTTP bodies from authentication.
    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                var directory = Path.Combine(DataDirectory, "logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                    File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("LyricFloat could not write its diagnostic log.");
            }
        }
    }
}
