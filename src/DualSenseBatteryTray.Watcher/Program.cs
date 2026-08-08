using System.Diagnostics;
using System.Security.Principal;
using DualSenseBatteryTray.Core.Devices;
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

        try
        {
            var appMutexName = WatcherRuntime.GetAppMutexName(userSid);
            var appStartInfo = WatcherRuntime.CreateAppStartInfo(AppContext.BaseDirectory);
            var launcher = new WatcherLauncher(
                new HidDeviceEnumerator(),
                () => WatcherRuntime.IsMutexPresent(appMutexName),
                () => Process.Start(appStartInfo)?.Dispose());
            using var notificationWindow = new DeviceNotificationWindow(launcher.CheckAndStart);

            launcher.CheckAndStart();
            System.Windows.Forms.Application.Run();
        }
        finally
        {
            watcherMutex.ReleaseMutex();
        }
    }
}

internal sealed class WatcherLauncher(
    IControllerPresence presence,
    Func<bool> appAlreadyRunning,
    Action startApp)
{
    private readonly IControllerPresence _presence =
        presence ?? throw new ArgumentNullException(nameof(presence));
    private readonly Func<bool> _appAlreadyRunning =
        appAlreadyRunning ?? throw new ArgumentNullException(nameof(appAlreadyRunning));
    private readonly Action _startApp = startApp ?? throw new ArgumentNullException(nameof(startApp));

    internal void CheckAndStart()
    {
        var controllerPresent = _presence.IsPresent(ControllerIdentity.UsbDualSense);
        if (WatcherDecision.ShouldStart(controllerPresent, _appAlreadyRunning()))
            _startApp();
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
