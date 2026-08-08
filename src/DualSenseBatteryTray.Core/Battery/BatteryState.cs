namespace DualSenseBatteryTray.Core.Battery;

public enum ConnectionState { Discharging, Charging, Full, Flat, Unknown }

public sealed record BatteryState(int? Percentage, ConnectionState State)
{
    public bool IsCharging => State is ConnectionState.Charging or ConnectionState.Full;
}
