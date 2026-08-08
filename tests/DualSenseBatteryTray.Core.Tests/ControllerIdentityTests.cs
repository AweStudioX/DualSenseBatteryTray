using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Core.Tests;

public sealed class ControllerIdentityTests
{
    [Fact]
    public void UsbDualSense_has_the_expected_usb_identity()
    {
        var controller = ControllerIdentity.UsbDualSense;

        Assert.Equal((ushort)0x054C, controller.VendorId);
        Assert.Equal((ushort)0x0CE6, controller.ProductId);
        Assert.Equal("DualSense Wireless Controller", controller.DisplayName);
    }
}
