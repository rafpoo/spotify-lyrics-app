using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LyricFloat.App.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const uint Modifiers = 0x0002 | 0x0004 | 0x4000; // Control, Shift, no repeat.
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private bool _disposed;

    public HotkeyService(Action toggleVisibility, Action toggleLock)
    {
        // Independent message-only window keeps receiving hotkeys while the overlay is hidden.
        _source = new HwndSource(new HwndSourceParameters("LyricFloat hotkeys")
        {
            ParentWindow = new nint(-3),
            WindowStyle = 0
        });
        _source.AddHook(OnMessage);
        Register(1, 0x4C, toggleVisibility);
        Register(2, 0x4B, toggleLock);
    }

    private void Register(int id, uint key, Action action)
    {
        if (RegisterHotKey(_source.Handle, id, Modifiers, key)) _actions.Add(id, action);
        else AppLog.Write($"Hotkey {id} unavailable (Win32 error {Marshal.GetLastWin32Error()}); use the tray menu.", "WARN", "Hotkeys");
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == HotkeyMessage && _actions.TryGetValue((int)wParam, out var action))
        {
            handled = true;
            action();
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _actions.Keys)
        {
            if (!UnregisterHotKey(_source.Handle, id))
                AppLog.Write($"Could not unregister hotkey {id}: {Marshal.GetLastWin32Error()}", "WARN", "Hotkeys");
        }
        _actions.Clear();
        _source.RemoveHook(OnMessage);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
