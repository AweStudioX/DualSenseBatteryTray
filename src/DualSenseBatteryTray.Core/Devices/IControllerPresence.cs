namespace DualSenseBatteryTray.Core.Devices;

public interface IControllerPresence
{
    bool IsPresent(ControllerIdentity identity);
}

public interface IControllerTransportPresence : IControllerPresence
{
    bool IsBluetooth(ControllerIdentity identity);
}
