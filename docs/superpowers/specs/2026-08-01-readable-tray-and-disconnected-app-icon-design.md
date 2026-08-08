# Readable Tray and Disconnected App Icon Design

## Goal

Make the live battery value readable on a real 96-DPI Windows taskbar and replace the static application identity icon with the supplied unconnected DualSense artwork, without changing USB detection, read-only HID access, or disconnect-to-exit behavior.

## Evidence and Root Cause

The installed build was verified with a connected `VID_054C&PID_0CE6` controller. Windows reported a 96-DPI system and a native small-icon size of 16×16 pixels. The application reported a real 45% charging state and was running in Compact mode because no layout preference file existed.

Compact mode places two battery digits inside the controller touchpad. At 16×16, the touchpad provides only a few vertical pixels for text. The resulting `45` cannot be read reliably even though automated clipping and contrast tests pass. This is a physical information-density limit of one standard notification-area slot, not a font-size or antialiasing defect.

## Tray Layout

`TwoIcons` becomes the default when no valid per-user preference exists:

1. The left notification slot shows the theme-aware connected DualSense controller with the blue touchpad.
2. The right notification slot shows the measured battery number at maximum readable size plus a smaller `%` glyph.

The percentage foreground follows the taskbar theme: light foreground on a dark taskbar and dark foreground on a light taskbar. Charging never changes the measured value to 100. The tooltip and status window continue to show the full value and charging state.

`Compact` remains available from the existing `Tray layout` menu for experimentation. Explicitly saved user choices remain authoritative; only missing, unreadable, or invalid preference files fall back to `TwoIcons`. Layout switching remains live, persisted after successful reconciliation, and must not restart or reopen the HID session.

Windows may independently place either notification icon in the overflow area. The application creates the controller icon first and the percentage icon second, but adjacency remains best effort under Windows control.

## Static Application Icon

The application/window/executable identity uses a disconnected DualSense icon derived from the user-provided light unconnected `Generated image 1.png` reference.

The production asset has:

- transparent background;
- white controller body with a strong dark outline;
- unlit, non-blue touchpad;
- simplified details that remain recognizable at 16, 24, 32, 48, and 256 pixels;
- no battery digits and no connection indicator.

The image-generation/edit workflow creates a clean project-owned PNG/ICO derivative. Runtime code never references the external `D:` path. The multi-frame `.ico` is configured as the application executable icon so Explorer and shortcuts use it. `MainWindow.Icon` uses the same disconnected artwork. This static application identity does not switch with the taskbar theme; its light body plus dark outline is designed to remain legible on both light and dark surfaces.

The connected tray icon remains separate and continues to show the blue touchpad. Replacing the application icon must not change live tray artwork.

## Components and Changes

- `TrayLayoutPreferenceStore`: change only the missing/invalid/error fallback from `Compact` to `TwoIcons`; preserve valid saved choices.
- `BatteryIconRenderer`: replace the generated Fluent static application icon path with the processed disconnected multi-frame asset; retain all connected Compact and Two-icons renderers.
- Application project metadata: embed/configure the disconnected `.ico` as the executable icon.
- Documentation: describe Two-icons as the readable default and Compact as optional.

No HID, watcher, notification threshold, reader, device-removal, or charging-parser code changes are permitted.

## Failure Handling

- Missing or corrupt layout preference: use `TwoIcons` without failing startup.
- Failure to save a newly selected layout: keep the already usable live layout and expose the existing failure behavior; do not destroy current icons.
- Failure to load the static icon resource during development/tests: fail the renderer test/build rather than silently substituting connected artwork.
- Tray reconciliation failure: retain the previous usable icon set and dispose all unused HICONs, matching the existing rollback contract.

## Testing

Automated tests verify:

- missing, corrupt, and undefined layout values default to `TwoIcons`;
- valid persisted `Compact` and `TwoIcons` values round-trip unchanged;
- default construction creates two visible tray icons with synchronized tooltip/menu/click behavior;
- the percentage renderer retains visible numeric and `%` glyphs at 16, 24, 32, and 48 pixels for `10`, `45`, `55`, `100`, and unknown values;
- the disconnected static app icon contains no connected-blue pixels, has transparent corners, strong light/dark contrast, and all required ICO frames;
- the built executable contains the configured application icon;
- connected tray renderers remain blue and unchanged;
- charging at 45% renders 45%, not 100%.

Fresh Release tests/build, publish/install hashes, and real hardware verification follow implementation. At 96 DPI, the installed default must visibly use two notification slots; the right slot must show the current value including `%`. The tray menu must switch Two icons ⇄ Compact without interrupting the controller. Physical disconnection must remove every tray icon and exit the app while the watcher remains.

## Acceptance Criteria

- A fresh or invalid layout preference starts in Two-icons mode.
- The actual taskbar shows the controller icon followed by a separately readable percentage.
- Compact remains a persisted optional layout.
- Explorer, shortcuts, and the WPF window use the unconnected, unlit controller icon.
- The live tray controller uses the connected blue-touchpad artwork.
- Current battery and charging values remain truthful.
- Games retain normal controller access because HID behavior is unchanged.
