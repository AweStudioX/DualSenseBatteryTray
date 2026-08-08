# DualSense Battery Tray Design

## Goal

Build a small Windows x64 application that starts when the currently detected USB DualSense controller is connected, displays its battery state in the notification area and compact status window, warns at low battery levels, and exits when the controller is removed. It must observe the controller without interfering with games or controller-management software.

## Scope

The first release supports only the standard DualSense controller currently detected on this machine:

- USB HID only
- Sony vendor ID `0x054C`
- Product ID `0x0CE6`
- Windows x64
- Battery level and charging-state display
- Low-battery notifications at 20% and 10%

The first release does not support Bluetooth, DualSense Edge, DualShock 4, virtual controllers, input remapping, calibration, lighting, vibration, adaptive triggers, or firmware operations.

## Safety and Coexistence Requirements

The application opens the matching HID collection with shared read access. It reads input reports only and never sends output or feature reports. It does not install a driver, create a virtual controller, register input mappings, hide a physical controller, or consume input through a global hook.

If another program hides or exclusively opens the physical controller, the application reports that battery data is unavailable and exits. It does not repeatedly attempt to take access. A failure to display battery status must not change the controller path used by the game.

## Technology

Use .NET 8 with WPF for the compact status window, while `System.Windows.Forms.NotifyIcon` provides the notification-area integration and low-battery notifications. Use a narrow Win32 HID wrapper that opens the device with read access and explicit `FILE_SHARE_READ | FILE_SHARE_WRITE` sharing flags. The application does not request write access.

Publish for Windows x64. Distribution consists of self-contained single-file tray and watcher executables plus a small setup/uninstall mechanism for registering the sign-in watcher task.

The battery parsing behavior is derived from the MIT-licensed `dualshock-tools/dualshock-tools.github.io` project. The controller artwork is the MIT-licensed Microsoft Fluent UI System Icons `Games 16 Filled` asset. Preserve both required license attributions when code or artwork is adapted.

## Architecture

### DeviceArrivalWatcher

A lightweight, hidden watcher is started for the current user at Windows sign-in by a Scheduled Task. It listens for Windows device-arrival and device-removal notifications without opening or reading the controller. When `054C:0CE6` arrives, it enumerates present HID devices and starts the main application only if the matching controller is present. The watcher checks the single-instance mechanism so repeated notifications do not create duplicate tray processes.

The watcher remains running after the tray application exits so a later insertion can start it again. It has no window or tray icon, performs no polling while the system is idle, and exits when the user signs out or disables “Listen after Windows sign-in.” This avoids relying on inconsistent Plug and Play event-log IDs across Windows versions.

### SingleInstanceGuard

A named mutex scoped to the current user ensures that only one tray application instance is active. A second invocation exits without changing the first instance's HID connection.

### DualSenseBatteryReader

This component:

1. Enumerates the HID collection matching vendor `0x054C` and product `0x0CE6`.
2. Opens it using shared read-only access.
3. Reads input reports asynchronously.
4. Validates report length before accessing battery data.
5. Parses byte offset 52 without modifying the report or device.
6. Emits normalized battery-state changes.

For the battery byte, the low nibble is the charge value and the high nibble is the status. The normalized behavior matches the upstream project:

- Status `0`: on battery; percentage is `min(charge * 10 + 5, 100)`.
- Status `1`: charging with cable connected; percentage uses the same formula.
- Status `2`: fully charged with cable connected; percentage is 100%.
- Status `15`: flat/cable-connected state; percentage is 0%.
- Other status values: unknown/error state; no fabricated percentage is displayed.

### TrayApplication

At the verified 96-DPI taskbar size, the notification area uses `Two icons` by default. The first `NotifyIcon` shows the theme-aware connected controller with its blue touchpad; the second dedicates the full slot to the measured battery number plus a smaller `%` glyph. The separate percentage slot is the readable default because digits fitted inside a 16×16 controller touchpad are not reliably legible. The shared tooltip and status window show the same measured percentage and charging state.

The compact renderer rasterizes combined frames at 16, 24, 32, and 48 pixels. The controller body stays Fluent blue, charging adds a small yellow lightning mark without replacing the measured value, and the outer outline plus battery-number contrast automatically follow the Windows system taskbar theme. A Windows system-theme change refreshes the live icon without reopening the controller or requesting a HID refresh.

The `Tray layout` context submenu switches live between `Two icons` and `Compact`, and the successfully applied choice is persisted for later process starts and controller reconnects. Missing, unreadable, or invalid preference data falls back to `Two icons`; an explicitly saved `Compact` choice remains authoritative. Compact combines the controller and a percent-free number in one slot for experimentation. In `Two icons`, values such as `55%` maximize the numeric portion of the dedicated frame, while `100%` uses a fitted three-digit layout. Both layouts supply 16-, 24-, 32-, and 48-pixel frames and automatically adapt their contrast to the Windows system theme without reopening the HID session.

Every active icon shares the same tooltip, left-click status window, context menu, visibility lifecycle, and failure rollback behavior. A layout change first prepares the complete replacement icon set and leaves the prior live layout and stored preference intact if assignment fails. In `Two icons` mode, Windows controls notification-area ordering and overflow placement, so adjacency is best effort; the application creates the controller icon first and percentage icon second.

The executable, conventional WPF window, and taskbar button use the same static unconnected controller identity: a white body with dark outline, unlit touchpad, no blue connection indicator, and no battery text. This static application identity remains separate from the live connected tray controller. The program does not patch or extend Windows Explorer.

Clicking any active tray icon opens a compact status window containing only:

- Device name
- USB connection type
- Battery percentage
- Charging state

The tray context menu contains Refresh, Listen after Windows sign-in, Tray layout (`Compact` or `Two icons`), About, and Exit. The status window closing action hides the window rather than terminating the application while the controller remains attached.

### BatteryNotificationService

While discharging, crossing down to 20% sends one Windows notification. Crossing down to 10% sends one additional notification. Repeated reports at the same level do not repeat notifications. Connecting power or rising above a threshold resets the corresponding notification so it can fire during a later discharge cycle.

### DeviceLifecycleMonitor

The monitor responds to removal of the active matching controller. It cancels pending reads, closes the HID handle, removes every active tray icon, disposes application resources, and exits. It does not remain as a disconnected tray process.

## Data Flow

1. At user sign-in, the Scheduled Task starts `DeviceArrivalWatcher`.
2. Windows delivers a device-arrival notification to the watcher.
3. The watcher confirms that `054C:0CE6` is present and no application instance exists.
4. The tray application starts and opens the HID collection with shared read-only access.
5. Input reports flow to `DualSenseBatteryReader`.
6. Normalized state changes update both default Two-icons entries or the optional Compact icon, their shared tooltip, the status window, and the notification service.
7. Device removal triggers orderly shutdown and process exit.

## Error Handling

- No matching device: exit silently when launched by the helper; show a concise message when launched manually.
- Shared HID open denied: notify once that battery status cannot be read, log the error, and exit without retrying aggressively.
- Short or malformed input report: discard that report. Five consecutive read failures close the device and exit; a valid report resets the count.
- Unknown battery status: display `?` and `状态未知`; do not guess a percentage or send low-battery alerts.
- Scheduled Task installation failure: keep manual launch functional and explain that connect-to-start is unavailable.
- Unexpected application failure: remove every active tray icon where possible, release the HID handle, append one local diagnostic entry, and exit.

Logs remain local, contain no button/axis input values, are capped at 1 MiB with one rotated backup, and are never uploaded.

## Setup and Removal

Setup installs the application for the current user and registers a sign-in Scheduled Task that starts the hidden device watcher. Administrative rights should be avoided if current-user task registration is sufficient. Uninstall stops the watcher, removes the Scheduled Task, and removes installed application files. It must not remove controller drivers or modify Steam, DS4Windows, HidHide, or game settings.

The “Listen after Windows sign-in” menu item enables or disables the Scheduled Task and starts or stops the watcher accordingly. It does not start the full tray application at every sign-in.

## Testing

### Automated Tests

- Parse discharge, charging, full, flat, and unknown status bytes at offset 52.
- Reject input reports too short to contain offset 52.
- Verify percentage clamping and normalized states.
- Verify 20% and 10% threshold crossing, deduplication, and reset behavior.
- Verify device filtering for `054C:0CE6` and rejection of other devices.
- Verify single-instance behavior.
- Verify Two icons is the fallback default for a missing, unreadable, or invalid stored preference, valid Compact/Two-icons choices round-trip, and a successful live layout change is persisted without requesting a HID refresh.
- Verify every active notification icon receives synchronized state, tooltip, click, menu, visibility, replacement, rollback, and disposal updates.
- Verify Compact combines the Fluent controller and a percent-free number at 16, 24, 32, and 48 pixels; `10`, `55`, `100`, and `?` fit inside the touchpad without clipping.
- Verify Two icons renders the complete Fluent controller and white percentage independently at 16, 24, 32, and 48 pixels; `10%`, `55%`, `100%`, and `?%` fit without clipping.
- Verify both layouts refresh for light and dark Windows system themes and theme callbacks are marshalled to the tray application's UI thread.

### Hardware and Integration Tests

- Sign in and confirm one hidden watcher starts without launching the tray application when the controller is absent.
- Insert the USB DualSense and confirm exactly one tray application instance starts.
- Insert unrelated USB devices and confirm the main application does not start.
- Remove and reconnect the controller repeatedly and confirm clean exit and restart.
- Run a game using native controller input while the application is active; verify buttons, sticks, vibration, audio, and latency remain unaffected.
- Repeat coexistence testing with Steam Input enabled.
- Test behavior when a controller-hiding or exclusive-access tool makes the physical HID unavailable.
- Verify the default Two-icons layout at the actual 96-DPI taskbar size: the controller remains visibly connected and the separate measured percentage includes `%`; confirm the shared tooltip, status window, charging state, and notifications use the same truthful report.
- Switch Windows system themes in both directions and confirm the active tray icon or icons refresh without affecting controller input.
- Switch live from Two icons to Compact and back without changing the App PID or HID session; verify every icon is usable, adjacency is best effort under Windows, and the persisted choice survives disconnect and reconnect.
- Verify the executable-associated icon and WPF window icon use the unconnected, unlit artwork while the live tray controller retains its blue connected touchpad.
- Disable or remove the Scheduled Task and confirm manual launch still works.

## Acceptance Criteria

- Connecting the specified USB DualSense automatically starts one instance.
- Disconnecting it closes the application and removes all UI artifacts.
- The hidden watcher remains idle without polling after disconnect so a later insertion can restart the application.
- At 96 DPI, the notification area defaults to Two icons: a connected controller followed by a readable, theme-aware measured battery value including `%`; charging never fabricates a different percentage.
- The tray menu can switch live to persistent Compact and back without reopening the HID session; failure to apply a new layout leaves the prior live layout and preference intact.
- The executable and WPF window use the static unconnected, unlit controller identity, while live connected tray artwork retains the blue touchpad.
- Notifications occur once at 20% and once at 10% per discharge cycle.
- The application performs no HID writes and creates no virtual input device.
- Games retain normal controller input while the application runs.
- Failures degrade to missing battery display rather than controller disruption.
