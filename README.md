# LyricFloat

Native C# / WPF desktop overlay. Current checkpoint: **Phase 2**, with static sample lyrics.

## Build and run

Requires Windows 10/11 and .NET 10 SDK. Other machines need the .NET 10 Desktop Runtime for the framework-dependent executable.

```powershell
dotnet build -c Release
dotnet run --project LyricFloat.App -c Release
```

Executable: `LyricFloat.App/bin/Release/net10.0-windows/LyricFloat.exe`. Exit the existing instance before rebuilding or launching another copy.

## Controls

Starts visible and unlocked. Left-drag to move. Hover while unlocked for a small drag hint. Right-click no longer exits.

Find **LyricFloat** in the system tray, possibly inside the hidden-icons overflow. The menu offers Show/Hide Lyrics, Lock/Unlock Overlay, and Exit. Double-click also toggles visibility. The tray tooltip shows the lock state.

| Shortcut | Action |
| --- | --- |
| Ctrl + Shift + L | Toggle visibility |
| Ctrl + Shift + K | Toggle lock / click-through |

Hotkeys work globally, even while hidden. Registration conflicts are reported through .NET diagnostic tracing; the tray remains usable.

Lock passes mouse clicks through and disables dragging. Unlock restores mouse interaction. Hide/show preserves position and lock state within this session. Showing does not activate or reposition the overlay. Topmost stays enabled. Closing the overlay hides it; tray Exit unregisters hotkeys, disposes the tray icon, closes the overlay, and shuts down WPF.

## Architecture

- `App.xaml` / `App.xaml.cs`: explicit application lifetime and cleanup.
- `Views/OverlayWindow.*`: transparent sample lyrics, placement, and unlocked dragging.
- `ViewModels/OverlayViewModel.cs`: sample content.
- `Services/OverlayController.cs`: shared actions; window state is the single source of truth.
- `Services/TrayIconService.cs`: built-in NotifyIcon with a dynamic menu.
- `Services/HotkeyService.cs`: independent message-only window and global hotkey registration/cleanup.
- `Helpers/OverlayWindowInterop.cs`: extended window styles; preserves WPF's layered style and unrelated flags.

No third-party packages or network services.

## Manual acceptance tests

1. Launch: confirm visible sample lyrics, left-drag movement, and topmost behavior over Chrome/VS Code.
2. Tray Lock Overlay: click a button underneath the lyric text. Confirm the underlying app receives the click and dragging cannot move the overlay.
3. Tray Unlock Overlay: confirm dragging returns and topmost remains.
4. Move and lock, then Hide Lyrics: confirm the overlay disappears but the tray and process remain. Show Lyrics: confirm position, lock, and topmost remain. Repeat unlocked.
5. Focus Chrome/VS Code. Press Ctrl+Shift+L twice: confirm hide/show without focusing LyricFloat.
6. With that app focused, press Ctrl+Shift+K: confirm click-through; press again and confirm dragging returns. Also change lock while hidden, then show and verify the new state.
7. Unlock, click the overlay, then Alt+F4: confirm it hides but stays in the tray. Show it again.
8. Tray Exit: confirm overlay, tray icon, and process disappear. Relaunch and check both hotkeys to confirm cleanup.

## Limitations and next phase

State is not persisted across restarts. Topmost targets ordinary desktop windows, not exclusive fullscreen or secure Windows screens. The tray uses a standard Windows application icon. Launch only one instance when testing hotkeys.

Spotify, playback, LRCLIB, synchronization, settings, cache, and startup are not implemented. Phase 3 waits for Phase 2 verification.

**Spotify does not provide a public lyrics API. Lyrics are retrieved from an external lyrics provider.** LRCLIB is planned; this version makes no lyrics requests.
