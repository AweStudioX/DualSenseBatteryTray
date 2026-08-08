# Readable Tray and Disconnected App Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Default to a readable controller-plus-percentage tray layout at 16×16 and replace the executable/window identity with a multi-frame unconnected DualSense icon.

**Architecture:** Change only the invalid/missing layout fallback to `TwoIcons`, preserving every explicit stored choice and the existing live layout reconciliation. Derive a transparent disconnected application asset from the user reference, embed one multi-frame ICO for both executable metadata and WPF window identity, and keep all connected blue-touchpad tray renderers separate.

**Tech Stack:** .NET 8, WPF, Windows Forms `NotifyIcon`, WPF bitmap/ICO decoding, xUnit, built-in image generation/editing with deterministic local chroma-key cleanup.

## Global Constraints

- USB DualSense `054C:0CE6` only; no Bluetooth or controller-output support.
- HID access remains shared and read-only and must not interfere with games.
- Controller removal hides every tray icon and exits the main application.
- Missing, unreadable, or invalid layout preferences default to `TwoIcons`; valid saved `Compact` and `TwoIcons` values remain authoritative.
- The connected tray controller keeps its blue touchpad; the static application icon is unconnected and contains no connected-blue pixels.
- Charging at 45% remains 45%; no renderer may fabricate 100%.
- Runtime code must not reference the external `D:` source path.
- No HID, watcher, notification threshold, reader, device-removal, or charging-parser code changes are permitted.

---

### Task 1: Make Two icons the safe default

**Files:**
- Modify: `src/DualSenseBatteryTray.App/Tray/TrayLayoutPreferenceStore.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/TrayLayoutPreferenceStoreTests.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs`

**Interfaces:**
- Consumes: `TrayLayoutMode.Compact`, `TrayLayoutMode.TwoIcons`, `ITrayLayoutPreferenceStore.Load()`.
- Produces: `TrayLayoutPreferenceStore.Load()` returning `TwoIcons` only for missing, inaccessible, malformed, or undefined input; valid stored values are unchanged.

- [ ] **Step 1: Write failing default and preservation tests**

```csharp
[Fact]
public void Missing_or_invalid_preference_defaults_to_two_icons()
{
    var store = new TrayLayoutPreferenceStore(_temporaryDirectory);
    Assert.Equal(TrayLayoutMode.TwoIcons, store.Load());
    File.WriteAllText(store.PreferencePath, "broken");
    Assert.Equal(TrayLayoutMode.TwoIcons, store.Load());
}

[Theory]
[InlineData((int)TrayLayoutMode.Compact)]
[InlineData((int)TrayLayoutMode.TwoIcons)]
public void Valid_preference_round_trips(int modeValue)
{
    var mode = (TrayLayoutMode)modeValue;
    var store = new TrayLayoutPreferenceStore(_temporaryDirectory);
    store.Save(mode);
    Assert.Equal(mode, new TrayLayoutPreferenceStore(_temporaryDirectory).Load());
}
```

Add a shell test whose in-memory preference starts at `TwoIcons` and assert two visible icons with one synchronized tooltip. Keep the existing explicit Compact test to prove the optional layout still works.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter "FullyQualifiedName~TrayLayoutPreferenceStoreTests|FullyQualifiedName~ApplicationShellTests"
```

Expected: missing/invalid preference assertions report `Compact` instead of `TwoIcons`.

- [ ] **Step 3: Change only the fallback values**

In `TrayLayoutPreferenceStore.Load()`, return `TrayLayoutMode.TwoIcons` from the invalid-enum branch and expected file-error catch. Do not reorder enum members, change serialization text, or modify `TrayApplicationContext`.

- [ ] **Step 4: Run focused and full tests**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter "FullyQualifiedName~TrayLayoutPreferenceStoreTests|FullyQualifiedName~ApplicationShellTests"
dotnet test DualSenseBatteryTray.sln -c Release
```

Expected: all focused tests and the full solution pass.

- [ ] **Step 5: Commit**

```powershell
git add src/DualSenseBatteryTray.App/Tray/TrayLayoutPreferenceStore.cs tests/DualSenseBatteryTray.App.Tests/TrayLayoutPreferenceStoreTests.cs tests/DualSenseBatteryTray.App.Tests/ApplicationShellTests.cs
git commit -m "fix: default to readable tray layout"
```

### Task 2: Replace the static application identity with an unconnected DualSense

**Files:**
- Create: `src/DualSenseBatteryTray.App/Assets/App/dualsense-disconnected.png`
- Create: `src/DualSenseBatteryTray.App/Assets/App/dualsense-disconnected.ico`
- Modify: `src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj`
- Modify: `src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs`
- Modify: `tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs`

**Interfaces:**
- Consumes: the user-provided `Generated image 1.png` unconnected-controller reference as an edit/reference input only.
- Produces: `BatteryIconRenderer.RenderApplicationIcon()` backed by the embedded disconnected ICO; executable metadata uses `Assets\App\dualsense-disconnected.ico`.

- [ ] **Step 1: Create the disconnected project asset through imagegen**

Read the imagegen skill, inspect the reference, and use built-in edit/generation to preserve the DualSense silhouette while removing the lit blue touchpad. Request a perfectly flat `#00ff00` background, no text, no shadow, an unlit center, white body, strong dark outline, centered square composition, and generous padding. Copy the chosen output into temporary workspace storage, remove the key with the installed helper or the already validated deterministic .NET equivalent, and save a transparent 512×512 PNG at the exact project path.

- [ ] **Step 2: Generate and inspect one multi-frame ICO**

Encode PNG frames at 16, 24, 32, 48, and 256 pixels into `dualsense-disconnected.ico`. Inspect the PNG and every decoded frame. All four corners must be transparent; controller bounds must remain inside each frame; no pixel may match the connected blue predicate `(Blue >= 150 && Blue > Red * 1.3 && Blue > Green * 1.05)`; the 16px frame must retain both light body and dark outline pixels.

- [ ] **Step 3: Write failing application-icon tests**

```csharp
[Fact]
public void RenderApplicationIcon_uses_unconnected_multi_frame_asset()
{
    var icon = BatteryIconRenderer.RenderApplicationIcon();
    var decoder = Assert.IsType<IconBitmapDecoder>(icon.Decoder);
    Assert.Equal([16, 24, 32, 48, 256],
        decoder.Frames.Select(frame => frame.PixelWidth).Order().ToArray());
    foreach (var frame in decoder.Frames)
    {
        var pixels = CopyPixels(frame);
        Assert.Equal(0, pixels[0].Alpha);
        Assert.DoesNotContain(pixels, IsConnectedBluePixel);
        Assert.Contains(pixels, IsLightControllerPixel);
        Assert.Contains(pixels, IsDarkOutlinePixel);
    }
}
```

Add a connected-controller regression that decodes the 48px frame from `RenderControllerTrayIcon(state, TrayTheme.DarkTaskbar)` and asserts it still contains a connected-blue pixel. Add an executable metadata test that locates `DualSenseBatteryTray.App.exe` beside the referenced test output, calls `System.Drawing.Icon.ExtractAssociatedIcon`, and asserts it returns a non-null icon whose bitmap contains visible pixels.

- [ ] **Step 4: Run renderer tests and verify RED**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~BatteryIconRendererTests
```

Expected: old generated application icon has only 16/24/32/48 frames and the executable lacks the configured disconnected ICO.

- [ ] **Step 5: Embed and consume the disconnected ICO**

Add these project entries:

```xml
<ApplicationIcon>Assets\App\dualsense-disconnected.ico</ApplicationIcon>
```

```xml
<Resource Include="Assets\App\dualsense-disconnected.ico" />
```

Replace `RenderApplicationFrame` usage with one lazy, frozen `IconBitmapDecoder` loaded from the component resource URI `assets/app/dualsense-disconnected.ico`. `RenderApplicationIcon()` returns the frozen 256px frame while retaining its decoder so tests and WPF can access all frames. Delete only the now-unused static application-frame drawing code; keep connected tray rendering unchanged.

- [ ] **Step 6: Run renderer tests, full solution, and build**

Run:

```powershell
dotnet test tests/DualSenseBatteryTray.App.Tests/DualSenseBatteryTray.App.Tests.csproj -c Release --filter FullyQualifiedName~BatteryIconRendererTests
dotnet test DualSenseBatteryTray.sln -c Release
dotnet build DualSenseBatteryTray.sln -c Release --no-restore
```

Expected: all tests pass; build reports zero warnings and zero errors.

- [ ] **Step 7: Commit**

```powershell
git add src/DualSenseBatteryTray.App/Assets/App src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj src/DualSenseBatteryTray.App/Tray/BatteryIconRenderer.cs tests/DualSenseBatteryTray.App.Tests/BatteryIconRendererTests.cs
git commit -m "feat: use disconnected DualSense app icon"
```

### Task 3: Document, install, and verify the readable final layout

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-07-19-dualsense-battery-tray-design.md`
- Modify: `docs/superpowers/specs/2026-07-31-adaptive-single-tray-icon-design.md`

**Interfaces:**
- Consumes: existing publish/install scripts and the installed watcher task.
- Produces: installed Two-icons default, disconnected executable/window identity, final hashes, and real connected-controller evidence.

- [ ] **Step 1: Update documentation**

Describe Two icons as the readable default at 96 DPI, Compact as a persisted optional mode, and the unconnected artwork as the static application identity. Preserve disconnect-to-exit, automatic theme selection, truthful charging percentage, and shared read-only HID statements.

- [ ] **Step 2: Run fresh verification**

Run:

```powershell
dotnet test DualSenseBatteryTray.sln -c Release
dotnet build DualSenseBatteryTray.sln -c Release --no-restore
git diff --check
```

Expected: every test passes, build has zero warnings/errors, and whitespace check is clean.

- [ ] **Step 3: Publish and install the current branch**

Publish self-contained single-file win-x64 App and Watcher to a fresh artifact directory. Stop only installed App/Watcher processes whose resolved executable paths are under `%LOCALAPPDATA%\Programs\DualSenseBatteryTray`, run `scripts/install.ps1`, and verify exactly one watcher remains. Record SHA-256 for both installed executables and confirm published/installed hashes match.

- [ ] **Step 4: Verify with the currently connected controller**

Confirm exactly one App starts and its status window reports USB, the measured percentage, and the actual charging state. With no valid preference file, verify two visible notification icons are created. Confirm the percentage icon represents the same measured value and includes `%`; switch Two icons → Compact → Two icons through the tray menu and confirm the App PID/HID session remains stable.

- [ ] **Step 5: Verify static and connected identities remain separate**

Inspect the installed EXE associated icon and WPF window icon: both must show an unlit controller without blue. Inspect the live controller tray icon: it must retain the blue connected touchpad. If Computer Use cannot capture the taskbar on this machine, record the exact capture error and request one user screenshot rather than fabricating visual evidence.

- [ ] **Step 6: Verify physical lifecycle with user assistance**

Ask the user to unplug once. Confirm App count becomes zero and all tray UI disappears while watcher count stays one. Ask the user to reconnect. Confirm exactly one App returns and the persisted Two-icons layout remains. Do not simulate USB removal.

- [ ] **Step 7: Commit documentation**

```powershell
git add README.md docs/superpowers/specs/2026-07-19-dualsense-battery-tray-design.md docs/superpowers/specs/2026-07-31-adaptive-single-tray-icon-design.md
git commit -m "docs: finalize readable tray identity"
```
