# DualSense Battery Tray

## Install

Download the `win-x64.zip` archive from the latest GitHub Release, extract it, and run:

```powershell
.\install.ps1 -PublishDirectory .\publish
```

The installer deploys the App and Watcher for the current Windows user and registers the logon task. To remove them, run `uninstall.ps1` from the extracted archive.

## Behavior

- A supported USB DualSense uses the readable `Two icons` layout by default at 96 DPI: a connected controller icon followed by a separate battery value that includes `%`.
- `Compact` remains an optional persisted layout with one controller-and-number icon; it omits `%` inside the icon while hover text and the status window retain the complete percentage.
- Connected tray artwork automatically adapts its outline and number contrast when the Windows system theme changes. Charging never replaces the measured percentage with a fabricated value.
- `Tray layout` in the right-click menu switches live between `Two icons` and `Compact`; the successful choice is remembered across reconnects without restarting the HID session.
- The executable and status window use a separate static, unconnected controller identity with an unlit touchpad and no blue connection indicator.
- Clicking any active tray icon opens the status window; right-clicking opens Refresh, listener, tray-layout, About, and Exit commands.
- VID/PID presence alone is not treated as a connected controller. The hidden watcher launches the tray application only after the DualSense sequence byte or sensor timestamp advances; an always-present adapter that returns frozen reports keeps every tray/window UI hidden.
- When advancing reports stop, the tray application closes and removes its icon or icons after the liveness timeout; the hidden watcher remains available and restores the application automatically when advancing reports resume without requiring a USB reconnect.
- HID access is shared and read-only: the application reads input reports and never sends output or feature reports.
- Controller artwork is adapted from Microsoft Fluent UI System Icons under MIT; see `LICENSES/fluentui-system-icons-MIT.txt`.

## Releases

Pushing a SemVer tag such as `v1.0.0` runs the Windows Release workflow. It tests the solution, publishes self-contained `win-x64` App and Watcher executables, packages the installer files, emits a SHA-256 checksum, and creates the matching GitHub Release.
