using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Notifications;

namespace DualSenseBatteryTray.Core.Tests;

public sealed class LowBatteryAlertTrackerTests
{
    [Fact]
    public void Observe_notifies_once_at_each_threshold()
    {
        var tracker = new LowBatteryAlertTracker();
        Assert.Null(tracker.Observe(new(25, ConnectionState.Discharging)));
        Assert.Equal(20, tracker.Observe(new(20, ConnectionState.Discharging)));
        Assert.Null(tracker.Observe(new(15, ConnectionState.Discharging)));
        Assert.Equal(10, tracker.Observe(new(10, ConnectionState.Discharging)));
        Assert.Null(tracker.Observe(new(5, ConnectionState.Discharging)));
    }

    [Fact]
    public void Observe_resets_after_charging()
    {
        var tracker = new LowBatteryAlertTracker();
        Assert.Equal(20, tracker.Observe(new(20, ConnectionState.Discharging)));
        Assert.Null(tracker.Observe(new(20, ConnectionState.Charging)));
        Assert.Equal(20, tracker.Observe(new(20, ConnectionState.Discharging)));
    }

    [Fact]
    public void Observe_ignores_unknown_state()
    {
        Assert.Null(new LowBatteryAlertTracker().Observe(new(null, ConnectionState.Unknown)));
    }
}
