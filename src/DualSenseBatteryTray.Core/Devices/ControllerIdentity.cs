namespace DualSenseBatteryTray.Core.Devices;

public sealed record ControllerIdentity(ushort VendorId, ushort ProductId, string DisplayName)
{
    public static ControllerIdentity UsbDualSense { get; } =
        new(0x054C, 0x0CE6, "DualSense Wireless Controller");
}
