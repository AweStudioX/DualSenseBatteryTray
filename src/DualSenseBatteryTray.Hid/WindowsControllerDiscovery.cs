using Microsoft.Win32;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid;

public sealed record ActiveController(
    ControllerProfile Profile,
    Func<IBatteryReportSource> CreateBatteryReader);

internal interface IFlydigiDeviceDetector
{
    bool IsFlydigiReceiverPresent();
}

internal sealed class RegistryFlydigiDeviceDetector : IFlydigiDeviceDetector
{
    private const string XInputEmulationKey =
        @"SYSTEM\CurrentControlSet\Enum\USB\VID_045E&PID_028E";

    public bool IsFlydigiReceiverPresent()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(XInputEmulationKey);
            return key?.GetSubKeyNames().Any(name =>
                name.StartsWith("FLYDIGI_VADER", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("FLYDIGI_APEX", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}

public sealed class WindowsControllerDiscovery
{
    private readonly IXInputApi _xinput;
    private readonly IFlydigiDeviceDetector _flydigi;
    private readonly IFlydigiServiceClient _flydigiService;
    private readonly IControllerPresence _hid;

    public WindowsControllerDiscovery()
        : this(
            new WindowsXInputApi(),
            new RegistryFlydigiDeviceDetector(),
            new FlydigiServiceClient(),
            new HidDeviceEnumerator())
    {
    }

    internal WindowsControllerDiscovery(
        IXInputApi xinput,
        IFlydigiDeviceDetector flydigi,
        IFlydigiServiceClient flydigiService,
        IControllerPresence hid)
    {
        _xinput = xinput;
        _flydigi = flydigi;
        _flydigiService = flydigiService;
        _hid = hid;
    }

    public ActiveController? FindActiveController()
    {
        var xinputSlots = Enumerable.Range(0, 4)
            .Where(index => _xinput.TryGetBattery(index, out _))
            .ToArray();
        if (xinputSlots.Length == 1 && _flydigi.IsFlydigiReceiverPresent())
        {
            var slot = xinputSlots[0];
            var serviceDevice = _flydigiService
                .QueryControllerAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var profile = IsApex4(serviceDevice)
                ? ControllerProfile.FlydigiApex4Receiver
                : ControllerProfile.FlydigiXInputCompatibility;
            return new ActiveController(
                profile,
                () => new FlydigiBatteryReader(slot));
        }

        if (_hid.IsPresent(ControllerIdentity.UsbDualSense))
        {
            return new ActiveController(
                ControllerProfile.DualSenseUsb,
                () => new DualSenseHidReader());
        }

        if (xinputSlots.Length > 0)
        {
            var slot = xinputSlots[0];
            return new ActiveController(
                ControllerProfile.GenericXInput,
                () => new XInputBatteryReader(slot));
        }

        return null;
    }

    public bool IsAnySupportedControllerPresent() =>
        Enumerable.Range(0, 4).Any(index => _xinput.TryGetBattery(index, out _))
        || _hid.IsPresent(ControllerIdentity.UsbDualSense);

    private static bool IsApex4(FlydigiDeviceState? device) =>
        device is not null
        && (device.DeviceCode.Equals("k2", StringComparison.OrdinalIgnoreCase)
            || device.Name.Contains("APEX 4", StringComparison.OrdinalIgnoreCase));
}
