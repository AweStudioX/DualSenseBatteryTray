using DualSenseBatteryTray.Core.Devices;
using DualSenseBatteryTray.Hid.Native;

namespace DualSenseBatteryTray.Hid;

public sealed class HidDeviceEnumerator : IControllerPresence
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
                && productId == identity.ProductId)
            {
                matchingPaths.Add(path);
            }
        }

        return matchingPaths;
    }

    public bool IsPresent(ControllerIdentity identity) => FindPaths(identity).Any();
}
