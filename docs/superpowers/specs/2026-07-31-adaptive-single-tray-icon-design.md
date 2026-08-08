# Adaptive Single Tray Icon Design

## Goal

Retain the theme-aware single-slot DualSense rendering as an optional persisted layout while making the existing two-icon layout the readable default at the verified 96-DPI taskbar size. Keep connected tray artwork visually separate from the static unconnected application identity.

## Confirmed Behavior

- The main application starts only while the supported USB DualSense is present.
- Removing the controller removes every tray icon and exits the main application.
- The hidden device watcher remains responsible for starting the application on the next insertion.
- The tray artwork follows the Windows system/taskbar light or dark theme automatically and refreshes when that preference changes.
- A lit blue touchpad represents the connected state in live tray artwork. The unlit, non-blue artwork is used only as the static executable/window identity; it is not a disconnected tray state because the application exits on removal.

## Visual Layouts

### Compact layout (optional, persisted)

One square `NotifyIcon` contains a simplified DualSense outline and its current battery number inside the blue touchpad region:

- Light taskbar: dark controller outline, blue touchpad, light battery digits.
- Dark taskbar: light controller outline, blue touchpad, light battery digits.
- Known battery levels display `0` through `100` without a percent glyph.
- Unknown battery level displays `?`.
- The tooltip and compact window retain the complete percentage with `%` and charging state.
- Charging does not replace or fabricate the measured percentage. It remains available in the tooltip and compact window instead of adding another tiny overlay to the icon.

The supplied connected light and connected dark images are visual references. Production assets are cropped, background-free, contrast-normalized derivatives optimized independently for 16, 24, 32, and 48 pixels. Fine button details may be removed when they reduce small-size readability, but the DualSense silhouette and blue central touchpad must remain recognizable.

### Expanded layout (readable default)

The context menu exposes a `Tray layout` choice with `Compact` and `Two icons`. `Two icons` uses:

1. The same theme-aware connected controller artwork on the left.
2. A separate large theme-aware battery percentage on the right, including a smaller `%` glyph.

Switching layouts updates the live tray UI without restarting or changing the HID session. The selected layout is stored per user and restored next time; a missing, unreadable, or invalid preference falls back to Two icons, while a valid saved Compact choice remains authoritative. This preserves the one-slot experiment without sacrificing a readable fresh-install default.

## Theme Detection

Read the current Windows system theme from the current-user Personalize preference used by the taskbar. Missing or invalid values fall back to dark-taskbar artwork because a light foreground is the safer default on common Windows taskbars.

Subscribe to the Windows user-preference change notification while the tray application is alive. Re-read the setting on notification rather than trusting the event category. Marshal icon replacement to the UI thread, update all currently visible icons atomically where practical, and unsubscribe during disposal. Theme-detection failures must leave the last valid icon in place.

## Rendering Architecture

Introduce explicit, testable concepts rather than embedding theme and layout branches in `TrayApplicationContext`:

- `TrayTheme`: light-taskbar or dark-taskbar visual selection.
- `TrayLayoutMode`: compact or two-icons.
- A theme provider that reads and watches the Windows preference.
- A layout preference store with safe parsing and atomic replacement.
- A renderer that produces compact controller-plus-number frames and expanded controller/percentage frames at all native icon sizes.

`TrayApplicationContext` owns at most two `NotifyIcon` instances. It creates the second icon only for expanded layout, gives both the same tooltip/menu/click behavior, and disposes unused icons and HICONs during layout, state, or theme transitions. If a multi-step replacement fails, retain the previous usable icon set and dispose newly created handles.

The dedicated percentage renderer supplies the default expanded layout. The static application/window identity uses the project-owned disconnected DualSense artwork with an unlit touchpad and no connected-blue pixels; it is not reused by either live tray renderer.

## Asset Handling

Use the two connected reference images supplied by the user:

- `Generated image 1 (2).png` for a light-taskbar source.
- `Generated image 1 (5).png` for a dark-taskbar source.

One unlit image supplies the reference for the project-owned static executable/window identity, while connected images supply live tray references. Processed assets are committed under the application asset directory with transparent backgrounds and deterministic names. Runtime code must not depend on the original external `D:` paths.

## Testing

Automated tests cover:

- Theme preference parsing, fallback, change notification, and disposal.
- Compact rendering for `10`, `55`, `100`, and `?` at 16, 24, 32, and 48 pixels.
- Presence of the correct light/dark controller contrast and connected blue region.
- No clipping of numeric glyphs and no `%` glyph in compact mode.
- Expanded rendering retains a visible, larger number and smaller `%` glyph.
- Layout preference round-trip, valid Compact preservation, and Two-icons fallback for missing or invalid data.
- Runtime Compact/Two-icons transitions, synchronized tooltips/click/menu behavior, rollback, icon ownership, and disposal.
- Device removal still hides all visible icons and exits.
- Charging reports retain their measured percentage.

Hardware verification checks both taskbar themes at the machine's actual DPI, including `100%`, and confirms that the default Two-icons value includes `%`, charging preserves the measured percentage, and switching Two icons → Compact → Two icons does not interrupt controller input or reopen the HID device. The executable/window identity is verified as unlit and non-blue while the live tray controller remains blue.

## Acceptance Criteria

- The default tray UI occupies two notification-area slots at 96 DPI: a recognizable connected DualSense and a separate readable battery value including `%`; `100%` remains unclipped.
- Compact remains available as a persisted optional one-slot layout.
- Changing the Windows taskbar theme refreshes the artwork without restarting.
- The user can switch live between Two icons and Compact without restarting or reopening the HID session.
- The static executable/window icon is unconnected and unlit, while live connected tray artwork retains its blue touchpad.
- Disconnecting the controller removes the tray UI and exits the application.
- Battery monitoring remains shared, read-only, and non-interfering with games.
