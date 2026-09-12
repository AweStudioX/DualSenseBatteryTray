using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DualSenseBatteryTray.Watcher;

internal sealed class DeviceNotificationWindow : NativeWindow, IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int WmWtsSessionChange = 0x02B1;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceTypeInterface = 0x00000005;
    private const int WtsSessionLogoff = 0x6;
    private const uint DeviceNotifyWindowHandle = 0;
    private const uint NotifyForThisSession = 0;

    private static readonly nint HwndMessage = new(-3);

    private readonly DeviceArrivalDebouncer _arrivalDebouncer;
    private readonly Func<nint, bool> _unregisterSessionNotification;
    private nint _deviceNotification;
    private bool _sessionNotificationRegistered;
    private bool _disposed;

    internal DeviceNotificationWindow(Action requestCheck)
        : this(
            requestCheck,
            static handle => WTSRegisterSessionNotification(handle, NotifyForThisSession),
            static handle => WTSUnRegisterSessionNotification(handle))
    {
    }

    internal DeviceNotificationWindow(
        Action requestCheck,
        Func<nint, bool> registerSessionNotification,
        Func<nint, bool> unregisterSessionNotification)
    {
        ArgumentNullException.ThrowIfNull(registerSessionNotification);
        _unregisterSessionNotification = unregisterSessionNotification
            ?? throw new ArgumentNullException(nameof(unregisterSessionNotification));
        _arrivalDebouncer = new DeviceArrivalDebouncer(requestCheck);

        CreateHandle(new CreateParams
        {
            Caption = "DualSenseBatteryTray.DeviceWatcher",
            Parent = HwndMessage,
        });

        var filter = new DeviceBroadcastInterface
        {
            Size = Marshal.SizeOf<DeviceBroadcastInterface>(),
            DeviceType = DbtDeviceTypeInterface,
            ClassGuid = GetHidClassGuid(),
        };
        _deviceNotification = RegisterDeviceNotificationW(
            Handle,
            ref filter,
            DeviceNotifyWindowHandle);
        if (_deviceNotification == nint.Zero)
        {
            var error = new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not register HID device-arrival notifications.");
            CleanupResources(
                _arrivalDebouncer.Dispose,
                UnregisterDeviceNotifications,
                UnregisterSessionNotifications,
                DestroyWindow);
            throw error;
        }

        _sessionNotificationRegistered = registerSessionNotification(Handle);
    }

    internal static bool IsDeviceArrivalNotification(int message, nint eventType) =>
        message == WmDeviceChange && eventType == DbtDeviceArrival;

    internal static bool IsCurrentSessionLogoffNotification(int message, nint eventType) =>
        message == WmWtsSessionChange && eventType == WtsSessionLogoff;

    internal static void CleanupResources(
        Action disposeDebouncer,
        Action unregisterDeviceNotifications,
        Action unregisterSessionNotifications,
        Action destroyWindow)
    {
        try
        {
            disposeDebouncer();
        }
        finally
        {
            try
            {
                unregisterDeviceNotifications();
            }
            finally
            {
                try
                {
                    unregisterSessionNotifications();
                }
                finally
                {
                    destroyWindow();
                }
            }
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (IsDeviceArrivalNotification(message.Msg, message.WParam))
            _arrivalDebouncer.NotifyArrival();
        else if (IsCurrentSessionLogoffNotification(message.Msg, message.WParam))
            Application.ExitThread();

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CleanupResources(
            _arrivalDebouncer.Dispose,
            UnregisterDeviceNotifications,
            UnregisterSessionNotifications,
            DestroyWindow);
    }

    private void UnregisterDeviceNotifications()
    {
        if (_deviceNotification != nint.Zero)
        {
            _ = UnregisterDeviceNotification(_deviceNotification);
            _deviceNotification = nint.Zero;
        }
    }

    private void UnregisterSessionNotifications()
    {
        if (!_sessionNotificationRegistered)
            return;

        _ = _unregisterSessionNotification(Handle);
        _sessionNotificationRegistered = false;
    }

    private void DestroyWindow()
    {
        if (Handle != nint.Zero)
            DestroyHandle();
    }

    private static Guid GetHidClassGuid()
    {
        HidD_GetHidGuid(out var hidClassGuid);
        return hidClassGuid;
    }

    [DllImport("hid.dll", ExactSpelling = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint RegisterDeviceNotificationW(
        nint recipient,
        ref DeviceBroadcastInterface notificationFilter,
        uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterDeviceNotification(nint notificationHandle);

    [DllImport("wtsapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(nint window);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceBroadcastInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public char Name;
    }
}

internal interface IDebounceDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class TaskDebounceDelay : IDebounceDelay
{
    internal static TaskDebounceDelay Instance { get; } = new();

    private TaskDebounceDelay()
    {
    }

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

internal sealed class DeviceArrivalDebouncer : IDisposable
{
    internal static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);

    private readonly Action _presenceCheck;
    private readonly IDebounceDelay _delaySource;
    private readonly TimeSpan _delay;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _arrivalSignal = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _disposeCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _worker;
    private bool _disposeStarted;

    internal DeviceArrivalDebouncer(Action presenceCheck, TimeSpan? delay = null)
        : this(presenceCheck, TaskDebounceDelay.Instance, delay)
    {
    }

    internal DeviceArrivalDebouncer(
        Action presenceCheck,
        IDebounceDelay delaySource,
        TimeSpan? delay = null)
    {
        _presenceCheck = presenceCheck ?? throw new ArgumentNullException(nameof(presenceCheck));
        _delaySource = delaySource ?? throw new ArgumentNullException(nameof(delaySource));
        _delay = delay ?? DefaultDelay;
        _worker = RunAsync();
    }

    internal void NotifyArrival()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted, this);
            try
            {
                _arrivalSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _arrivalSignal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
                DrainArrivalSignal();

                while (true)
                {
                    using var deadlineCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    var nextArrivalTask = _arrivalSignal.WaitAsync(deadlineCancellation.Token);
                    var deadlineTask = _delaySource.WaitAsync(
                        _delay,
                        deadlineCancellation.Token);

                    _ = await Task.WhenAny(deadlineTask, nextArrivalTask).ConfigureAwait(false);
                    deadlineCancellation.Cancel();
                    await ObserveCancellationAsync(
                        deadlineTask,
                        deadlineCancellation.Token).ConfigureAwait(false);
                    await ObserveCancellationAsync(
                        nextArrivalTask,
                        deadlineCancellation.Token).ConfigureAwait(false);
                    var deadlineWasReset = nextArrivalTask.IsCompletedSuccessfully;

                    if (_shutdown.IsCancellationRequested)
                        return;

                    if (deadlineWasReset)
                    {
                        DrainArrivalSignal();
                        continue;
                    }

                    lock (_sync)
                    {
                        if (_disposeStarted)
                            return;
                    }

                    try
                    {
                        _presenceCheck();
                    }
                    catch (Exception)
                    {
                        // Arrival checks are best effort; a later arrival can try again.
                    }

                    break;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private void DrainArrivalSignal()
    {
        while (_arrivalSignal.Wait(0))
        {
        }
    }

    private static async Task ObserveCancellationAsync(
        Task task,
        CancellationToken cancellationToken)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        var ownsDisposal = false;
        lock (_sync)
        {
            if (!_disposeStarted)
            {
                _disposeStarted = true;
                ownsDisposal = true;
            }
        }

        if (!ownsDisposal)
        {
            _disposeCompleted.Task.GetAwaiter().GetResult();
            return;
        }

        try
        {
            _shutdown.Cancel();
            _worker.GetAwaiter().GetResult();
        }
        finally
        {
            _arrivalSignal.Dispose();
            _shutdown.Dispose();
            _disposeCompleted.TrySetResult();
        }
    }
}
