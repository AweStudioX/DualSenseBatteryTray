# Controller Liveness Verification

Date: 2026-08-15 (Asia/Hong_Kong)

Scope: Final verification of the controller-liveness implementation through commits `328f1d8` (`fix: bound and report controller liveness probes`) and `b86672b` (`fix: serialize pending controller opens`). Verification was performed from the clean `feature/implementation` worktree at full commit `b86672ba9237f954669fd5adc1a6bc1a21462a3d` before this report update. No GitHub push was performed.

## Release verification

- The exact installed root scheduled task was stopped before testing because it immediately restarted the installed Watcher and App. Both named processes were resolved to existing executable paths below `%LOCALAPPDATA%\Programs\DualSenseBatteryTray` before only those two processes were stopped. The resulting task state was `Ready` and the named-process count was zero.
- A fresh `dotnet test DualSenseBatteryTray.sln -c Release --no-restore` run passed 310/310 tests with no failures or skips: Core 17, HID 46, Watcher 37, App 210.
- A subsequent `dotnet build DualSenseBatteryTray.sln -c Release --no-restore` succeeded with 0 warnings and 0 errors.
- Pester 6.0.1 passed all 32 installer tests with no failures or skips.
- `git diff --check` produced no diagnostics before publish/install and again after the stable runtime sample.

## Privacy and HID access audit

The prescribed privacy scan was run over `src`, `tests`, and `README.md`. Every match was reviewed:

- `trigger` matches were scheduled-task XML/tests, not controller telemetry.
- `button` matches were a negative logger assertion, WPF/WinForms mouse-button UI code, and native HID capability-structure field names.
- No user email, source-embedded user path, device-path logging, report bytes/hex/fingerprint logging, stick telemetry, or controller-button telemetry was found.

The prescribed HID-write scan found only `FileAccess.Write` in the bounded App log and tray-layout preference store. No `GENERIC_WRITE`, `HidD_SetFeature`, `WriteFile`, `WriteAsync`, exclusive HID open, output report, or Feature Report write exists in `src`. `HidNative.ReadOnlySharedOpenParameters` continues to request `GENERIC_READ` with `FILE_SHARE_READ | FILE_SHARE_WRITE`, and `HidInputReportStreamFactory` remains the only production caller of `OpenReadOnlyShared`.

The complete installed `watcher.log` and `app.log` were also scanned for `VID_`, `PID_`, device-path, report-byte/fingerprint, button, stick, and trigger fields. Both logs contained zero prohibited matches. The final install added no transition line because no matching adapter/controller identity was present; the existing bounded transition examples predate this final install:

```text
timestamp=2026-08-15T12:06:28.7302157+00:00 event=adapter.present
timestamp=2026-08-15T12:06:28.7868824+00:00 event=controller.stale
timestamp=2026-08-15T12:46:32.1755879+00:00 event=controller.live
```

## Publish and install

The App and Watcher were published self-contained for `win-x64`, as single files, into the new ignored directory `artifacts/controller-liveness-publish-20260815-final-b86672b`. Installation used the explicit command:

```powershell
.\scripts\install.ps1 -PublishDirectory $publishDir
```

Before installation, the exact root scheduled task was `Ready` and there were zero named App/Watcher processes. The transactional installer completed successfully, registered the exact root scheduled task, and started it.

| Artifact | Published SHA-256 | Installed SHA-256 | Result |
| --- | --- | --- | --- |
| `DualSenseBatteryTray.App.exe` | `68E4536BD372B6613DD6E196C46AEA1B3651CFD90702D224AE3855F111E30794` | `68E4536BD372B6613DD6E196C46AEA1B3651CFD90702D224AE3855F111E30794` | Match |
| `DualSenseBatteryTray.Watcher.exe` | `B719E048253EB931F6C0497C39A14C8A3814791E4FF16F7EED668603ECB9FB08` | `B719E048253EB931F6C0497C39A14C8A3814791E4FF16F7EED668603ECB9FB08` | Match |

Post-install evidence:

- Scheduled task: exact name `\DualSenseBatteryTray-DeviceWatcher`, state `Running`.
- Task executable: `%LOCALAPPDATA%\Programs\DualSenseBatteryTray\DualSenseBatteryTray.Watcher.exe`.
- Installed Watcher count: exactly 1 (PID 30308), created 2026-08-15 22:15:58.6902197 +08:00.
- Installed App count: 0 because no matching Sony DualSense identity was currently present.
- Both the immediate sample and a sample 12 seconds later reported Watcher 1 / App 0; every sampled process path resolved below the installation directory.
- The Watcher/App log sizes and modification times remained unchanged from their pre-install values, consistent with adapter absence and the policy that `AdapterAbsent` does not emit a transition event.

## Hardware acceptance

At the final runtime sample, `Get-PnpDevice -PresentOnly` returned zero identities matching `VID_054C/PID_0CE6`. The final installed binaries therefore exercised the adapter-absent state: one hidden Watcher remained running, no App/UI process existed, and no false `adapter.present`, `controller.live`, or `controller.stale` transition was logged.

| Acceptance item | Result | Evidence / limitation |
| --- | --- | --- |
| Adapter absent keeps App/UI hidden | Passed | Stable samples reported one installed Watcher and zero App processes; no matching Sony identity was present. |
| Adapter present, controller live, App starts without USB reconnect | Requires user action | The adapter/controller identity was absent during final verification, so this cannot be attributed to the final binaries from terminal evidence. |
| Live 100% reports remain visible | Requires user action | Requires a connected, powered controller reporting 100% battery. |
| Controller off while adapter remains: one Watcher, zero App/UI after timeout | Requires user action | Safely powering off the physical controller cannot be performed by this verification process without user interaction or adding a prohibited HID write path. |
| Controller on again without USB reconnect restores App | Requires user action | Depends on the preceding physical power transition. |
| Tray/window display the real battery | Requires visual confirmation | Terminal-only verification cannot attest to notification-area/window rendering or the displayed percentage. |
| Controller tester/game continues receiving input | Requires user action | No external tester/game was opened and no input was synthesized or intercepted. |

No physical off/on transition was performed and no transition timing is fabricated. The final installation is ready for the user to connect the Raspberry Pi adapter, power the controller on, confirm App/tray appearance and input in a controller tester, then power the controller off and on again without reconnecting USB while recording the App/UI transitions.
