# Per-User Windows Setup Design

## Goal

Make the primary v1.0.1 download a single, conventional `Setup.exe`. A user should double-click it, complete a short wizard, and find DualSense Battery Tray in Windows Installed apps. Installation must not require administrator privileges or a separate .NET runtime. Keep the v1.0.0-style ZIP and PowerShell workflow as a fallback.

## Decisions and Scope

- Use NSIS with its Modern UI 2 wizard. Build one `win-x64` installer for the current Windows user; do not offer an all-users mode or a destination chooser.
- Install to `%LOCALAPPDATA%\Programs\DualSenseBatteryTray`. The App and Watcher remain separate internal processes, but the user installs one product with one executable.
- Continue to register the existing current-user logon task. The Watcher may run after sign-in, but the tray App must still appear only when the controller is live; packaging does not change controller detection or HID access.
- Add a Windows Installed apps entry and Start Menu launch/uninstall shortcuts. Use the existing disconnected-controller app icon for product identity.
- Keep the ZIP as a manual-install fallback, not a portable edition. Do not replace or edit the existing v1.0.0 release; publish v1.0.1 from a new tag.
- Code signing is outside this change. The installer will be unsigned, so Windows SmartScreen may warn even though no UAC elevation is requested.

## Package and Installation Flow

The tagged Windows workflow publishes self-contained, single-file App and Watcher executables as it does today. It then compiles an NSIS script with NSIS 3.12.0, verifying the compiler download against a pinned upstream SHA-256 digest. The generated `DualSenseBatteryTray-v1.0.1-win-x64-Setup.exe` embeds the two executables, `install.ps1`, `uninstall.ps1`, `device-watcher-task.xml`, required license notices, and an NSIS uninstaller.

The wizard uses a fixed per-user target. It extracts the installation script and task template to NSIS's private temporary directory. It creates a payload directory containing the two executables, an installed copy of `uninstall.ps1`, the uninstaller, and required notices. It invokes `install.ps1 -PublishDirectory <payload>` using Windows PowerShell and waits for its exit code. The existing script validates the fixed destination, stages the payload, backs up any prior install, replaces it, registers the task, starts the Watcher, and rolls back its own filesystem/task changes on failure.

Only after that script succeeds does NSIS write the HKCU Installed apps entry (`DisplayName`, version, publisher, install location, icon, uninstall command, and `NoModify`/`NoRepair`) and current-user Start Menu shortcuts. There is no runtime download. The wizard reports errors rather than claiming a successful installation when the script or post-install registration fails. If Windows prevents metadata registration after the script has succeeded, the files and installed uninstaller remain available for manual recovery; the error must state that the product may be installed and identify the fixed installation path.

For an upgrade, run the same installer over either a prior ZIP/script installation or a prior NSIS installation. Do not run the old uninstaller first. The existing `install.ps1` backup/rollback transaction protects the previous binaries and task. Because the new uninstaller is part of the staged payload, a successful upgrade always installs a matching uninstaller; an unsuccessful script transaction restores the old installation. The Installed apps version and shortcuts are updated only after the new script succeeds.

## Uninstallation Flow

Installed apps or the Start Menu invokes the NSIS uninstaller. Before any removal it verifies that its installation directory is the one fixed per-user destination; an uninstaller copied elsewhere must not remove the product. NSIS runs from its temporary copy, so the installed uninstaller can be deleted. It stages `uninstall.ps1` outside the installation directory, invokes it with Windows PowerShell, and waits for success. That script unregisters the named task, stops only App/Watcher processes actually running from the fixed installation directory, and safely removes that directory.

Only after the script succeeds does NSIS remove its exact HKCU Installed apps key and Start Menu shortcuts. If the script fails, retain those entry points and display the error so the user can retry. The manual ZIP `uninstall.ps1` remains documented for ZIP-only installations; users of Setup.exe should use Installed apps or its uninstall shortcut.

## Failure and Security Boundaries

- Neither installer nor uninstaller requests elevation. Registry writes use HKCU and shortcuts use the current user's Start Menu.
- Preserve the existing PowerShell destination/reparse-point checks and process-path filtering. NSIS must not recursively delete the installation directory itself or terminate processes by name alone.
- The installer must quote every path passed to PowerShell and inspect its exit code. A missing executable, failed task registration, or failed script call is a hard failure.
- On an upgrade failure in `install.ps1`, leave the prior Installed apps record and shortcuts untouched. On uninstall failure, keep the record and shortcuts for retry.
- Do not embed secrets, personal paths, private repository data, or build-machine credentials in the distributable or release notes. Include third-party notices needed for shipped artwork and NSIS.

## Release and Verification

Update the release workflow to produce the existing ZIP plus `Setup.exe`, with a SHA-256 sidecar for each. Both assets and checksums attach to the same v1.0.1 GitHub Release. Update the README so Setup.exe is the first install path, with current-user scope, auto-start behavior, Installed apps removal, SmartScreen/signing limitation, and ZIP fallback explicit.

Before tagging, verify on a clean Windows environment: compile the NSIS script; run a silent fresh install; inspect both installed executables, the task, and HKCU uninstall registration; run an in-place upgrade over a script-only installation and over an existing Setup installation; then silently uninstall and verify product files, task, registry entry, and shortcuts are gone. Exercise a failed-payload or failed-script path and confirm it does not report success or replace a working prior installation. Run the existing .NET tests unchanged. The release workflow must fail before publishing if packaging or smoke verification fails.

After the source change is merged, create and push `v1.0.1` once, wait for the GitHub Actions job to succeed, and inspect the public release assets and their hashes. Do not modify the v1.0.0 release.

## Technical References

- [NSIS Modern UI 2](https://nsis.sourceforge.io/Docs/Modern%20UI%202/Readme.html)
- [NSIS uninstaller temporary-copy behavior](https://nsis.sourceforge.io/Docs/Chapter4.html)
- [NSIS current-user Installed apps registration](https://nsis.sourceforge.io/Docs/AppendixD.html)
- [NSIS 3.12 upstream release](https://sourceforge.net/p/nsis/news/2026/04/nsis-312-released/)
