# LyricFloat 0.1.0 for Windows x64

LyricFloat displays synchronized lyrics above your other desktop applications.

## Install and connect

Use the per-user installer when available, or extract the entire portable ZIP to a permanent folder and launch LyricFloat.exe. Keep all included runtime files together. The self-contained build does not require the .NET SDK or a separate .NET installation.

Find LyricFloat in the Windows tray (including the hidden-icons overflow). Open Settings → Spotify. Enter your Spotify Developer application's Client ID and save it. No client secret is needed. Register this exact callback in the Spotify Developer Dashboard:

`http://127.0.0.1:43821/callback`

Choose Connect Spotify, authorize in your normal browser, then play a song in Spotify. Your Spotify account must be allowed to use that developer application. Developer-mode access restrictions still apply; distributing the executable does not grant Spotify API access. See the [Developer Dashboard](https://developer.spotify.com/dashboard) and [Spotify access requirements](https://developer.spotify.com/documentation/web-api/concepts/quota-modes).

## Daily controls

- Ctrl+Shift+L: show/hide lyrics.
- Ctrl+Shift+K: lock/unlock click-through.
- Drag while unlocked. Open Settings for fonts, appearance, timing, and Windows startup.
- Positive timing offset means later lyrics; negative means earlier.
- Launching again opens Settings in the existing instance.
- Closing Settings keeps LyricFloat running. Use tray Exit to quit.

For scripted release checks, `LyricFloat.exe --exit` requests the same graceful shutdown from the running instance. It does not start a new session when no instance is running.

Windows startup registers the executable you are currently running. Set it from the installed/permanent location. Exit LyricFloat before installing, upgrading, moving, or uninstalling it. Normal uninstall preserves user data; it removes the startup entry only if that entry points to the uninstalled executable.

## Data, privacy, and diagnostics

Settings, protected Spotify tokens, cache, and logs are under `%AppData%/LyricFloat/`. Settings → Maintenance → Open Log Folder opens local diagnostics. Corrupt preferences fall back to defaults and are backed up as settings.json.invalid.bak. Logs rotate at 2 MB with three backups.

Tokens stay local and use Windows DPAPI CurrentUser protection. LyricFloat communicates directly with Spotify for playback metadata/position and LRCLIB for lyrics. There is no custom cloud backend, analytics, or telemetry. LRCLIB receives the song metadata needed for matching. Logs include song titles/artists and diagnostic events, never OAuth tokens or full lyric text.

Some songs have no synchronized lyrics; coverage and timing depend on LRCLIB. LyricFloat does not own the lyrics displayed. Availability and usage are subject to the provider and relevant rights holders; no commercial redistribution rights are claimed.

The release is unsigned. Windows may show an unknown-publisher warning. No automatic updater is included. This MVP should be manually tested with your account, display scaling, and playback before wider distribution.
