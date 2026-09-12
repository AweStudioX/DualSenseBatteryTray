# Controller Liveness Behind an Always-Present Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Start and keep the App visible only while a physical DualSense produces advancing input reports behind the always-enumerated Raspberry Pi adapter.

**Architecture:** Add a deterministic HID report-liveness observer, then reuse it in a bounded startup probe and the existing App reader. Replace the Watcher's presence-only launch decision with a single-flight two-second polling coordinator, while retaining the current mutex, device-arrival debounce, and reader-ended App shutdown path.

**Tech Stack:** C# 12, .NET 8 Windows, WinForms message-only Watcher, WPF App, Windows HID APIs, xUnit.

## Global Constraints

- Only `ControllerLive` permits the App to run or any user-facing UI to exist.
- USB report ID `0x01` is structurally valid only when it contains sequence byte offset `7` and the 32-bit little-endian sensor timestamp at offset `28`.
- A report is progressive when either sequence or sensor timestamp differs from the preceding structurally valid report; wraparound is a change and therefore progress.
- Battery value is never a liveness signal; progressive `0x29 / Full / 100%` is live and frozen `0x29` is stale.
- Startup probing lasts at most 1.5 seconds and requires three structurally valid observations; three frozen observations return `Stale`.
- While the App mutex is absent, the Watcher retries every two seconds; HID arrivals retain the existing 500-millisecond debounce and requests must coalesce into one active probe.
- Runtime liveness expires after two seconds without sequence or sensor timestamp progress and follows the existing reader-ended App shutdown path.
- All HID input handles remain `GENERIC_READ` with `FILE_SHARE_READ | FILE_SHARE_WRITE`; do not add output reports, Feature Report writes, exclusive handles, virtual controllers, remapping, hooks, or input interception.
- Production logs contain only bounded transition event names, timestamps, and sanitized exception summaries—never raw HID bytes, controls, serial numbers, device paths, or report fingerprints.
- Fresh Release tests and build must pass without requiring a connected controller.

## File Structure

- Create `src/DualSenseBatteryTray.Hid/DualSenseReportLivenessObserver.cs`: parse only the sequence/timestamp liveness fields and expose deterministic observation results.
- Create `src/DualSenseBatteryTray.Hid/HidInputReportStreamFactory.cs`: centralize shared read-only HID path lookup, handle opening, report length, and stream ownership for both probe and reader.
- Create `src/DualSenseBatteryTray.Hid/ControllerLivenessProbe.cs`: expose the bounded asynchronous startup probe used by the Watcher.
- Modify `src/DualSenseBatteryTray.Hid/DualSenseHidReader.cs`: apply the same liveness observer and runtime stale deadline without changing battery deduplication.
- Create `src/DualSenseBatteryTray.Watcher/WatcherLivenessCoordinator.cs`: own periodic requests, single-flight/coalescing, mutex suppression, launch, and disposal.
- Create `src/DualSenseBatteryTray.Watcher/WatcherEventLogger.cs`: write bounded Watcher transition events without accepting device/report data.
- Modify `src/DualSenseBatteryTray.Watcher/Program.cs`: compose the liveness probe/coordinator instead of presence-only `WatcherLauncher`.
- Modify `src/DualSenseBatteryTray.Watcher/DeviceNotificationWindow.cs`: send debounced arrival requests to the coordinator and cancel it on disposal/logoff.
- Modify `src/DualSenseBatteryTray.App/App.xaml.cs`: log stale termination distinctly and retain existing complete UI shutdown.
- Add focused tests beside the corresponding HID, Watcher, and App test projects.

---

### Task 1: Deterministic DualSense Report-Liveness Observer

**Files:**
- Create: `src/DualSenseBatteryTray.Hid/DualSenseReportLivenessObserver.cs`
- Create: `tests/DualSenseBatteryTray.Hid.Tests/DualSenseReportLivenessObserverTests.cs`

**Interfaces:**
- Consumes: raw USB input reports including report ID at byte `0`.
- Produces: `public enum ControllerLivenessObservation { Insufficient, Progressing, Stale }` and `public ControllerLivenessObservation Observe(ReadOnlySpan<byte> report)`.

- [ ] **Step 1: Write failing observer tests**

Create a report helper that allocates 64 bytes, writes `0x01` at offset `0`, the sequence at offset `7`, the timestamp with `BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(28, 4), timestamp)`, and an optional battery byte at `BatteryParser.BatteryOffset + 1`. Add tests with these exact expectations:

```csharp
[Fact]
public void Three_identical_valid_reports_become_stale()
{
    var observer = new DualSenseReportLivenessObserver(requiredObservations: 3);
    Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
    Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
    Assert.Equal(ControllerLivenessObservation.Stale, observer.Observe(Report(4, 99)));
}

[Theory]
[InlineData(4, 99u, 5, 99u)]
[InlineData(4, 99u, 4, 100u)]
[InlineData(255, uint.MaxValue, 0, 0u)]
public void Either_changed_field_is_progress(
    byte firstSequence, uint firstTimestamp,
    byte nextSequence, uint nextTimestamp)
{
    var observer = new DualSenseReportLivenessObserver(3);
    _ = observer.Observe(Report(firstSequence, firstTimestamp));
    Assert.Equal(
        ControllerLivenessObservation.Progressing,
        observer.Observe(Report(nextSequence, nextTimestamp)));
}
```

Also assert truncated reports, 64-byte reports with an unsupported ID, and two valid observations remain `Insufficient`. Add paired tests showing progressing `0x29` is `Progressing` while three frozen `0x29` reports are `Stale`.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj -c Release --filter FullyQualifiedName~DualSenseReportLivenessObserverTests
```

Expected: compilation fails because the observer and result enum do not exist.

- [ ] **Step 3: Implement the minimum observer**

Implement constants and state without copying the complete report:

```csharp
public enum ControllerLivenessObservation
{
    Insufficient,
    Progressing,
    Stale,
}

public sealed class DualSenseReportLivenessObserver(int requiredObservations = 3)
{
    public const byte UsbReportId = 0x01;
    public const int SequenceOffset = 7;
    public const int SensorTimestampOffset = 28;
    public const int MinimumReportLength = SensorTimestampOffset + sizeof(uint);

    private int _validObservations;
    private byte _lastSequence;
    private uint _lastTimestamp;
    private bool _hasPrevious;

    public ControllerLivenessObservation Observe(ReadOnlySpan<byte> report)
    {
        if (report.Length < MinimumReportLength || report[0] != UsbReportId)
            return ControllerLivenessObservation.Insufficient;

        var sequence = report[SequenceOffset];
        var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(
            report.Slice(SensorTimestampOffset, sizeof(uint)));
        _validObservations++;

        var progressed = _hasPrevious
            && (sequence != _lastSequence || timestamp != _lastTimestamp);
        _hasPrevious = true;
        _lastSequence = sequence;
        _lastTimestamp = timestamp;

        if (progressed)
            return ControllerLivenessObservation.Progressing;

        return _validObservations >= requiredObservations
            ? ControllerLivenessObservation.Stale
            : ControllerLivenessObservation.Insufficient;
    }
}
```

Validate `requiredObservations >= 2` in the constructor. Do not store the source span or any control fields.

- [ ] **Step 4: Run observer and existing parser tests**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj -c Release --filter "FullyQualifiedName~DualSenseReportLivenessObserverTests|FullyQualifiedName~HidReportProcessorTests"
```

Expected: all selected tests pass; existing battery parsing/deduplication remains unchanged.

- [ ] **Step 5: Commit the observer**

```powershell
git add src/DualSenseBatteryTray.Hid/DualSenseReportLivenessObserver.cs tests/DualSenseBatteryTray.Hid.Tests/DualSenseReportLivenessObserverTests.cs
git commit -m "feat: detect advancing DualSense reports"
```

---

### Task 2: Shared Read-Only Stream Factory and Bounded Startup Probe

**Files:**
- Create: `src/DualSenseBatteryTray.Hid/HidInputReportStreamFactory.cs`
- Create: `src/DualSenseBatteryTray.Hid/ControllerLivenessProbe.cs`
- Modify: `src/DualSenseBatteryTray.Hid/DualSenseHidReader.cs`
- Create: `tests/DualSenseBatteryTray.Hid.Tests/ControllerLivenessProbeTests.cs`
- Modify: `tests/DualSenseBatteryTray.Hid.Tests/HidOpenContractTests.cs`

**Interfaces:**
- Consumes: `HidDeviceEnumerator.FindPaths(ControllerIdentity)` and `HidNative.OpenReadOnlyShared(string)`.
- Produces: `public interface IControllerLivenessProbe { Task<ControllerLivenessProbeResult> ProbeAsync(ControllerIdentity identity, CancellationToken cancellationToken); }`, `public enum ControllerLivenessProbeResult { AdapterAbsent, Insufficient, Stale, Progressing }`, and an internal injectable input-session factory used by both probe and reader.

- [ ] **Step 1: Write failing probe tests with an in-memory report session**

Define a test fake for the internal factory that returns a session backed by a queue of byte arrays or throws a configured `IOException`. Cover these cases:

```csharp
[Fact]
public async Task Progressive_reports_return_progressing_even_at_full_battery()
{
    var reports = new[] { Report(9, 100, 0x29), Report(10, 101, 0x29) };
    var probe = CreateProbe(reports, timeout: TimeSpan.FromSeconds(1.5));

    var result = await probe.ProbeAsync(
        ControllerIdentity.UsbDualSense, CancellationToken.None);

    Assert.Equal(ControllerLivenessProbeResult.Progressing, result);
}

[Fact]
public async Task Three_frozen_reports_return_stale()
{
    var frozen = Report(9, 100, 0x29);
    var probe = CreateProbe(new[] { frozen, frozen, frozen });
    Assert.Equal(
        ControllerLivenessProbeResult.Stale,
        await probe.ProbeAsync(ControllerIdentity.UsbDualSense, CancellationToken.None));
}
```

Also test: no matching path returns `AdapterAbsent`; malformed reports until the 1.5-second deadline return `Insufficient`; open/read errors return `Insufficient`; external cancellation throws `OperationCanceledException`; the fake records exactly `ControllerIdentity.UsbDualSense`.

- [ ] **Step 2: Run probe tests and verify RED**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj -c Release --filter FullyQualifiedName~ControllerLivenessProbeTests
```

Expected: compilation fails because the probe types and injectable stream boundary do not exist.

- [ ] **Step 3: Extract the shared input-session factory**

Create:

```csharp
internal interface IHidInputReportSessionFactory
{
    ValueTask<HidInputReportSession?> OpenAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken);
}

internal sealed class HidInputReportSession : IAsyncDisposable
{
    internal HidInputReportSession(Stream stream, int reportLength)
    {
        Stream = stream;
        ReportLength = reportLength;
    }

    internal Stream Stream { get; }
    internal int ReportLength { get; }
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
```

The production implementation must find the first matching path, call only `HidNative.OpenReadOnlyShared`, obtain `GetInputReportByteLength`, construct the asynchronous `FileStream`, and transfer handle ownership exactly once. Return `null` when no path exists. Refactor `DualSenseHidReader` to use this factory without changing its observable battery behavior.

- [ ] **Step 4: Implement the bounded probe**

Use these defaults and cancellation semantics:

```csharp
public sealed class ControllerLivenessProbe : IControllerLivenessProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1.5);
    public const int RequiredObservations = 3;

    public async Task<ControllerLivenessProbeResult> ProbeAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var session = await _sessions.OpenAsync(identity, cancellationToken);
        if (session is null)
            return ControllerLivenessProbeResult.AdapterAbsent;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        var observer = new DualSenseReportLivenessObserver(RequiredObservations);
        var buffer = new byte[session.ReportLength];
        try
        {
            while (true)
            {
                var count = await session.Stream.ReadAsync(buffer, deadline.Token);
                if (count == 0)
                    return ControllerLivenessProbeResult.Insufficient;

                var observation = observer.Observe(buffer.AsSpan(0, count));
                if (observation == ControllerLivenessObservation.Progressing)
                    return ControllerLivenessProbeResult.Progressing;
                if (observation == ControllerLivenessObservation.Stale)
                    return ControllerLivenessProbeResult.Stale;
            }
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ControllerLivenessProbeResult.Insufficient;
        }
        catch (IOException)
        {
            return ControllerLivenessProbeResult.Insufficient;
        }
    }
}
```

Allow external cancellation to propagate. Validate nonzero positive timeout in the injectable constructor.

- [ ] **Step 5: Verify the shared read-only contract and focused suite**

Extend `HidOpenContractTests` to retain the exact `ReadOnlySharedOpenParameters` assertions. Add constructor tests that inject the same `IHidInputReportSessionFactory` fake into `DualSenseHidReader` and `ControllerLivenessProbe`, proving neither consumer opens a separate native channel; production `HidInputReportStreamFactory` remains the only type that calls `HidNative.OpenReadOnlyShared`. Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj -c Release --filter "FullyQualifiedName~ControllerLivenessProbeTests|FullyQualifiedName~HidOpenContractTests|FullyQualifiedName~DualSenseHidReaderTests"
```

Expected: all selected tests pass, including open/read failure and cancellation cases.

- [ ] **Step 6: Commit the shared factory and probe**

```powershell
git add src/DualSenseBatteryTray.Hid tests/DualSenseBatteryTray.Hid.Tests
git commit -m "feat: probe physical controller liveness"
```

---

### Task 3: Runtime Stale Detection and App Shutdown Classification

**Files:**
- Modify: `src/DualSenseBatteryTray.Hid/DualSenseHidReader.cs`
- Modify: `tests/DualSenseBatteryTray.Hid.Tests/DualSenseHidReaderTests.cs`
- Modify: `src/DualSenseBatteryTray.App/App.xaml.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/BoundedFileLoggerTests.cs`

**Interfaces:**
- Consumes: `DualSenseReportLivenessObserver` and shared input sessions from Tasks 1–2.
- Produces: `public sealed class ControllerReportsStaleException : IOException`, `DualSenseHidReader.DefaultStaleTimeout == TimeSpan.FromSeconds(2)`, and App event `controller.stale` routed through existing shutdown.

- [ ] **Step 1: Write failing deterministic runtime tests**

Inject `IHidInputReportSessionFactory`, an internal `IAsyncDelay` (`Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)`), and a stale timeout through an internal reader constructor. Tests use a `ControlledAsyncDelay` whose queued request exposes `Complete()` and `Canceled`; production uses `Task.Delay`. Assert:

```csharp
[Fact]
public async Task Frozen_reports_end_with_stale_after_two_seconds()
{
    var frozen = Report(3, 500, 0x29);
    var fixture = ReaderFixture.Repeating(frozen);
    var read = fixture.Reader.ReadStatesAsync(fixture.Cancellation.Token).GetAsyncEnumerator();

    Assert.True(await read.MoveNextAsync());
    fixture.Time.Advance(DualSenseHidReader.DefaultStaleTimeout);

    await Assert.ThrowsAsync<ControllerReportsStaleException>(async () =>
        await read.MoveNextAsync().AsTask());
}
```

Add tests that progressive reports reset the two-second deadline, duplicate battery values are still emitted once while liveness advances, cancellation/disposal do not throw stale, and five consecutive I/O failures retain the existing `IOException` policy.

- [ ] **Step 2: Run runtime tests and verify RED**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj -c Release --filter FullyQualifiedName~DualSenseHidReaderTests
```

Expected: compilation fails on the injectable constructor, stale timeout, and stale exception.

- [ ] **Step 3: Add runtime liveness without a second HID reader**

Inside the existing read loop, race each `ReadAsync` against a stale deadline produced from the injected `TimeProvider`. The initial deadline starts when the stream opens; every `Progressing` observation replaces it with a fresh two-second deadline. If the deadline wins, dispose/cancel the pending read and throw:

```csharp
public sealed class ControllerReportsStaleException()
    : IOException("The physical DualSense input report stopped progressing.");
```

Offer every structurally valid report to the liveness observer before the existing `HidReportProcessor`. Keep the processor's duplicate suppression untouched. Ensure cancellation and disposal branches are checked before classifying stale.

- [ ] **Step 4: Write failing App classification test**

Add the internal pure mapping:

```csharp
[Theory]
[InlineData(typeof(ControllerReportsStaleException), "controller.stale")]
[InlineData(typeof(IOException), "reader.failure")]
public void Reader_end_event_distinguishes_stale_from_failure(Type type, string expected)
{
    var error = (Exception)Activator.CreateInstance(type)!;
    Assert.Equal(expected, App.ReaderEndEventName(error));
}
```

Also retain an assertion that `OnReaderEnded` invokes `ShutdownApplicationAsync`, which disposes tray context, calls `MainWindow.BeginShutdown`, releases the mutex, and calls `Shutdown(0)`.

- [ ] **Step 5: Implement App logging classification and verify privacy**

Implement:

```csharp
internal static string ReaderEndEventName(Exception? error) => error switch
{
    ControllerReportsStaleException => "controller.stale",
    null => "reader.ended",
    _ => "reader.failure",
};
```

Use this name in `OnReaderEnded`. Pass the fixed `ControllerReportsStaleException` message for stale termination; no report-derived data reaches the logger. Add a logger test proving the resulting entry contains `event=controller.stale` and the fixed generic message and contains none of `VID_`, `PID_`, `report=`, `button=`, or `path=`.

- [ ] **Step 6: Run HID and App focused tests**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj -c Release
dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter "FullyQualifiedName~ApplicationShellTests|FullyQualifiedName~BoundedFileLoggerTests"
```

Expected: all selected tests pass; cancellation, disposal, refresh, and stale shutdown remain distinguishable.

- [ ] **Step 7: Commit runtime stale handling**

```powershell
git add src/DualSenseBatteryTray.Hid/DualSenseHidReader.cs src/DualSenseBatteryTray.App/App.xaml.cs tests/DualSenseBatteryTray.Hid.Tests/DualSenseHidReaderTests.cs tests/DualSenseBatteryTray.App.Tests
git commit -m "fix: close app when controller reports freeze"
```

---

### Task 4: Single-Flight Watcher Polling and Live-Only Launch

**Files:**
- Create: `src/DualSenseBatteryTray.Watcher/WatcherLivenessCoordinator.cs`
- Create: `src/DualSenseBatteryTray.Watcher/WatcherEventLogger.cs`
- Modify: `src/DualSenseBatteryTray.Watcher/Program.cs`
- Modify: `src/DualSenseBatteryTray.Watcher/DeviceNotificationWindow.cs`
- Modify: `src/DualSenseBatteryTray.Watcher/WatcherDecision.cs`
- Create: `tests/DualSenseBatteryTray.Watcher.Tests/WatcherLivenessCoordinatorTests.cs`
- Modify: `tests/DualSenseBatteryTray.Watcher.Tests/WatcherDecisionTests.cs`

**Interfaces:**
- Consumes: `IControllerLivenessProbe.ProbeAsync`, App mutex predicate, App launch action, and debounced device-arrival signals.
- Produces: `WatcherLivenessCoordinator.RequestCheck()`, `DefaultPollingInterval == TimeSpan.FromSeconds(2)`, and async disposal that cancels and joins all workers.

- [ ] **Step 1: Replace presence-only decision tests with liveness decisions**

Change the pure decision to:

```csharp
public static bool ShouldStart(
    ControllerLivenessProbeResult liveness,
    bool appAlreadyRunning) =>
    liveness == ControllerLivenessProbeResult.Progressing && !appAlreadyRunning;
```

Test all four probe results with both mutex states. Explicitly assert `Stale`, `Insufficient`, and `AdapterAbsent` never launch.

- [ ] **Step 2: Write failing coordinator tests**

Use a controlled delay and probe fake with `TaskCompletionSource` gates. Cover:

- the constructor schedules an immediate check and then a two-second idle retry;
- a `Progressing` result launches exactly once;
- an App mutex suppresses probe and launch;
- once the mutex disappears, the next tick probes again;
- timer and arrival calls during an active probe coalesce into exactly one subsequent probe;
- `Stale`, `Insufficient`, thrown probe errors, and `AdapterAbsent` keep the worker alive;
- disposal cancels the active probe, prevents later launch, and waits for the worker to stop.

Use this launch assertion:

```csharp
probe.Enqueue(ControllerLivenessProbeResult.Stale);
probe.Enqueue(ControllerLivenessProbeResult.Progressing);
coordinator.RequestCheck();
await probe.WaitForCallsAsync(2);
Assert.Equal(1, launchCount);
Assert.Equal(1, probe.MaximumConcurrency);
```

- [ ] **Step 3: Run Watcher tests and verify RED**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Watcher.Tests/DualSenseBatteryTray.Watcher.Tests.csproj -c Release --filter "FullyQualifiedName~WatcherLivenessCoordinatorTests|FullyQualifiedName~WatcherDecisionTests"
```

Expected: compilation fails because the coordinator and new decision signature do not exist.

- [ ] **Step 4: Implement the coordinator as one serialized worker**

Use a capacity-one `SemaphoreSlim` signal plus a cancellation source. `RequestCheck()` performs a nonthrowing best-effort `Release()` and swallows `SemaphoreFullException`. The worker waits for either a request or the two-second delay, drains the signal, checks the App mutex first, then awaits exactly one probe. After `Progressing`, recheck the mutex immediately before calling `_startApp()`.

The worker must catch probe exceptions other than external shutdown, emit a rate-limited `liveness.failure`, and continue. `DisposeAsync` sets disposed state under a lock, cancels the worker/probe, awaits it, then disposes synchronization resources.

- [ ] **Step 5: Compose Program and device arrivals**

Replace `WatcherLauncher` construction in `Program.Main` with:

```csharp
await using var coordinator = new WatcherLivenessCoordinator(
    new ControllerLivenessProbe(),
    () => WatcherRuntime.IsMutexPresent(appMutexName),
    () => Process.Start(appStartInfo)?.Dispose());
using var notificationWindow = new DeviceNotificationWindow(coordinator.RequestCheck);
coordinator.RequestCheck();
System.Windows.Forms.Application.Run();
```

Because `Main` is synchronous STA, dispose the coordinator with `DisposeAsync().AsTask().GetAwaiter().GetResult()` in `finally` rather than changing the WinForms entry point to `async void`. Keep the existing 500 ms `DeviceArrivalDebouncer`; its callback now only calls `RequestCheck`, so it never blocks the window thread.

- [ ] **Step 6: Add transition logging with privacy bounds**

Create `WatcherEventLogger` with the exact API `void Log(string eventName, Exception? error = null)`. It writes to `%LOCALAPPDATA%\DualSenseBatteryTray\watcher.log`, rotates at 1 MiB using one `.1` backup, and accepts only `adapter.present`, `controller.live`, `controller.stale`, and `liveness.failure`. For failures, write only `error=<ExceptionType>` rather than `Exception.Message`; this prevents native messages from leaking device paths. Add a one-minute per-event failure rate limiter inside the coordinator so the two-second poll cannot emit repeated `liveness.failure` lines. Tests must prove unsupported event names throw, repeated failures inside one minute write once, and the logger API has no parameter capable of accepting a report buffer or device path.

- [ ] **Step 7: Run the complete Watcher suite**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Watcher.Tests/DualSenseBatteryTray.Watcher.Tests.csproj -c Release
```

Expected: all tests pass, `MaximumConcurrency` is one, periodic retry is two seconds, debounce remains 500 ms, and cleanup tests remain green.

- [ ] **Step 8: Commit Watcher live-only launch**

```powershell
git add src/DualSenseBatteryTray.Watcher tests/DualSenseBatteryTray.Watcher.Tests
git commit -m "fix: launch tray only for live controller reports"
```

---

### Task 5: Full Verification, Privacy Audit, Publish, Install, and Hardware Acceptance

**Files:**
- Modify: `README.md`
- Verify: `scripts/install.ps1`
- Create: `docs/superpowers/reports/2026-08-13-controller-liveness-verification.md`

**Interfaces:**
- Consumes: Tasks 1–4 and the existing self-contained win-x64 installer.
- Produces: tested Release binaries, matching installed hashes, and an evidence report for offline/start/reconnect behavior.

- [ ] **Step 1: Run fresh Release tests and build**

Run from a clean shell:

```powershell
dotnet test DualSenseBatteryTray.sln -c Release --no-restore
dotnet build DualSenseBatteryTray.sln -c Release --no-restore
git diff --check
```

Expected: every test passes; build reports zero warnings and zero errors; diff check prints nothing.

- [ ] **Step 2: Run a source privacy and HID-write audit**

Run:

```powershell
rg -n -i "C:\\Users\\|kaaaaai@sina\.cn|device(path)?=|report(_| )?(bytes|hex|fingerprint)|button|stick|trigger" src tests README.md
rg -n "GENERIC_WRITE|HidD_SetFeature|WriteFile|WriteAsync|FileAccess\.Write|Open.*Exclusive" src
```

Expected: no user-specific path/email or production logging of raw reports/controls/device paths; no HID write/exclusive API. Legitimate test names or UI words must be reviewed individually and recorded, not blindly deleted.

- [ ] **Step 3: Publish App and Watcher into a fresh directory**

```powershell
$publishDir = Join-Path $PWD 'artifacts/controller-liveness-publish'
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
dotnet publish src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publishDir
dotnet publish src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publishDir
```

Expected: both executables are present in the fresh publish directory.

- [ ] **Step 4: Install safely and confirm hashes/process ownership**

Stop only installed App/Watcher processes whose resolved executable paths are children of `%LOCALAPPDATA%\Programs\DualSenseBatteryTray`, then run:

```powershell
& .\scripts\install.ps1 -PublishDirectory $publishDir
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\DualSenseBatteryTray'
Get-FileHash (Join-Path $publishDir 'DualSenseBatteryTray.App.exe'), (Join-Path $installDir 'DualSenseBatteryTray.App.exe') -Algorithm SHA256
Get-FileHash (Join-Path $publishDir 'DualSenseBatteryTray.Watcher.exe'), (Join-Path $installDir 'DualSenseBatteryTray.Watcher.exe') -Algorithm SHA256
Get-ScheduledTask -TaskName 'DualSenseBatteryTray-DeviceWatcher' | Select-Object TaskName, State
```

Expected: each published/installed pair has an identical SHA-256; the scheduled task exists and exactly one installed Watcher runs.

- [ ] **Step 5: Verify the physical offline/start/stop/reconnect sequence**

With the Raspberry Pi adapter left connected:

1. Turn the physical controller off and restart the scheduled Watcher task. Wait four seconds. Verify one Watcher, zero App processes, and no tray/window UI.
2. Turn the controller on without reconnecting USB. Verify the App starts within 3.5 seconds (two-second poll plus 1.5-second maximum probe), and the tray/window show the real battery.
3. If the controller reports 100%, verify it remains visible while sequence/timestamp progress.
4. Turn the controller off while leaving the adapter connected. Verify App, tray, and window disappear within two seconds plus scheduling tolerance while Watcher remains.
5. Turn the controller on again. Verify automatic restoration without USB reconnect.
6. Open a game/controller tester and confirm inputs remain available throughout App monitoring.

- [ ] **Step 6: Record verification evidence and update documentation**

Write `docs/superpowers/reports/2026-08-13-controller-liveness-verification.md` with exact test/build totals, publish and installed hashes, scheduled-task/process counts, transition timings, log event names observed, privacy scan disposition, and any hardware item that could not be exercised. Update README to state that VID/PID presence alone is insufficient: the hidden Watcher launches the App only after sequence or sensor timestamp progress, and frozen adapter reports keep all UI hidden.

- [ ] **Step 7: Commit verification documentation**

```powershell
git add README.md docs/superpowers/reports/2026-08-13-controller-liveness-verification.md
git commit -m "docs: verify controller liveness lifecycle"
```

- [ ] **Step 8: Request final code review**

Invoke `superpowers:requesting-code-review` against the complete Task 1–5 diff. Resolve every Critical, High, or Important finding through a new failing test and minimal fix, rerun the affected focused suite plus the fresh Release suite, and amend the verification report with the final evidence.
