using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Core.Tests;

public sealed class ControllerProfileTests
{
    [Fact]
    public void Vader4_uses_XInput_with_a_dedicated_icon_and_coarse_battery_levels()
    {
        var profile = ControllerProfile.FlydigiVader4Receiver;

        Assert.Equal(ControllerProtocol.XInput, profile.Protocol);
        Assert.Equal(ControllerIconKind.FlydigiVader, profile.IconKind);
        Assert.False(profile.HasPrecisePercentage);
        Assert.Contains("Vader 4", profile.DisplayName);
    }

    [Fact]
    public void Unknown_XInput_controller_uses_the_generic_gamepad_icon()
    {
        Assert.Equal(
            ControllerIconKind.GenericGamepad,
            ControllerProfile.GenericXInput.IconKind);
    }

    [Fact]
    public void Unverified_Flydigi_compatibility_identity_uses_the_generic_icon()
    {
        Assert.Equal(
            ControllerIconKind.GenericGamepad,
            ControllerProfile.FlydigiXInputCompatibility.IconKind);
    }
}
