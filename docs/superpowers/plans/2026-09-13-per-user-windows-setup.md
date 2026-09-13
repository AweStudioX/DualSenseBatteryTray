# Per-User Windows Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a single, per-user Windows Setup.exe alongside the existing ZIP in a verified v1.0.1 GitHub Release.

**Architecture:** NSIS Modern UI 2 wraps the two existing self-contained executables and invokes the existing transactional PowerShell install/uninstall scripts. The release workflow compiles the installer from a pinned, hash-verified NSIS compiler, verifies it on an ephemeral Windows runner, then publishes Setup.exe, ZIP, and checksum files only for a tag.

**Tech Stack:** NSIS 3.12, PowerShell 7/Windows PowerShell 5.1, Pester, .NET 8, GitHub Actions, GitHub CLI.

**Spec:** `docs/superpowers/specs/2026-09-13-per-user-windows-setup-design.md`

## Global Constraints

- Current-user installation only; `RequestExecutionLevel user`, HKCU Installed apps registration, current-user Start Menu shortcuts, no destination chooser.
- Fixed target: `%LOCALAPPDATA%\Programs\DualSenseBatteryTray`; no change to HID access, controller liveness, or Watcher/App behavior.
- Use the existing `scripts/install.ps1` and `scripts/uninstall.ps1` for filesystem/task/process operations. NSIS must not implement a second recursive removal path.
- Keep the ZIP/manual PowerShell flow. Release v1.0.1 without editing v1.0.0.
- NSIS compiler version 3.12.0; upstream `nsis-3.12.zip` SHA-256 is `56581f90db321581c5381193d796fffcf2d24b2f8fed2160a6c6a3baa67f2c4f` ([release data](https://github.com/NSIS-Dev/release-data/blob/main/data/versions.json)).
- The installer is unsigned; document possible SmartScreen warning. No admin or external .NET runtime required.
- Keep checksums and release assets free of personal paths, credentials, and private data.

## File Map

- Create `installer/DualSenseBatteryTray.nsi`: wizard, payload staging, PowerShell handoff, HKCU product metadata, shortcuts, uninstaller.
- Create `scripts/build-installer.ps1`: validate inputs and compiler, invoke `makensis.exe` with explicit paths/version/output.
- Create `tests/scripts/installer.Tests.ps1`: build behavior tests that do not install on the developer's machine.
- Create `tests/scripts/installer.Smoke.ps1`: opt-in destructive-on-current-user runner test for fresh install, upgrade, failure, and uninstall.
- Modify `.github/workflows/release.yml`: fetch verified compiler, build installer, run smoke test, attach Setup.exe and checksums; allow branch preflight via `workflow_dispatch` without publishing.
- Modify `README.md`: Setup.exe primary install/uninstall path and ZIP fallback.
- Add `LICENSES/nsis-zlib-libpng.txt` from the NSIS distribution's license text; include notices in the installer and ZIP.

---

### Task 1: NSIS Installer and Build Contract

**Files:**
- Create: `installer/DualSenseBatteryTray.nsi`
- Create: `scripts/build-installer.ps1`
- Create: `tests/scripts/installer.Tests.ps1`
- Create: `LICENSES/nsis-zlib-libpng.txt`

**Interfaces:**
- Consumes: published `DualSenseBatteryTray.App.exe`, `DualSenseBatteryTray.Watcher.exe`; existing `scripts/install.ps1`, `scripts/uninstall.ps1`, `scripts/device-watcher-task.xml`.
- Produces: `scripts/build-installer.ps1 -PublishDirectory <absolute directory> -MakensisPath <absolute exe> -Version <semver> -OutputPath <absolute exe>`; installer supports NSIS `/S` and uninstaller `/S`.

- [ ] **Step 1: Add failing contract tests.** In `tests/scripts/installer.Tests.ps1`, invoke the real build script with an invalid version and with a missing publish executable; assert precise failures. With a verified NSIS compiler available, compile a Setup.exe from two nonempty dummy files and assert a nonempty result. Do not run Setup.exe here.

```powershell
Describe 'Windows Setup build' {
    It 'rejects an invalid version before invoking the compiler' {
        {
            & $buildScript -PublishDirectory $TestDrive -MakensisPath $makensisPath `
                -Version '1.0.1; malicious' -OutputPath (Join-Path $TestDrive 'Setup.exe')
        } | Should -Throw '*Invalid version*'
    }
}
```

- [ ] **Step 2: Run the failing Pester test.** Run `pwsh -NoProfile -Command "Invoke-Pester tests/scripts/installer.Tests.ps1 -CI"`; expect failure because the new source/build files do not exist.

- [ ] **Step 3: Implement the build wrapper and NSIS source.** Validate absolute paths, both nonempty executables, exact `^\d+\.\d+\.\d+$` version, and `makensis.exe` existence before compiler execution. Pass all values via NSIS `/D...` defines, never by editing the source in place. The NSIS install section must stage scripts/template and payload in `$PLUGINSDIR`, write `Uninstall.exe` into that payload *before* invoking the PowerShell installer, and stop on nonzero exit. After success, write HKCU uninstall metadata and current-user shortcuts. The uninstall section must compare `$INSTDIR` to the fixed path, copy `uninstall.ps1` to `$PLUGINSDIR`, wait for it to succeed, then remove only its exact HKCU key and shortcuts.

```nsis
Unicode true
RequestExecutionLevel user
!include "MUI2.nsh"
!include "LogicLib.nsh"
InstallDir "$LOCALAPPDATA\Programs\DualSenseBatteryTray"
Function .onInit
  SetShellVarContext current
FunctionEnd
Section "Install"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=install.ps1 "${PROJECT_ROOT}\scripts\install.ps1"
  File /oname=device-watcher-task.xml "${PROJECT_ROOT}\scripts\device-watcher-task.xml"
  SetOutPath "$PLUGINSDIR\payload"
  File "${PUBLISH_DIR}\DualSenseBatteryTray.App.exe"
  File "${PUBLISH_DIR}\DualSenseBatteryTray.Watcher.exe"
  File "${PROJECT_ROOT}\scripts\uninstall.ps1"
  WriteUninstaller "$PLUGINSDIR\payload\Uninstall.exe"
  ClearErrors
  ExecWait '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\install.ps1" -PublishDirectory "$PLUGINSDIR\payload"' $0
  IfErrors install_failed
  ${If} $0 != 0
    Goto install_failed
  ${EndIf}
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DualSenseBatteryTray" "DisplayName" "DualSense Battery Tray"
  IfErrors install_failed
  Goto install_done
install_failed:
  SetErrorLevel 1
  Abort "Setup failed; check $INSTDIR for a recoverable installation."
install_done:
SectionEnd
```

Expand this section with version/publisher/icon/uninstall metadata, shortcuts, matching uninstaller section, and checks after each registry/shortcut operation. In the uninstaller section, validate `$INSTDIR` against the fixed per-user path before running the staged PowerShell script. The installed `uninstall.ps1` must remain in the installed directory. Use the existing disconnected icon at `src/DualSenseBatteryTray.App/Assets/App/dualsense-disconnected.ico` for installer and uninstaller identity. Copy the NSIS zlib/libpng license verbatim from the pinned distribution and verify its text against that distribution.

- [ ] **Step 4: Run tests and compile with a dummy publish directory.** First run Pester; expect pass. Build with two nonempty dummy `.exe` files to test NSIS syntax without touching the user's installation. Inspect compiler exit code, generated Setup.exe existence, and nonzero size. Do not execute this dummy installer.

```powershell
pwsh -NoProfile -Command "Invoke-Pester tests/scripts/installer.Tests.ps1 -CI"
pwsh -NoProfile -File scripts/build-installer.ps1 -PublishDirectory "$env:TEMP\dualsense-dummy-publish" -MakensisPath "$env:TEMP\nsis-3.12\makensis.exe" -Version 1.0.1 -OutputPath "$env:TEMP\DualSenseBatteryTray-v1.0.1-win-x64-Setup.exe"
```

- [ ] **Step 5: Commit.** `git add installer/DualSenseBatteryTray.nsi scripts/build-installer.ps1 tests/scripts/installer.Tests.ps1 LICENSES/nsis-zlib-libpng.txt` then `git commit -m "feat: add per-user Windows setup installer"`.

### Task 2: Runner Smoke Test and Release Workflow

**Files:**
- Create: `tests/scripts/installer.Smoke.ps1`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `scripts/build-installer.ps1` interface from Task 1; existing PowerShell installer, published App/Watcher, `makensis.exe`.
- Produces: `tests/scripts/installer.Smoke.ps1 -SetupPath <absolute exe> -LegacyPublishDirectory <absolute directory>`, exits nonzero on any failed assertion; release workflow outputs ZIP, Setup.exe, and their `.sha256` sidecars.

- [ ] **Step 1: Write the failing smoke assertions.** Require `$env:GITHUB_ACTIONS -eq 'true'` before any install/uninstall call, so a local accidental invocation cannot touch the user's installation. The script must run a fresh `Setup.exe /S`, assert both installed executables, the named scheduled task, and `HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DualSenseBatteryTray`; run `Setup.exe /S` again as an installer upgrade; uninstall via installed `Uninstall.exe /S`; assert all product artifacts removed. Add a script-only upgrade case: invoke existing `install.ps1 -PublishDirectory <LegacyPublishDirectory>`, then run Setup.exe and assert a matching installed uninstaller. Add a failure case by placing an ordinary file at the fixed install-directory path on a clean runner, running Setup.exe, and asserting nonzero status and no Installed apps entry. Ensure a `finally` block removes only test-created product state via the product uninstaller or existing safe script; if the sentinel file remains, check it is an ordinary file at the exact target path and remove that file only.

```powershell
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installer smoke test is CI-only.' }
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\DualSenseBatteryTray'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DualSenseBatteryTray'
& $SetupPath /S
if ($LASTEXITCODE -ne 0) { throw "Setup exited $LASTEXITCODE" }
if (-not (Test-Path (Join-Path $installRoot 'DualSenseBatteryTray.App.exe'))) { throw 'App missing' }
if (-not (Test-Path $uninstallKey)) { throw 'Installed apps record missing' }
```

- [ ] **Step 2: Run smoke script against a missing Setup.exe.** On a disposable runner or equivalent isolated Windows account, expect a failure before it changes any product state. Never run a real installer smoke test against the user's existing `%LOCALAPPDATA%` installation.

- [ ] **Step 3: Extend the workflow.** Add `workflow_dispatch` with a `version` input for pre-tag validation; tag runs derive version from `vX.Y.Z`, dispatch runs use its input, and only tag runs execute `gh release create`. Download `https://downloads.sourceforge.net/project/nsis/NSIS%203/3.12/nsis-3.12.zip`, assert SHA-256 equals the global-constraint digest, then expand it under `$RUNNER_TEMP`. Build Setup.exe from the two published executables via `scripts/build-installer.ps1`. Run Pester, .NET tests, and `installer.Smoke.ps1` before packaging/publishing. Compute one SHA-256 sidecar per release asset and pass all four paths to `gh release create`. The branch dispatch must upload its unsigned Setup.exe/ZIP as non-release Actions artifacts for inspection, not create a release.

```yaml
on:
  push:
    tags: ["v*.*.*"]
  workflow_dispatch:
    inputs:
      version:
        description: "Version to validate without publishing"
        required: true
        default: "1.0.1"
        type: string
```

- [ ] **Step 4: Run local non-install tests, then a branch preflight.** Run `dotnet test DualSenseBatteryTray.sln --configuration Release`, `Invoke-Pester tests/scripts/*.Tests.ps1 -CI`, and `git diff --check`. Push the branch only after review; dispatch the workflow on that branch with `version=1.0.1`, wait for success, and inspect smoke output and artifacts. A failed smoke test blocks tagging.

- [ ] **Step 5: Commit.** `git add tests/scripts/installer.Smoke.ps1 .github/workflows/release.yml` then `git commit -m "ci: verify and package Windows setup"`.

### Task 3: User Documentation and v1.0.1 Publication

**Files:**
- Modify: `README.md`
- Verify: `.github/workflows/release.yml`, public GitHub Release assets.

**Interfaces:**
- Consumes: the verified Setup.exe/ZIP workflow from Task 2.
- Produces: clear public install/uninstall instructions and the v1.0.1 release assets.

- [ ] **Step 1: Update README.** Lead with downloading/running `DualSenseBatteryTray-v1.0.1-win-x64-Setup.exe`; explain current-user/no admin/fixed location, bundled .NET, Watcher logon task and controller-live-only tray UI, Windows Installed apps removal, unsigned SmartScreen notice, and ZIP plus `install.ps1`/`uninstall.ps1` fallback. State ZIP is not portable. Review the rendered Markdown manually; do not add a source-text test for human prose.

- [ ] **Step 2: Re-run source and package checks.** Run Pester, .NET tests, `git diff --check`, inspect source for secrets and user-specific paths, and ensure only expected distributable files are included. Commit: `git add README.md docs/superpowers/plans/2026-09-13-per-user-windows-setup.md` and `git commit -m "docs: make setup the primary download"`.

- [ ] **Step 3: Publish only after preflight succeeds.** Merge/push the reviewed source changes to the public repository's main branch; check `git status --short` is empty and that `v1.0.1` does not already exist locally or remotely. Create exactly one annotated `v1.0.1` tag at the verified commit, push the tag, wait for the Release workflow, and inspect the public GitHub Release. Confirm Setup.exe, ZIP, and both `.sha256` files are directly downloadable; compare downloaded asset hashes with sidecars. Do not overwrite v1.0.0 or create a second v1.0.1 tag if the workflow fails—repair the release process without retagging silently.

```powershell
git tag -a v1.0.1 -m "DualSense Battery Tray v1.0.1"
git push origin v1.0.1
gh run list --workflow release.yml --limit 5
gh release view v1.0.1 --json url,assets
```
