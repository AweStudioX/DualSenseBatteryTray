using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class SupportedControllerLivenessProbeTests
{
    [Fact]
    public async Task Connected_XInput_controller_is_live_without_opening_DualSense_HID()
    {
        var dualSense = new RecordingProbe();
        var probe = new SupportedControllerLivenessProbe(new ConnectedApi(), dualSense);

        var result = await probe.ProbeAsync(
            ControllerIdentity.UsbDualSense,
            CancellationToken.None);

        Assert.Equal(ControllerLivenessProbeResult.Progressing, result);
        Assert.False(dualSense.Called);
    }

    private sealed class ConnectedApi : IXInputApi
    {
        public bool TryGetBattery(int userIndex, out XInputBattery battery)
        {
            battery = new XInputBattery(1, 3);
            return userIndex == 0;
        }
    }

    private sealed class RecordingProbe : IControllerLivenessProbe
    {
        public bool Called { get; private set; }

        public Task<ControllerLivenessProbeResult> ProbeAsync(
            ControllerIdentity identity,
            CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(ControllerLivenessProbeResult.AdapterAbsent);
        }
    }
}
