# Adaptive Single Tray Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make one theme-aware DualSense tray icon the default, place the live battery number inside its blue touchpad, and preserve a runtime-selectable two-icon fallback.

**Architecture:** Keep device detection and HID reading unchanged. Add small theme and layout services, load two processed transparent controller assets as WPF resources, and let `TrayApplicationContext` reconcile either one compact `NotifyIcon` or a synchronized controller-plus-percentage pair. All HICON creation remains multi-frame and all transitions retain the previous usable icon set on failure.

**Tech Stack:** .NET 8, WPF drawing/bitmap APIs, Windows Forms `NotifyIcon`, `Microsoft.Win32.SystemEvents`, xUnit, user-provided raster references.

## Global Constraints

- USB DualSense `054C:0CE6` only; no Bluetooth or controller-output support.
- HID access remains shared and read-only and must not interfere with games.
- Controller removal hides every tray icon and exits the main application.
- Default layout is Compact; Two icons remains an immediate persisted fallback.
- The Windows taskbar theme is detected automatically and refreshed at runtime.
- Compact mode displays `0` through `100` or `?` without `%`; tooltip/window retain the full value and charging state.
- Runtime code must not reference the original external `D:` image paths.

---

### Task 1: Preserve and finish the expanded percentage fallback

**Files:**
- Create: `src/DualSenseBatteryTray.App/Tray/TrayTheme.cs`
- Modify: `src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs`

**Interfaces:**
- Consumes: `BatteryState`, native icon sizes `16`, `24`, `32`, `48`.
- Produces: `RenderPercentage(BatteryState state, TrayTheme theme, int size)` and `RenderPercentageTrayIcon(BatteryState state, TrayTheme theme)`; numeric glyphs dominate a smaller `%` glyph.

- [ ] **Step 1: Convert the interrupted white-only tests into theme-aware failing tests**

```csharp
[Theory]
[InlineData(TrayTheme.DarkTaskbar, 255)]
[InlineData(TrayTheme.LightTaskbar, 20)]
public void RenderPercentage_uses_theme_foreground(TrayTheme theme, byte expected)
{
    var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
        new BatteryState(55, ConnectionState.Discharging), theme, 48));
    Assert.Contains(pixels, p => p.Alpha > 0 && Math.Abs(p.Red - expected) < 12);
}
```

- [ ] **Step 2: Run the focused renderer tests and verify RED**

Run: `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~BatteryIconRendererTests`

Expected: compilation failure because `TrayTheme` and the theme overloads do not exist.

- [ ] **Step 3: Add `TrayTheme` and complete the existing layered renderer**

Create `src/DualSenseBatteryTray.App/Tray/TrayTheme.cs`:

```csharp
namespace DualSenseBatteryTray.App.Tray;

internal enum TrayTheme
{
    LightTaskbar,
    DarkTaskbar,
}
```

Update percentage rendering so `DarkTaskbar` uses white fill with dark outline and `LightTaskbar` uses near-black fill with a light outline. Preserve `RenderPercentageLayers`, keep the number region at 78%, and keep `%` in the upper-right region.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 1 command. Expected: all `BatteryIconRendererTests` pass.

- [ ] **Step 5: Commit the isolated fallback renderer**

```powershell
git add src/DualSenseBatteryTray.App/Tray/TrayTheme.cs src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs
git commit -m "feat: preserve theme-aware expanded battery icon"
```

### Task 2: Detect and watch the Windows taskbar theme

**Files:**
- Create: `src/DualSenseBatteryTray.App/Tray/WindowsTrayThemeProvider.cs`
- Create: `tests/DualSenseBatteryTray.App.Tests/WindowsTrayThemeProviderTests.cs`

**Interfaces:**
- Produces: `ITrayThemeProvider.Current`, `ITrayThemeProvider.ThemeChanged`, `WindowsTrayThemeProvider.Refresh()`, and deterministic `ParseSystemUsesLightTheme(object? value)`.
- Consumes later: `TrayApplicationContext` subscribes to `ThemeChanged` and disposes the provider.

- [ ] **Step 1: Write parsing, notification, deduplication, and disposal tests**

```csharp
[Theory]
[InlineData(1, TrayTheme.LightTaskbar)]
[InlineData(0, TrayTheme.DarkTaskbar)]
[InlineData(null, TrayTheme.DarkTaskbar)]
public void ParseSystemUsesLightTheme_maps_registry_value(object? value, TrayTheme expected) =>
    Assert.Equal(expected, WindowsTrayThemeProvider.ParseSystemUsesLightTheme(value));
```

Use injected `Func<object?>` and an injected `IUserPreferenceChangeSource` fake to assert one event for an actual theme transition, no event for the same value, and no callback after `Dispose()`.

- [ ] **Step 2: Run the new test class and verify RED**

Run: `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~WindowsTrayThemeProviderTests`

Expected: compilation failure because the provider types do not exist.

- [ ] **Step 3: Implement the provider and system-event adapter**

```csharp
internal interface ITrayThemeProvider : IDisposable
{
    TrayTheme Current { get; }
    event Action<TrayTheme>? ThemeChanged;
    void Refresh();
}

internal interface IUserPreferenceChangeSource : IDisposable
{
    event Action? Changed;
}
```

Read `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme`. The adapter forwards `SystemEvents.UserPreferenceChanged`; the provider re-reads on every forwarded event, defaults invalid/missing values to `DarkTaskbar`, and catches registry access failures without losing the last valid theme.

- [ ] **Step 4: Run the new tests and the full app test project**

Run: `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release`

Expected: all tests pass.

- [ ] **Step 5: Commit theme detection**

```powershell
git add src/DualSenseBatteryTray.App/Tray/WindowsTrayThemeProvider.cs tests/DualSenseBatteryTray.App.Tests/WindowsTrayThemeProviderTests.cs
git commit -m "feat: follow Windows taskbar theme"
```

### Task 3: Add a durable Compact/Two-icons preference

**Files:**
- Create: `src/DualSenseBatteryTray.App/Tray/TrayLayoutPreferenceStore.cs`
- Create: `tests/DualSenseBatteryTray.App.Tests/TrayLayoutPreferenceStoreTests.cs`

**Interfaces:**
- Produces: `TrayLayoutMode.Compact`, `TrayLayoutMode.TwoIcons`, `ITrayLayoutPreferenceStore.Load()`, and `Save(TrayLayoutMode mode)`.
- Consumes later: `TrayApplicationContext` loads once, saves only after a successful live layout transition.

- [ ] **Step 1: Write round-trip and recovery tests**

```csharp
[Fact]
public void Missing_or_invalid_preference_defaults_to_compact()
{
    var store = new TrayLayoutPreferenceStore(_temporaryDirectory);
    Assert.Equal(TrayLayoutMode.Compact, store.Load());
    File.WriteAllText(store.PreferencePath, "broken");
    Assert.Equal(TrayLayoutMode.Compact, store.Load());
}
```

Also save `TwoIcons`, construct a new store, and assert it loads `TwoIcons`; assert no `.tmp` file remains after success.

- [ ] **Step 2: Run the new tests and verify RED**

Run: `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~TrayLayoutPreferenceStoreTests`

Expected: compilation failure because store types do not exist.

- [ ] **Step 3: Implement safe parsing and same-directory replacement**

```csharp
internal enum TrayLayoutMode { Compact, TwoIcons }

internal interface ITrayLayoutPreferenceStore
{
    TrayLayoutMode Load();
    void Save(TrayLayoutMode mode);
}
```

The production directory is `%LOCALAPPDATA%\DualSenseBatteryTray`; write `tray-layout.txt.tmp`, flush/close it, then `File.Move(temp, destination, overwrite: true)`. Catch load errors and return Compact; let save errors be reported to the tray context so the live usable layout is not destroyed.

- [ ] **Step 4: Run tests and verify GREEN**

Run the Task 3 command. Expected: all new tests pass.

- [ ] **Step 5: Commit preference support**

```powershell
git add src/DualSenseBatteryTray.App/Tray/TrayLayoutPreferenceStore.cs tests/DualSenseBatteryTray.App.Tests/TrayLayoutPreferenceStoreTests.cs
git commit -m "feat: persist tray layout preference"
```

### Task 4: Produce connected controller assets and compact renderer

**Files:**
- Create: `src/DualSenseBatteryTray.App/Assets/Tray/dualsense-connected-light.png`
- Create: `src/DualSenseBatteryTray.App/Assets/Tray/dualsense-connected-dark.png`
- Modify: `src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj`
- Modify: `src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs`

**Interfaces:**
- Produces: `RenderCompact(BatteryState state, TrayTheme theme, int size)`, `RenderCompactTrayIcon(BatteryState state, TrayTheme theme)`, and `RenderControllerTrayIcon(BatteryState state, TrayTheme theme)`.
- Consumes: the two connected user references; resource URIs embedded in the application assembly.

- [ ] **Step 1: Use the image-generation workflow to derive clean source assets**

Use `Generated image 1 (2).png` and `Generated image 1 (5).png` as references. Generate/edit tightly cropped, centered, high-contrast DualSense silhouettes with an enlarged blue touchpad and a solid chroma-key background; remove the key locally, verify alpha with pixel inspection, and save transparent 512×512 PNGs at the exact paths above. Do not copy external paths into source code.

- [ ] **Step 2: Inspect both generated assets at original size and 16/24/32/48 previews**

Create preview bitmaps only under the test output/temp directory. Verify: controller not clipped, background alpha is zero, blue touchpad survives at 16 px, dark outline is visible on white, and light outline is visible on black.

- [ ] **Step 3: Write failing compact-renderer tests**

```csharp
[Theory]
[InlineData(16)]
[InlineData(24)]
[InlineData(32)]
[InlineData(48)]
public void RenderCompact_preserves_controller_blue_and_number(int size)
{
    var image = BatteryIconRenderer.RenderCompact(
        new BatteryState(55, ConnectionState.Discharging), TrayTheme.DarkTaskbar, size);
    var pixels = CopyPixels(image);
    Assert.Contains(pixels, IsConnectedBluePixel);
    Assert.True(GetOpaqueBounds(image).Width >= size * 0.75);
    Assert.True(CountLightDigitPixels(pixels) > 0);
}

private static int CountLightDigitPixels(IEnumerable<Pixel> pixels) =>
    pixels.Count(p => p.Alpha > 0 && p.Red >= 220 && p.Green >= 220 && p.Blue >= 220);
```

Add cases for `10`, `55`, `100`, and `?`; assert no clipping, no `%` glyph in the touchpad region, theme-specific outline contrast, and all native ICO frames.

- [ ] **Step 4: Run focused tests and verify RED**

Run: `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~BatteryIconRendererTests`

Expected: failure because compact methods/assets are not wired.

- [ ] **Step 5: Embed assets and implement compact composition**

Add the PNGs as WPF `Resource` items. Load/freeze each bitmap once. Draw the selected controller asset to the square frame, then fit `Segoe UI Semibold` battery text into the enlarged touchpad bounds. Use light digits with a dark 0.5 px stroke over blue; render `100` with the same fit algorithm rather than a hard-coded font size. Encode all four native frames into one HICON.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run the Task 4 test command. Expected: all renderer tests pass.

- [ ] **Step 7: Commit assets and compact rendering**

```powershell
git add src/DualSenseBatteryTray.App/Assets/Tray src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs
git commit -m "feat: render adaptive DualSense battery icon"
```

### Task 5: Reconcile one or two live tray icons safely

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs`
- Modify: `src/DualSenseBatteryTray.App/App.xaml.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs`

**Interfaces:**
- Consumes: `ITrayThemeProvider`, `ITrayLayoutPreferenceStore`, compact/controller/percentage renderers.
- Produces: live menu choices `Compact` and `Two icons`; one compact icon by default or two synchronized icons in fallback mode; internal test seams `LayoutMode`, `ActiveIcons`, and `ChangeLayout(TrayLayoutMode)`.

- [ ] **Step 1: Replace single-icon reflection tests with layout-state tests**

```csharp
[Fact]
public void Compact_mode_owns_one_visible_icon_and_two_icons_mode_owns_two()
{
    RunOnStaThread(() =>
    {
        var window = new MainWindow();
        using var theme = new FakeTrayThemeProvider(TrayTheme.DarkTaskbar);
        var preferences = new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.Compact);
        using var context = new TrayApplicationContext(
            window, () => { }, () => { }, new AvailableWatcherTaskService(),
            theme, preferences);

        Assert.Equal(TrayLayoutMode.Compact, context.LayoutMode);
        Assert.Single(context.ActiveIcons.Where(icon => icon.Visible));

        context.ChangeLayout(TrayLayoutMode.TwoIcons);

        Assert.Equal(TrayLayoutMode.TwoIcons, context.LayoutMode);
        Assert.Equal(2, context.ActiveIcons.Count(icon => icon.Visible));
        Assert.Single(context.ActiveIcons.Select(icon => icon.Text).Distinct());
        Assert.Equal(TrayLayoutMode.TwoIcons, preferences.Value);
        window.BeginShutdown();
    });
}
```

Add these exact fakes to the test file and use them for persisted-selection and theme-refresh cases:

```csharp
private sealed class FakeTrayThemeProvider(TrayTheme current) : ITrayThemeProvider
{
    public TrayTheme Current { get; private set; } = current;
    public event Action<TrayTheme>? ThemeChanged;
    public void Set(TrayTheme theme) { Current = theme; ThemeChanged?.Invoke(theme); }
    public void Refresh() { }
    public void Dispose() { }
}

private sealed class MemoryTrayLayoutPreferenceStore(TrayLayoutMode value)
    : ITrayLayoutPreferenceStore
{
    public TrayLayoutMode Value { get; private set; } = value;
    public TrayLayoutMode Load() => Value;
    public void Save(TrayLayoutMode mode) => Value = mode;
}
```

Also assert both expanded icons share tooltip/menu/click behavior, layout switching never invokes the refresh/HID callback, a forced second-icon assignment failure retains the compact icon, and `Dispose()` makes every icon invisible.

- [ ] **Step 2: Run shell tests and verify RED**

Run: `dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~ApplicationShellTests`

Expected: failures because constructor dependencies and live layout transitions do not exist.

- [ ] **Step 3: Implement a single reconciliation path**

Update the constructor to accept:

```csharp
ITrayThemeProvider trayThemeProvider,
ITrayLayoutPreferenceStore layoutPreferenceStore
```

Create `Tray layout` submenu radio items. A reconciliation method renders the full next icon set before mutating visibility, applies icons/tooltips/menu/click handlers, then disposes superseded handles. On failure, dispose the new set and keep the old set. Marshal `ThemeChanged` through the WPF dispatcher. Save the preference only after successful reconciliation.

- [ ] **Step 4: Wire production dependencies in `App.xaml.cs`**

Construct `WindowsTrayThemeProvider` and `TrayLayoutPreferenceStore` in the composition root and transfer disposal ownership to `TrayApplicationContext`. Do not alter `BatteryReaderSession` or watcher startup/shutdown behavior.

- [ ] **Step 5: Run shell tests and the complete solution**

Run:

```powershell
dotnet test DualSenseBatteryTray.sln -c Release
dotnet build DualSenseBatteryTray.sln -c Release --no-restore
```

Expected: every test passes; build has zero errors and zero warnings.

- [ ] **Step 6: Commit tray reconciliation**

```powershell
git add src/DualSenseBatteryTray.App/Tray/TrayApplicationContext.cs src/DualSenseBatteryTray.App/App.xaml.cs tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs
git commit -m "feat: switch between compact and expanded tray layouts"
```

### Task 6: Publish, install, and perform taskbar verification

**Files:**
- Modify if behavior text changed: `README.md`
- Modify: `docs/superpowers/specs/2026-07-19-dualsense-battery-tray-design.md`

**Interfaces:**
- Consumes: the repository's existing publish/setup scripts and installed Scheduled Task.
- Produces: verified installed Compact default with a working Two-icons fallback.

- [ ] **Step 1: Update the main design and user documentation**

Replace the old mandatory two-icon description with the default Compact layout, automatic theme switching, missing `%` in compact mode, and the live Two-icons fallback. Keep disconnect-to-exit and read-only HID statements unchanged.

- [ ] **Step 2: Run verification-before-completion checks**

Run the full Release test/build commands from Task 5 and capture totals. Inspect `git diff --check` and `git status --short`; only intentional files may remain.

- [ ] **Step 3: Publish and reinstall using the repository's existing scripts**

Stop only the installed tray/watcher processes inside `%LOCALAPPDATA%\Programs\DualSenseBatteryTray`, publish Release x64, run the existing per-user installer, and confirm the Scheduled Task `DualSenseBatteryTray-DeviceWatcher` remains registered and running.

- [ ] **Step 4: Verify with the connected controller**

At the actual Windows DPI, confirm `55`-style two-digit values and `100` are readable, switch Windows system theme in both directions and confirm icon refresh, use the tray menu to switch Compact ⇄ Two icons, and confirm the controller remains available to Windows/game input throughout.

- [ ] **Step 5: Verify disconnect lifecycle**

Unplug the controller and confirm the main process and every tray icon disappear while the watcher remains. Reconnect and confirm exactly one app instance returns with the persisted layout.

- [ ] **Step 6: Commit documentation and record final hashes**

```powershell
git add README.md docs/superpowers/specs/2026-07-19-dualsense-battery-tray-design.md
git commit -m "docs: document adaptive tray layouts"
```

Record SHA-256 hashes of the installed app and watcher executables in the handoff response.
