# DualSense Battery Tray Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows x64 tray application that starts when the attached USB DualSense `054C:0CE6` appears, displays battery state in the tray and taskbar, sends 20%/10% alerts, never writes to the controller, and exits on removal.

**Architecture:** A WPF tray process owns a narrow Win32 HID reader, battery parser, alert state machine, UI projection, and device-removal shutdown. A separate hidden watcher starts at user sign-in, waits for native device notifications without polling or reading HID data, and launches the tray process only when the target controller appears.

**Tech Stack:** .NET 8, C# 12, WPF, `System.Windows.Forms.NotifyIcon`, Win32 SetupAPI/HID P/Invoke, xUnit, PowerShell setup scripts.

## Global Constraints

- Keep Core and pure logic tests on `net8.0`; use `net8.0-windows` and x64 for all Windows API/UI/HID/watcher projects.
- Support USB DualSense only: vendor ID `0x054C`, product ID `0x0CE6`.
- Open HID with `GENERIC_READ` and `FILE_SHARE_READ | FILE_SHARE_WRITE`; never request write access.
- Never call `WriteFile`, `HidD_SetFeature`, or `HidD_SetOutputReport`.
- Do not install drivers, create virtual controllers, hide devices, or hook controller input.
- Send low-battery notifications once at 20% and once at 10% per discharge cycle.
- Exit the tray process when the active controller is removed.
- Start one hidden, non-polling device watcher at user sign-in; do not start the tray process until the target controller is present.
- Store local logs only, without button/axis values, capped at 1 MiB plus one backup.
- Preserve MIT attribution for battery parsing derived from `dualshock-tools/dualshock-tools.github.io`.
- The machine currently has the .NET 8 runtime but no SDK; Task 1 installs the SDK before scaffolding.

## Planned File Structure

```text
DualSenseBatteryTray.sln
Directory.Build.props
LICENSES/dualshock-tools-MIT.txt
src/DualSenseBatteryTray.Core/
  Battery/BatteryParser.cs
  Battery/BatteryState.cs
  Notifications/LowBatteryAlertTracker.cs
  Devices/ControllerIdentity.cs
  Devices/IControllerPresence.cs
  Devices/IBatteryReportSource.cs
src/DualSenseBatteryTray.Hid/
  Native/HidNative.cs
  Native/SetupApiNative.cs
  HidDeviceEnumerator.cs
  DualSenseHidReader.cs
src/DualSenseBatteryTray.App/
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  SingleInstanceGuard.cs
  Tray/TrayIconRenderer.cs
  Tray/TaskbarIconRenderer.cs
  Tray/TrayApplicationContext.cs
  Notifications/WindowsBatteryNotifier.cs
  Logging/BoundedFileLogger.cs
src/DualSenseBatteryTray.Watcher/
  Program.cs
scripts/install.ps1
scripts/uninstall.ps1
tests/DualSenseBatteryTray.Core.Tests/
  BatteryParserTests.cs
  LowBatteryAlertTrackerTests.cs
tests/DualSenseBatteryTray.Hid.Tests/
  HidOpenContractTests.cs
tests/DualSenseBatteryTray.App.Tests/
  TrayIconRendererTests.cs
  BoundedFileLoggerTests.cs
tests/DualSenseBatteryTray.Watcher.Tests/
  WatcherDecisionTests.cs
```

---

### Task 1: SDK, Solution, and Domain Model

**Files:**
- Create: `DualSenseBatteryTray.sln`
- Create: `Directory.Build.props`
- Create: `src/DualSenseBatteryTray.Core/DualSenseBatteryTray.Core.csproj`
- Create: `src/DualSenseBatteryTray.Core/Battery/BatteryState.cs`
- Create: `src/DualSenseBatteryTray.Core/Devices/ControllerIdentity.cs`
- Create: `tests/DualSenseBatteryTray.Core.Tests/DualSenseBatteryTray.Core.Tests.csproj`

**Interfaces:**
- Produces: `BatteryState`, `ConnectionState`, and `ControllerIdentity.UsbDualSense` used by every later task.

- [ ] **Step 1: Install the missing .NET 8 SDK and verify it**

Run:

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact --accept-package-agreements --accept-source-agreements
dotnet --list-sdks
```

Expected: output contains an `8.0.x` SDK. If `winget` reports the package is already installed, reopen the terminal and rerun `dotnet --list-sdks`.

- [ ] **Step 2: Scaffold the solution and projects**

Run:

```powershell
dotnet new sln -n DualSenseBatteryTray
dotnet new classlib -n DualSenseBatteryTray.Core -o src/DualSenseBatteryTray.Core -f net8.0
dotnet new xunit -n DualSenseBatteryTray.Core.Tests -o tests/DualSenseBatteryTray.Core.Tests -f net8.0
dotnet sln add src/DualSenseBatteryTray.Core/DualSenseBatteryTray.Core.csproj
dotnet sln add tests/DualSenseBatteryTray.Core.Tests/DualSenseBatteryTray.Core.Tests.csproj
dotnet add tests/DualSenseBatteryTray.Core.Tests/DualSenseBatteryTray.Core.Tests.csproj reference src/DualSenseBatteryTray.Core/DualSenseBatteryTray.Core.csproj
```

Expected: all commands succeed and the solution contains two projects.

- [ ] **Step 3: Add common build settings and domain types**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>12</LangVersion>
  </PropertyGroup>
</Project>
```

Create `src/DualSenseBatteryTray.Core/Battery/BatteryState.cs`:

```csharp
namespace DualSenseBatteryTray.Core.Battery;

public enum ConnectionState { Discharging, Charging, Full, Flat, Unknown }

public sealed record BatteryState(int? Percentage, ConnectionState State)
{
    public bool IsCharging => State is ConnectionState.Charging or ConnectionState.Full;
}
```

Create `src/DualSenseBatteryTray.Core/Devices/ControllerIdentity.cs`:

```csharp
namespace DualSenseBatteryTray.Core.Devices;

public sealed record ControllerIdentity(ushort VendorId, ushort ProductId, string DisplayName)
{
    public static ControllerIdentity UsbDualSense { get; } =
        new(0x054C, 0x0CE6, "DualSense Wireless Controller");
}
```

- [ ] **Step 4: Build the empty foundation**

Run: `dotnet build DualSenseBatteryTray.sln -c Debug`

Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add DualSenseBatteryTray.sln Directory.Build.props src tests
git commit -m "build: scaffold DualSense battery tray solution"
```

---

### Task 2: Battery Report Parser

**Files:**
- Create: `src/DualSenseBatteryTray.Core/Battery/BatteryParser.cs`
- Create: `tests/DualSenseBatteryTray.Core.Tests/BatteryParserTests.cs`
- Create: `LICENSES/dualshock-tools-MIT.txt`

**Interfaces:**
- Consumes: `BatteryState` and `ConnectionState` from Task 1.
- Produces: `BatteryParser.Parse(ReadOnlySpan<byte>) -> BatteryState`.

- [ ] **Step 1: Write parser tests**

Create `tests/DualSenseBatteryTray.Core.Tests/BatteryParserTests.cs`:

```csharp
using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Core.Tests;

public sealed class BatteryParserTests
{
    [Theory]
    [InlineData(0x00, 5, ConnectionState.Discharging)]
    [InlineData(0x09, 95, ConnectionState.Discharging)]
    [InlineData(0x0F, 100, ConnectionState.Discharging)]
    [InlineData(0x13, 35, ConnectionState.Charging)]
    [InlineData(0x20, 100, ConnectionState.Full)]
    [InlineData(0xF0, 0, ConnectionState.Flat)]
    public void Parse_maps_known_status(byte batteryByte, int percentage, ConnectionState state)
    {
        var report = new byte[53];
        report[52] = batteryByte;
        Assert.Equal(new BatteryState(percentage, state), BatteryParser.Parse(report));
    }

    [Fact]
    public void Parse_maps_unknown_status_without_percentage()
    {
        var report = new byte[53];
        report[52] = 0xB4;
        Assert.Equal(new BatteryState(null, ConnectionState.Unknown), BatteryParser.Parse(report));
    }

    [Fact]
    public void Parse_rejects_short_report()
    {
        Assert.Throws<ArgumentException>(() => BatteryParser.Parse(new byte[52]));
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DualSenseBatteryTray.Core.Tests -c Debug --filter BatteryParserTests`

Expected: FAIL because `BatteryParser` does not exist.

- [ ] **Step 3: Implement the parser**

Create `src/DualSenseBatteryTray.Core/Battery/BatteryParser.cs`:

```csharp
namespace DualSenseBatteryTray.Core.Battery;

public static class BatteryParser
{
    public const int BatteryOffset = 52;

    public static BatteryState Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length <= BatteryOffset)
            throw new ArgumentException("DualSense report must contain byte 52.", nameof(report));

        var value = report[BatteryOffset];
        var charge = value & 0x0F;
        var status = value >> 4;
        var percentage = Math.Min(charge * 10 + 5, 100);

        return status switch
        {
            0 => new(percentage, ConnectionState.Discharging),
            1 => new(percentage, ConnectionState.Charging),
            2 => new(100, ConnectionState.Full),
            15 => new(0, ConnectionState.Flat),
            _ => new(null, ConnectionState.Unknown)
        };
    }
}
```

Copy the upstream MIT license verbatim from `https://github.com/dualshock-tools/dualshock-tools.github.io/blob/main/LICENSE.txt` to `LICENSES/dualshock-tools-MIT.txt`, then add a one-line note at the end: `Battery status interpretation in BatteryParser.cs is derived from this project.`

- [ ] **Step 4: Run parser tests**

Run: `dotnet test tests/DualSenseBatteryTray.Core.Tests -c Debug --filter BatteryParserTests`

Expected: 8 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/DualSenseBatteryTray.Core/Battery tests/DualSenseBatteryTray.Core.Tests/BatteryParserTests.cs LICENSES
git commit -m "feat: parse DualSense USB battery reports"
```

---

### Task 3: Low-Battery Alert State Machine

**Files:**
- Create: `src/DualSenseBatteryTray.Core/Notifications/LowBatteryAlertTracker.cs`
- Create: `tests/DualSenseBatteryTray.Core.Tests/LowBatteryAlertTrackerTests.cs`

**Interfaces:**
- Consumes: `BatteryState`.
- Produces: `LowBatteryAlertTracker.Observe(BatteryState) -> int?`, returning `20`, `10`, or `null`.

- [ ] **Step 1: Write alert-state tests**

Create `tests/DualSenseBatteryTray.Core.Tests/LowBatteryAlertTrackerTests.cs`:

```csharp
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Notifications;

namespace DualSenseBatteryTray.Core.Tests;

public sealed class LowBatteryAlertTrackerTests
{
    [Fact]
    public void Observe_notifies_once_at_each_threshold()
    {
        var tracker = new LowBatteryAlertTracker();
        Assert.Null(tracker.Observe(new(25, ConnectionState.Discharging)));
        Assert.Equal(20, tracker.Observe(new(20, ConnectionState.Discharging)));
        Assert.Null(tracker.Observe(new(15, ConnectionState.Discharging)));
        Assert.Equal(10, tracker.Observe(new(10, ConnectionState.Discharging)));
        Assert.Null(tracker.Observe(new(5, ConnectionState.Discharging)));
    }

    [Fact]
    public void Observe_resets_after_charging()
    {
        var tracker = new LowBatteryAlertTracker();
        Assert.Equal(20, tracker.Observe(new(20, ConnectionState.Discharging)));
        Assert.Null(tracker.Observe(new(20, ConnectionState.Charging)));
        Assert.Equal(20, tracker.Observe(new(20, ConnectionState.Discharging)));
    }

    [Fact]
    public void Observe_ignores_unknown_state()
    {
        Assert.Null(new LowBatteryAlertTracker().Observe(new(null, ConnectionState.Unknown)));
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DualSenseBatteryTray.Core.Tests -c Debug --filter LowBatteryAlertTrackerTests`

Expected: FAIL because `LowBatteryAlertTracker` does not exist.

- [ ] **Step 3: Implement the state machine**

Create `src/DualSenseBatteryTray.Core/Notifications/LowBatteryAlertTracker.cs`:

```csharp
using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Core.Notifications;

public sealed class LowBatteryAlertTracker
{
    private bool _sent20;
    private bool _sent10;

    public int? Observe(BatteryState state)
    {
        if (state.IsCharging)
        {
            _sent20 = false;
            _sent10 = false;
            return null;
        }

        if (state.State != ConnectionState.Discharging || state.Percentage is not int level)
            return null;

        if (level > 20) _sent20 = false;
        if (level > 10) _sent10 = false;

        if (level <= 10 && !_sent10)
        {
            _sent10 = true;
            _sent20 = true;
            return 10;
        }

        if (level <= 20 && !_sent20)
        {
            _sent20 = true;
            return 20;
        }

        return null;
    }
}
```

- [ ] **Step 4: Run core tests**

Run: `dotnet test tests/DualSenseBatteryTray.Core.Tests -c Debug`

Expected: all parser and alert tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/DualSenseBatteryTray.Core/Notifications tests/DualSenseBatteryTray.Core.Tests/LowBatteryAlertTrackerTests.cs
git commit -m "feat: track low battery notification thresholds"
```

---

### Task 4: Shared Read-Only Win32 HID Layer

**Files:**
- Create: `src/DualSenseBatteryTray.Core/Devices/IControllerPresence.cs`
- Create: `src/DualSenseBatteryTray.Core/Devices/IBatteryReportSource.cs`
- Create: `src/DualSenseBatteryTray.Hid/DualSenseBatteryTray.Hid.csproj`
- Create: `src/DualSenseBatteryTray.Hid/Native/HidNative.cs`
- Create: `src/DualSenseBatteryTray.Hid/Native/SetupApiNative.cs`
- Create: `src/DualSenseBatteryTray.Hid/HidDeviceEnumerator.cs`
- Create: `src/DualSenseBatteryTray.Hid/DualSenseHidReader.cs`
- Create: `tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj`
- Create: `tests/DualSenseBatteryTray.Hid.Tests/HidOpenContractTests.cs`

**Interfaces:**
- Produces: `IControllerPresence.IsPresent(ControllerIdentity)`, `IBatteryReportSource.ReadStatesAsync(CancellationToken)`, and `DualSenseHidReader`.
- Consumes: `BatteryParser.Parse` and `ControllerIdentity.UsbDualSense`.

- [ ] **Step 1: Add projects and abstraction contracts**

Run:

```powershell
dotnet new classlib -n DualSenseBatteryTray.Hid -o src/DualSenseBatteryTray.Hid -f net8.0
dotnet new xunit -n DualSenseBatteryTray.Hid.Tests -o tests/DualSenseBatteryTray.Hid.Tests -f net8.0
dotnet sln add src/DualSenseBatteryTray.Hid/DualSenseBatteryTray.Hid.csproj tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj
dotnet add src/DualSenseBatteryTray.Hid/DualSenseBatteryTray.Hid.csproj reference src/DualSenseBatteryTray.Core/DualSenseBatteryTray.Core.csproj
dotnet add tests/DualSenseBatteryTray.Hid.Tests/DualSenseBatteryTray.Hid.Tests.csproj reference src/DualSenseBatteryTray.Hid/DualSenseBatteryTray.Hid.csproj
```

Create `src/DualSenseBatteryTray.Core/Devices/IControllerPresence.cs`:

```csharp
namespace DualSenseBatteryTray.Core.Devices;
public interface IControllerPresence { bool IsPresent(ControllerIdentity identity); }
```

Create `src/DualSenseBatteryTray.Core/Devices/IBatteryReportSource.cs`:

```csharp
using DualSenseBatteryTray.Core.Battery;
namespace DualSenseBatteryTray.Core.Devices;
public interface IBatteryReportSource
{
    IAsyncEnumerable<BatteryState> ReadStatesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write a source-level safety contract test**

Create `tests/DualSenseBatteryTray.Hid.Tests/HidOpenContractTests.cs`:

```csharp
namespace DualSenseBatteryTray.Hid.Tests;

public sealed class HidOpenContractTests
{
    [Fact]
    public void Native_layer_exposes_read_only_shared_flags()
    {
        Assert.Equal(0x80000000u, Native.HidNative.GenericRead);
        Assert.Equal(0x00000001u, Native.HidNative.FileShareRead);
        Assert.Equal(0x00000002u, Native.HidNative.FileShareWrite);
        Assert.Equal(0u, Native.HidNative.RequestedWriteAccess);
    }
}
```

- [ ] **Step 3: Run the contract test and verify failure**

Run: `dotnet test tests/DualSenseBatteryTray.Hid.Tests -c Debug`

Expected: FAIL because `HidNative` does not exist.

- [ ] **Step 4: Implement enumeration and shared read-only opening**

In `Native/HidNative.cs`, define constants exactly as tested and P/Invoke `HidD_GetHidGuid`, `HidD_GetAttributes`, and `CreateFileW`. `OpenReadOnlyShared(string path)` must call:

```csharp
CreateFileW(
    path,
    GenericRead,
    FileShareRead | FileShareWrite,
    IntPtr.Zero,
    OpenExisting,
    FileFlagOverlapped,
    IntPtr.Zero);
```

Do not declare or wrap `WriteFile`, `HidD_SetFeature`, or `HidD_SetOutputReport` anywhere in the project.

In `Native/SetupApiNative.cs`, wrap `SetupDiGetClassDevsW`, `SetupDiEnumDeviceInterfaces`, `SetupDiGetDeviceInterfaceDetailW`, and `SetupDiDestroyDeviceInfoList`. In `HidDeviceEnumerator.cs`, enumerate HID paths, open each with zero desired access to read `HIDD_ATTRIBUTES`, match `VendorID`/`ProductID`, close the probe handle, and return matching paths. Implement `IControllerPresence.IsPresent` as `FindPaths(identity).Any()`.

In `DualSenseHidReader.cs`, open the first matching path via `OpenReadOnlyShared`, read asynchronously into a buffer sized from `HIDP_CAPS.InputReportByteLength`, parse valid reports with `BatteryParser`, suppress duplicate `BatteryState` values, discard short reports, and throw `IOException` after five consecutive read failures. Ensure cancellation and disposal close the `SafeFileHandle`.

- [ ] **Step 5: Verify tests, build, and forbidden API scan**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Hid.Tests -c Debug
dotnet build DualSenseBatteryTray.sln -c Debug
rg -n "WriteFile|HidD_SetFeature|HidD_SetOutputReport|GENERIC_WRITE" src
```

Expected: tests/build pass; `rg` returns no matches.

- [ ] **Step 6: Hardware smoke test without a game**

Add a temporary test-only console harness that prints normalized `BatteryState` changes, run it for 30 seconds with the attached controller, then delete the harness before committing.

Expected: at least one valid percentage/state is printed and unplugging cancels or fails cleanly.

- [ ] **Step 7: Commit**

```powershell
git add src/DualSenseBatteryTray.Core/Devices src/DualSenseBatteryTray.Hid tests/DualSenseBatteryTray.Hid.Tests DualSenseBatteryTray.sln
git commit -m "feat: read DualSense battery through shared HID"
```

---

### Task 5: Tray UI, Taskbar Overlay, Notifications, and Bounded Logging

**Files:**
- Create: `src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj`
- Create: `src/DualSenseBatteryTray.App/App.xaml`
- Create: `src/DualSenseBatteryTray.App/App.xaml.cs`
- Create: `src/DualSenseBatteryTray.App/MainWindow.xaml`
- Create: `src/DualSenseBatteryTray.App/MainWindow.xaml.cs`
- Create: `src/DualSenseBatteryTray.App/SingleInstanceGuard.cs`
- Create: `src/DualSenseBatteryTray.App/Tray/TrayIconRenderer.cs`
- Create: `src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs`
- Create: `src/DualSenseBatteryTray.App/Tray/TaskbarIconRenderer.cs`
- Create: `src/DualSenseBatteryTray.App/Notifications/WindowsBatteryNotifier.cs`
- Create: `src/DualSenseBatteryTray.App/Logging/BoundedFileLogger.cs`
- Create: `tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj`
- Create: `tests/DualSenseBatteryTray.App.Tests/TrayIconRendererTests.cs`
- Create: `tests/DualSenseBatteryTray.App.Tests/TaskbarIconRendererTests.cs`
- Create: `tests/DualSenseBatteryTray.App.Tests/BoundedFileLoggerTests.cs`

**Interfaces:**
- Consumes: `IBatteryReportSource`, `BatteryState`, and `LowBatteryAlertTracker`.
- Produces: a single-instance WPF tray process, a multi-frame `TrayIconRenderer.Render(BatteryState) -> Icon`, and a static controller `TaskbarIconRenderer.RenderApplicationIcon() -> ImageSource`.

- [ ] **Step 1: Scaffold app and test projects**

Run:

```powershell
dotnet new wpf -n DualSenseBatteryTray.App -o src/DualSenseBatteryTray.App -f net8.0-windows
dotnet new xunit -n DualSenseBatteryTray.App.Tests -o tests/DualSenseBatteryTray.App.Tests -f net8.0-windows
dotnet sln add src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj
dotnet add src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj reference src/DualSenseBatteryTray.Core/DualSenseBatteryTray.Core.csproj src/DualSenseBatteryTray.Hid/DualSenseBatteryTray.Hid.csproj
dotnet add tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj reference src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj
```

Set `<UseWindowsForms>true</UseWindowsForms>` in the app project.

- [ ] **Step 2: Write renderer and logger tests**

`TrayIconRendererTests` must render `5`, `75`, `100`, charging, and unknown states to 32×32 bitmaps and assert non-transparent pixels plus distinct SHA-256 hashes. `TaskbarIconRendererTests` must assert a dark rounded background, a recognizable white controller silhouette in the left 40%, exact right-side strings `10%`, `50%`, and `100%`, green/amber/red threshold colors, a charging lightning symbol, and distinct 16/24/32/48 pixel variants. `BoundedFileLoggerTests` must write more than 1 MiB into a temporary directory and assert that the active log is at most 1 MiB, exactly one `.1` backup exists, and a log call containing an axis-like payload is rejected by the logger API because it accepts only event name and exception message fields.

- [ ] **Step 3: Run tests and verify failure**

Run: `dotnet test tests/DualSenseBatteryTray.App.Tests -c Debug`

Expected: FAIL because renderer and logger classes do not exist.

- [ ] **Step 4: Implement deterministic icon rendering and bounded logging**

Implement `TrayIconRenderer.Render` using `System.Drawing.Bitmap`, a battery outline, centered numeric text (`?`, `5`, `75`, or `100`), green charging accent, amber 20% accent, and red 10% accent. Implement `TaskbarIconRenderer.Render` separately as a square dark rounded icon with a simplified white DualSense silhouette in the left 40% and exact percentage text including `%` in the right 60%. Use bold condensed fitted text, narrower sizing for `100%`, green normal, amber at 20% or below, red at 10% or below, and a small charging lightning symbol below the controller. Provide 16/24/32/48 pixel variants. Dispose every `Graphics`, `Font`, `Brush`, `Pen`, bitmap HICON, and replaced tray icon.

Implement `BoundedFileLogger.Log(string eventName, Exception? error)` using `%LOCALAPPDATA%/DualSenseBatteryTray/app.log`; before append, rotate `app.log` to `app.log.1` when the next UTF-8 entry would exceed 1,048,576 bytes. Never accept report bytes, button values, or axis values in its public API.

- [ ] **Step 5: Implement the application shell**

`SingleInstanceGuard.TryAcquire()` uses a current-user named mutex `Local\\DualSenseBatteryTray-{WindowsIdentity.GetCurrent().User!.Value}`.

`MainWindow.xaml` is a compact non-resizable status window with four bound text values: device name, `USB`, percentage, and charge state. Keep a static controller application icon on the WPF window/taskbar button and leave its overlay empty. Update the multi-frame left-controller/right-percentage ICO on `NotifyIcon` whenever battery state changes. Closing the window calls `Hide()` unless the application is shutting down.

`TrayApplicationContext` creates one `NotifyIcon`, updates its icon and tooltip on distinct states, opens the status window on left click, and exposes Refresh, Listen after Windows sign-in, About, and Exit menu items. Refresh cancels the current reader and creates one fresh shared read-only connection. The listener menu item queries and enables/disables the exact `DualSenseBatteryTray-DeviceWatcher` Scheduled Task; enabling also starts the task immediately, while disabling stops the watcher process whose executable path is inside the application install directory. `WindowsBatteryNotifier` uses `NotifyIcon.ShowBalloonTip` with Chinese text for 20% and 10% alerts.

`App.xaml.cs` acquires the mutex, constructs `DualSenseHidReader`, consumes `ReadStatesAsync`, updates UI via the Dispatcher, calls `LowBatteryAlertTracker.Observe`, and exits on device removal or five consecutive read failures. Explicit user Exit cancels reads before disposing the tray icon and mutex.

- [ ] **Step 6: Run UI tests and build**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.App.Tests -c Debug
dotnet build DualSenseBatteryTray.sln -c Debug
```

Expected: all tests pass and build has 0 warnings/errors.

- [ ] **Step 7: Manually verify tray lifecycle**

Run: `dotnet run --project src/DualSenseBatteryTray.App -c Debug`

Expected: one tray icon and one taskbar button appear; left click opens the status window; a second launch exits; unplugging the controller removes both UI artifacts and exits the process.

- [ ] **Step 8: Commit**

```powershell
git add src/DualSenseBatteryTray.App tests/DualSenseBatteryTray.App.Tests DualSenseBatteryTray.sln
git commit -m "feat: display DualSense battery in Windows shell"
```

---

### Task 6: Hidden Device-Arrival Watcher

**Files:**
- Create: `src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj`
- Create: `src/DualSenseBatteryTray.Watcher/WatcherDecision.cs`
- Create: `src/DualSenseBatteryTray.Watcher/DeviceNotificationWindow.cs`
- Create: `src/DualSenseBatteryTray.Watcher/Program.cs`
- Create: `tests/DualSenseBatteryTray.Watcher.Tests/DualSenseBatteryTray.Watcher.Tests.csproj`
- Create: `tests/DualSenseBatteryTray.Watcher.Tests/WatcherDecisionTests.cs`

**Interfaces:**
- Consumes: `IControllerPresence` and `ControllerIdentity.UsbDualSense`.
- Produces: one hidden message-only window that receives `WM_DEVICECHANGE`, plus a detached launch of `DualSenseBatteryTray.App.exe` only when the controller is present and the tray-app mutex is absent.

- [ ] **Step 1: Scaffold watcher projects**

Run:

```powershell
dotnet new console -n DualSenseBatteryTray.Watcher -o src/DualSenseBatteryTray.Watcher -f net8.0
dotnet new xunit -n DualSenseBatteryTray.Watcher.Tests -o tests/DualSenseBatteryTray.Watcher.Tests -f net8.0
dotnet sln add src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj tests/DualSenseBatteryTray.Watcher.Tests/DualSenseBatteryTray.Watcher.Tests.csproj
dotnet add src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj reference src/DualSenseBatteryTray.Core/DualSenseBatteryTray.Core.csproj src/DualSenseBatteryTray.Hid/DualSenseBatteryTray.Hid.csproj
dotnet add tests/DualSenseBatteryTray.Watcher.Tests/DualSenseBatteryTray.Watcher.Tests.csproj reference src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj
```

- [ ] **Step 2: Write decision tests**

Test these exact cases through a pure `WatcherDecision.ShouldStart(bool controllerPresent, bool appAlreadyRunning)` method: `(false,false) => false`, `(true,true) => false`, `(true,false) => true`, `(false,true) => false`. Also test that arrival notifications schedule one debounced presence check within 500 ms when several notifications arrive together.

- [ ] **Step 3: Run tests and verify failure**

Run: `dotnet test tests/DualSenseBatteryTray.Watcher.Tests -c Debug`

Expected: FAIL because `WatcherDecision` does not exist.

- [ ] **Step 4: Implement the watcher**

Implement:

```csharp
public static class WatcherDecision
{
    public static bool ShouldStart(bool controllerPresent, bool appAlreadyRunning) =>
        controllerPresent && !appAlreadyRunning;
}
```

`DeviceNotificationWindow` creates an HWND message-only window and registers the HID interface class GUID with `RegisterDeviceNotificationW`. On `WM_DEVICECHANGE` with `DBT_DEVICEARRIVAL`, it debounces for 500 ms, then checks presence with `HidDeviceEnumerator`. `Program.cs` owns a separate current-user watcher mutex, probes the tray-app mutex without taking ownership, resolves `DualSenseBatteryTray.App.exe` beside the watcher, and starts it with `UseShellExecute = true`. At startup it performs one presence check so a controller already attached at sign-in launches the tray app. It never opens the controller for report reads and performs no periodic polling.

- [ ] **Step 5: Run watcher tests and build**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.Watcher.Tests -c Debug
dotnet build DualSenseBatteryTray.sln -c Debug
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/DualSenseBatteryTray.Watcher tests/DualSenseBatteryTray.Watcher.Tests DualSenseBatteryTray.sln
git commit -m "feat: watch for target controller arrival"
```

---

### Task 7: Install, Uninstall, and Device-Arrival Scheduled Task

**Files:**
- Create: `scripts/install.ps1`
- Create: `scripts/uninstall.ps1`
- Create: `scripts/device-watcher-task.xml`
- Create: `tests/scripts/install.Tests.ps1`

**Interfaces:**
- Consumes: published watcher and app executables.
- Produces: current-user installation directory `%LOCALAPPDATA%/Programs/DualSenseBatteryTray` and sign-in Scheduled Task `DualSenseBatteryTray-DeviceWatcher`.

- [ ] **Step 1: Add Pester validation tests**

Install Pester for the current user if missing:

```powershell
Install-Module Pester -Scope CurrentUser -Force -SkipPublisherCheck
```

Create tests that parse `device-watcher-task.xml`, assert it contains a `LogonTrigger` for the current user, assert its command ends in `DualSenseBatteryTray.Watcher.exe`, and assert install/uninstall scripts use only the exact task name and `%LOCALAPPDATA%/Programs/DualSenseBatteryTray` target. The tests must also assert neither script contains `Remove-Item -Recurse` against a variable that has not first been resolved and checked as a child of `%LOCALAPPDATA%/Programs`.

- [ ] **Step 2: Run tests and verify failure**

Run: `Invoke-Pester tests/scripts/install.Tests.ps1 -Output Detailed`

Expected: FAIL because scripts and task XML do not exist.

- [ ] **Step 3: Implement current-user setup**

`install.ps1` must:

1. Resolve the publish source passed via mandatory `-PublishDirectory`.
2. Set the fixed destination to `Join-Path $env:LOCALAPPDATA 'Programs\\DualSenseBatteryTray'`.
3. Verify the resolved destination starts with the resolved `%LOCALAPPDATA%/Programs/` prefix.
4. Copy published files.
5. Replace a `${WATCHER_PATH}` token in the XML with the escaped absolute watcher path.
6. Register `DualSenseBatteryTray-DeviceWatcher` with a current-user logon trigger and hidden execution settings.
7. Start the task immediately so connect-to-start works without signing out.
8. If registration fails, preserve copied files and print the exact manual-launch path.

The XML uses a current-user logon trigger. The watcher receives native `WM_DEVICECHANGE` notifications and performs the authoritative `054C:0CE6` filter.

`uninstall.ps1` unregisters only `DualSenseBatteryTray-DeviceWatcher`, stops only processes whose executable path resolves inside the fixed installation directory, verifies the destination boundary, then removes that exact installation directory.

- [ ] **Step 4: Run script tests**

Run: `Invoke-Pester tests/scripts/install.Tests.ps1 -Output Detailed`

Expected: all tests pass.

- [ ] **Step 5: Publish and install locally**

Run:

```powershell
dotnet publish src/DualSenseBatteryTray.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/publish
dotnet publish src/DualSenseBatteryTray.Watcher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/publish
& ./scripts/install.ps1 -PublishDirectory ./artifacts/publish
Get-ScheduledTask -TaskName DualSenseBatteryTray-DeviceWatcher
```

Expected: both executables exist in the install directory; the task is Ready or Running; one hidden watcher process is running.

- [ ] **Step 6: Verify real event behavior**

With the watcher running and tray app closed, insert an unrelated USB device and confirm the tray app does not start. Then connect the target DualSense and confirm exactly one tray instance starts. Disconnect it and confirm the tray app exits while the watcher remains running and idle.

- [ ] **Step 7: Commit**

```powershell
git add scripts tests/scripts
git commit -m "feat: install sign-in device watcher task"
```

---

### Task 8: End-to-End Safety, Game Coexistence, and Release Documentation

**Files:**
- Create: `README.md`
- Create: `NOTICE.md`
- Create: `docs/testing/manual-acceptance.md`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: the complete application and installer.
- Produces: a verified release artifact and reproducible acceptance record.

- [ ] **Step 1: Add repository hygiene and user documentation**

`.gitignore` must exclude `.vs/`, `bin/`, `obj/`, `TestResults/`, `artifacts/`, and `.superpowers/`.

`README.md` must document Windows x64/USB-only scope, install/uninstall commands, tray behavior, 20%/10% notifications, manual launch fallback, local log location, and the DS4Windows/HidHide exclusive-access limitation. State prominently that the app performs HID reads only and does not control vibration, lights, triggers, or input mappings.

`NOTICE.md` must attribute `dualshock-tools/dualshock-tools.github.io` and point to `LICENSES/dualshock-tools-MIT.txt`.

- [ ] **Step 2: Create the manual acceptance checklist**

`docs/testing/manual-acceptance.md` contains dated pass/fail fields for:

- Sign-in starts exactly one hidden watcher and does not start the tray app when the target is absent.
- Target insert starts exactly one tray-app instance.
- Unrelated USB insert does not leave the app running.
- Target removal exits the tray app, removes tray/taskbar UI, and leaves the watcher idle.
- Battery percentage matches the upstream web tool for the same controller session.
- Charging/full/unknown states render correctly.
- 20% and 10% each notify once per discharge cycle.
- Native game input works with the app active.
- Steam Input works with the app active.
- Buttons, axes, vibration, controller audio, and perceived latency remain unchanged.
- Exclusive/hidden HID failure produces one message and exits.
- Uninstall removes the task and application but no controller driver or third-party configuration.

- [ ] **Step 3: Run the complete automated verification**

Run:

```powershell
dotnet test DualSenseBatteryTray.sln -c Release
dotnet build DualSenseBatteryTray.sln -c Release
Invoke-Pester tests/scripts/install.Tests.ps1 -Output Detailed
rg -n "WriteFile|HidD_SetFeature|HidD_SetOutputReport|GENERIC_WRITE" src
git diff --check
```

Expected: all .NET and Pester tests pass, build has 0 warnings/errors, forbidden API scan returns no matches, and `git diff --check` prints nothing.

- [ ] **Step 4: Perform hardware and game acceptance**

Execute every item in `docs/testing/manual-acceptance.md`, record Windows version, controller firmware version, game name, Steam Input state, observed result, and mark each required item PASS. Any controller-input regression blocks release.

- [ ] **Step 5: Produce and hash release artifacts**

Run:

```powershell
dotnet publish src/DualSenseBatteryTray.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/release
dotnet publish src/DualSenseBatteryTray.Watcher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/release
Get-FileHash artifacts/release/*.exe -Algorithm SHA256 | Format-Table Path,Hash
```

Expected: app and watcher executables are produced and each has a SHA-256 hash.

- [ ] **Step 6: Commit**

```powershell
git add .gitignore README.md NOTICE.md docs/testing/manual-acceptance.md
git commit -m "docs: add installation and acceptance guidance"
```

- [ ] **Step 7: Final clean-tree verification**

Run:

```powershell
git status --short
git log --oneline -8
```

Expected: status is clean and the task commits appear in order.
