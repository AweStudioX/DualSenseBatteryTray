using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Core.Notifications;

public sealed class LowBatteryAlertTracker
{
    private bool _sent20;
    private bool _sent10;

    public int? Observe(BatteryState state)
    {
        if (state.IsCharging)
        {
            _sent20 = false;
            _sent10 = false;
            return null;
        }

        if (state.State != ConnectionState.Discharging || state.Percentage is not int level)
            return null;

        if (level > 20) _sent20 = false;
        if (level > 10) _sent10 = false;

        if (level <= 10 && !_sent10)
        {
            _sent10 = true;
            _sent20 = true;
            return 10;
        }

        if (level <= 20 && !_sent20)
        {
            _sent20 = true;
            return 20;
        }

        return null;
    }
}
