# Merged Tray Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the paired notification icons with one Fluent controller-outline icon containing a large white battery number.

**Architecture:** `BatteryIconRenderer` adds a dedicated merged renderer that combines the official Fluent `Games 16 Regular` outer shell with fitted Segoe UI text. `TrayApplicationContext` returns to one `NotifyIcon`, retaining the existing single-icon rollback and owned-handle lifecycle; tooltip and status window keep the complete value including `%`.

**Tech Stack:** .NET 8, WPF vector rendering, Windows Forms `NotifyIcon`, xUnit, Microsoft Fluent UI System Icons (MIT).

## Global Constraints

- Continue supporting only USB HID `VID 0x054C / PID 0x0CE6` on Windows x64.
- HID access remains shared and read-only; do not add writes, feature reports, hooks, drivers, or virtual devices.
- Use exactly one notification-area `NotifyIcon`.
- The ICO contains native 16, 24, 32, and 48 pixel frames.
- Use the official Microsoft Fluent UI System Icons `Games 16 Regular` outer controller shell; preserve MIT attribution.
- The icon number is fitted Segoe UI Semibold, white with a dark stroke, and omits `%`: `10`, `75`, `100`, or `?`.
- The tooltip and status window retain the complete value including `%`.
- The tray number remains white at every threshold; low-battery information remains available through notifications, tooltip, and status window.
- Charging adds a yellow lightning mark without replacing the measured number.
- The WPF window/taskbar-button icon remains static and controller-only.

---

### Task 1: Merged Fluent Outline and White Number Renderer

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs`

**Interfaces:**
- Consumes: existing WPF geometry/ICO helpers and `BatteryState`.
- Produces: `RenderMerged(BatteryState state, int size)`, `RenderMergedTrayIcon(BatteryState state)`, and `FormatIconNumber(BatteryState state)`.

- [ ] **Step 1: Write failing merged-renderer tests**

Add these behaviors before production code:

```csharp
[Theory]
[InlineData(10, "10")]
[InlineData(75, "75")]
[InlineData(100, "100")]
[InlineData(null, "?")]
public void FormatIconNumber_omits_the_percent_sign(int? percentage, string expected)
{
    Assert.Equal(expected, BatteryIconRenderer.FormatIconNumber(
        new BatteryState(percentage, ConnectionState.Unknown)));
}

[Theory]
[InlineData(16)]
[InlineData(24)]
[InlineData(32)]
[InlineData(48)]
public void RenderMerged_contains_a_white_controller_and_number(int size)
{
    var pixels = CopyPixels(BatteryIconRenderer.RenderMerged(
        new BatteryState(75, ConnectionState.Discharging), size));
    Assert.Contains(pixels, pixel => pixel == White);
    Assert.DoesNotContain(pixels, pixel => pixel == Green);
    Assert.DoesNotContain(pixels, pixel => pixel == Amber);
    Assert.DoesNotContain(pixels, pixel => pixel == Red);
}

[Theory]
[InlineData(10)]
[InlineData(75)]
[InlineData(100)]
[InlineData(null)]
public void RenderMerged_fits_each_number_inside_the_native_frame(int? percentage)
{
    const int size = 16;
    var pixels = CopyPixels(BatteryIconRenderer.RenderMerged(
        new BatteryState(percentage, ConnectionState.Unknown), size));
    Assert.Contains(pixels, pixel => pixel.Alpha != 0);
    Assert.Equal(0, CountPixels(pixels, size, 0, 1, pixel => pixel.Alpha != 0));
    Assert.Equal(0, CountPixels(pixels, size, size - 1, size, pixel => pixel.Alpha != 0));
}

[Fact]
public void RenderMerged_charging_adds_yellow_without_changing_the_number_to_100()
{
    var charging = CopyPixels(BatteryIconRenderer.RenderMerged(
        new BatteryState(75, ConnectionState.Charging), 48));
    var full = CopyPixels(BatteryIconRenderer.RenderMerged(
        new BatteryState(100, ConnectionState.Full), 48));
    Assert.Contains(charging, pixel => pixel == Lightning);
    Assert.DoesNotContain(full, pixel => pixel == Lightning);
    Assert.NotEqual(ComputeHash(charging), ComputeHash(full));
}

[Fact]
public void RenderMergedTrayIcon_contains_all_native_frames()
{
    using var icon = BatteryIconRenderer.RenderMergedTrayIcon(
        new BatteryState(75, ConnectionState.Discharging));
    Assert.Equal([16, 24, 32, 48], DecodeFrames(icon).Keys.Order().ToArray());
}
```

- [ ] **Step 2: Run tests and verify RED**

Run `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj --filter "FullyQualifiedName~BatteryIconRendererTests" --no-restore`.

Expected: compilation fails only because `FormatIconNumber`, `RenderMerged`, and `RenderMergedTrayIcon` do not exist.

- [ ] **Step 3: Add the official Fluent Regular outer shell**

Use the outer-shell compound path from the official `ic_fluent_games_16_regular.svg`:

```csharp
private const string FluentGames16RegularOuterShellPath =
    "M1.00098 7.5C1.00098 5.01472 3.0157 3 5.50098 3H10.5094C12.9947 3 15.0094 5.01472 15.0094 7.5C15.0094 9.98528 12.9947 12 10.5094 12H5.50098C3.0157 12 1.00098 9.98528 1.00098 7.5Z " +
    "M5.50098 4C3.56798 4 2.00098 5.567 2.00098 7.5C2.00098 9.433 3.56798 11 5.50098 11H10.5094C12.4424 11 14.0094 9.433 14.0094 7.5C14.0094 5.567 12.4424 4 10.5094 4H5.50098Z";

private static readonly Geometry FluentGamesRegularOuterShell =
    CreateFrozenGeometry(FluentGames16RegularOuterShellPath);
```

Render this ring in white at `size / 16d`; do not redraw a custom controller silhouette.

- [ ] **Step 4: Render the centered white number**

Add:

```csharp
internal static string FormatIconNumber(BatteryState state) =>
    state.Percentage is int percentage
        ? percentage.ToString(CultureInfo.InvariantCulture)
        : "?";

public static BitmapSource RenderMerged(BatteryState state, int size)
{
    ValidateStateAndSize(state, size);
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        var scale = size / 16d;
        context.PushTransform(new ScaleTransform(scale, scale));
        context.DrawGeometry(Brushes.White, null, FluentGamesRegularOuterShell);
        DrawFittedIconNumber(context, FormatIconNumber(state));
        if (state.IsCharging)
            context.DrawGeometry(new SolidColorBrush(LightningMedia), null, MergedChargingGeometry);
        context.Pop();
    }
    return RenderVisual(visual, size);
}

public static DrawingIcon RenderMergedTrayIcon(BatteryState state) =>
    RenderTrayIcon(state, RenderMerged);
```

`DrawFittedIconNumber` builds Segoe UI Semibold text geometry, fits it proportionally inside the controller's inner box `X=2`, `Y=4`, `Width=12`, `Height=7`, centers it, and draws a dark `OutlineMedia` stroke behind a white fill. `100` must fit without touching the outer frame. `MergedChargingGeometry` stays at the shell edge and does not cover the centered number.

- [ ] **Step 5: Verify GREEN and commit**

Run the focused renderer tests, then `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj --no-restore`. Both must pass without warnings.

```powershell
git add src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs
git commit -m "feat: merge white battery number into controller icon"
```

---

### Task 2: Restore One Transactional `NotifyIcon`

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `BatteryIconRenderer.RenderMergedTrayIcon(BatteryState)` and `RenderMerged(BatteryState, int)` from Task 1.
- Produces: one `_notifyIcon`, one `_currentIcon`, and single-icon update/rollback/disposal behavior.

- [ ] **Step 1: Write failing single-icon shell tests**

Change reflection and integration expectations first:

```csharp
private static Forms.NotifyIcon GetNotifyIcon(TrayApplicationContext context)
{
    var field = typeof(TrayApplicationContext).GetField(
        "_notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic);
    return Assert.IsType<Forms.NotifyIcon>(field?.GetValue(context));
}

[Fact]
public void TrayApplicationContext_assigns_the_merged_frames_to_one_NotifyIcon()
{
    RunOnStaThread(() =>
    {
        var window = new MainWindow();
        using var context = new TrayApplicationContext(
            window, () => { }, () => { }, new AvailableWatcherTaskService());
        var notifyIcon = GetNotifyIcon(context);
        var state = new BatteryState(75, ConnectionState.Discharging);
        context.UpdateState(state);
        Assert.Equal(
            ComputeHash(BatteryIconRenderer.RenderMerged(state, 32)),
            ComputeHash(DecodeFrames(notifyIcon.Icon)[32]));
        Assert.Contains("75%", notifyIcon.Text, StringComparison.Ordinal);
        window.BeginShutdown();
    });
}
```

Update disposal tests to assert only `_notifyIcon.Visible == false`. Remove tests for paired assignment rollback and replace them with a single assignment-failure rollback test using the existing icon ownership seam.

- [ ] **Step 2: Verify RED**

Run `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj --filter "FullyQualifiedName~ApplicationShellTests" --no-restore`.

Expected: tests fail because `_notifyIcon` is absent and the current context exposes two icons.

- [ ] **Step 3: Implement one icon with robust rollback**

Replace paired fields with:

```csharp
private readonly Forms.NotifyIcon _notifyIcon;
private Icon? _currentIcon;
```

Construct one icon with the existing menu, tooltip, left-click behavior, and `WindowsBatteryNotifier`. In `UpdateState`, render `RenderMergedTrayIcon(state)`, assign it, update tooltip/window, set `_currentState` only after success, and restore the previous assigned handle/text if any later update throws. Reuse `ReconcileIconOwnership` so every unassigned old/new handle is disposed exactly once. `Dispose()` hides and disposes the one `NotifyIcon`, the current owned `Icon`, and the shared menu.

- [ ] **Step 4: Update README to the merged behavior**

Replace paired/overflow wording with:

```markdown
- A supported USB DualSense shows one notification icon: a Fluent controller outline containing a large white battery number.
- The icon omits `%` for readability; hover text and the status window show the complete percentage.
- Clicking the icon opens the status window; right-clicking opens Refresh, listener, About, and Exit commands.
- Controller artwork is adapted from Microsoft Fluent UI System Icons under MIT; see `LICENSES/fluentui-system-icons-MIT.txt`.
```

- [ ] **Step 5: Verify GREEN and commit**

Run application tests and the full solution. Expected: all tests pass without warnings or STA hangs.

```powershell
git add src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs README.md
git commit -m "feat: use one merged battery tray icon"
```

---

### Task 3: Release, Install, and Visual Acceptance

**Files:** No tracked production changes unless verification exposes a defect.

**Interfaces:**
- Consumes: merged renderer/context from Tasks 1-2 and `scripts/install.ps1`.
- Produces: verified Release build installed for the current user.

- [ ] **Step 1: Run clean Release verification**

Stop only the exact installed app/watcher if the named mutex blocks tests. Run:

```powershell
dotnet test DualSenseBatteryTray.sln --configuration Release --no-restore
dotnet build DualSenseBatteryTray.sln --configuration Release --no-restore
```

Expected: all tests pass and build reports zero warnings/errors.

- [ ] **Step 2: Publish and reinstall**

Publish both self-contained win-x64 executables to `artifacts/final-publish`, run `scripts/install.ps1 -PublishDirectory`, and confirm installed/published SHA-256 hashes match.

- [ ] **Step 3: Verify installed state**

Confirm root task `\DualSenseBatteryTray-DeviceWatcher` is Running and exactly one installed App plus one installed Watcher process exist while the connected controller is present.

- [ ] **Step 4: Perform visual acceptance**

Verify one notification icon is visible, the white number is larger than the prior split/paired version, tooltip contains `%`, click/menu behavior works, charging does not fabricate 100%, and disconnect/reconnect removes/restores the icon. Capture a fresh screenshot if the desktop session permits; otherwise request one user screenshot and do not claim visual acceptance.

---

## Final Verification

- [ ] Release tests pass.
- [ ] Release build has zero warnings/errors.
- [ ] Installed/published hashes match.
- [ ] Root watcher task and one App/Watcher process are running.
- [ ] Real notification-area screenshot or explicit user visual confirmation is obtained.
- [ ] Game input acceptance is reported only if physically observed.
