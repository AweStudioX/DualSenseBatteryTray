# Dual Notification Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the cramped hand-drawn composite tray icon with two synchronized notification icons: a Microsoft Fluent controller icon on the left and an exact battery percentage icon on the right.

**Architecture:** `BatteryIconRenderer` exposes independent multi-frame renderers for controller state and percentage state. `TrayApplicationContext` owns two `NotifyIcon` instances that share tooltip, menu, click behavior, and lifecycle while retaining separate owned `System.Drawing.Icon` handles.

**Tech Stack:** .NET 8, WPF vector rendering, Windows Forms `NotifyIcon`, xUnit, Microsoft Fluent UI System Icons `Games 16 Filled` SVG geometry (MIT).

## Global Constraints

- Continue supporting only USB HID `VID 0x054C / PID 0x0CE6` on Windows x64.
- HID access remains shared and read-only; do not add writes, feature reports, hooks, drivers, or virtual devices.
- Each ICO contains native 16, 24, 32, and 48 pixel frames.
- The WPF window/taskbar-button icon remains static and controller-only.
- The left notification icon uses Microsoft Fluent UI System Icons `Games 16 Filled`; preserve its MIT attribution.
- The right notification icon displays the exact value including `%`: `10%`, `75%`, `100%`, or `?%`.
- Normal, warning, and critical values use green, amber, and red; charging uses a yellow lightning overlay on the controller.
- Windows controls final notification-area ordering and overflow placement; adjacency is best effort.

---

### Task 1: Independent Fluent Controller and Percentage Renderers

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs`
- Create: `LICENSES/fluentui-system-icons-MIT.txt`

**Interfaces:**
- Consumes: `BatteryState`, `ConnectionState`, existing ICO encoding and icon ownership conventions.
- Produces: `RenderController(BatteryState, int)`, `RenderPercentage(BatteryState, int)`, `RenderControllerTrayIcon(BatteryState)`, and `RenderPercentageTrayIcon(BatteryState)`.

- [ ] **Step 1: Add failing renderer tests**

Add tests for the desired APIs before production changes:

```csharp
[Theory]
[InlineData(16)]
[InlineData(24)]
[InlineData(32)]
[InlineData(48)]
public void RenderController_uses_the_Fluent_shape_at_every_native_size(int size)
{
    var pixels = CopyPixels(BatteryIconRenderer.RenderController(
        new BatteryState(75, ConnectionState.Discharging), size));
    var bounds = GetBounds(pixels, size, pixel => pixel == White);
    Assert.True(bounds.Width >= (int)Math.Floor(size * 0.75));
    Assert.True(bounds.Height >= (int)Math.Floor(size * 0.45));
    Assert.True(bounds.Width > bounds.Height);
}

[Theory]
[InlineData(10, 231, 76, 60)]
[InlineData(20, 243, 156, 18)]
[InlineData(75, 46, 204, 113)]
public void RenderPercentage_uses_threshold_color(
    int percentage, byte red, byte green, byte blue)
{
    var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
        new BatteryState(percentage, ConnectionState.Discharging), 48));
    Assert.Contains(pixels, pixel => pixel == new Pixel(red, green, blue, 255));
}

[Theory]
[InlineData(10)]
[InlineData(75)]
[InlineData(100)]
[InlineData(null)]
public void RenderPercentage_fits_supported_values(int? percentage)
{
    const int size = 16;
    var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
        new BatteryState(percentage, ConnectionState.Unknown), size));
    Assert.Contains(pixels, pixel => pixel.Alpha != 0);
    Assert.Equal(0, CountPixels(pixels, size, 0, 1, pixel => pixel.Alpha != 0));
    Assert.Equal(0, CountPixels(pixels, size, size - 1, size, pixel => pixel.Alpha != 0));
}

[Fact]
public void Independent_tray_icons_contain_all_native_frames()
{
    var state = new BatteryState(75, ConnectionState.Charging);
    using var controller = BatteryIconRenderer.RenderControllerTrayIcon(state);
    using var percentage = BatteryIconRenderer.RenderPercentageTrayIcon(state);
    Assert.Equal([16, 24, 32, 48], DecodeFrames(controller).Keys.Order().ToArray());
    Assert.Equal([16, 24, 32, 48], DecodeFrames(percentage).Keys.Order().ToArray());
}
```

- [ ] **Step 2: Verify RED**

Run `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj --filter "FullyQualifiedName~BatteryIconRendererTests" --no-restore`.

Expected: compilation fails only because the four new renderer methods are absent.

- [ ] **Step 3: Implement the Fluent controller renderer**

Store the exact official `Games 16 Filled` SVG path in `FluentGames16FilledPath`, parse it once into a frozen `Geometry`, and render it with WPF:

```csharp
private static readonly Geometry FluentGamesGeometry =
    CreateFrozenGeometry(FluentGames16FilledPath);

public static BitmapSource RenderController(BatteryState state, int size)
{
    ValidateStateAndSize(state, size);
    var visual = new DrawingVisual();
    using var context = visual.RenderOpen();
    var scale = size / 16d;
    context.PushTransform(new ScaleTransform(scale, scale));
    context.DrawGeometry(
        Brushes.White,
        new Pen(new SolidColorBrush(OutlineMedia), 0.55),
        FluentGamesGeometry);
    if (state.IsCharging)
        context.DrawGeometry(new SolidColorBrush(LightningMedia), null, ChargingGeometry);
    context.Pop();
    return RenderVisual(visual, size);
}
```

`FluentGames16FilledPath` must be copied byte-for-byte from `assets/Games/SVG/ic_fluent_games_16_filled.svg` except for XML wrapping. `ChargingGeometry` is a frozen path contained within the 16-unit controller viewbox.

- [ ] **Step 4: Implement fitted Segoe percentage rendering**

Use `FormattedText.BuildGeometry` and scale the geometry into a one-pixel inset:

```csharp
public static BitmapSource RenderPercentage(BatteryState state, int size)
{
    ValidateStateAndSize(state, size);
    var formatted = new FormattedText(
        FormatPercentage(state),
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface("Segoe UI Semibold"),
        size,
        SelectPercentageBrush(state.Percentage),
        1d);
    var geometry = formatted.BuildGeometry(new Point(0, 0));
    return FitAndRenderGeometry(
        geometry,
        SelectPercentageBrush(state.Percentage),
        new Pen(new SolidColorBrush(OutlineMedia), 0.5),
        size,
        inset: 1d);
}
```

`FitAndRenderGeometry` centers the transformed geometry, preserves aspect ratio, and returns a frozen BGRA32 `BitmapSource`. Refactor the existing ICO encoder to accept `Func<BatteryState, int, BitmapSource>` and expose:

```csharp
public static DrawingIcon RenderControllerTrayIcon(BatteryState state) =>
    RenderTrayIcon(state, RenderController);

public static DrawingIcon RenderPercentageTrayIcon(BatteryState state) =>
    RenderTrayIcon(state, RenderPercentage);
```

Retain `RenderApplicationIcon()` as controller-only.

- [ ] **Step 5: Add exact Fluent MIT attribution**

Download `https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/LICENSE` to `LICENSES/fluentui-system-icons-MIT.txt` without modifying the body.

- [ ] **Step 6: Verify GREEN and commit**

Run `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj --no-restore` and expect all application tests to pass without warnings.

Then run:

```powershell
git add src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs LICENSES/fluentui-system-icons-MIT.txt
git commit -m "feat: render fluent controller and battery icons"
```

---

### Task 2: Synchronized Dual `NotifyIcon` Lifecycle

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs`

**Interfaces:**
- Consumes: the four renderer APIs from Task 1.
- Produces: `_controllerNotifyIcon` and `_percentageNotifyIcon`, synchronized by `UpdateState(BatteryState)` and disposed by `Dispose()`.

- [ ] **Step 1: Add failing dual-icon shell tests**

Replace the reflection helper and add a synchronized-state assertion:

```csharp
private static Forms.NotifyIcon GetNotifyIcon(
    TrayApplicationContext context,
    string fieldName)
{
    var field = typeof(TrayApplicationContext).GetField(
        fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    return Assert.IsType<Forms.NotifyIcon>(field?.GetValue(context));
}

[Fact]
public void TrayApplicationContext_updates_two_synchronized_notification_icons()
{
    RunOnStaThread(() =>
    {
        var window = new MainWindow();
        using var context = new TrayApplicationContext(
            window, () => { }, () => { }, new AvailableWatcherTaskService());
        var controller = GetNotifyIcon(context, "_controllerNotifyIcon");
        var percentage = GetNotifyIcon(context, "_percentageNotifyIcon");
        var state = new BatteryState(75, ConnectionState.Charging);
        context.UpdateState(state);
        Assert.NotSame(controller, percentage);
        Assert.Equal(controller.Text, percentage.Text);
        Assert.Same(controller.ContextMenuStrip, percentage.ContextMenuStrip);
        Assert.Equal(
            ComputeHash(BatteryIconRenderer.RenderController(state, 32)),
            ComputeHash(DecodeFrames(controller.Icon)[32]));
        Assert.Equal(
            ComputeHash(BatteryIconRenderer.RenderPercentage(state, 32)),
            ComputeHash(DecodeFrames(percentage.Icon)[32]));
        window.BeginShutdown();
    });
}
```

Add a disposal test that calls `context.Dispose()` and asserts both captured `NotifyIcon.Visible` properties are false. Update existing single-icon tests to compare each notification frame with its matching renderer while keeping the static WPF icon and null taskbar-overlay assertions.

- [ ] **Step 2: Verify RED**

Run `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj --filter "FullyQualifiedName~ApplicationShellTests" --no-restore`.

Expected: tests fail because `_controllerNotifyIcon` and `_percentageNotifyIcon` do not exist.

- [ ] **Step 3: Implement paired notification ownership**

Replace the single fields with:

```csharp
private readonly Forms.NotifyIcon _controllerNotifyIcon;
private readonly Forms.NotifyIcon _percentageNotifyIcon;
private Icon? _currentControllerIcon;
private Icon? _currentPercentageIcon;
```

Create the controller first and percentage second, and connect both to the same UI behavior:

```csharp
_controllerNotifyIcon = CreateNotifyIcon();
_percentageNotifyIcon = CreateNotifyIcon();
_controllerNotifyIcon.MouseClick += ShowWindowOnLeftClick;
_percentageNotifyIcon.MouseClick += ShowWindowOnLeftClick;
_notifier = new WindowsBatteryNotifier(_percentageNotifyIcon);

Forms.NotifyIcon CreateNotifyIcon() => new()
{
    ContextMenuStrip = _menu,
    Text = "DualSense USB · ? · Unknown",
    Visible = true,
};
```

`ShowWindowOnLeftClick` calls `_mainWindow.ShowFromTray()` only for the left mouse button.

- [ ] **Step 4: Update both icons from one state**

Render before assignment, then replace both handles and shared text:

```csharp
var nextController = BatteryIconRenderer.RenderControllerTrayIcon(state);
var nextPercentage = BatteryIconRenderer.RenderPercentageTrayIcon(state);
var tooltip = FormatToolTip(state);
ReplaceAssignedIcon(_controllerNotifyIcon, nextController, ref _currentControllerIcon);
ReplaceAssignedIcon(_percentageNotifyIcon, nextPercentage, ref _currentPercentageIcon);
_controllerNotifyIcon.Text = tooltip;
_percentageNotifyIcon.Text = tooltip;
_mainWindow.Update(state);
_currentState = state;
```

`ReplaceAssignedIcon` assigns the new handle and calls the existing `ReconcileIconOwnership` logic so every unassigned old/new handle is disposed. `Dispose()` sets both icons invisible, disposes both `NotifyIcon` instances and both current `Icon` handles, nulls the handle fields, then disposes the shared menu.

- [ ] **Step 5: Verify GREEN and commit**

Run both commands and expect all tests to pass without warnings or STA hangs:

```powershell
dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj --no-restore
dotnet test DualSenseBatteryTray.sln --no-restore
```

Then run:

```powershell
git add src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs
git commit -m "feat: show controller and battery as paired tray icons"
```

---

### Task 3: Publish and Real Notification-Area Acceptance

**Files:**
- Create or modify: `README.md`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: paired icons from Task 2 and existing `scripts/install.ps1`.
- Produces: a verified installed build and a real notification-area screenshot.

- [ ] **Step 1: Document behavior and attribution**

Add these exact facts to the README:

```markdown
- A supported USB DualSense shows two notification icons: a Fluent controller and an exact battery percentage.
- Both icons open the same status window and share the same context menu and tooltip.
- Windows controls notification-area ordering and may put either icon in overflow; pin both icons in taskbar settings to keep them visible together.
- Controller artwork is adapted from Microsoft Fluent UI System Icons under MIT; see `LICENSES/fluentui-system-icons-MIT.txt`.
```

Ensure `.gitignore` contains `/artifacts/`, `/diagnostics/`, and `/.superpowers/brainstorm/`.

- [ ] **Step 2: Run clean Release verification**

Run:

```powershell
dotnet test DualSenseBatteryTray.sln --configuration Release --no-restore
dotnet build DualSenseBatteryTray.sln --configuration Release --no-restore
```

Expected: all tests pass; build reports zero warnings and zero errors.

- [ ] **Step 3: Publish and reinstall the tested revision**

Run:

```powershell
$publish = Join-Path $PWD 'artifacts/final-publish'
dotnet publish src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publish
dotnet publish src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publish
& ./scripts/install.ps1 -PublishDirectory $publish
```

Expected: install succeeds, the root scheduled task is running, and installed executable SHA-256 hashes match the publish directory.

- [ ] **Step 4: Verify live behavior**

With the controller connected, verify exactly one application and watcher process; matching percentage between tray and window; shared left-click/right-click behavior; charging lightning without fabricated 100%; removal of both icons on disconnect; restoration on reconnect. Capture a fresh lower-right notification-area screenshot. If Windows hides either icon, pin both under **Settings → Personalization → Taskbar → Other system tray icons**.

- [ ] **Step 5: Commit documentation**

```powershell
git add README.md .gitignore
git commit -m "docs: explain paired battery tray icons"
```

---

## Final Verification

- [ ] `dotnet test DualSenseBatteryTray.sln --configuration Release --no-restore` passes.
- [ ] `dotnet build DualSenseBatteryTray.sln --configuration Release --no-restore` reports zero warnings and errors.
- [ ] Installed/published SHA-256 hashes match for both executables.
- [ ] Scheduled task remains at root path `\` and is running.
- [ ] A game still receives native controller input while both icons are visible.
- [ ] `git diff` contains no unrelated changes; generated screenshots and builds remain untracked.
