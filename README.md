# LyricFloat

Native C# / WPF / .NET 10 Windows desktop utility. **Phase 6 / version 0.1.0** adds single-instance activation, bounded diagnostics, release publishing, an Inno Setup installer definition, and first-run/offline cleanup.

## Windows release: install and use

Read [RELEASE-README.md](RELEASE-README.md) for end-user instructions. Extract the complete portable ZIP, or use the installer when compiled. Launch LyricFloat.exe, find its tray icon, open Settings → Spotify, save your developer application's Client ID, then choose Connect Spotify. Authorize in your usual browser and play a song. Ctrl+Shift+L shows/hides lyrics; Ctrl+Shift+K locks/unlocks click-through. Self-contained builds require no separate SDK or runtime installation.

The release does not embed a developer Client ID. Spotify developer application/account access restrictions still apply. No client secret is requested. Settings writes the public Client ID to `%AppData%/LyricFloat/spotify.json`, which takes precedence over the optional development configuration. Tokens stay in the separate DPAPI store.

Launching again signals the existing instance to open/focus Settings and exits the new process. A per-user/session mutex plus activation event prevents duplicate windows, tray icons, pollers, and hotkeys. The first-run connection hint disappears after five seconds; login never opens automatically.

### Build release artifacts

```powershell
.\scripts\Build-Release.ps1
```

The script builds, tests, publishes self-contained win-x64, copies the release README, and creates the portable ZIP. It compiles the installer if Inno Setup is available. Otherwise it explicitly reports that compilation was skipped. For separate commands:

```powershell
dotnet publish LyricFloat.App -p:PublishProfile=WindowsPortable
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' '.\installer\LyricFloat.iss'
```

Use Inno Setup 6.3+. The release script also accepts `-IsccPath` and `-SkipInstaller`. Publishing uses `LyricFloat.App/Properties/PublishProfiles/WindowsPortable.pubxml`, with trimming, single-file bundling, and ReadyToRun disabled. Icon artwork is original geometry in `scripts/New-AppIcon.ps1`; the generated multi-resolution ICO is used by the executable, windows, tray, and installer.

```text
artifacts/release/portable/LyricFloat.exe
artifacts/release/LyricFloat-0.1.0-win-x64.zip
artifacts/release/installer/LyricFloat-Setup-0.1.0.exe  (requires ISCC)
```

The per-user installer targets `%LocalAppData%/Programs/LyricFloat`, creates a Start Menu entry, optionally creates a Desktop shortcut, and registers uninstall. Exit LyricFloat before installing, upgrading, moving, or uninstalling. Enable Windows startup from the installed/permanent copy. Uninstall removes the startup value only if it targets that installed executable and preserves AppData preferences, credentials, cache, and logs.

This release is unsigned and has no auto-updater. Windows may show an unknown-publisher warning. An installer definition alone is not a compiled or validated installer; consult the validation report below.

### Validation performed for this build

- Release build: zero warnings/errors; 200 automated assertions passed.
- Self-contained portable executable launched directly. Required runtime files are present and developer configuration is excluded.
- A second executable launch exited and focused Settings in the original process.
- A 180-second desktop observation, including Settings/connection interaction, averaged 3.626% of one CPU core. Working set rose from 191.79 MB during initial activity to 256.29 MB, remaining around that level for the final minute. This is not an isolated idle benchmark or an authenticated playback soak test.
- The published process exited through its ordered shutdown path with exit code 0. No OAuth listener remained.
- Inno Setup compiler was unavailable: installer source is supplied, but installer compilation, installation, shortcuts, and uninstall remain unverified.
- No Spotify Client ID or saved tokens were present for this run. Real login/restore/playback timing and several minutes of live Spotify playback remain manual release checks.

`artifacts/release/runtime-check.json` records the runtime observation. Run `scripts/Test-PortableRelease.ps1` with all existing LyricFloat copies closed to repeat the launch/activation/shutdown observation.

## Requirements and build

- Windows 10 or 11.
- .NET 10 SDK for development; .NET 10 Desktop Runtime for the framework-dependent executable on another machine.
- A Spotify developer application and an account permitted to use it.

```powershell
dotnet build -c Release
dotnet run --project LyricFloat.App -c Release
```

Executable: `LyricFloat.App/bin/Release/net10.0-windows/LyricFloat.exe`. Exit the existing instance before rebuilding or launching another copy.

## Spotify Setup

1. Open the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and sign in.
2. Create an application for LyricFloat and select Web API if asked which API you plan to use.
3. Open the application's settings. Copy its **Client ID**.
4. Add and save this **exact** redirect URI, including the port and path, with no trailing slash:

   ```text
   http://127.0.0.1:43821/callback
   ```

5. From the repository root, create the local configuration:

   ```powershell
   Copy-Item LyricFloat.App/appsettings.example.json LyricFloat.App/appsettings.Development.json
   ```

6. Edit `LyricFloat.App/appsettings.Development.json` and replace the placeholder with your Client ID:

   ```json
   {
     "Spotify": {
       "ClientId": "YOUR_SPOTIFY_CLIENT_ID",
       "RedirectUri": "http://127.0.0.1:43821/callback"
     }
   }
   ```

7. For a development-mode app, ensure the Spotify account you will authorize is permitted in the Dashboard's user/access management. Consult [Spotify's current development-mode requirements](https://developer.spotify.com/documentation/web-api/concepts/quota-modes), including app-owner eligibility and user limits, if creation or access is denied.
8. Build/run using the commands above. Find **LyricFloat** in the system tray (possibly the hidden-icons overflow), then choose **Connect Spotify**.
9. Login and consent in your normal default browser. After the success response, close the callback tab and return to your desktop. Play a song in Spotify itself.

**No Spotify Client Secret is required. LyricFloat uses Authorization Code with PKCE.** Do not add a ClientSecret property. The local development configuration is Git-ignored, copied beside the build output, and excluded from publishing. Configuration is read from the executable directory, so rebuilding copies local edits there. No browser opens automatically on startup.

Spotify requires explicit loopback IP addresses rather than `localhost`; this build always uses the URI above. If port 43821 is occupied, the overlay gives an error and you can reconnect after freeing the port. There is no alternate host or port fallback. See the official [redirect rules](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri) and [PKCE flow](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow).

## Authentication and local storage

Login generates a cryptographically random 64-byte verifier, SHA-256/Base64URL S256 challenge, and 32-byte random state. State is validated before code exchange. A temporary TCP listener binds only to `127.0.0.1:43821`, validates the callback path and Host, bounds headers and request time, and closes after success, failure, cancellation, or the three-minute login timeout. The browser receives a minimal success/failure text response; this is not a web frontend.

The requested scopes are exactly `user-read-currently-playing` and `user-read-playback-state`. No playback-changing scopes, client secret, Basic client authentication, or private Spotify APIs are used.

Tokens are encrypted with **Windows DPAPI CurrentUser** and saved to:

```text
%AppData%/LyricFloat/spotify.tokens.dpapi
```

The complete token model is encrypted before writing. Saves replace the encrypted file atomically; there is no plaintext token file. The token store belongs to the current Windows user and is not portable to another account. Tokens, codes, verifiers, and callback queries never enter normal logs or UI bindings.

Stored login is restored on startup. Tokens refresh with a 60-second safety margin using a semaphore to serialize refresh attempts. A rotated refresh token replaces the old one; an omitted refresh token preserves the previous value. Disconnect stops polling, removes local credentials, and clears the display. It does not sign out of Spotify in your browser. If file permissions prevent credential deletion, the app reports a file-access error; fix that before relying on a persistent disconnect.

## Playback and error states

A reused HttpClient reads the [official currently-playing endpoint](https://developer.spotify.com/documentation/web-api/reference/get-the-users-currently-playing-track). Requests are sequential, followed by a cancellation-aware two-second delay; slow requests never overlap. Reported progress anchors the local lyric clock. The metadata tooltip retains the unmodified Spotify progress sample.

- Song title and artist appear for three seconds on track changes. Hovering while unlocked shows title, artists, album, and sampled `mm:ss / mm:ss` alongside the drag hint.
- Paused tracks show **PAUSED** and retain the reported position.
- HTTP 204 or a null item shows **Nothing playing** and continues polling.
- Episodes, ads, and unknown content show **Unsupported playback item**.
- HTTP 401 refreshes and retries once. Rejected authorization disconnects the session.
- HTTP 403 shows an access message and retries after 30 seconds; check the app's account access/scopes.
- HTTP 429 respects Retry-After, defaulting to 30 seconds when absent.
- Network failure/timeouts keep the process alive and retry later. The lyric clock freezes while retaining loaded lyrics, rather than blanking the overlay; recovery uses the next valid checkpoint without refetching the same song. A transport error during token refresh also preserves credentials. Rejected authorization still disconnects normally.

Track ID changes are logged. Logs rotate at approximately 2 MB, retaining three previous files (`app.log.1` through `.3`), about 8 MB total. Entries include timestamp, level, component, and curated event text. Unexpected exceptions include type/HResult and method names, never raw exception messages, callback queries, tokens, or source paths:

```text
%AppData%/LyricFloat/logs/app.log
```

Logs contain event names and track title/artist, not authentication secrets or raw HTTP responses.

## Desktop controls

Defaults to visible and unlocked. Position, lock state, and visibility now persist across restarts. Left-drag to move. Locked mode passes clicks through and disables dragging; unlocking restores interaction. The window stays topmost and does not activate when shown. Right-click no longer exits. Start minimized to tray overrides saved visibility at launch without replacing the saved preference until you show/hide it yourself.

Tray: Show/Hide Lyrics, Lock/Unlock Overlay, Settings, Connect Spotify (or Connected / Disconnect Spotify), Exit. Reset Overlay Position remains in Settings. Double-click toggles visibility. Connect is disabled during authentication to prevent duplicate browser windows/listeners.

| Shortcut | Action |
| --- | --- |
| Ctrl + Shift + L | Toggle visibility |
| Ctrl + Shift + K | Toggle lock/click-through |

Shortcuts remain global while another app is focused and while the overlay is hidden. Conflicts do not crash the app; tray controls remain available. Closing the overlay hides it; closing Settings closes only that window. Tray Exit unregisters hotkeys, disposes the tray icon, closes Settings, stops and awaits Spotify/lyrics work and maintenance, flushes preferences, closes the overlay, then shuts down WPF.

## Settings and persistence

Use **Tray → Settings**. There is one normal interactive WPF settings window; choosing Settings again restores/focuses the same window. Settings is not click-through or permanently topmost, even when the overlay is locked. Tabs cover Spotify, Appearance, Lyrics, Overlay, Windows, and Maintenance. Maintenance includes version and Open Log Folder.

Preferences are stored separately from credentials, cache, and logs:

```text
%AppData%/LyricFloat/settings.json
```

Schema version is 1. Version 0 migrates while preserving known preferences and defaulting missing fields. Unknown fields are ignored. Unsupported future versions or invalid JSON fall back safely and are copied to `settings.json.invalid.bak` before replacement when file permissions allow. Unreadable files also fall back without crashing. Numeric values are validated/clamped. A missing installed font falls back to Segoe UI (or another installed font if necessary).

Changes apply immediately. Saving is debounced by 400 ms and uses a temporary file followed by atomic replacement. Exit flushes the final preferences. Position is captured after dragging, not for every mouse-move event. No settings writes are triggered by lyric ticks or Spotify polls. A save failure is logged and shown in Settings.

To recover manually, exit LyricFloat, rename `settings.json` as a backup, and relaunch. The application also recovers automatically from malformed settings. Never remove `spotify.tokens.dpapi` unless you intend to disconnect Spotify.

Appearance controls include installed font family, current size 16–72 px, secondary size 12–48 px, current weight, alignment, independent current/secondary text opacity, and background opacity. Defaults are 32/22 px, centered semibold current lyrics, 100%/50% opacity, and a transparent background. Subtle background adds a rounded dark container; selecting it initially sets 20% background opacity when the previous value was zero. You can adjust it afterward. Click-through continues to apply to the overlay in either background mode.

**Minimal** displays only the current lyric. **ThreeLines** displays previous/current/next lyrics, the brief track caption, and pause indicator. These modes change locally without restarting or downloading lyrics. Track metadata remains available in the unlocked hover hint.

### Lyrics timing offset

**Positive timing offset = lyrics appear later. Negative timing offset = lyrics appear earlier.** The Settings slider spans -3000 to +3000 ms in 100 ms increments. Reset Timing returns to zero.

```text
effective lyric position = estimated playback position - timing offset
```

A lyric at 10.000 seconds appears at 10.500 with +500 ms, and at 9.500 with -500 ms. Changing offset immediately recomputes selection; it does not alter Spotify position, parsed/cached timestamps, or LRCLIB requests. This preference is separate from LRC offset tags, which the parser still ignores.

### Position, startup, and reset

Stored coordinates are checked against active monitor working areas on launch. Fully off-screen positions return to primary-monitor bottom-center; nearly inaccessible placements are clamped. Valid negative coordinates on secondary monitors are preserved. **Reset Overlay Position** moves to primary-monitor bottom-center; it does not change lock or visibility.

**Start LyricFloat with Windows** writes only the `LyricFloat` value under:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

The actual executable path is quoted, including paths with spaces. Disabling removes only that named value, with no administrator privileges. The existing registry entry is checked on launch and whenever Settings opens; it wins over a stale JSON startup preference. A registration pointing to a different executable location is not considered enabled for this copy. If you move the executable, toggle the setting from the new copy to register its new location. Actual registry integration remains a manual test; automated tests use a fake registry.

**Start minimized to tray** takes precedence over saved overlay visibility at startup. The tray, Spotify polling, and lyrics engine still run. Ctrl+Shift+L shows the current lyric. Turning the option on does not hide the overlay immediately; it applies to the next launch.

**Reset to Defaults** asks for confirmation in a native dialog. It resets ordinary preferences, disables LyricFloat's startup entry, unlocks/shows the overlay, and returns it to the default position. It does not delete Spotify authorization, lyrics cache, or logs.

**Clear Lyrics Cache** deletes only LyricFloat's cache-shaped JSON/temporary filenames in the cache folder. Current lyrics may remain in memory; switching away and back downloads again. In-flight lookups started before clearing cannot repopulate the cleared cache. Credentials, preferences, and logs are retained. Locked/inaccessible cache files produce a recoverable status message.

## Architecture

- `App.xaml` / `App.xaml.cs`: composition, explicit application lifetime, cleanup.
- `Views/OverlayWindow.*`: overlay presentation, placement, unlocked dragging.
- `ViewModels/OverlayViewModel.cs`: metadata and lyric frame properties, updated through the WPF dispatcher only when selected lyric text changes.
- `Services/OverlayController.cs`, `TrayIconService.cs`, `HotkeyService.cs`: preserved Phase 2 desktop controls.
- `Helpers/OverlayWindowInterop.cs`: native extended window styles.
- `Services/SpotifySessionController.cs`: authentication actions, polling lifecycle, and UI dispatching.
- `Services/SpotifyAuthService.cs`: PKCE, token exchange, refresh, logout, and restore.
- `Services/LoopbackCallbackListener.cs`: temporary bounded loopback callback transport.
- `Services/SecureTokenStore.cs`: isolated DPAPI storage.
- `Services/SpotifyConfiguration.cs`, `AppLog.cs`: local developer configuration and bounded diagnostics.
- `Services/SpotifyPlaybackService.cs`: HTTP behavior and JSON parsing.
- `Models/SpotifyTrack.cs`, `SpotifyToken.cs`, `Helpers/PkceHelper.cs`: playback/token data and testable PKCE primitives.
- `Models/LyricLine.cs`: lyric lines, result status, presentation frame, and LRCLIB DTO.
- `Services/LyricsService.cs`, `LyricsCache.cs`: exact LRCLIB lookup, explicit result states, and local JSON cache.
- `Helpers/LrcParser.cs`, `LyricLookup.cs`: sorted LRC parsing and binary selection.
- `Services/PlaybackClock.cs`, `LyricsSyncService.cs`: monotonic interpolation, request cancellation/generation guards, and frame selection.
- `LyricFloat.Tests/Phase4Tests.cs`: deterministic parsing, cache, HTTP fixtures, clock, lifecycle/race, and notification tests.
- `Models/AppSettings.cs`, `Services/SettingsService.cs`: versioned preferences, defaults, validation, and debounced atomic persistence.
- `Views/SettingsWindow.xaml` / `.xaml.cs`, `ViewModels/SettingsViewModel.cs`: native live preferences and maintenance actions.
- `Services/StartupService.cs`: isolated current-user Run entry with a fakeable registry interface.
- `Helpers/OverlayPosition.cs`: pure working-area position validation.
- `LyricFloat.Tests/Phase5Tests.cs`, `Phase5WindowTests.cs`: preference/startup/position/offset tests and native window regression checks.
- `Services/SingleInstanceService.cs`: per-user/session mutex and activation/exit events.
- `Services/ExceptionHandlingService.cs`: dispatcher, unobserved-task, and AppDomain exception diagnostics.
- `Helpers/AppIcon.cs`, `Assets/LyricFloat.ico`: centralized original application artwork.
- `Properties/PublishProfiles/WindowsPortable.pubxml`: conservative self-contained Windows publish settings.
- `installer/LyricFloat.iss`, `scripts/Build-Release.ps1`, `scripts/New-AppIcon.ps1`, `scripts/Test-PortableRelease.ps1`: installer source and reproducible release/observation commands.
- `LyricFloat.Tests/Phase6Tests.cs`: mutex signals, log bounds/privacy, migration, cache schema, and release configuration tests.
- `LyricFloat.Tests`: dependency-free executable unit/integration test runner. No Spotify servers are called.

## Automated validation

```powershell
dotnet run --project LyricFloat.Tests -c Release
```

The runner exits nonzero on failure. It covers the RFC 7636 S256 vector, verifier/state format and independent samples, callback state rejection before exchange, duplicate login, exact code exchange fields, DPAPI round-trip/corruption/deletion, serialized refresh, refresh-token preservation/rotation, bounded 401 retry, 403/429/204, playback fixtures, and loopback cancellation/port release. Tests use synthetic credentials, fake HTTP handlers, temporary token/cache directories, and the real local callback port. Run while no OAuth attempt is active. They do not statistically prove randomness; generation uses the platform cryptographic RNG.

Phase 4 adds parser formats/edge cases, binary boundary selection, an injected clock without Thread.Sleep, forward/backward seeks, pause/resume, safe cache keys, corruption recovery, offline cache reuse, request de-duplication, a deliberately late cancelled response after the newer song is displayed, shutdown cancellation, and unchanged-frame notification checks. The automated suite calls neither Spotify nor LRCLIB servers.

Phase 5 adds defaults and round-trip persistence, invalid settings and font fallback, future fields/versions, offset sign/live changes, quoted startup commands and value-name isolation using a fake registry, monitor geometry fixtures, cache-clear safety, and STA WPF window checks. Tests use temporary directories, not your actual settings or startup values. The native checks briefly open test windows and then close them.

Phase 6 covers instance activation/shutdown signals, mutex release, legacy migration, corrupt-settings backups, bounded structured logging, exception redaction, old-cache fallback, embedded icon loading, and release/installer configuration. A diagnostic `LyricFloat.exe --exit` signals the existing instance through the same ordered shutdown action as tray Exit; if no instance is running, it simply exits. This supports release lifecycle checks without force-killing the application.

## Release privacy and fault handling

Tokens are stored locally using Windows DPAPI CurrentUser. LyricFloat communicates directly with Spotify and LRCLIB; it has no custom cloud backend, analytics SDK, or telemetry. Spotify receives playback/authentication requests, and LRCLIB receives song metadata for matching. Lyrics themselves are cached locally. Logs include track title/artist but never full lyrics or OAuth secrets.

Dispatcher exceptions are logged and trigger ordered shutdown instead of continuing with unknown UI state. Unobserved task exceptions are logged and marked observed. AppDomain fatal exceptions are synchronously logged before the runtime terminates; resource handles are then released by Windows. Normal Exit cancels polling/lyrics/authentication, stops the timer, unregisters hotkeys, disposes the tray, awaits maintenance and preference writes, closes windows, and releases the instance mutex. Shutdown waits are bounded for fault cases.

LyricFloat does not own the displayed lyrics. Availability and usage are subject to the provider and relevant rights holders. No commercial redistribution rights over lyrics are claimed.

## Manual release regression checklist

1. Extract the ZIP on Windows x64 and launch LyricFloat.exe directly without an SDK. Confirm tray icon, friendly first-run hint, and Settings. Launch again: only one process/tray remains and existing Settings focuses.
2. Configure the exact Spotify redirect and Client ID in Settings, connect, restart to restore login, then test playback, pause/resume, seeks in both directions, rapid skips, and disconnect/reconnect. Leave connected long enough to test refresh.
3. Check synced lyrics, timing offset signs, cache hit/clear, unavailable/plain-only/instrumental states. Temporarily remove network access: loaded lyrics should remain frozen, with recovery after connectivity returns.
4. Check transparency, topmost, dragging, lock/click-through, show/hide, hotkeys, saved position, font/background changes, both display modes, reset, minimized startup, and disconnected-monitor recovery.
5. Enable Windows startup from the installed/permanent location and verify the quoted Run path. Open logs and check rotation/redaction. Exit with Settings open and during an OAuth/lyrics request; confirm no process, callback listener, or tray icon remains. Relaunch to check released hotkeys/mutex.
6. Compile the installer with ISCC, install per-user, test Start Menu/Desktop shortcuts and installed launch, then uninstall. Confirm binaries/shortcuts are removed, user data is preserved, and only a startup entry targeting that installed executable is removed.
7. Observe several minutes of actual Spotify playback in Task Manager: no sustained CPU spikes or rapid memory growth, Spotify requests about every two seconds, and LRCLIB only on uncached track changes. Automated fixtures and idle observation do not replace this account-dependent test.

## Manual Phase 5 acceptance tests

A. Choose Tray → Settings twice. Confirm one window opens and the second action focuses it. Close Settings: LyricFloat must continue in the tray.

B. Set current lyric size to 40. Confirm a live change, exit from the tray, relaunch, and confirm 40 persists.

C. Select Minimal. Confirm only current lyric remains; restart and confirm the mode persists. Restore ThreeLines and check previous/next lines.

D. Change current/secondary opacity and subtle-background opacity. Confirm live appearance and click-through while locked over an underlying button.

E. During a synced song, set +500 ms and confirm lines appear later; set -500 ms and confirm earlier timing. Logs must not show a fresh LRCLIB lookup solely for this change.

F. Drag the unlocked overlay, exit, and relaunch. Confirm the same position returns.

G. Move it to a secondary monitor, exit, disconnect that monitor, and relaunch. Confirm recovery on primary. Include mixed-DPI monitors if available.

H. Lock, exit, relaunch. Confirm click-through is restored and Settings remains interactive.

I. Choose Reset Overlay Position. Confirm primary-monitor bottom-center. If hidden, show it using the tray/hotkey.

J. Change several preferences. Choose Reset to Defaults, first cancel, then confirm. Verify reset only after confirmation and that Spotify remains authenticated and cache is retained.

K. Enable Start LyricFloat with Windows. Inspect the current-user Run key: only LyricFloat's entry should be added with a quoted executable path. Disable it and confirm only that entry is removed. Optionally sign out/in to verify launch.

L. Enable Start minimized to tray with the overlay visible, then restart. Confirm tray exists and overlay is hidden. Ctrl+Shift+L should show lyrics for the current playback position.

M. Clear Lyrics Cache. Switch away from the current song and return; confirm fresh retrieval. Verify credentials/settings/logs remain.

N. Recheck Spotify restore, disconnect/reconnect, token refresh, synced lyrics, pause/resume, forward/backward seeks, rapid skips, cache, topmost, dragging, locking, click-through, hide/show, both hotkeys, and tray Exit with Settings open. Confirm the process and tray icon disappear and the final preferences restore on relaunch.

## Lyrics source and synchronization

**Spotify is used for playback metadata and playback position. Spotify's public Web API is NOT used to retrieve lyrics. Lyrics are retrieved separately from [LRCLIB](https://lrclib.net/docs).** No HTML scraping or private Spotify endpoints are used.

On a new track identity, check the local cache, otherwise request `https://lrclib.net/api/get` once with URL-encoded `track_name`, `artist_name`, `album_name`, and `duration`. Duration uses invariant decimal seconds (289533 ms becomes 289.533). Artist names are preserved, including commas, ampersands, and featuring credits. There is no fuzzy search or second provider.

Each track change cancels the previous lookup, resets parsed lyrics and the playback anchor, and shows Loading lyrics. A generation check also discards late responses that ignore cancellation. Same-track Spotify checkpoints never refetch LRCLIB. Failed and missing requests are not automatically retried during that track session; switching away and back can retry.

The LRC parser supports `[mm:ss]`, tenths/hundredths/milliseconds, large minute values, and multiple timestamps on one line. It ignores metadata, malformed lines, and **offset tags (offset is not applied in Phase 4)**. Empty timed lines remain intentional blank boundaries. Sorting is stable. Duplicates retain source order, and binary lookup selects the last line at an identical timestamp. Before the first timestamp the current lyric is empty; after the last timestamp the final lyric remains.

PlaybackClock anchors Spotify's reported progress to Stopwatch's monotonic time. It advances locally only while playing, clamps at track duration, and freezes at Spotify's reported paused position. Every valid poll replaces the anchor; a difference over 1000 ms is logged as a seek. Binary lookup supports both forward and backward movement. System wall-clock changes do not affect elapsed playback timing. Pause, resume, and seek become visible after Spotify's next response, normally around two seconds plus request latency.

A 75 ms WPF dispatcher timer reads the local clock and selects previous/current/next lyrics in O(log n). This timer makes **no HTTP calls**. Bound lyric text notifies only when its selected value changes. Hiding the overlay leaves state running, so showing it returns to the current lyric. The same window and native click-through styles are retained; no child overlay windows were added.

Plain-only results show Synced lyrics unavailable; missing results show Lyrics unavailable; network/API failures show a brief unobtrusive error. These messages expire after four seconds. Plain lyrics never receive guessed timing. Recognized instrumental tracks show ♪ Instrumental ♪ without retries. Synced empty lines can leave the current text blank for an intentional gap.

## Local lyrics cache

Cache location:

```text
%AppData%/LyricFloat/cache/
```

Files use SHA-256 hashes of Spotify track IDs (or a stable metadata fallback when an ID is absent), never raw remote text as a filename. Versioned JSON contains the cache identity, LRCLIB ID, instrumental flag, and plain/synced lyrics. Synced, plain-only, and instrumental results are cached; NotFound and errors are not persisted. Saves use unique temporary files followed by replacement. Corrupt/unreadable cache entries are ignored and fetched afresh; cache write failures still allow in-memory lyrics. OAuth tokens remain exclusively in DPAPI storage.

Logs record lookups, cache hits/misses, parsed line count, result category, failures/cancellation, sync resets, and detected seeks. Full lyric text is never logged. Cache files intentionally contain lyric data; they contain no OAuth credentials.

## Manual Phase 4 acceptance tests

A. Play a song with LRCLIB synced lyrics (for example Iris by Goo Goo Dolls). Expect previous/current/next lines to change automatically.

B. Compare against Spotify's lyric display if available. Small source/recording differences are acceptable for this MVP.

C. Pause mid-lyric; after the next Spotify poll, wait several seconds and confirm no advancement.

D. Resume and confirm lyrics continue from the newest reported position.

E. Seek forward approximately 30 seconds. Confirm the correct line after the next Spotify checkpoint.

F. Seek backward and confirm the lyric selection moves backward.

G. Rapidly skip A → B → C. Wait for earlier requests to finish; only C's lyrics may remain.

H. Play a song, switch away, then return. Expect a fast cached load and a Lyrics cache hit log entry.

I. Play an unavailable track. Expect Lyrics unavailable briefly, then blank current text, no crash, and no request on every Spotify poll.

J. Play a recognized instrumental. Expect ♪ Instrumental ♪ without repeated requests. Try a plain-only result if available; it must not scroll with fabricated timing.

K. Check topmost, drag, lock/click-through onto a button underneath, unlock, hide/show, both global shortcuts, and tray Exit. Hide during playback, wait, then show; lyrics must reflect the current position.

L. Check Spotify session restore, disconnect/reconnect, pause/resume and track detection, and token refresh (leave connected over an hour). Disconnect must clear lyric state. Exit during a lookup must terminate the process; relaunch and verify hotkeys and login still work.

Watch request/log cadence: Spotify roughly every two seconds, LRCLIB once per new uncached track, no lyric text log spam. Confirm Task Manager CPU stays low while the overlay is idle between lyric changes. Cached lyrics should remain available if LRCLIB becomes unreachable; loss of Spotify connectivity freezes interpolation until a valid checkpoint returns.

## Manual Phase 3 acceptance tests

A. **First connection:** launch without stored credentials. Expect Spotify not connected. Tray Connect Spotify should open your browser. Click only once; repeated actions must not start duplicate login windows.

B. **Callback:** authorize. Expect browser success text and Spotify: Connected in the tray, without a client secret. Also try denying consent on a separate connection attempt and confirm failure is handled.

C. **Currently playing:** start a Spotify song. Within a few seconds check title, artist, album, and position/duration against Spotify.

D. **Switch songs:** skip in Spotify. Confirm the overlay switches to the new track within a polling cycle plus network latency.

E. **Pause:** pause in Spotify. Expect PAUSED and non-advancing sampled position.

F. **Resume:** resume in Spotify. Expect PAUSED to disappear and sampled progress to update.

G. **Nothing playing:** when Spotify reports no current item, expect Nothing playing; the app remains alive. A merely paused track may still be reported by Spotify and will retain its metadata.

H. **Restart:** tray Exit, then launch again. Valid stored login should restore without browser authorization. Leave connected for over an hour to exercise automatic refresh against Spotify.

I. **Disconnect:** tray Disconnect Spotify. Confirm metadata clears, Connect Spotify returns, and the encrypted token file is gone. Restart: expect disconnected.

J. **Phase 2 regression:** verify drag, lock/click-through onto an underlying button, unlock, hide/show preserving position, Ctrl+Shift+L and Ctrl+Shift+K while another app is focused, and tray Exit. Confirm the process and tray icon disappear. Relaunch and retest hotkeys.

K. **Cancellation/errors:** exit while waiting in the OAuth browser, then relaunch and connect again (the port must be free). Temporarily disconnect the network during playback and confirm recovery. Check that no listener remains on port 43821 after a completed/cancelled login or Exit.

Live account authorization and Spotify playback require your Dashboard configuration and manual consent; automated fixtures do not establish those results.

## Scope

Phase 6 only. Some songs have no synced lyrics, some have only plain lyrics, and exact matching may miss alternate versions. Timing can differ by source/recording. Long lines may be clipped at the chosen font size. LRC offset tags remain ignored; the global manual offset is supported. No updater, word-level karaoke, translation, extra provider, backend, or database. Topmost targets normal desktop windows, not exclusive fullscreen or secure Windows screens. Installer execution and account-dependent live playback require separate verification.

**Spotify does not provide a public lyrics API. Lyrics are retrieved from an external lyrics provider: LRCLIB.** No Phase 7 work is included.
