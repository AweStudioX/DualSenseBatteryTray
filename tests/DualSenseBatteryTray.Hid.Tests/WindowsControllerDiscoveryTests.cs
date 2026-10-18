using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class WindowsControllerDiscoveryTests
{
    [Fact]
    public void A_single_XInput_slot_with_a_Flydigi_receiver_gets_the_Vader4_profile()
    {
        var discovery = new WindowsControllerDiscovery(
            new SlotApi(2),
            new FlydigiDetector(true),
            new ServiceClient(new FlydigiDeviceState("Vader 4 Pro", "f4", true, 4, 2)),
            new Presence(false));

        var controller = Assert.IsType<ActiveController>(discovery.FindActiveController());

        Assert.Equal(ControllerProfile.FlydigiVader4Receiver, controller.Profile);
        Assert.IsType<FlydigiBatteryReader>(controller.CreateBatteryReader());
    }

    [Fact]
    public void Multiple_XInput_slots_use_the_generic_profile_to_avoid_a_wrong_device_match()
    {
        var discovery = new WindowsControllerDiscovery(
            new SlotApi(0, 1),
            new FlydigiDetector(true),
            new ServiceClient(null),
            new Presence(false));

        Assert.Equal(
            ControllerProfile.GenericXInput,
            discovery.FindActiveController()!.Profile);
    }

    [Fact]
    public void Flydigi_compatibility_name_without_service_identity_uses_a_generic_icon_profile()
    {
        var discovery = new WindowsControllerDiscovery(
            new SlotApi(0),
            new FlydigiDetector(true),
            new ServiceClient(null),
            new Presence(false));

        Assert.Equal(
            ControllerProfile.FlydigiXInputCompatibility,
            discovery.FindActiveController()!.Profile);
    }

    [Fact]
    public void DualSense_is_used_when_no_XInput_controller_is_connected()
    {
        var discovery = new WindowsControllerDiscovery(
            new SlotApi(),
            new FlydigiDetector(false),
            new ServiceClient(null),
            new Presence(true));

        Assert.Equal(
            ControllerProfile.DualSenseUsb,
            discovery.FindActiveController()!.Profile);
    }

    private sealed class SlotApi(params int[] connected) : IXInputApi
    {
        public bool TryGetBattery(int userIndex, out XInputBattery battery)
        {
            battery = new XInputBattery(1, 3);
            return connected.Contains(userIndex);
        }
    }

    private sealed class FlydigiDetector(bool present) : IFlydigiDeviceDetector
    {
        public bool IsVaderReceiverPresent() => present;
    }

    private sealed class ServiceClient(FlydigiDeviceState? device) : IFlydigiServiceClient
    {
        public Task<FlydigiDeviceState?> QueryControllerAsync(CancellationToken cancellationToken) =>
            Task.FromResult(device);
    }

    private sealed class Presence(bool present) : IControllerPresence
    {
        public bool IsPresent(ControllerIdentity identity) => present;
    }
}
