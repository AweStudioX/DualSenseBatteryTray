<div align="center">

# Controller Battery Tray

**Your controller battery, at a glance.**

Windows tray utility · DualSense · XInput · Flydigi APEX 4

[简体中文](README.md) · **English**

[Download stable](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest) · [Compatibility](#compatibility) · [Build from source](#build-from-source) · [Report an issue](https://github.com/AweStudioX/DualSenseBatteryTray/issues)

</div>

> **Version note:** native Bluetooth support is implemented in the current source, pending hardware verification. Published **v1.0.2 does not include it**. Screenshots show a real USB DualSense session; they are not evidence of Bluetooth hardware testing.

## Preview

![Real USB DualSense status window](docs/images/controller-status.png)

### Two tray layouts

| Two icons · default | Compact · single icon |
| :---: | :---: |
| ![Separate controller and battery icons](docs/images/controller-tray.png) | ![Controller and number in one Compact icon](docs/images/controller-tray-compact.png) |
| Controller artwork plus a separate percentage for readability | Controller artwork and number combined to save space |

These are native-pixel captures from a vertical Windows taskbar. Compact omits the percent sign inside the icon; the tooltip and status window retain the full value. Right-click → **Tray layout** to switch. The choice persists without restarting the controller reader.

## Features

- **Battery and charging status in the tray**: click to view the device, connection, battery and state.
- **Low-battery alerts**: notify at or below 20% and 10% while discharging, with duplicate suppression.
- **Automatic visibility**: a background Watcher detects controllers after sign-in. DualSense liveness uses advancing report sequence/timestamp values to avoid mistaking an always-plugged adapter for a powered controller.
- **System theme support**: tray artwork follows the Windows light/dark system theme.
- **Per-user installation**: Setup includes the .NET runtime and supports removal through Windows Installed apps.

## Compatibility

| Controller / connection | Current source | Battery source and limits | Hardware verification |
| --- | --- | --- | --- |
| DualSense / USB HID | Supported | Converted from roughly 10% steps; 55% does not imply 1% accuracy | Reading and screenshots confirmed locally; game coexistence depends on the environment |
| DualSense / native Bluetooth | Implemented, preview | Full Bluetooth reports with CRC validation; reads a calibration Feature Report on open to enable full reports | **Pending**: initial pairing, reconnect, resume, Steam Input coexistence |
| XInput-compatible controllers | Detection supported; battery depends on device | Known wireless battery types expose approximately 0% / 25% / 50% / 100%; receivers reporting wired show unknown | Not a guarantee for all models or receivers |
| Flydigi APEX 4 / 2.4 GHz | Local-service reader supported | Requires Space Station's fcs.sock channel and k2 identity; levels 0–5 map to approximate percentages | Not all firmware versions have been tested |
| DualSense Edge / DualShock 4 / other Flydigi models | No support commitment | Require separate adaptation and verification | Not covered |

Unavailable readings display a question mark, never a fabricated 100%. Flydigi may expose charging state without a percentage. Minimal Bluetooth reports, corrupted data and unsupported reports are not interpreted as battery values.

## Install and use

1. Download the Windows x64 Setup.exe from [Releases](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest).
2. Install and connect a supported controller. Click the tray icon for details; right-click for layout, refresh and sign-in listener controls.
3. Disconnecting or persistently losing valid advancing reports closes the app and removes its icons. The Watcher stays available for later connections.
4. Remove via **Settings → Apps → Installed apps → DualSense Battery Tray**.

The installer is currently unsigned, so Windows SmartScreen may show a warning. Verify the GitHub Release source and supplied SHA-256. The ZIP is a **manual-install package, not a portable edition**: extract it and run install.ps1; remove it with uninstall.ps1. For Setup installations, use Windows Installed apps instead.

## Native Bluetooth (source preview)

Pair your DualSense through Windows first, then run a build of the current source. The app identifies the controller by the same Sony VID/PID, distinguishes USB and Bluetooth by HID report length, and shows **Bluetooth** in the status window.

On Bluetooth open, the app reads calibration Feature Report 0x05 to request full input reports containing battery data. It does not write calibration values. If the request fails and full reports remain unavailable, no battery value is fabricated; the Watcher can retry on later probes. This path has automated coverage but still needs hardware verification.

## Data and coexistence

- HID access remains shared and read-only. No virtual controller, input remapping, lighting, vibration or button-control commands.
- USB makes no Feature Report requests; Bluetooth performs the GET described above. A blanket claim of never accessing Feature Reports no longer applies to Bluetooth.
- XInput uses the battery-status API; Flydigi queries its local Space Station service.
- Logs and layout preferences remain local. No button or stick data is uploaded. HidHide, exclusive access or device hiding may prevent battery access.
- Switching Bluetooth report mode may affect other software's assumptions about report format; Steam Input and game coexistence require hardware testing.

## Build from source

Requires Windows x64 and .NET 8 SDK.

~~~powershell
dotnet restore DualSenseBatteryTray.sln
dotnet test DualSenseBatteryTray.sln -c Release
dotnet publish src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/app-publish
dotnet publish src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/watcher-publish
.\scripts\merge-publish.ps1 -AppDirectory (Resolve-Path artifacts/app-publish).Path -WatcherDirectory (Resolve-Path artifacts/watcher-publish).Path -OutputDirectory (Join-Path $PWD artifacts/publish)
.\scripts\install.ps1 -PublishDirectory (Resolve-Path artifacts/publish).Path
~~~

Pushing a version tag triggers the Windows Release workflow: test, publish self-contained App/Watcher binaries, build and validate Setup, and upload packages plus checksums. See [.github/workflows/release.yml](.github/workflows/release.yml).

## Acknowledgements

- [dualshock-tools](https://github.com/dualshock-tools/dualshock-tools.github.io): battery parsing reference; [MIT notice](LICENSES/dualshock-tools-MIT.txt).
- [JKWTCN/BatteryMonitor](https://github.com/JKWTCN/BatteryMonitor): Flydigi service framing reference; [MIT notice](LICENSES/batterymonitor-MIT.txt).
- [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons): controller artwork; [MIT notice](LICENSES/fluentui-system-icons-MIT.txt).
- Bluetooth layout and CRC independently implemented from the protocol definitions in [Linux hid-playstation](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c); full-report initialization behavior checked against [go-dualsense-battery](https://github.com/neolit123/go-dualsense-battery/blob/main/main.go).
