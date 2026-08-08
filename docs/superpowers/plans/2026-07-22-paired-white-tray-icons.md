# Paired White Tray Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore paired controller and percentage notification icons while making the percentage white and materially larger than the prior colored layout.

**Architecture:** The existing independent controller renderer remains the left icon. The right renderer separates numeric glyphs from a smaller percent glyph so the numeric portion fills most of its native frame. `TrayApplicationContext` returns to two transactionally synchronized `NotifyIcon` instances with shared behavior and independent owned handles.

**Tech Stack:** .NET 8, WPF vector text/geometry, Windows Forms `NotifyIcon`, xUnit, Microsoft Fluent UI System Icons (MIT).

## Global Constraints

- Support only USB HID `VID 0x054C / PID 0x0CE6` on Windows x64.
- HID access remains shared and read-only; no writes, feature reports, hooks, drivers, or virtual devices.
- Use two notification icons: complete Fluent `Games 16 Filled` controller first, percentage second.
- Both ICOs contain native 16, 24, 32, and 48 pixel frames.
- The percentage numeric glyphs and `%` are white with a dark contrast stroke at every battery threshold.
- The `%` glyph is smaller and positioned at the upper-right; numeric glyphs occupy the remaining frame as largely as possible.
- Render `10%`, `55%`, `100%`, and `?%` without clipping.
- Charging adds a yellow lightning mark to the controller without replacing the measured percentage.
- Both icons share tooltip, click, menu, visibility, state, rollback, and disposal behavior.
- The WPF window/taskbar-button icon remains static and controller-only.

---

### Task 1: White Large-Number Percentage Renderer

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs`

**Interfaces:**
- Consumes: `BatteryState`, existing `RenderController`, WPF geometry helpers, and multi-frame ICO encoder.
- Produces: updated `RenderPercentage(BatteryState, int)` and unchanged `RenderPercentageTrayIcon(BatteryState)` API with a new white large-number layout.

- [ ] **Step 1: Replace colored-percentage expectations with failing white-layout tests**

```csharp
[Theory]
[InlineData(10)]
[InlineData(55)]
[InlineData(100)]
[InlineData(null)]
public void RenderPercentage_uses_only_white_text_at_every_value(int? percentage)
{
    var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
        new BatteryState(percentage, ConnectionState.Discharging), 48));
    Assert.Contains(pixels, pixel => pixel == White);
    Assert.DoesNotContain(pixels, pixel => pixel == Green);
    Assert.DoesNotContain(pixels, pixel => pixel == Amber);
    Assert.DoesNotContain(pixels, pixel => pixel == Red);
}

[Theory]
[InlineData(16)]
[InlineData(24)]
[InlineData(32)]
[InlineData(48)]
public void RenderPercentage_places_a_small_percent_mark_at_upper_right(int size)
{
    var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
        new BatteryState(55, ConnectionState.Discharging), size));
    var numberRegion = CountPixels(
        pixels, size, 0, (int)Math.Ceiling(size * 0.78), pixel => pixel == White);
    var percentRegion = CountPixels(
        pixels, size, (int)Math.Floor(size * 0.78), size, pixel => pixel == White);
    Assert.True(numberRegion > percentRegion * 2);
    Assert.True(percentRegion > 0);
}

[Theory]
[InlineData(10)]
[InlineData(55)]
[InlineData(100)]
[InlineData(null)]
public void RenderPercentage_keeps_number_and_percent_inside_16px(int? percentage)
{
    const int size = 16;
    var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
        new BatteryState(percentage, ConnectionState.Unknown), size));
    Assert.Contains(pixels, pixel => pixel == White);
    Assert.Equal(0, CountPixels(pixels, size, 0, 1, pixel => pixel.Alpha != 0));
    Assert.Equal(0, CountPixels(pixels, size, size - 1, size, pixel => pixel.Alpha != 0));
}

[Fact]
public void RenderPercentage_numeric_glyphs_are_taller_than_the_percent_glyph()
{
    var layers = BatteryIconRenderer.RenderPercentageLayers(
        new BatteryState(55, ConnectionState.Discharging), 48);
    Assert.True(GetOpaqueBounds(layers.Number).Height > GetOpaqueBounds(layers.Percent).Height * 1.7);
}
```

`RenderPercentageLayers` is an internal testable renderer result containing frozen `Number` and `Percent` bitmap layers; `RenderPercentage` composites those layers. This avoids inferring glyph identity from a single white bitmap.

- [ ] **Step 2: Verify RED**

Run `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj --filter "FullyQualifiedName~BatteryIconRendererTests" --no-restore`.

Expected: old threshold-color tests fail and compilation fails because `RenderPercentageLayers` is absent.

- [ ] **Step 3: Split numeric value from percent sign**

Add:

```csharp
internal readonly record struct PercentageLayers(BitmapSource Number, BitmapSource Percent);

internal static PercentageLayers RenderPercentageLayers(BatteryState state, int size)
{
    ValidateStateAndSize(state, size);
    var number = state.Percentage is int value
        ? value.ToString(CultureInfo.InvariantCulture)
        : "?";
    var numberLayer = RenderFittedTextLayer(
        number,
        new Rect(1, 1, Math.Floor(size * 0.78) - 1, size - 2),
        size,
        Brushes.White);
    var percentLayer = RenderFittedTextLayer(
        "%",
        new Rect(Math.Floor(size * 0.78), 1, size - Math.Floor(size * 0.78) - 1, Math.Ceiling(size * 0.42)),
        size,
        Brushes.White);
    return new PercentageLayers(numberLayer, percentLayer);
}
```

`RenderFittedTextLayer` uses Segoe UI Semibold geometry, fits proportionally inside the supplied rectangle, centers it, and draws a dark `OutlineMedia` stroke plus white fill on a transparent frozen BGRA32 bitmap.

- [ ] **Step 4: Composite white layers**

```csharp
public static BitmapSource RenderPercentage(BatteryState state, int size)
{
    var layers = RenderPercentageLayers(state, size);
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        context.DrawImage(layers.Number, new Rect(0, 0, size, size));
        context.DrawImage(layers.Percent, new Rect(0, 0, size, size));
    }
    return RenderVisual(visual, size);
}
```

Remove threshold-color selection from percentage rendering only; retain those colors if legacy composite rendering still requires them. Keep `FormatPercentage` returning the complete string with `%` for tooltip/tests.

- [ ] **Step 5: Verify GREEN and commit**

Run focused renderer tests and full App tests. Expect all pass with no warnings.

```powershell
git add src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs
git commit -m "feat: enlarge white tray percentage"
```

---

### Task 2: Restore Transactional Paired `NotifyIcon` Lifecycle

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `RenderControllerTrayIcon`, `RenderPercentageTrayIcon`, `RenderController`, and updated `RenderPercentage`.
- Produces: `_controllerNotifyIcon`, `_percentageNotifyIcon`, `_currentControllerIcon`, and `_currentPercentageIcon`.

- [ ] **Step 1: Write failing paired-icon shell tests**

```csharp
[Fact]
public void TrayApplicationContext_assigns_controller_and_white_percentage_icons()
{
    RunOnStaThread(() =>
    {
        var window = new MainWindow();
        using var context = new TrayApplicationContext(
            window, () => { }, () => { }, new AvailableWatcherTaskService());
        var controller = GetNotifyIcon(context, "_controllerNotifyIcon");
        var percentage = GetNotifyIcon(context, "_percentageNotifyIcon");
        var state = new BatteryState(55, ConnectionState.Discharging);
        context.UpdateState(state);
        Assert.NotSame(controller, percentage);
        Assert.Same(controller.ContextMenuStrip, percentage.ContextMenuStrip);
        Assert.Equal(controller.Text, percentage.Text);
        Assert.Equal(
            ComputeHash(BatteryIconRenderer.RenderController(state, 32)),
            ComputeHash(DecodeFrames(controller.Icon)[32]));
        Assert.Equal(
            ComputeHash(BatteryIconRenderer.RenderPercentage(state, 32)),
            ComputeHash(DecodeFrames(percentage.Icon)[32]));
        Assert.Contains("55%", percentage.Text, StringComparison.Ordinal);
        window.BeginShutdown();
    });
}
```

Restore the paired rollback test that forces the second assignment to throw after mutation and asserts the first slot rolls back, both assigned old handles remain valid, and both unassigned new handles are disposed. Update disposal tests to require both icons invisible.

- [ ] **Step 2: Verify RED**

Run ApplicationShellTests. Expected: reflection cannot find the two paired fields and rollback helper.

- [ ] **Step 3: Restore the paired context**

Create controller icon first, percentage icon second, sharing the same menu, tooltip, and left-click handler. Use `WindowsBatteryNotifier` with the percentage icon. Render both next handles before assignment. Assign them transactionally: if either assignment or dependent update throws, best-effort restore both previous handles/text, reconcile actual assigned handles, and do not commit `_currentState`. On success dispose both unassigned previous handles and commit state.

Dispose by hiding both `NotifyIcon` objects, disposing both, disposing both owned current handles, nulling both fields, then disposing the shared menu.

- [ ] **Step 4: Update README**

```markdown
- A supported USB DualSense shows two notification icons: a complete Fluent controller followed by a large white battery percentage.
- The percentage uses large numeric glyphs and a smaller `%` mark for readability.
- Both icons share the same tooltip, status-window click, and context menu.
- Windows controls notification-area ordering and may place either icon in overflow.
- Controller artwork is adapted from Microsoft Fluent UI System Icons under MIT.
```

- [ ] **Step 5: Verify GREEN and commit**

Run App tests and the full solution; all must pass without warnings or hangs.

```powershell
git add src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs README.md
git commit -m "feat: restore paired white battery tray icons"
```

---

### Task 3: Release, Install, and Visual Acceptance

- [ ] Run clean Release tests and build after stopping only the exact installed app/task if the mutex requires it.
- [ ] Publish App and Watcher self-contained win-x64 to `artifacts/final-publish` and reinstall with `scripts/install.ps1`.
- [ ] Confirm publish/install SHA-256 hashes match, root watcher task is Running, and exactly one installed App and Watcher process exist.
- [ ] Verify the controller and white percentage icons are visible; numeric glyphs are larger than the previous green version; `%` is smaller; tooltip contains the exact percentage; both icons share click/menu behavior.
- [ ] Capture a fresh screenshot or request one from the user if the desktop session cannot capture the notification area. Do not claim charging, disconnect/reconnect, or game-input acceptance unless physically observed.

---

## Final Verification

- [ ] Release tests pass and build has zero warnings/errors.
- [ ] Installed hashes match the tested publish.
- [ ] Root watcher and one App/Watcher process are running.
- [ ] Real notification-area screenshot or explicit user visual confirmation is obtained.
- [ ] Whole-branch code review has no open Critical or Important findings.
