using System.Runtime.InteropServices;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Watcher.Tests;

public sealed class WatcherDecisionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void ShouldStart_requires_controller_presence_and_an_absent_app(
        bool controllerPresent,
        bool appAlreadyRunning,
        bool expected)
    {
        var result = WatcherDecision.ShouldStart(controllerPresent, appAlreadyRunning);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Arrival_notifications_schedule_one_debounced_check_within_500_milliseconds()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(500),
            DeviceArrivalDebouncer.DefaultDelay);

        var checkCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checkCount = 0;
        var delays = new ControlledDebounceDelay();
        using var debouncer = new DeviceArrivalDebouncer(
            () =>
            {
                Interlocked.Increment(ref checkCount);
                checkCompleted.TrySetResult();
            },
            delays);

        debouncer.NotifyArrival();
        debouncer.NotifyArrival();
        debouncer.NotifyArrival();

        using var deadline = await delays.NextAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(500), deadline.Delay);
        deadline.Complete();
        await checkCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, Volatile.Read(ref checkCount));
        Assert.False(delays.HasPendingRequest);
    }

    [Fact]
    public async Task Later_arrival_resets_the_debounce_deadline()
    {
        var checkCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checkCount = 0;
        var delays = new ControlledDebounceDelay();
        using var debouncer = new DeviceArrivalDebouncer(
            () =>
            {
                Interlocked.Increment(ref checkCount);
                checkCompleted.TrySetResult();
            },
            delays);

        debouncer.NotifyArrival();
        using var firstDeadline = await delays.NextAsync();
        debouncer.NotifyArrival();

        await firstDeadline.Canceled.WaitAsync(TimeSpan.FromSeconds(2));
        using var resetDeadline = await delays.NextAsync();
        Assert.Equal(DeviceArrivalDebouncer.DefaultDelay, resetDeadline.Delay);
        Assert.Equal(0, Volatile.Read(ref checkCount));

        resetDeadline.Complete();
        await checkCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, Volatile.Read(ref checkCount));
    }

    [Fact]
    public async Task Arrival_consumed_at_the_deadline_boundary_schedules_a_later_check()
    {
        var checkCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checkCount = 0;
        var delays = new ControlledDebounceDelay();
        using var debouncer = new DeviceArrivalDebouncer(
            () =>
            {
                Interlocked.Increment(ref checkCount);
                checkCompleted.TrySetResult();
            },
            delays);

        debouncer.NotifyArrival();
        using var firstDeadline = await delays.NextAsync();
        firstDeadline.BeforeCancellation(debouncer.NotifyArrival);
        firstDeadline.Complete();

        using var resetDeadline = await delays.NextAsync();
        Assert.Equal(0, Volatile.Read(ref checkCount));
        resetDeadline.Complete();
        await checkCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, Volatile.Read(ref checkCount));
    }

    [Fact]
    public async Task Dispose_at_the_delay_boundary_is_exception_free_and_waits_for_the_callback()
    {
        var callbackStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        var delays = new ControlledDebounceDelay();
        var debouncer = new DeviceArrivalDebouncer(
            () =>
            {
                callbackStarted.TrySetResult();
                releaseCallback.Wait(TimeSpan.FromSeconds(2));
            },
            delays);
        debouncer.NotifyArrival();
        using var deadline = await delays.NextAsync();
        deadline.Complete();
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var firstDispose = Task.Run(() => Record.Exception(debouncer.Dispose));
        var secondDispose = Task.Run(() => Record.Exception(debouncer.Dispose));
        Assert.True(SpinWait.SpinUntil(
            () => Record.Exception(debouncer.NotifyArrival) is ObjectDisposedException,
            TimeSpan.FromSeconds(2)));

        Assert.False(firstDispose.IsCompleted);
        Assert.False(secondDispose.IsCompleted);
        releaseCallback.Set();
        Assert.Null(await firstDispose.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Null(await secondDispose.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Null(Record.Exception(debouncer.Dispose));
    }

    [Fact]
    public async Task Dispose_is_exception_free_after_a_callback_failure()
    {
        var callbackAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var delays = new ControlledDebounceDelay();
        var debouncer = new DeviceArrivalDebouncer(
            () =>
            {
                callbackAttempted.TrySetResult();
                throw new InvalidOperationException("Presence check failed.");
            },
            delays);
        debouncer.NotifyArrival();
        using var deadline = await delays.NextAsync();
        deadline.Complete();
        await callbackAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(Record.Exception(debouncer.Dispose));
    }

    [Fact]
    public async Task Dispose_prevents_a_pending_callback_from_starting_after_it_returns()
    {
        var checkCount = 0;
        var delays = new ControlledDebounceDelay();
        var debouncer = new DeviceArrivalDebouncer(
            () => Interlocked.Increment(ref checkCount),
            delays);
        debouncer.NotifyArrival();
        using var deadline = await delays.NextAsync();

        var error = Record.Exception(debouncer.Dispose);
        await deadline.Canceled.WaitAsync(TimeSpan.FromSeconds(2));
        deadline.Complete();

        Assert.Null(error);
        Assert.Equal(0, Volatile.Read(ref checkCount));
        Assert.Throws<ObjectDisposedException>(debouncer.NotifyArrival);
    }

    [Fact]
    public void Arrival_message_requires_WM_DEVICECHANGE_and_DBT_DEVICEARRIVAL()
    {
        Assert.True(DeviceNotificationWindow.IsDeviceArrivalNotification(0x0219, 0x8000));
        Assert.False(DeviceNotificationWindow.IsDeviceArrivalNotification(0x0219, 0x8004));
        Assert.False(DeviceNotificationWindow.IsDeviceArrivalNotification(0x0010, 0x8000));
    }

    [Fact]
    public void Session_logoff_requires_WM_WTSSESSION_CHANGE_and_WTS_SESSION_LOGOFF()
    {
        Assert.True(DeviceNotificationWindow.IsCurrentSessionLogoffNotification(0x02B1, 0x6));
        Assert.False(DeviceNotificationWindow.IsCurrentSessionLogoffNotification(0x02B1, 0x7));
        Assert.False(DeviceNotificationWindow.IsCurrentSessionLogoffNotification(0x0016, 0x6));
    }

    [Fact]
    public void Device_notification_window_is_message_only_and_releases_native_registrations()
    {
        nint createdWindow = nint.Zero;
        nint foundWindow = nint.Zero;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new DeviceNotificationWindow(() => { });
                createdWindow = window.Handle;
                foundWindow = FindMessageOnlyWatcherWindow(createdWindow);
            }
            catch (Exception caught)
            {
                error = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

        Assert.Null(error);
        Assert.NotEqual(nint.Zero, createdWindow);
        Assert.Equal(createdWindow, foundWindow);
    }

    [Fact]
    public void Wts_registration_failure_keeps_the_Hid_watcher_usable_and_cleanup_complete()
    {
        nint createdWindow = nint.Zero;
        nint foundWindowBeforeDispose = nint.Zero;
        nint foundWindowAfterDispose = nint.Zero;
        var checkCount = 0;
        var sessionUnregisterCount = 0;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using (var window = new DeviceNotificationWindow(
                    () => Interlocked.Increment(ref checkCount),
                    registerSessionNotification: _ => false,
                    unregisterSessionNotification: _ =>
                    {
                        Interlocked.Increment(ref sessionUnregisterCount);
                        return true;
                    }))
                {
                    createdWindow = window.Handle;
                    foundWindowBeforeDispose = FindMessageOnlyWatcherWindow(createdWindow);
                    _ = SendMessageW(createdWindow, 0x0219, 0x8000, nint.Zero);
                    Assert.True(SpinWait.SpinUntil(
                        () => Volatile.Read(ref checkCount) == 1,
                        TimeSpan.FromSeconds(2)));
                }

                foundWindowAfterDispose = FindMessageOnlyWatcherWindow(createdWindow);
            }
            catch (Exception caught)
            {
                error = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

        Assert.Null(error);
        Assert.NotEqual(nint.Zero, createdWindow);
        Assert.Equal(createdWindow, foundWindowBeforeDispose);
        Assert.Equal(nint.Zero, foundWindowAfterDispose);
        Assert.Equal(1, Volatile.Read(ref checkCount));
        Assert.Equal(0, Volatile.Read(ref sessionUnregisterCount));
    }

    [Fact]
    public void Native_cleanup_reaches_unregister_and_window_destroy_when_debounce_dispose_throws()
    {
        var calls = new List<string>();

        Assert.Throws<InvalidOperationException>(() =>
            DeviceNotificationWindow.CleanupResources(
                () =>
                {
                    calls.Add("debounce");
                    throw new InvalidOperationException("Dispose failed.");
                },
                () => calls.Add("device"),
                () => calls.Add("session"),
                () => calls.Add("window")));

        Assert.Equal(["debounce", "device", "session", "window"], calls);
    }

    [Fact]
    public void Native_cleanup_reaches_window_destroy_when_an_unregister_step_throws()
    {
        var calls = new List<string>();

        Assert.Throws<InvalidOperationException>(() =>
            DeviceNotificationWindow.CleanupResources(
                () => calls.Add("debounce"),
                () =>
                {
                    calls.Add("device");
                    throw new InvalidOperationException("Unregister failed.");
                },
                () => calls.Add("session"),
                () => calls.Add("window")));

        Assert.Equal(["debounce", "device", "session", "window"], calls);
    }

    [Fact]
    public void Startup_check_uses_the_USB_DualSense_identity_and_starts_once()
    {
        var presence = new RecordingPresence(present: true);
        var startCount = 0;
        var launcher = new WatcherLauncher(
            presence,
            () => false,
            () => startCount++);

        launcher.CheckAndStart();

        Assert.Same(ControllerIdentity.UsbDualSense, presence.Identity);
        Assert.Equal(1, startCount);
    }

    [Fact]
    public void App_mutex_name_matches_the_tray_application_contract()
    {
        const string sid = "S-1-5-21-123";

        Assert.Equal(
            @"Local\DualSenseBatteryTray-S-1-5-21-123",
            WatcherRuntime.GetAppMutexName(sid));
        Assert.Equal(
            @"Local\DualSenseBatteryTray-Watcher-S-1-5-21-123",
            WatcherRuntime.GetWatcherMutexName(sid));
    }

    [Fact]
    public void Mutex_probe_detects_presence_without_taking_ownership()
    {
        var name = $@"Local\DualSenseBatteryTray-Test-{Guid.NewGuid():N}";
        using var owner = new Mutex(initiallyOwned: true, name, out var createdNew);
        Assert.True(createdNew);

        Assert.True(WatcherRuntime.IsMutexPresent(name));

        owner.ReleaseMutex();
        var acquired = false;
        var probe = new Thread(() =>
        {
            using var existing = Mutex.OpenExisting(name);
            acquired = existing.WaitOne(TimeSpan.FromSeconds(1));
            if (acquired)
                existing.ReleaseMutex();
        });
        probe.Start();
        Assert.True(probe.Join(TimeSpan.FromSeconds(2)));

        Assert.True(acquired);
    }

    [Fact]
    public void Mutex_probe_reports_an_absent_mutex()
    {
        var name = $@"Local\DualSenseBatteryTray-Absent-{Guid.NewGuid():N}";

        Assert.False(WatcherRuntime.IsMutexPresent(name));
    }

    [Fact]
    public void App_launch_is_shell_executed_from_beside_the_watcher()
    {
        var installDirectory = Path.Combine("C:\\", "DualSenseBatteryTray");

        var startInfo = WatcherRuntime.CreateAppStartInfo(installDirectory);

        Assert.Equal(
            Path.Combine(installDirectory, "DualSenseBatteryTray.App.exe"),
            startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }

    private sealed class RecordingPresence(bool present) : IControllerPresence
    {
        public ControllerIdentity? Identity { get; private set; }

        public bool IsPresent(ControllerIdentity identity)
        {
            Identity = identity;
            return present;
        }
    }

    private sealed class ControlledDebounceDelay : IDebounceDelay
    {
        private readonly object _sync = new();
        private readonly Queue<DelayRequest> _requests = new();
        private readonly SemaphoreSlim _requestAvailable = new(0);

        public bool HasPendingRequest
        {
            get
            {
                lock (_sync)
                    return _requests.Count > 0;
            }
        }

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var request = new DelayRequest(delay, cancellationToken);
            lock (_sync)
                _requests.Enqueue(request);

            _requestAvailable.Release();
            return request.Completion;
        }

        public async Task<DelayRequest> NextAsync()
        {
            if (!await _requestAvailable.WaitAsync(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("The debouncer did not schedule a deadline.");

            lock (_sync)
                return _requests.Dequeue();
        }
    }

    private sealed class DelayRequest : IDisposable
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _canceled = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private Action? _beforeCancellation;

        public DelayRequest(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delay = delay;
            _cancellationRegistration = cancellationToken.Register(() =>
            {
                _beforeCancellation?.Invoke();
                _canceled.TrySetResult();
                _completion.TrySetCanceled(cancellationToken);
            });
        }

        public TimeSpan Delay { get; }
        public Task Completion => _completion.Task;
        public Task Canceled => _canceled.Task;

        public void Complete() => _completion.TrySetResult();

        public void BeforeCancellation(Action action) =>
            _beforeCancellation = action ?? throw new ArgumentNullException(nameof(action));

        public void Dispose() => _cancellationRegistration.Dispose();
    }

    private static nint FindMessageOnlyWatcherWindow(nint target)
    {
        nint found = nint.Zero;
        do
        {
            found = FindWindowExW(
                new nint(-3),
                found,
                null,
                "DualSenseBatteryTray.DeviceWatcher");
        }
        while (found != nint.Zero && found != target);

        return found;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SendMessageW(
        nint window,
        int message,
        nint wParam,
        nint lParam);
}
