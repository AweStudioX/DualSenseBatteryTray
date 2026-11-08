# Rust native migration verification (2026-11-07)

## Scope

The native app is installed alongside, not over, the C# stable release. It uses the same read-only DualSense HID reports and XInput battery API; the Flydigi query writes only to the local Flydigi service pipe. Bluetooth opens with a read-only calibration Feature Report request, like the current C# source. Game/Steam coexistence remains a hardware test, not a code-only guarantee.

## Local results

- Rust: 12 unit tests passed after the multi-controller update; formatting and Clippy (`-D warnings`) passed on the local gnullvm target.
- C#: 354 tests passed across Core, HID, Watcher and App.
- The native NSIS Setup was built locally; silent install, upgrade, current-user sign-in registration, one-Watcher check and silent uninstall passed. A real uninstall defect (running executable left behind) was found in the first test and corrected by stopping only processes whose executable path matches the exact native install directory.
- Installed native build displayed a live USB DualSense at 95% and then an offline window when the controller disappeared. The Compact-to-TwoIcons menu action and sign-in listener toggle were exercised in the actual UI. A Win32 menu dispatch defect found during that test was corrected.
- On 2026-11-08, after a manual disconnect/reconnect, the C# stable app and Rust probe both reported the same Flydigi APEX 4 at approximately 40%, discharging; the installed Rust window displayed the same value. The controller icon was revised against the [official APEX 4 manual front view](https://shops.flydigi.com/pages/flydigi-apex4-gaming-controller-user-manual), without bundling Flydigi's image. The former C# Watcher task was disabled locally, with its installation preserved for rollback; the native sign-in Watcher is registered and running.
- On 2026-11-08 the user tested a game while the Rust tray was running and confirmed that controller input and vibration worked normally. This is a real-world coexistence check for that game, not a guarantee for all games or Steam Input configurations.
- The multi-controller preview adds Sony DualShock 4 (original USB/Bluetooth full HID reports, PIDs `05c4`/`09cc`) and DualSense Edge (`0df2`) without new runtime dependencies. It uses shared, read-only HID handles; the DS4 parser checks Bluetooth CRC and treats error or minimal reports as unavailable, not a fake percentage. Xbox and third-party XInput controllers remain supported through the existing coarse Windows battery API. Nintendo Switch Pro/Joy-Con-specific battery reading is not enabled: obtaining a full report can require an output command that would change controller mode, outside this app's read-only policy. DS4/Edge hardware behavior still needs a physical device test. Protocol interpretation follows [Linux hid-playstation](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) and device IDs follow [Linux hid-ids](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-ids.h); no code or assets were copied.
- Against the immediately preceding local package, the new native executable grew from 596,480 to 599,040 bytes (+2,560 bytes); the NSIS Setup grew from 1,576,036 to 1,576,660 bytes (+624 bytes). A 12-second post-install idle sample measured the new tray at 16.22 MiB working set / 2.62 MiB private bytes and Watcher at 11.53 / 2.04 MiB, in line with the earlier short baseline below. This is not a long-run or matched-age A/B performance test. After upgrade, the native probe still read the connected Flydigi APEX 4 at 20%, discharging, and exactly one native Watcher plus one tray process were running.
- A controlled native background exit-and-relaunch across a physical disconnect has not yet been confirmed. Resume, native Bluetooth hardware, and explicit Steam Input paths remain unverified.

## Short idle measurement

Measured on the same Windows session and controller, 12 samples one second apart after launch. Measurements are point-in-time and are not a long-run leak or game-impact benchmark.

| Process | Mean working set | Mean private bytes | CPU seconds/minute over sample |
| --- | ---: | ---: | ---: |
| C# stable tray app | 121.57 MiB | 74.64 MiB | 0.17 |
| Rust native tray app | 16.27 MiB | 2.68 MiB | 0 (below sample resolution) |
| Rust native sign-in Watcher | 12.45 MiB | 2.23 MiB | 0 (below sample resolution) |

Working sets may share DLL-backed pages and should not simply be interpreted as unique physical memory. The native Setup is 1.47 MiB and its unpacked payload 2.70 MiB; the currently installed C# self-contained directory is 160.80 MiB. Those are packaging choices as well as language/runtime differences.

## Release gate

The release workflow now builds and smoke-tests the native Setup and ZIP in addition to the existing C# stable artifacts. The [2026-11-08 GitHub Actions preflight](https://github.com/AweStudioX/DualSenseBatteryTray/actions/runs/42426768250) passed the native MSVC tests, Clippy, packaging, and installer install/upgrade/uninstall smoke test; it uploaded a `windows-setup-preflight` artifact without publishing a Release. The first run exposed a missing `xinput1_4.lib` on the runner, corrected by loading the Windows System32 XInput DLL at runtime. The native Setup is a separate opt-in download; the C# stable installer remains the default until the reconnect, resume, Bluetooth and Steam Input checks pass. A 2026-11-08 XInput-only probe identified Flydigi APEX 4 but correctly reported battery `Unknown`; precise 40% was observed only while the Flydigi service supplied data.
