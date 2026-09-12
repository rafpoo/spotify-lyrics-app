using System.Diagnostics;
using System.IO;

namespace LyricFloat.App.Services;

internal static class AppLog
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricFloat");
    private static readonly object Gate = new();

    // Callers supply curated event text, never exception messages or HTTP response bodies.
    public static void Write(string message, string level = "INFO", string component = "LyricFloat")
    {
        lock (Gate)
        {
            try
            {
                var directory = Path.Combine(DataDirectory, "logs");
                Directory.CreateDirectory(directory);
                Append(directory, message, level, component);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("LyricFloat could not write its diagnostic log.");
            }
        }
    }

    internal static void Append(string directory, string message, string level, string component, long limit = 2_000_000)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "app.log");
        if (File.Exists(path) && new FileInfo(path).Length >= limit)
        {
            for (var i = 2; i >= 1; i--)
                if (File.Exists(path + "." + i)) File.Move(path + "." + i, path + "." + (i + 1), true);
            File.Move(path, path + ".1", true);
        }
        var safe = message.Replace('\r', ' ').Replace('\n', ' ');
        if (safe.Length > 2048) safe = safe[..2048];
        File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{level}] [{component}] {safe}{Environment.NewLine}");
    }

    internal static string ExceptionDetails(Exception exception)
    {
        // Method names provide useful fault locations without messages, local variables, URLs or source paths.
        var methods = new StackTrace(exception, false).GetFrames()?.Take(12).Select(f =>
        {
            var method = f.GetMethod();
            return $"{method?.DeclaringType?.FullName}.{method?.Name}";
        }) ?? [];
        return $"ExceptionType={exception.GetType().FullName}; HResult={exception.HResult:X8}; Frames={string.Join(" <- ", methods)}";
    }

    public static void Exception(string component, Exception exception, bool fatal = false) =>
        Write(ExceptionDetails(exception), fatal ? "FATAL" : "ERROR", component);
}
