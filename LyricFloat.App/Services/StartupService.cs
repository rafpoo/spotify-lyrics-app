using Microsoft.Win32;

namespace LyricFloat.App.Services;

internal interface IStartupRegistry
{
    string? Read(string name);
    void Write(string name, string value);
    void Delete(string name);
}

internal sealed class StartupService(string executable, IStartupRegistry? registry = null)
{
    internal const string ValueName = "LyricFloat";
    private readonly IStartupRegistry _registry = registry ?? new StartupRegistry();
    public string Command => QuoteExecutable(executable);
    internal static string QuoteExecutable(string path)
    {
        if (!System.IO.Path.IsPathFullyQualified(path) || path.Contains('"')) throw new ArgumentException("Invalid startup executable path.");
        return $"\"{path}\"";
    }
    public bool IsEnabled() => string.Equals(_registry.Read(ValueName)?.Trim(), Command, StringComparison.OrdinalIgnoreCase);
    public void SetEnabled(bool enabled)
    {
        if (enabled) _registry.Write(ValueName, Command);
        else _registry.Delete(ValueName);
    }

    private sealed class StartupRegistry : IStartupRegistry
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public string? Read(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name) as string;
        }
        public void Write(string name, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
            key.SetValue(name, value, RegistryValueKind.String);
        }
        public void Delete(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            key?.DeleteValue(name, false);
        }
    }
}
