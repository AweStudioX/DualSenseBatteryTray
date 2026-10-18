# Controller Battery Tray

## Install

Download `DualSenseBatteryTray-v1.0.1-win-x64-Setup.exe` from the [latest GitHub Release](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest) and run it. This is one per-user installer: it bundles the App, background Watcher, and .NET runtime, so there is no second download or administrator prompt. It installs to `%LOCALAPPDATA%\Programs\DualSenseBatteryTray`, registers the Watcher to start at Windows sign-in, and appears in Windows **Installed apps**. The tray App still appears only while a live controller is detected.

To remove it, use Windows **Settings → Apps → Installed apps → DualSense Battery Tray → Uninstall**, or the Start Menu uninstall shortcut. The installer is currently unsigned, so Windows SmartScreen may show a warning; verify the release download and its `.sha256` checksum before choosing to run it.

The `win-x64.zip` archive remains available for manual installation. It is not a portable edition. Extract it and run:

```powershell
.\install.ps1 -PublishDirectory .\publish
```

This PowerShell route installs the same App, Watcher, and logon task for the current user. If you installed from ZIP, remove it with `uninstall.ps1` from the extracted archive. If you installed with Setup.exe, use Windows Installed apps instead.

## Behavior

- Supported controllers currently include USB DualSense and XInput-compatible controllers. Flydigi receivers are recognized separately when Windows exposes their `FLYDIGI_VADER` or `FLYDIGI_APEX` compatibility identity; other XInput devices use a generic gamepad profile and icon.
- A supported controller uses the readable `Two icons` layout by default at 96 DPI: a controller-specific icon followed by a separate battery value that includes `%`.
- DualSense reports a fine-grained percentage. When XInput exposes a known wireless battery type, the application displays its four coarse buckets as `≈ 0%`, `≈ 25%`, `≈ 50%`, or `≈ 100%`. Some third-party receivers—including current Flydigi compatibility firmware—report themselves as wired; their battery remains `?` instead of showing a misleading 100%.
- Flydigi APEX 4 exposes its real 0–5 battery level and charging state through the Space Station background channel. When its local `fcs.sock` pipe is available, the app identifies the controller as `k2`, uses the APEX icon, and reads that channel without opening or taking over controller input. Without a verified identity source, the receiver is still detected, but its misleading `VADER3` compatibility identity is not treated as an exact model match: the app uses the generic gamepad icon and shows battery as `?`.
- `Compact` remains an optional persisted layout with one controller-and-number icon; it omits `%` inside the icon while hover text and the status window retain the complete percentage.
- Connected tray artwork automatically adapts its outline and number contrast when the Windows system theme changes. Charging never replaces the measured percentage with a fabricated value.
- `Tray layout` in the right-click menu switches live between `Two icons` and `Compact`; the successful choice is remembered across reconnects without restarting the HID session.
- The executable and status window use a separate static, unconnected controller identity with an unlit touchpad and no blue connection indicator.
- Clicking any active tray icon opens the status window; right-clicking opens Refresh, listener, tray-layout, About, and Exit commands.
- VID/PID presence alone is not treated as a connected DualSense. The hidden watcher launches for advancing DualSense reports or an active XInput slot; an idle DualSense adapter that returns frozen reports keeps every tray/window UI hidden.
- When advancing reports stop, the tray application closes and removes its icon or icons after the liveness timeout; the hidden watcher remains available and restores the application automatically when advancing reports resume without requiring a USB reconnect.
- DualSense HID access is shared and read-only. XInput support calls only the battery-status API. The application never sends controller output, feature reports, vibration, lighting, or input commands.
- Controller artwork is adapted from Microsoft Fluent UI System Icons under MIT; see `LICENSES/fluentui-system-icons-MIT.txt`.
- Flydigi local-service protocol framing is adapted from JKWTCN/BatteryMonitor under MIT; see `LICENSES/batterymonitor-MIT.txt`.

## Releases

Pushing a version tag such as `v1.0.1` runs the Windows Release workflow. It tests the solution, publishes self-contained `win-x64` App and Watcher executables, builds and smoke-tests Setup.exe, then attaches Setup.exe, the manual-install ZIP, and a SHA-256 checksum for each to the GitHub Release. A manual workflow run validates the same packages without publishing a release.
