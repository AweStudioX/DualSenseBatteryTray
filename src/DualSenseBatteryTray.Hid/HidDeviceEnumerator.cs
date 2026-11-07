using DualSenseBatteryTray.Core.Devices;
using DualSenseBatteryTray.Hid.Native;

namespace DualSenseBatteryTray.Hid;

public sealed class HidDeviceEnumerator : IControllerTransportPresence
{
    public IReadOnlyList<string> FindPaths(ControllerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var matchingPaths = new List<string>();
        var hidClassGuid = HidNative.GetHidClassGuid();
        foreach (var path in SetupApiNative.GetPresentDevicePaths(hidClassGuid))
        {
            using var handle = HidNative.OpenForAttributes(path);
            if (handle.IsInvalid)
                continue;

            if (HidNative.TryGetAttributes(handle, out var vendorId, out var productId)
                && vendorId == identity.VendorId
                && productId == identity.ProductId
                && IsSupportedInputCollection(handle))
            {
                matchingPaths.Add(path);
            }
        }

        return matchingPaths;
    }

    public bool IsPresent(ControllerIdentity identity) => FindPaths(identity).Any();

    private static bool IsSupportedInputCollection(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        try
        {
            return HidNative.GetInputReportByteLength(handle) is
                DualSenseInputReport.UsbLength or DualSenseInputReport.BluetoothLength;
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public bool IsBluetooth(ControllerIdentity identity)
    {
        var path = FindPaths(identity).FirstOrDefault();
        if (path is null)
            return false;
        using var handle = HidNative.OpenForAttributes(path);
        return !handle.IsInvalid
            && HidNative.GetInputReportByteLength(handle) == DualSenseInputReport.BluetoothLength;
    }
}
