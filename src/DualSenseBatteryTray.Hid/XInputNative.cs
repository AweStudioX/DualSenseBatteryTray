using System.Runtime.InteropServices;

namespace DualSenseBatteryTray.Hid;

internal readonly record struct XInputBattery(byte Type, byte Level);

internal interface IXInputApi
{
    bool TryGetBattery(int userIndex, out XInputBattery battery);
}

internal sealed class WindowsXInputApi : IXInputApi
{
    internal const byte BatteryTypeDisconnected = 0x00;
    internal const byte BatteryTypeWired = 0x01;
    internal const byte BatteryTypeAlkaline = 0x02;
    internal const byte BatteryTypeNimh = 0x03;
    internal const byte BatteryTypeUnknown = 0xFF;
    internal const byte BatteryLevelEmpty = 0x00;
    internal const byte BatteryLevelLow = 0x01;
    internal const byte BatteryLevelMedium = 0x02;
    internal const byte BatteryLevelFull = 0x03;
    private const uint ErrorSuccess = 0;
    private const byte DeviceTypeGamepad = 0;

    public bool TryGetBattery(int userIndex, out XInputBattery battery)
    {
        if ((uint)userIndex >= 4)
            throw new ArgumentOutOfRangeException(nameof(userIndex));

        var result = XInputGetBatteryInformation(
            (uint)userIndex,
            DeviceTypeGamepad,
            out var nativeBattery);
        battery = new XInputBattery(nativeBattery.BatteryType, nativeBattery.BatteryLevel);
        return result == ErrorSuccess
            && nativeBattery.BatteryType != BatteryTypeDisconnected;
    }

    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetBatteryInformation(
        uint dwUserIndex,
        byte devType,
        out NativeBatteryInformation batteryInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }
}
