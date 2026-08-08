using System.Security.Principal;
using System.Threading;

namespace DualSenseBatteryTray.App;

public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    public static SingleInstanceGuard? TryAcquire()
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user has no SID.");
        var mutex = new Mutex(
            initiallyOwned: true,
            $@"Local\DualSenseBatteryTray-{userSid}",
            out var createdNew);

        if (createdNew)
            return new SingleInstanceGuard(mutex);

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
            return;

        mutex.ReleaseMutex();
        mutex.Dispose();
        GC.SuppressFinalize(this);
    }
}
