namespace DualSenseBatteryTray.Core.Devices;

public interface IControllerPresence
{
    bool IsPresent(ControllerIdentity identity);
}
