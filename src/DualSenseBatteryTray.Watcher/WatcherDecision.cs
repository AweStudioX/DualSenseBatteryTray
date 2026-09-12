using DualSenseBatteryTray.Hid;

namespace DualSenseBatteryTray.Watcher;

public static class WatcherDecision
{
    public static bool ShouldStart(
        ControllerLivenessProbeResult liveness,
        bool appAlreadyRunning) =>
        liveness == ControllerLivenessProbeResult.Progressing && !appAlreadyRunning;
}
