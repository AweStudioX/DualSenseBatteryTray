using System.Diagnostics;
using System.Security.Principal;
using DualSenseBatteryTray.Hid;

namespace DualSenseBatteryTray.Watcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user has no SID.");
        using var watcherMutex = new Mutex(
            initiallyOwned: true,
            WatcherRuntime.GetWatcherMutexName(userSid),
            out var createdNew);
        if (!createdNew)
            return;

        WatcherLivenessCoordinator? coordinator = null;
        try
        {
            var appMutexName = WatcherRuntime.GetAppMutexName(userSid);
            var appStartInfo = WatcherRuntime.CreateAppStartInfo(AppContext.BaseDirectory);
            coordinator = new WatcherLivenessCoordinator(
                new ControllerLivenessProbe(),
                () => WatcherRuntime.IsMutexPresent(appMutexName),
                () => Process.Start(appStartInfo)?.Dispose());
            using var notificationWindow = new DeviceNotificationWindow(coordinator.RequestCheck);

            coordinator.RequestCheck();
            System.Windows.Forms.Application.Run();
        }
        finally
        {
            coordinator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            watcherMutex.ReleaseMutex();
        }
    }
}

internal static class WatcherRuntime
{
    internal static string GetAppMutexName(string userSid) =>
        $@"Local\DualSenseBatteryTray-{userSid}";

    internal static string GetWatcherMutexName(string userSid) =>
        $@"Local\DualSenseBatteryTray-Watcher-{userSid}";

    internal static bool IsMutexPresent(string name)
    {
        try
        {
            using var mutex = Mutex.OpenExisting(name);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    internal static ProcessStartInfo CreateAppStartInfo(string installDirectory) => new()
    {
        FileName = Path.Combine(installDirectory, "DualSenseBatteryTray.App.exe"),
        UseShellExecute = true,
    };
}
