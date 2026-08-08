using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class HidDeviceEnumeratorTests
{
    [Fact]
    public void FindPaths_returns_no_paths_for_an_unregistered_identity()
    {
        var identity = new ControllerIdentity(0xFFFF, 0xFFFF, "Not a real controller");
        var enumerator = new HidDeviceEnumerator();

        var paths = enumerator.FindPaths(identity);

        Assert.Empty(paths);
        Assert.False(enumerator.IsPresent(identity));
    }
}
