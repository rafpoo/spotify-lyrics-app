using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LyricFloat.App.Helpers;

internal static class OverlayWindowInterop
{
    private const int ExtendedStyle = -20;
    private const int Transparent = 0x00000020;
    private const int Layered = 0x00080000;
    private const int NoActivate = 0x08000000;

    public static void SetClickThrough(Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        Marshal.SetLastPInvokeError(0);
        var style = GetWindowLong(handle, ExtendedStyle);
        ThrowIfFailed(style);
        // WPF owns the layered style for AllowsTransparency; preserve all other flags.
        var updated = enabled
            ? style | Layered | Transparent | NoActivate
            : style & ~(Transparent | NoActivate);
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLong(handle, ExtendedStyle, updated);
        ThrowIfFailed(previous);
    }

    private static void ThrowIfFailed(int result)
    {
        var error = Marshal.GetLastPInvokeError();
        if (result == 0 && error != 0) throw new Win32Exception(error);
    }

    // Window styles are 32-bit values on both x86 and x64.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);
}
