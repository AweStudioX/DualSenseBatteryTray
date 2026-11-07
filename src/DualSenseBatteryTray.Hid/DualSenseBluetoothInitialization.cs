namespace DualSenseBatteryTray.Hid;

internal static class DualSenseBluetoothInitialization
{
    internal static bool Initialize(int inputLength, int featureLength, Func<int, bool> requestCalibration)
    {
        return inputLength != DualSenseInputReport.BluetoothLength
            || requestCalibration(Math.Max(41, featureLength));
    }
}
