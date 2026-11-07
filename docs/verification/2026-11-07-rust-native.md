# Rust native migration verification (2026-11-07)

## Scope

The native app is installed alongside, not over, the C# stable release. It uses the same read-only DualSense HID reports and XInput battery API; the Flydigi query writes only to the local Flydigi service pipe. Bluetooth opens with a read-only calibration Feature Report request, like the current C# source. Game/Steam coexistence remains a hardware test, not a code-only guarantee.

## Local results

- Rust: 9 unit tests passed; formatting and Clippy (`-D warnings`) passed on the local gnullvm target.
- C#: 354 tests passed across Core, HID, Watcher and App.
- The native NSIS Setup was built locally; silent install, upgrade, current-user sign-in registration, one-Watcher check and silent uninstall passed. A real uninstall defect (running executable left behind) was found in the first test and corrected by stopping only processes whose executable path matches the exact native install directory.
- Installed native build displayed a live USB DualSense at 95% and then an offline window when the controller disappeared. The Compact-to-TwoIcons menu action and sign-in listener toggle were exercised in the actual UI. A Win32 menu dispatch defect found during that test was corrected.
- Reconnect and game/Steam coexistence are not yet confirmed by this test. Nor are native Bluetooth and Flydigi hardware paths reverified here.

## Short idle measurement

Measured on the same Windows session and controller, 12 samples one second apart after launch. Measurements are point-in-time and are not a long-run leak or game-impact benchmark.

| Process | Mean working set | Mean private bytes | CPU seconds/minute over sample |
| --- | ---: | ---: | ---: |
| C# stable tray app | 121.57 MiB | 74.64 MiB | 0.17 |
| Rust native tray app | 16.27 MiB | 2.68 MiB | 0 (below sample resolution) |
| Rust native sign-in Watcher | 12.45 MiB | 2.23 MiB | 0 (below sample resolution) |

Working sets may share DLL-backed pages and should not simply be interpreted as unique physical memory. The native Setup is 1.47 MiB and its unpacked payload 2.70 MiB; the currently installed C# self-contained directory is 160.80 MiB. Those are packaging choices as well as language/runtime differences.

## Release gate

The release workflow now builds and smoke-tests the native Setup and ZIP in addition to the existing C# stable artifacts. The native MSVC CI build is not claimed to have passed until a GitHub Actions run completes. The native Setup is a separate opt-in download; the C# stable installer remains the default until the reconnect, resume, hardware and game-coexistence checks pass.
