# Controller Liveness Behind an Always-Present Adapter Design

## Goal

Prevent the App, notification icon, windows, and future floating battery window from appearing when the Raspberry Pi adapter is present but the physical DualSense is offline, while preserving automatic startup when the controller becomes live and retaining shared read-only HID access.

## Root-Cause Evidence

At Windows sign-in, the scheduled task starts only `DualSenseBatteryTray.Watcher.exe`. The Watcher then starts the App because Windows reports a present HID device with DualSense vendor/product identity `VID_054C&PID_0CE6`.

The adapter remains enumerated even when the physical controller is offline. In that state it continuously returns a 64-byte USB input report whose battery byte is `0x29`. Under the DualSense protocol this correctly parses as status `Full` and percentage `100`.

A six-second shared read-only capture collected 24 reports from the offline adapter. All 24 had:

- report ID `0x01`;
- battery byte `0x29`;
- one distinct report sequence value;
- one distinct sensor timestamp;
- one distinct full-report fingerprint.

Therefore neither Windows device presence nor battery value establishes that the physical controller is online. The reliable observed distinction is report liveness: a real controller advances protocol sequence and/or sensor timestamp fields even while physically still, whereas the adapter's offline placeholder report is completely frozen.

No raw report, button state, device path, or user-specific machine path is written to production logs.

## State Model

The system distinguishes three states:

```text
AdapterAbsent  — no matching VID/PID HID interface exists
AdapterIdle    — matching interface exists, but reports are frozen or unavailable
ControllerLive — matching interface exists and protocol liveness advances
```

Only `ControllerLive` permits the App to run or any user-facing UI to exist.

The battery value is never used as a liveness signal. `Full / 100%` remains a valid live-controller battery state when sequence or sensor time advances.

## Liveness Signal

For USB report ID `0x01`, the liveness observer extracts:

- the report sequence field already present in the standard input report;
- the 32-bit sensor timestamp field already present in the standard input report.

An observation is progressive when either field differs from the previous structurally valid report. The observer does not parse or retain buttons, sticks, triggers, touch contacts, motion values, audio state, or the complete report.

The liveness observer returns one of:

```text
Insufficient — fewer than the required structurally valid observations
Progressing  — sequence or sensor timestamp advanced
Stale        — enough reports arrived, but neither field advanced
```

Malformed, truncated, or unsupported report IDs do not count as structurally valid observations.

## Startup Probe

The Watcher no longer equates `IControllerPresence.IsPresent` with a live controller.

When a matching HID interface exists, it opens the same shared read-only input handle contract used by the App and performs a bounded probe:

- maximum duration: 1.5 seconds;
- required structurally valid observations: 3;
- success: sequence or sensor timestamp advances before the deadline;
- idle result: three valid reports arrive without progress;
- failure result: the deadline expires, the device disappears, or reads fail without sufficient evidence.

Only a successful `Progressing` result launches the App. `Stale`, `Insufficient`, and probe errors leave the Watcher running without user-facing UI.

The probe is single-flight. Device-arrival notifications and periodic retries may request a check, but only one shared read session may be active at a time. A pending request coalesces into one subsequent check.

## Idle Adapter Polling

An always-present adapter may not generate a new USB arrival notification when the physical controller later comes online. While the App is absent, the Watcher therefore performs a liveness attempt every two seconds.

The poll loop behaves as follows:

- `AdapterAbsent`: wait for the next two-second tick or HID arrival notification;
- `AdapterIdle`: retry after two seconds;
- `ControllerLive`: start the App and continue watching process/mutex state;
- App already running: do not probe or launch a second instance;
- App later exits while the Watcher remains: resume probing on the next tick.

Arrival notifications still trigger the existing 500-millisecond debounce and then request an immediate liveness check. They do not create parallel probes.

The Watcher stops its polling timer and cancels active reads on session logoff or disposal.

## Runtime Liveness in the App

The App cannot rely on physical USB removal because the adapter remains enumerated. `DualSenseHidReader` therefore reports both battery state and liveness through the same input stream.

The runtime monitor has these rules:

- every structurally valid report is offered to the liveness observer;
- a progressive report resets the stale deadline;
- battery state continues to be emitted only when its parsed value changes;
- two seconds without sequence or sensor timestamp progress ends the reader as a stale-controller condition;
- cancellation, App refresh, and shutdown remain distinct from stale-controller termination;
- transient read failures continue to use the existing bounded failure policy and do not independently fabricate a live or stale state.

When runtime liveness becomes stale, the App follows the existing reader-ended shutdown path. It removes every notification icon, closes the information window and future floating window, disposes the HID reader, releases the single-instance mutex, and exits. The Watcher remains and resumes its two-second liveness probes.

## Shared Read-Only Contract

Both Watcher probes and App monitoring use:

- `GENERIC_READ` only;
- `FILE_SHARE_READ | FILE_SHARE_WRITE`;
- no output report;
- no Feature Report set operation;
- no exclusive handle;
- no virtual controller, remapping, input interception, or global hooks.

The Watcher never probes while the App mutex is present, avoiding an unnecessary second reader during normal operation. Games continue to receive controller input.

## Components and Responsibilities

### `DualSenseReportLivenessObserver`

A Core/HID-domain unit that consumes only the minimum report fields and produces `Insufficient`, `Progressing`, or `Stale`. It owns no timers, streams, logs, or UI and is fully deterministic in tests.

### `IControllerLivenessProbe`

An asynchronous Watcher-facing boundary:

```csharp
Task<ControllerLivenessProbeResult> ProbeAsync(
    ControllerIdentity identity,
    CancellationToken cancellationToken);
```

Its HID implementation performs the 1.5-second, three-observation bounded shared-read probe.

### `WatcherLivenessCoordinator`

Owns the two-second periodic trigger, debounced arrival requests, single-flight coordination, App-mutex check, and launch decision. It never parses HID reports itself.

### `DualSenseHidReader`

Continues to own the App's shared input stream. It incorporates the deterministic observer and a two-second stale deadline while preserving battery parsing, duplicate battery suppression, cancellation, disposal, and bounded read failures.

### App and UI Layer

No UI component guesses connection state from battery value. UI exists only during the App lifetime; stale reader termination closes all App UI through the established shutdown path.

## Logging

Production logging adds bounded transition events only:

- `adapter.present` when a matching adapter first becomes observable;
- `controller.live` when a probe or runtime stream establishes progress;
- `controller.stale` when adequate frozen reports or a runtime stale deadline establishes idle state;
- `liveness.failure` for probe/read failures, rate-limited to avoid a line every two seconds.

Logs contain event name, timestamp, and sanitized exception summary where applicable. They never contain raw HID bytes, control values, serial number, device path, or report fingerprint.

## Failure Handling

- No matching device: remain in `AdapterAbsent`; do not launch the App.
- Adapter present with frozen reports: remain in `AdapterIdle`; do not launch the App.
- Real live controller at 100%: launch because protocol progress, not percentage, proves liveness.
- Fewer than three valid reports before the startup deadline: treat as not live and retry later.
- Probe open/read failure: keep the Watcher alive, log with rate limiting, and retry later.
- Adapter disappears during a probe: cancel/finish the probe and return to `AdapterAbsent`.
- App stream stops progressing: close the App after the two-second stale deadline.
- Sequence wraparound: any value change counts as progress; arithmetic ordering is unnecessary.
- Sensor timestamp wraparound: any value change counts as progress.
- One liveness field is constant but the other advances: treat as live.
- App mutex remains after a process failure: existing single-instance ownership behavior governs; the Watcher never force-kills a process.

## Testing

Automated tests cover:

- three identical valid reports produce `Stale`;
- sequence-only progress produces `Progressing`;
- sensor-timestamp-only progress produces `Progressing`;
- sequence and timestamp wraparound still count as progress;
- malformed, truncated, and unsupported reports remain `Insufficient`;
- `0x29 / Full / 100%` with progress is live;
- `0x29 / Full / 100%` without progress is stale;
- the startup probe succeeds within 1.5 seconds on progressive reports;
- the startup probe rejects frozen reports after three valid observations;
- startup timeout and read failures do not launch the App;
- Watcher polling repeats every two seconds while idle and stops after launch;
- arrival and timer requests coalesce into one active probe;
- App-running state suppresses probes and duplicate launches;
- App exit resumes probing;
- runtime progress resets the two-second stale deadline;
- runtime frozen reports terminate the reader and App;
- battery duplicate suppression remains independent of liveness progress;
- cancellation and disposal terminate promptly without reporting false stale state;
- the HID open parameters remain shared and read-only.

Fresh Release tests and build must pass without requiring a connected controller.

## Installed Acceptance Criteria

With the Raspberry Pi adapter left connected:

1. At Windows sign-in with the physical controller offline, only one Watcher process runs; there is no App process, tray icon, information window, or floating window.
2. Bringing the controller online without reconnecting USB starts the App within the two-second polling interval plus the bounded probe time.
3. A live controller at 100% displays 100% normally.
4. Taking the controller offline while leaving the adapter connected closes the App and all UI within two seconds of lost report progress, plus normal scheduling tolerance.
5. Bringing it online again automatically restores the App and UI.
6. Games continue receiving all controller inputs because monitoring remains shared and read-only.

This liveness acceptance must pass before the single-tray floating-window feature is considered ready for installed verification.
