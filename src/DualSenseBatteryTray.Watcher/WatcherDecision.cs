namespace DualSenseBatteryTray.Watcher;

public static class WatcherDecision
{
    public static bool ShouldStart(bool controllerPresent, bool appAlreadyRunning) =>
        controllerPresent && !appAlreadyRunning;
}
