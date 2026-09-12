# LyricFloat

A native Windows desktop lyrics overlay built with C# and WPF. No browser frontend, Electron, or backend server.

## Current checkpoint: Phase 1

This version displays **static sample text**, including “Hello LyricFloat”, in a transparent, borderless, always-on-top window. Drag the overlay with the left mouse button. Right-click it and choose **Exit LyricFloat** to quit. It is intentionally absent from the taskbar.

Spotify authentication, live playback, lyrics retrieval, click-through, tray support, persistence, settings, startup, and hotkeys are **not implemented yet**. The right-click exit is temporary until Phase 2 introduces the tray menu. Closing the overlay currently ends the application.

## Requirements and running

- Windows 10 or 11
- .NET 10 SDK to build (includes WPF support)
- .NET 10 Desktop Runtime to run the framework-dependent executable on another machine

From this repository:

```powershell
dotnet build LyricFloat.sln -c Release
dotnet run --project LyricFloat.App -c Release
```

Or run the built native executable:

```powershell
.\LyricFloat.App\bin\Release\net10.0-windows\LyricFloat.exe
```

## Phase 1 manual verification

1. Run LyricFloat and check the three sample lines appear near the bottom of the primary monitor.
2. Open Chrome or VS Code and give that application focus. Check that the lyrics stay above it and the desktop/application is visible behind the text.
3. Drag the lyric area to another position. Check that there is no title bar or resize border.
4. Right-click the overlay and choose **Exit LyricFloat**. Check the process exits.

The overlay accepts mouse input in this phase; click-through arrives in Phase 2. Position resets on launch until persistence is implemented. Always-on-top is intended for ordinary desktop windows; exclusive fullscreen applications and secure Windows screens are outside this checkpoint.

## Architecture

- `LyricFloat.sln`: solution entry point.
- `LyricFloat.App/App.xaml`: WPF application lifecycle.
- `LyricFloat.App/Views/OverlayWindow.xaml`: overlay appearance and window flags.
- `LyricFloat.App/Views/OverlayWindow.xaml.cs`: initial placement, native WPF dragging, and temporary exit command.
- `LyricFloat.App/ViewModels/OverlayViewModel.cs`: sample display content, separated from the view.

Services, models, and interop helpers will be introduced as their features are implemented, rather than filled with nonfunctional placeholders.

## Next checkpoints

After confirming the overlay works: tray and click-through; Spotify PKCE login; playback polling and local timing; LRCLIB and parser tests; synchronized lyrics; then settings, cache, startup, hotkeys, and error handling.

The planned OAuth flow uses a configurable Spotify Client ID, an external browser, a loopback callback, and Windows-protected tokens without an embedded client secret. Developer registration instructions and the exact redirect URI will be documented when that flow is implemented. There is no active OAuth configuration in Phase 1.

The planned sync engine estimates playback locally between periodic Spotify API polls and uses binary search to select lyrics. There are no keyboard shortcuts in Phase 1.

**Spotify does not provide a public lyrics API. Lyrics are retrieved from an external lyrics provider.** LRCLIB is the planned provider; this checkpoint makes no network requests.
