# Single Tray Icon and Floating Battery Window Design

## Goal

Replace the two-slot controller-plus-percentage notification-area layout with one readable connected DualSense icon and provide an optional, persistent floating battery window for at-a-glance battery information without taking focus or controller input from games.

## Relationship to Earlier Designs

This design supersedes the tray-layout portions of:

- `2026-07-31-adaptive-single-tray-icon-design.md`;
- `2026-08-01-readable-tray-and-disconnected-app-icon-design.md`;
- `2026-08-10-small-icon-readability-design.md` where it requires a separate percentage notification icon.

The approved native-size refined DualSense glyph, the disconnected application/taskbar identity, and the dedicated window-title small icon remain valid and must be preserved.

The existing percentage and Compact renderers remain in source as rollback paths for this implementation, but production UI no longer selects them. Removing those renderers is explicitly outside this implementation and requires a later reviewed change after installed acceptance.

## Notification-Area Behavior

The application owns exactly one visible `NotifyIcon` while a supported USB DualSense is connected.

- The icon is the theme-aware connected refined DualSense glyph.
- A blue touchpad indicates the connected state.
- The independent percentage icon is not created.
- The Compact/Two-icons layout submenu is removed.
- Existing persisted tray-layout values are ignored but not deleted, so rollback does not destroy the user's previous choice.
- The existing native tooltip remains and includes connection type, measured percentage, and charge state, for example `DualSense USB · 35% · Discharging`.
- A left click opens the existing full information window.
- The context menu includes one checked `Show floating window` command.
- Disconnecting the controller removes the tray icon, closes the floating and information windows, and exits the App while the Watcher continues waiting.

The tray icon itself does not encode battery percentage. The floating window and native tooltip carry the value, avoiding unreadable digits in a 16-pixel notification slot.

## Floating Window Appearance

The floating window is a compact WPF surface sized 168×68 device-independent pixels:

- a 32-pixel refined DualSense icon on the left;
- a large battery percentage in the primary text position;
- a smaller textual state such as `Charging`, `On battery`, `Full`, or `Unknown`;
- a lightning symbol paired with the charging text, so state never depends on color alone.

The surface follows the Windows light/dark application theme. Its background uses 94% opacity, rounded corners, and a restrained shadow so values remain readable over complex wallpaper. The battery value remains the dominant element at 100%, unknown, and two-digit values without resizing the window.

Battery cycle count is excluded because no verified DualSense HID field exposes it. Firmware, hardware model, serial number, and battery barcode are also excluded from the compact window. They may be considered later for the full information window through a separately reviewed, read-only feature-report design.

## Interaction and Focus Contract

The floating window is enabled by default on a fresh install. The `Show floating window` tray command persists the user's enabled/disabled preference.

When enabled:

- connecting the controller automatically shows the window unless a fullscreen foreground application is active;
- the window can be dragged from any non-interactive portion of its surface;
- the final position is persisted after a successful drag;
- a click that does not cross the Windows drag threshold opens the full information window;
- the window does not appear in the taskbar or Alt+Tab.

The window is created with `ShowInTaskbar = false`, `ShowActivated = false`, and Win32 `WS_EX_NOACTIVATE`. It does not register hotkeys, keyboard hooks, controller handlers, or global mouse hooks. The click/drag implementation consumes only mouse events directed at this window. The existing shared, read-only HID handle remains unchanged, so games retain controller access.

The persisted enabled preference is authoritative across reconnects. A fullscreen transition hides the window temporarily and never changes that preference.

## Visibility State Machine

`FloatingWindowCoordinator` is the sole owner of show/hide decisions:

```text
actual visibility = controller connected
                 && user preference enabled
                 && no fullscreen foreground window
```

The App only exists while a supported controller is connected, so controller connection is normally implicit in the coordinator lifetime. It remains an explicit input in tests and state transitions to prevent a future lifecycle change from showing stale UI.

Events are handled as follows:

- battery state update: update content without activating or repositioning the window;
- user disables: hide immediately and persist disabled;
- user enables: persist enabled and show immediately unless fullscreen is active;
- enter fullscreen: hide without changing the persisted preference;
- leave fullscreen: restore only when the persisted preference is enabled;
- controller disconnect or App shutdown: close and dispose the window and monitor.

## Fullscreen Detection

`FullscreenWindowMonitor` polls the foreground window every 750 milliseconds. This rate is responsive enough for UI hiding while avoiding a high-frequency background loop.

The Win32 probe reads the foreground HWND, visibility/minimized state, owning process, window bounds, and monitor bounds. It reports fullscreen when a visible, non-minimized foreign foreground window covers its monitor bounds within a small pixel tolerance. It excludes:

- the desktop and shell/taskbar windows;
- this application's process and floating/information windows;
- zero-sized or invalid rectangles.

Coverage of the monitor bounds detects exclusive and borderless fullscreen applications. A normal maximized window that leaves the taskbar/work area visible is not fullscreen.

Probe errors are logged and treated as `not fullscreen` so an API failure cannot leave the battery window permanently hidden. The monitor performs read-only Win32 queries and never injects, opens, or modifies the foreground process.

## Position and Multi-Monitor Behavior

`FloatingWindowPreferenceStore` persists:

- `Enabled` as a Boolean;
- the monitor device name;
- the last window left/top coordinates in WPF device-independent pixels.

The store uses a small per-user JSON file under the application's existing LocalAppData settings directory. Missing, invalid, truncated, or unsupported values fall back to `Enabled = true` and no saved position.

On show and after display/DPI changes, `FloatingWindowPlacement` validates the saved rectangle against current monitor work areas:

- a valid saved monitor and visible rectangle are reused;
- an unavailable monitor or fully off-screen rectangle is moved to the primary work area's bottom-right corner with a safe taskbar margin;
- partially off-screen rectangles are clamped into the nearest work area;
- the window never intentionally covers the taskbar.

Coordinates are converted at the Win32/WPF boundary so persisted placement remains stable across mixed-DPI monitors.

## Components and Responsibilities

### `TrayApplicationContext`

- owns exactly one `NotifyIcon` and its one connected controller HICON;
- updates its tooltip from `BatteryState`;
- handles left-click opening of the information window;
- exposes the checked floating-window menu command;
- preserves existing icon assignment rollback and HICON disposal behavior.

It no longer owns tray-layout mode selection or renders a percentage slot.

### `FloatingBatteryWindow`

- owns only WPF presentation, click-versus-drag handling, and no-activate window styles;
- binds to `FloatingBatteryViewModel`;
- reports drag completion and click activation through narrow events or callbacks.

It does not read HID, persist settings, or inspect foreground applications.

### `FloatingBatteryViewModel`

- maps `BatteryState` into percentage, charge-state text, and charging-symbol visibility;
- exposes theme-independent semantic values;
- treats unknown percentage and state explicitly.

### `FloatingWindowPreferenceStore`

- loads and saves enabled state and placement;
- validates serialized shape/version;
- fails safely without preventing the App from running.

### `FullscreenWindowMonitor`

- polls a testable `IForegroundWindowProbe`;
- emits state changes only when fullscreen state changes;
- owns and disposes its timer.

### `FloatingWindowCoordinator`

- receives battery, preference, fullscreen, placement, and shutdown events;
- owns the `FloatingBatteryWindow` lifetime;
- applies the visibility state machine;
- ensures programmatic hide/show never activates the window.

## Data Flow

```text
shared read-only HID input
        ↓
BatteryState
   ├──→ TrayApplicationContext → one connected controller icon + native tooltip
   ├──→ MainWindowViewModel    → full information window
   └──→ FloatingWindowCoordinator → FloatingBatteryViewModel → floating window

Foreground Win32 probe → FullscreenWindowMonitor ────────────┘
Preference store ─────────────────────────────────────────────┘
```

No component downstream of `BatteryState` can write to the controller.

## Failure Handling and Rollback

- Preference file missing or corrupt: enable the floating window and use default placement.
- Preference save failure: keep the current live UI state and position; log the bounded error.
- Fullscreen probe failure: retain/show according to the user's enabled preference; log without repeated notification spam.
- Floating window construction or style application failure: keep tray monitoring and the full information window functional.
- Tray icon assignment failure: restore the previous assigned icon, tooltip, and visibility through the existing reconciliation rollback.
- Theme update failure: retain the previous usable icon/window presentation.
- Shutdown: unsubscribe events, stop timers, hide/dispose the `NotifyIcon`, dispose HICONs, and close WPF windows once.

The existing Compact and percentage renderer code provides a source-level rollback path but is unreachable from the released UI under this design.

## Testing

Automated tests verify:

- all initial, update, theme-change, and rollback paths own exactly one visible `NotifyIcon`;
- only the connected refined controller renderer supplies that icon;
- no layout submenu or percentage HICON is created;
- tooltip text remains truthful for charging, discharging, full, flat, and unknown states;
- floating enabled state and DIP placement round-trip;
- missing/corrupt settings fall back safely;
- the visibility state machine covers every combination of connected, enabled, and fullscreen inputs;
- user-hidden state is not overwritten by fullscreen enter/leave;
- battery updates do not activate or reposition the floating window;
- fullscreen monitor filters own/shell/minimized windows and emits only transitions;
- failed foreground probes choose the safe visible behavior;
- placement restores, clamps, and falls back across monitor removal and mixed DPI;
- click and drag paths are mutually exclusive at the system drag threshold;
- window styles exclude taskbar/Alt+Tab and apply `WS_EX_NOACTIVATE`;
- construction, save, icon assignment, and disposal failures preserve the documented fallback;
- existing shared read-only HID contract and disconnect-to-exit tests remain unchanged.

Fresh Release tests/build, privacy scanning, publish/install hashes, and process counts are required before completion.

## Installed Acceptance Criteria

- A connected controller creates one notification-area hand-controller icon and no percentage icon.
- The native tooltip contains the current measured percentage and charging state.
- The floating window is readable on light and dark desktop backgrounds at 100%, 125%, and 150% scaling.
- Dragging persists a visible position across App restart and controller reconnect.
- Disabling through the tray persists across reconnect; re-enabling restores the window when not fullscreen.
- A normal maximized desktop window does not hide the floating window.
- exclusive and borderless fullscreen applications hide it and restore it afterward when enabled.
- the floating window never appears in Alt+Tab or the taskbar and does not take keyboard or controller focus.
- clicking opens the full information window; dragging does not.
- disconnecting the controller removes all App UI while leaving one Watcher process.
- the installed taskbar/executable icon and dedicated title-bar small icon remain the previously approved disconnected artwork.
