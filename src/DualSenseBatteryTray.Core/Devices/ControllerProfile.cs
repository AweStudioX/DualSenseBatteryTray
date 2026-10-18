namespace DualSenseBatteryTray.Core.Devices;

public enum ControllerProtocol
{
    DualSenseHid,
    XInput,
}

public enum ControllerIconKind
{
    DualSense,
    FlydigiVader,
    GenericGamepad,
}

public sealed record ControllerProfile(
    string DisplayName,
    string ConnectionType,
    ControllerProtocol Protocol,
    ControllerIconKind IconKind,
    bool HasPrecisePercentage)
{
    public static ControllerProfile DualSenseUsb { get; } = new(
        "DualSense Wireless Controller",
        "USB",
        ControllerProtocol.DualSenseHid,
        ControllerIconKind.DualSense,
        HasPrecisePercentage: true);

    public static ControllerProfile FlydigiVader4Receiver { get; } = new(
        "Flydigi Vader 4 (八爪鱼 4)",
        "2.4 GHz receiver · XInput",
        ControllerProtocol.XInput,
        ControllerIconKind.FlydigiVader,
        HasPrecisePercentage: false);

    public static ControllerProfile FlydigiXInputCompatibility { get; } = new(
        "Flydigi XInput Controller",
        "2.4 GHz receiver · XInput",
        ControllerProtocol.XInput,
        ControllerIconKind.GenericGamepad,
        HasPrecisePercentage: false);

    public static ControllerProfile GenericXInput { get; } = new(
        "XInput Controller",
        "XInput",
        ControllerProtocol.XInput,
        ControllerIconKind.GenericGamepad,
        HasPrecisePercentage: false);
}
