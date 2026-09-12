using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LyricFloat.App.Models;
using LyricFloat.App.Services;
using LyricFloat.App.ViewModels;
using LyricFloat.App.Views;

namespace LyricFloat.Tests;

internal static class Phase5WindowTests
{
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window, int index);

    public static Task<int> RunAsync()
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var count = 0;
            void Check(bool condition, string label) { if (!condition) throw new Exception("Native settings: " + label); count++; }
            var directory = Path.Combine(Path.GetTempPath(), "LyricFloat.WindowTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            OverlayController? overlay = null;
            SettingsWindow? window = null;
            SettingsService? service = null;
            SettingsViewModel? model = null;
            try
            {
                service = new SettingsService(Path.Combine(directory, "settings.json"));
                service.Update(s => { s.CurrentFontSize = 40; s.DisplayMode = OverlayDisplayMode.Minimal; s.StartMinimizedToTray = true; s.IsOverlayVisible = true; s.IsOverlayLocked = true; s.OverlayLeft = 120; s.OverlayTop = 150; });
                var lyricsWindow = new OverlayWindow();
                overlay = new OverlayController(lyricsWindow, service);
                overlay.InitializeFromSettings();
                Check(!overlay.IsOverlayVisible && overlay.IsOverlayLocked, "minimized startup overrides saved visibility and restores lock");
                Check(service.Current.IsOverlayVisible, "startup precedence does not overwrite saved visibility");
                var handle = new WindowInteropHelper(lyricsWindow).Handle;
                Check((GetWindowLongW(handle, -20) & 0x20) != 0, "locked startup native click-through");
                model = new SettingsViewModel(service, new StartupService(@"C:\Test\LyricFloat.exe", new FakeRegistry()), overlay, new LyricsCache(Path.Combine(directory, "cache")), ["Segoe UI"]);
                window = new SettingsWindow(model);
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Check(window.IsVisible && !window.Topmost && (GetWindowLongW(new WindowInteropHelper(window).Handle, -20) & 0x20) == 0, "settings remains interactive while overlay locked");
                Check(service.Current.CurrentFontSize == 40 && service.Current.DisplayMode == OverlayDisplayMode.Minimal, "settings binding preserves saved values");
                model.CurrentFontSize = 48;
                var current = (System.Windows.Controls.TextBlock)lyricsWindow.FindName("CurrentText");
                var previous = (System.Windows.Controls.TextBlock)lyricsWindow.FindName("PreviousText");
                Check(current.FontSize == 48 && previous.Visibility == Visibility.Collapsed, "live size and minimal mode");
                model.DisplayMode = OverlayDisplayMode.ThreeLines;
                Check(previous.Visibility == Visibility.Visible, "live ThreeLines mode");
                model.BackgroundMode = OverlayBackgroundMode.Subtle;
                model.BackgroundOpacity = 0.4;
                Check((GetWindowLongW(handle, -20) & 0x20) != 0 && lyricsWindow.Topmost, "appearance retains click-through and topmost");
                overlay.ShowOverlay();
                Check(Math.Abs(lyricsWindow.Left - 120) < 1 && Math.Abs(lyricsWindow.Top - 150) < 1, "stored position survives first show");
                window.Close();
                Check(lyricsWindow.IsVisible && overlay.IsOverlayLocked, "closing settings leaves overlay running");
                overlay.SetLocked(false);
                overlay.HideOverlay();
                Check(!service.Current.IsOverlayLocked && !service.Current.IsOverlayVisible, "controller state saved");
                overlay.CloseForExit();
                model.Dispose();
                service.FlushAsync().GetAwaiter().GetResult();
                Check(!new SettingsService(Path.Combine(directory, "settings.json")).Current.IsOverlayVisible, "exit does not replace saved visibility");
                completion.SetResult(count);
            }
            catch (Exception e) { completion.SetException(e); }
            finally
            {
                window?.Close(); overlay?.CloseForExit(); model?.Dispose();
                service?.FlushAsync().GetAwaiter().GetResult();
                Directory.Delete(directory, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class FakeRegistry : IStartupRegistry
    {
        public string? Read(string name) => null;
        public void Write(string name, string value) { }
        public void Delete(string name) { }
    }
}
