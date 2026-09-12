# Small Icon Readability Design

## Goal

Make the WPF window title icon and notification-area controller icon recognizable at native Windows small-icon sizes without changing the taskbar/executable artwork, battery percentage layout, HID access, watcher behavior, or connection semantics.

## Evidence and Root Cause

The current window icon renderer selects only the 256×256 frame from the disconnected application ICO. WPF then downsamples that detailed frame to the roughly 16-pixel title-bar slot. The static white controller body also loses contrast against a white title bar.

The notification-area controller renderer similarly downsamples a detailed 512×512 connected-controller PNG into native 16, 24, 32, and 48-pixel ICO frames. At the smallest native frame, including its roughly 16–20-pixel effective display under Windows scaling, the D-pad, four face buttons, two sticks, touchpad, and outline compete for too few pixels. Increasing stroke width would merge those details rather than recover them.

The taskbar/executable icon remains relatively readable because Windows displays it at a larger effective size. It must therefore remain visually unchanged.

## Approved Visual Direction

Use a dedicated, size-aware DualSense glyph instead of shrinking the large illustration for every surface.

At the smallest 16-pixel native frame, the glyph retains only:

- a refined DualSense outer contour;
- shoulder-button steps;
- the central waist and outward grips;
- a separate touchpad with a recognizable lower curve.

The small glyph intentionally omits the D-pad, face-button symbols, and analog sticks. These details were tested in the design preview and were not legible at native size.

The disconnected window version uses a white body with a strong dark outline and an unlit touchpad. The connected notification-area version uses a theme-aware high-contrast body and a blue touchpad. Color is not the sole connection indicator: the connected artwork is confined to the live tray surface, while the window and executable identity remain disconnected artwork.

At 24, 32, and 48 pixels, the renderer restores the fuller DualSense details already represented by the approved assets. Each frame is rendered intentionally for its native size rather than produced by shrinking a 256 or 512-pixel frame at runtime.

## Surface Responsibilities

### Window Title Bar

`MainWindow.Icon` receives a title-bar-specific small frame. The selected frame must preserve a dark exterior edge on a white title bar. This change must not replace the executable icon embedded by the project file.

### Notification Area

The controller slot in `TwoIcons` mode uses the size-aware connected glyph. The adjacent percentage slot remains unchanged: a large theme-aware value with a visible percent sign. `Compact` mode remains available and is outside this change.

### Taskbar and Executable Identity

The existing multi-frame disconnected ICO remains the executable/application identity. No new overlay, battery value, or connection state is added to the taskbar button.

## Components and Data Flow

- `BatteryIconRenderer` owns the native small-glyph geometry and chooses the correct renderer by requested frame size and tray theme.
- `MainWindow` requests the dedicated small window frame rather than the 256-pixel frame.
- `TrayApplicationContext` continues to request a controller icon and a separate percentage icon; its ownership, rollback, tooltip, menu, and disposal contracts do not change.
- Battery state continues to flow from the read-only shared HID reader to the existing renderers. No new HID writes or exclusive handles are introduced.

The renderer must keep a clear boundary between static disconnected identity and live connected state so a future asset change cannot accidentally make the window icon appear connected.

## Failure Handling and Rollback

- Invalid frame size or theme values continue to fail fast in renderer tests and internal APIs.
- Failure while replacing live notification icons keeps the previous usable icon set through the existing reconciliation rollback path.
- All newly created HICONs are disposed under the existing ownership rules.
- The previous detailed assets and Two-icons layout remain in the repository, allowing the small-glyph selection to be reverted without changing HID or shell architecture.

## Testing

Automated tests cover:

- native 16, 24, 32, and 48-pixel frames in the generated controller ICO;
- the dedicated window small frame and unchanged executable ICO frames;
- transparent corners and content bounds that do not touch the frame edge;
- a visible dark exterior edge for the disconnected glyph on a white title bar;
- a visible connected-blue touchpad for both light and dark taskbar themes;
- deliberate absence of micro-details in the 16-pixel frame;
- restored detail or approved source artwork at 24 pixels and above;
- unchanged percentage rendering, including the `%` glyph and `100%` fitting;
- unchanged charging values, layout switching, icon disposal, and rollback behavior.

Fresh Release tests and build must pass. Installed verification must inspect the real window title bar and notification area at the current Windows DPI, with the controller connected. Disconnecting the controller must still remove all tray icons and exit the application while leaving the watcher active.

## Acceptance Criteria

- The window title icon remains recognizable against a white title bar at native size.
- The notification-area controller remains recognizable at 16 pixels and visibly indicates connection through its blue touchpad.
- The controller glyph and percentage remain separate in the default Two-icons layout.
- The taskbar/executable icon looks unchanged from the current approved version.
- Small frames use intentional native artwork rather than automatic downscaling of large detailed images.
- Controller input remains available to games because shared read-only HID behavior is unchanged.
