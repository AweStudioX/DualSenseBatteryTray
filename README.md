# DualSense Battery Tray

- A supported USB DualSense uses the readable `Two icons` layout by default at 96 DPI: a connected controller icon followed by a separate battery value that includes `%`.
- `Compact` remains an optional persisted layout with one controller-and-number icon; it omits `%` inside the icon while hover text and the status window retain the complete percentage.
- Connected tray artwork automatically adapts its outline and number contrast when the Windows system theme changes. Charging never replaces the measured percentage with a fabricated value.
- `Tray layout` in the right-click menu switches live between `Two icons` and `Compact`; the successful choice is remembered across reconnects without restarting the HID session.
- The executable and status window use a separate static, unconnected controller identity with an unlit touchpad and no blue connection indicator.
- Clicking any active tray icon opens the status window; right-clicking opens Refresh, listener, tray-layout, About, and Exit commands.
- Disconnecting the controller closes the tray application and removes its icon or icons; the hidden watcher remains available for the next connection.
- HID access is shared and read-only: the application reads input reports and never sends output or feature reports.
- Controller artwork is adapted from Microsoft Fluent UI System Icons under MIT; see `LICENSES/fluentui-system-icons-MIT.txt`.
