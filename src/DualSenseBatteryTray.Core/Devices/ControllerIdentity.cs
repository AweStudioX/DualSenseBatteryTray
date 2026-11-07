namespace DualSenseBatteryTray.Core.Devices;

public sealed record ControllerIdentity(ushort VendorId, ushort ProductId, string DisplayName)
{
    public static ControllerIdentity DualSense { get; } =
        new(0x054C, 0x0CE6, "DualSense Wireless Controller");

    // Retained for existing callers; VID/PID identify both USB and native Bluetooth HID.
    public static ControllerIdentity UsbDualSense => DualSense;
}
