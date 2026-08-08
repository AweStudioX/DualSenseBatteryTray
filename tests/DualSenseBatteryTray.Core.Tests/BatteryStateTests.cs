using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Core.Tests;

public sealed class BatteryStateTests
{
    [Theory]
    [InlineData(ConnectionState.Charging, true)]
    [InlineData(ConnectionState.Full, true)]
    [InlineData(ConnectionState.Discharging, false)]
    [InlineData(ConnectionState.Flat, false)]
    [InlineData(ConnectionState.Unknown, false)]
    public void IsCharging_reflects_the_connection_state(ConnectionState state, bool expected)
    {
        var battery = new BatteryState(50, state);

        Assert.Equal(expected, battery.IsCharging);
    }
}
