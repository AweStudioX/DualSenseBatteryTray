# Small Icon Readability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render an approved, refined DualSense glyph at native Windows small-icon sizes for the window title bar and notification area while leaving the executable/taskbar icon and battery percentage slot unchanged.

**Architecture:** `BatteryIconRenderer` will render the approved DualSense geometry directly at each ICO frame size, using a minimal detail level at 16 pixels and a fuller level at 24 pixels and above. A focused `WindowSmallIconController` will apply the dedicated small HICON through `WM_SETICON/ICON_SMALL` after WPF creates the HWND, while `MainWindow.Icon` continues to carry the existing 256-pixel disconnected artwork for the taskbar and application identity.

**Tech Stack:** .NET 8, WPF `DrawingVisual`/`Geometry`, `System.Drawing.Icon`, Win32 `SendMessageW`, xUnit, PowerShell.

## Global Constraints

- Keep the default `TwoIcons` notification-area layout: controller slot first, percentage slot second.
- Keep the percentage renderer, `%` glyph, charging values, tooltip, layout switching, and Compact mode unchanged.
- Keep `Assets/App/dualsense-disconnected.ico` configured as the executable icon and preserve its 16, 24, 32, 48, and 256-pixel frames.
- Keep HID access read-only and shared; do not add HID output or feature-report writes.
- At 16 pixels, render only the refined shell, shoulder steps, waist/grips, and touchpad; omit the D-pad, face buttons, and analog sticks.
- At 24, 32, and 48 pixels, restore the fuller DualSense control details.
- Connected tray artwork has a blue touchpad and follows the taskbar theme; disconnected window artwork has no connected-blue pixels and remains visible on white.
- Preserve the existing tray reconciliation rollback and HICON disposal contracts.

---

### Task 1: Native Size-Aware DualSense Glyph Renderer

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs`
- Test: `tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs`

**Interfaces:**
- Produces: `internal static BitmapSource RenderDisconnectedWindowGlyph(int size)`.
- Produces: `internal static BitmapSource RenderConnectedControllerGlyph(TrayTheme theme, int size)`.
- Produces: `internal static ControllerGlyphDetail SelectControllerGlyphDetail(int size)` where `ControllerGlyphDetail` is `Minimal` or `Full`.
- Produces: `internal static System.Drawing.Icon RenderWindowSmallIcon(int nativeSize)` returning an owned icon selected at 16, 24, 32, or 48 pixels.
- Preserves: `RenderControllerTrayIcon(BatteryState, TrayTheme)` and its owned, multi-frame ICO contract.

- [ ] **Step 1: Add failing detail-selection and disconnected-glyph tests**

Add the enum/API expectations and pixel assertions below to `BatteryIconRendererTests.cs`:

```csharp
[Theory]
[InlineData(16, ControllerGlyphDetail.Minimal)]
[InlineData(24, ControllerGlyphDetail.Full)]
[InlineData(32, ControllerGlyphDetail.Full)]
[InlineData(48, ControllerGlyphDetail.Full)]
public void Controller_glyph_selects_detail_by_native_size(
    int size,
    ControllerGlyphDetail expected) =>
    Assert.Equal(expected, BatteryIconRenderer.SelectControllerGlyphDetail(size));

[Theory]
[InlineData(16)]
[InlineData(24)]
[InlineData(32)]
[InlineData(48)]
public void Disconnected_window_glyph_is_bounded_dark_edged_and_unlit(int size)
{
    var image = BatteryIconRenderer.RenderDisconnectedWindowGlyph(size);
    var pixels = CopyPixels(image);
    var bounds = GetOpaqueBounds(image);

    Assert.True(bounds.Left > 0 && bounds.Top > 0);
    Assert.True(bounds.Left + bounds.Width < size);
    Assert.True(bounds.Top + bounds.Height < size);
    Assert.Contains(pixels, IsLightControllerPixel);
    Assert.Contains(pixels, IsDarkOutlinePixel);
    Assert.DoesNotContain(pixels, IsConnectedBluePixel);
}
```

Add a 16-pixel minimal-detail assertion by rendering the details layer through an internal test seam:

```csharp
[Fact]
public void Sixteen_pixel_controller_omits_micro_detail_layer()
{
    var layer = BatteryIconRenderer.RenderControllerDetailLayer(16);
    Assert.All(CopyPixels(layer), pixel => Assert.Equal(0, pixel.Alpha));
}

[Theory]
[InlineData(24)]
[InlineData(32)]
[InlineData(48)]
public void Larger_controller_frames_restore_micro_details(int size)
{
    var layer = BatteryIconRenderer.RenderControllerDetailLayer(size);
    Assert.Contains(CopyPixels(layer), pixel => pixel.Alpha != 0);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests\DualSenseBatteryTray.App.Tests\DualSenseBatteryTray.App.Tests.csproj -c Release --filter "FullyQualifiedName~Controller_glyph_selects_detail_by_native_size|FullyQualifiedName~Disconnected_window_glyph_is_bounded_dark_edged_and_unlit|FullyQualifiedName~controller_omits_micro_detail_layer|FullyQualifiedName~controller_frames_restore_micro_details"
```

Expected: compilation fails because `ControllerGlyphDetail`, `SelectControllerGlyphDetail`, `RenderDisconnectedWindowGlyph`, and `RenderControllerDetailLayer` do not exist.

- [ ] **Step 3: Implement the approved shell and detail layers**

Add this internal enum and the approved 16-unit geometry to `BatteryIconRenderer.cs`:

```csharp
internal enum ControllerGlyphDetail { Minimal, Full }

private const string RefinedDualSenseShellPath =
    "M3.25 4.15 C2.7 4.3 2.4 5 2.1 5.8 " +
    "L1.15 9.25 C0.73 10.9 0.97 12.3 2.05 12.7 " +
    "C2.87 13 3.5 12.5 3.9 11.55 L4.82 9.5 " +
    "C5.02 9.05 5.37 8.85 5.92 8.85 H10.08 " +
    "C10.63 8.85 10.98 9.05 11.18 9.5 L12.1 11.55 " +
    "C12.5 12.5 13.13 13 13.95 12.7 " +
    "C15.03 12.3 15.27 10.9 14.85 9.25 L13.9 5.8 " +
    "C13.6 5 13.3 4.3 12.75 4.15 L10.7 3.8 H5.3 Z";

private const string RefinedDualSenseShouldersPath =
    "M3.25 4.25 L3.5 3.3 L5.05 3.05 L5.2 3.9 " +
    "M10.8 3.9 L10.95 3.05 L12.5 3.3 L12.75 4.25";

private const string RefinedDualSenseTouchpadPath =
    "M5.35 3.85 H10.65 L10.3 6.1 " +
    "C10.22 6.65 9.88 6.92 9.35 6.92 H6.65 " +
    "C6.12 6.92 5.78 6.65 5.7 6.1 Z";
```

Create frozen `Geometry` instances from those paths. Implement the public test seams exactly as follows:

```csharp
internal static ControllerGlyphDetail SelectControllerGlyphDetail(int size) => size switch
{
    16 => ControllerGlyphDetail.Minimal,
    24 or 32 or 48 => ControllerGlyphDetail.Full,
    _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
};

internal static BitmapSource RenderControllerDetailLayer(int size)
{
    _ = SelectControllerGlyphDetail(size);
    var visual = new DrawingVisual();
    if (size == 16)
        return RenderVisual(visual, size);

    using (var context = visual.RenderOpen())
    {
        var scale = size / 16d;
        context.PushTransform(new ScaleTransform(scale, scale));
        DrawFullControllerDetails(context, new SolidColorBrush(OutlineMedia), scale);
        context.Pop();
    }
    return RenderVisual(visual, size);
}
```

Add the full-detail drawing helper using the 16-unit coordinate space:

```csharp
private static void DrawFullControllerDetails(
    DrawingContext context,
    MediaBrush brush,
    double scale)
{
    var pen = new MediaPen(brush, 1d / scale);

    context.DrawLine(pen, new System.Windows.Point(3.65, 7.15), new System.Windows.Point(3.65, 9.35));
    context.DrawLine(pen, new System.Windows.Point(2.55, 8.25), new System.Windows.Point(4.75, 8.25));

    context.DrawEllipse(brush, null, new System.Windows.Point(11.8, 7.2), 0.34, 0.34);
    context.DrawEllipse(brush, null, new System.Windows.Point(12.75, 8.15), 0.34, 0.34);
    context.DrawEllipse(brush, null, new System.Windows.Point(11.8, 9.1), 0.34, 0.34);
    context.DrawEllipse(brush, null, new System.Windows.Point(10.85, 8.15), 0.34, 0.34);

    context.DrawEllipse(null, pen, new System.Windows.Point(6.45, 9.65), 0.55, 0.55);
    context.DrawEllipse(null, pen, new System.Windows.Point(9.55, 9.65), 0.55, 0.55);
}
```

Implement `RenderDisconnectedWindowGlyph` with a white shell, `OutlineMedia` shell/shoulder stroke, and an unlit touchpad filled with `OutlineMedia`. Implement `RenderConnectedControllerGlyph` with these exact theme mappings:

```csharp
var (body, details) = theme switch
{
    TrayTheme.DarkTaskbar => (MediaBrushes.White, new SolidColorBrush(OutlineMedia)),
    TrayTheme.LightTaskbar => (new SolidColorBrush(OutlineMedia), MediaBrushes.White),
    _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
};
```

Use one shared renderer so the disconnected and connected variants cannot drift geometrically:

```csharp
internal static BitmapSource RenderDisconnectedWindowGlyph(int size) =>
    RenderControllerGlyphCore(
        size,
        MediaBrushes.White,
        new SolidColorBrush(OutlineMedia),
        new SolidColorBrush(OutlineMedia),
        new SolidColorBrush(OutlineMedia));

internal static BitmapSource RenderConnectedControllerGlyph(TrayTheme theme, int size)
{
    var (body, details) = theme switch
    {
        TrayTheme.DarkTaskbar => (MediaBrushes.White, new SolidColorBrush(OutlineMedia)),
        TrayTheme.LightTaskbar => (new SolidColorBrush(OutlineMedia), MediaBrushes.White),
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };
    return RenderControllerGlyphCore(
        size,
        body,
        details,
        CreateFrozenBrush(87, 151, 246),
        details);
}

private static BitmapSource RenderControllerGlyphCore(
    int size,
    MediaBrush body,
    MediaBrush outline,
    MediaBrush touchpad,
    MediaBrush details)
{
    var detail = SelectControllerGlyphDetail(size);
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        var scale = (size - 2d) / 16d;
        context.PushTransform(new TranslateTransform(1, 1));
        context.PushTransform(new ScaleTransform(scale, scale));
        var shellPen = new MediaPen(outline, 1d / scale);
        var touchpadPen = new MediaPen(outline, 0.5d / scale);

        context.DrawGeometry(body, shellPen, RefinedDualSenseShell);
        context.DrawGeometry(null, shellPen, RefinedDualSenseShoulders);
        context.DrawGeometry(touchpad, touchpadPen, RefinedDualSenseTouchpad);
        if (detail == ControllerGlyphDetail.Full)
            DrawFullControllerDetails(context, details, scale);

        context.Pop();
        context.Pop();
    }
    return RenderVisual(visual, size);
}
```

The connected touchpad is therefore exactly RGB `(87, 151, 246)`. The translate-plus-scale transform keeps one transparent device pixel around all four frame edges.

- [ ] **Step 4: Route the tray controller through the native renderer and add owned ICO output**

Replace the body of `RenderConnectedController` with:

```csharp
private static BitmapSource RenderConnectedController(
    BatteryState state,
    TrayTheme theme,
    int size)
{
    ValidateStateAndSize(state, size);
    return RenderConnectedControllerGlyph(theme, size);
}
```

Generalize ICO encoding with a non-battery overload:

```csharp
private static byte[] EncodeIcon(Func<int, BitmapSource> renderFrame) =>
    EncodeIconFrames(NativeIconSizes.Select(size => (size, renderFrame(size))));

internal static DrawingIcon RenderWindowSmallIcon()
{
    var requested = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
    var nativeSize = NativeIconSizes
        .OrderBy(size => Math.Abs(size - requested))
        .ThenByDescending(size => size)
        .First();
    return RenderWindowSmallIcon(nativeSize);
}

internal static DrawingIcon RenderWindowSmallIcon(int nativeSize)
{
    if (!NativeIconSizes.Contains(nativeSize))
        throw new ArgumentOutOfRangeException(nameof(nativeSize), nativeSize, null);

    var bytes = EncodeIcon(RenderDisconnectedWindowGlyph);
    using var stream = new MemoryStream(bytes, writable: false);
    using var icon = new DrawingIcon(stream, nativeSize, nativeSize);
    return (DrawingIcon)icon.Clone();
}
```

Move the existing ICO header/data writing into this shared helper and have both battery and non-battery overloads call it:

```csharp
private static byte[] EncodeIconFrames(
    IEnumerable<(int Size, BitmapSource Frame)> frames)
{
    var items = frames
        .Select(item => (item.Size, Data: EncodePng(item.Frame)))
        .ToArray();
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)items.Length);

    var imageOffset = 6 + (items.Length * 16);
    foreach (var item in items)
    {
        writer.Write((byte)item.Size);
        writer.Write((byte)item.Size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)item.Data.Length);
        writer.Write((uint)imageOffset);
        imageOffset += item.Data.Length;
    }

    foreach (var item in items)
        writer.Write(item.Data);
    writer.Flush();
    return stream.ToArray();
}
```

Do not change percentage or Compact rendering.

- [ ] **Step 5: Add connected-theme, frame, and ownership tests**

Add:

```csharp
[Theory]
[InlineData((int)TrayTheme.DarkTaskbar, true)]
[InlineData((int)TrayTheme.LightTaskbar, false)]
public void Connected_glyph_is_theme_aware_and_blue(int themeValue, bool lightBody)
{
    const int size = 16;
    var image = BatteryIconRenderer.RenderConnectedControllerGlyph((TrayTheme)themeValue, size);
    var pixels = CopyPixels(image);

    Assert.Contains(pixels, IsConnectedBluePixel);
    Assert.True(HasOpaqueEdgeColor(pixels, size, lightBody));
}

[Fact]
public void Window_small_icon_is_owned_and_contains_all_native_frames()
{
    using var icon = BatteryIconRenderer.RenderWindowSmallIcon(16);
    Assert.Equal(new System.Drawing.Size(16, 16), icon.Size);
    Assert.Equal([16, 24, 32, 48], DecodeFrames(icon).Keys.Order().ToArray());
}
```

Update `Connected_controller_tray_icon_keeps_its_blue_touchpad` to assert blue in every decoded frame, not only 48 pixels. Retain all percentage and application ICO assertions unchanged.

- [ ] **Step 6: Run focused renderer tests and verify GREEN**

Run:

```powershell
dotnet test tests\DualSenseBatteryTray.App.Tests\DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~BatteryIconRendererTests
```

Expected: all `BatteryIconRendererTests` pass, including the unchanged percentage, Compact, executable metadata, and application-icon tests.

- [ ] **Step 7: Commit the native renderer**

```powershell
git add -- src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs
git commit -m "fix: render readable native controller glyphs"
```

---

### Task 2: Separate the Window Small Icon from the Taskbar Icon

**Files:**
- Create: `src/DualSenseBatteryTray.App/Shell/WindowSmallIconController.cs`
- Modify: `src/DualSenseBatteryTray.App/MainWindow.xaml.cs`
- Test: `tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs`

**Interfaces:**
- Consumes: `BatteryIconRenderer.RenderWindowSmallIcon()` from Task 1; this overload maps the current Windows small-icon width to the nearest 16, 24, 32, or 48-pixel native frame.
- Produces: `internal sealed class WindowSmallIconController : IDisposable` with `Apply(nint windowHandle)`.
- Preserves: `MainWindow.Icon == BatteryIconRenderer.RenderApplicationIcon()` for WPF/taskbar identity.

- [ ] **Step 1: Add failing lifecycle tests for the small HICON owner**

Add these tests to `ApplicationShellTests.cs`:

```csharp
[Fact]
public void WindowSmallIconController_applies_once_and_owns_icon_until_disposed()
{
    var created = 0;
    var applied = new List<(nint Window, nint Icon)>();
    using var expected = BatteryIconRenderer.RenderWindowSmallIcon(16);
    using var controller = new WindowSmallIconController(
        () => { created++; return (System.Drawing.Icon)expected.Clone(); },
        (window, icon) => applied.Add((window, icon)));

    controller.Apply((nint)42);
    controller.Apply((nint)42);

    Assert.Equal(1, created);
    Assert.Single(applied);
    Assert.Equal((nint)42, applied[0].Window);
    Assert.NotEqual(nint.Zero, applied[0].Icon);
}

[Fact]
public void WindowSmallIconController_disposes_new_icon_when_apply_fails()
{
    using var icon = BatteryIconRenderer.RenderWindowSmallIcon(16);
    var disposed = 0;
    var controller = new WindowSmallIconController(
        () => icon,
        (_, _) => throw new InvalidOperationException("apply failed"),
        _ => disposed++);

    Assert.Throws<InvalidOperationException>(() => controller.Apply((nint)42));
    Assert.Equal(1, disposed);
}
```

- [ ] **Step 2: Run lifecycle tests and verify RED**

Run:

```powershell
dotnet test tests\DualSenseBatteryTray.App.Tests\DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~WindowSmallIconController
```

Expected: compilation fails because `WindowSmallIconController` does not exist.

- [ ] **Step 3: Implement `WindowSmallIconController` and Win32 boundary**

Create `src/DualSenseBatteryTray.App/Shell/WindowSmallIconController.cs`:

```csharp
using System.Runtime.InteropServices;
using DrawingIcon = System.Drawing.Icon;

namespace DualSenseBatteryTray.App.Shell;

internal sealed class WindowSmallIconController(
    Func<DrawingIcon> createIcon,
    Action<nint, nint>? applyIcon = null,
    Action<DrawingIcon>? disposeIcon = null) : IDisposable
{
    private DrawingIcon? _icon;
    private readonly Action<nint, nint> _applyIcon = applyIcon ?? WindowIconNative.SetSmallIcon;
    private readonly Action<DrawingIcon> _disposeIcon = disposeIcon ?? (icon => icon.Dispose());

    public void Apply(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
            throw new ArgumentException("A created window handle is required.", nameof(windowHandle));
        if (_icon is not null)
            return;

        var next = createIcon();
        try
        {
            _applyIcon(windowHandle, next.Handle);
            _icon = next;
        }
        catch
        {
            _disposeIcon(next);
            throw;
        }
    }

    public void Dispose()
    {
        if (_icon is not null)
            _disposeIcon(_icon);
        _icon = null;
    }
}

internal static class WindowIconNative
{
    private const uint WmSetIcon = 0x0080;
    private static readonly nint IconSmall = nint.Zero;

    internal static void SetSmallIcon(nint windowHandle, nint iconHandle) =>
        _ = SendMessageW(windowHandle, WmSetIcon, IconSmall, iconHandle);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
}
```

- [ ] **Step 4: Run lifecycle tests and verify GREEN**

Run:

```powershell
dotnet test tests\DualSenseBatteryTray.App.Tests\DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~WindowSmallIconController
```

Expected: both lifecycle tests pass.

- [ ] **Step 5: Integrate the small icon after HWND creation**

Modify `MainWindow.xaml.cs` to retain the existing large WPF icon and apply only `ICON_SMALL`:

```csharp
using System.Windows.Interop;
using DualSenseBatteryTray.App.Shell;

private readonly WindowSmallIconController _smallIcon = new(
    BatteryIconRenderer.RenderWindowSmallIcon);

public MainWindow()
{
    InitializeComponent();
    DataContext = _viewModel;
    Icon = ApplicationIcon;
    TaskbarItemInfo.Overlay = null;
    SourceInitialized += OnSourceInitialized;
    Closed += (_, _) => _smallIcon.Dispose();
    Closing += OnClosing;
}

private void OnSourceInitialized(object? sender, EventArgs eventArgs) =>
    _smallIcon.Apply(new WindowInteropHelper(this).Handle);
```

Do not replace `ApplicationIcon`, the `<ApplicationIcon>` project property, or `TaskbarItemInfo` behavior.

- [ ] **Step 6: Add a WPF regression test for unchanged taskbar identity**

Extend the existing STA test `TrayApplicationContext_updates_only_the_notification_area_icon_for_battery_changes` with these pre/post assertions:

```csharp
var expectedLargeIcon = BatteryIconRenderer.RenderApplicationIcon();
Assert.Same(expectedLargeIcon, window.Icon);
Assert.Equal(
    ComputeHash(CopyPixels(expectedLargeIcon)),
    ComputeHash(CopyPixels(window.Icon)));
```

After `context.UpdateState(...)`, repeat the hash assertion. This proves battery/tray updates do not replace the WPF/taskbar image source while the native small-icon controller owns only `ICON_SMALL`.

- [ ] **Step 7: Run application shell and renderer tests**

Run:

```powershell
dotnet test tests\DualSenseBatteryTray.App.Tests\DualSenseBatteryTray.App.Tests.csproj -c Release --filter "FullyQualifiedName~ApplicationShellTests|FullyQualifiedName~BatteryIconRendererTests"
```

Expected: all selected tests pass with no process-ownership or HICON disposal failures.

- [ ] **Step 8: Commit the window integration**

```powershell
git add -- src/DualSenseBatteryTray.App/Shell/WindowSmallIconController.cs src/DualSenseBatteryTray.App/MainWindow.xaml.cs tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs
git commit -m "fix: separate window and taskbar icon sizes"
```

---

### Task 3: Full Regression, Installed Visual Verification, and Public-Safety Check

**Files:**
- Verify only: `DualSenseBatteryTray.sln`
- Verify only: `scripts/install.ps1`
- Verify only: tracked repository content

**Interfaces:**
- Consumes: native glyph renderer and `WindowSmallIconController` from Tasks 1–2.
- Produces: verified Release binaries and an installed build with no source changes.

- [ ] **Step 1: Run the complete Release test suite**

Run:

```powershell
dotnet test DualSenseBatteryTray.sln -c Release --no-restore
```

Expected: every test passes. If `SingleInstanceGuard_allows_only_one_current_user_instance` reports an occupied mutex, identify the installed tray process, close only `DualSenseBatteryTray.App`, rerun the suite, and let the watcher restart it after installation; do not stop unrelated game or HID processes.

- [ ] **Step 2: Build the complete Release solution**

Run:

```powershell
dotnet build DualSenseBatteryTray.sln -c Release --no-restore
```

Expected: exit code 0, zero warnings, zero errors.

- [ ] **Step 3: Scan tracked content before any public update**

Run:

```powershell
git diff --check
git status --short
git ls-files | rg -i "(^|/)(\.env|id_rsa|credentials|secrets?|.*\.pfx|.*\.p12)$"
$fixedPatterns = @(
    ('C:' + [char]92 + 'Users' + [char]92),
    ('AppData' + [char]92 + 'Local' + [char]92 + 'Temp'),
    ('D:' + [char]92 + 'Tencent'),
    ('-----BEGIN ' + 'PRIVATE KEY-----'))
foreach ($pattern in $fixedPatterns) {
    git grep -n -I -F -- $pattern
    if ($LASTEXITCODE -eq 0) { throw "Sensitive tracked content matched a fixed pattern." }
}
$tokenPattern = 'gh' + '[pousr]_[A-Za-z0-9_]{20,}'
git grep -n -I -E -- $tokenPattern
if ($LASTEXITCODE -eq 0) { throw "Sensitive tracked content matched a token pattern." }
```

Expected: `git diff --check` is clean; only intentional plan/implementation files appear in status; sensitive filename and content searches return no matches.

- [ ] **Step 4: Install the verified build**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

Expected: installation succeeds, one watcher remains running, and connecting the USB DualSense launches one tray application instance.

- [ ] **Step 5: Verify the three Windows icon surfaces on real hardware**

With the DualSense connected, verify:

1. The white status-window title bar shows the refined disconnected glyph with a visible dark edge and no blue touchpad.
2. The notification area shows the refined connected glyph with a blue touchpad followed by the unchanged readable percentage and `%` sign.
3. The taskbar button retains the existing detailed disconnected application artwork.
4. Switching Windows between light and dark taskbar themes keeps the connected tray glyph visible.
5. Switching `Two icons` ⇄ `Compact` ⇄ `Two icons` does not restart the HID session.
6. Disconnecting the controller removes every tray icon and exits the app while the watcher remains.

- [ ] **Step 6: Record final verification evidence**

Record the exact test total, Release build warning/error counts, installed executable SHA-256, active watcher/app process counts, connected battery value/state, and the six visual checks in the implementation report under `.superpowers/sdd/2026-08-10-small-icon-readability/`. Keep screenshots and machine-local paths out of git.
