# Native Bluetooth implementation verification

- Date: 2026-11-07 (Asia/Hong_Kong).
- Source adds 78-byte report 0x31 parsing, shifted common payload offsets and CRC32 seeded with 0xA1. USB remains 64-byte report 0x01.
- Bluetooth sessions reject minimal reports even if padded to USB length. Invalid reports cannot update battery or liveness.
- Shared GENERIC_READ access remains unchanged. Bluetooth session open performs one HidD_GetFeature calibration-report read (0x05), sized from HID capabilities with a 41-byte minimum. No output report or SetFeature is introduced. Failed initialization does not invent a battery reading; only valid full reports are accepted.
- Reader and Watcher both use Bluetooth-aware liveness, including sequence/timestamp wrap and stale-report timeout. Discovery labels Bluetooth using the HID input report length.
- Automated tests: 351 passed (Core 20, HID 81, Watcher 37, App 213). Installer build-contract tests: 9 passed; install/uninstall transactional tests: 32 passed. Release build: 0 warnings, 0 errors.
- The initial single-instance test failed while the installed App held its mutex; after stopping that App and its Watcher task, all tests passed. No product defect was inferred from that environmental conflict.
- Local source preview ran with a real USB DualSense and displayed 55%, Discharging. Compact and Two icons screenshots were captured from that session at native desktop resolution, without fabricating values.
- Native Bluetooth hardware verification is intentionally pending at the user's request. Initial pairing/full-mode activation, physical off/on reconnect, Windows resume, and coexistence with Steam Input/games are not claimed as tested.
- Published stable v1.0.2 does not contain this feature. Source/preview documentation distinguishes that release from this implementation.

- Integrated remote commit b9ae52d (shared self-contained runtime packaging); source build instructions now publish separately and merge via scripts/merge-publish.ps1.
- Shared-runtime App and Watcher publish outputs were built and merged successfully after integration. The installed stable Watcher task and original tray preference were restored after verification.
