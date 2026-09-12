using DualSenseBatteryTray.Core.Devices;
using DualSenseBatteryTray.Hid;

namespace DualSenseBatteryTray.Watcher;

internal interface IWatcherDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class TaskWatcherDelay : IWatcherDelay
{
    internal static TaskWatcherDelay Instance { get; } = new();

    private TaskWatcherDelay()
    {
    }

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class WatcherLivenessCoordinator : IAsyncDisposable
{
    public static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

    private readonly IControllerLivenessProbe _probe;
    private readonly Func<bool> _appAlreadyRunning;
    private readonly Action _startApp;
    private readonly IWatcherDelay _delay;
    private readonly TimeSpan _pollingInterval;
    private readonly WatcherEventLogger? _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _requestSignal = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private readonly TaskCompletionSource _disposeCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _worker;
    private bool _disposeStarted;
    private DateTimeOffset? _lastFailureLog;
    private bool _adapterPresent;
    private ControllerLivenessProbeResult? _lastControllerState;

    public WatcherLivenessCoordinator(
        IControllerLivenessProbe probe,
        Func<bool> appAlreadyRunning,
        Action startApp)
        : this(
            probe,
            appAlreadyRunning,
            startApp,
            TaskWatcherDelay.Instance,
            DefaultPollingInterval,
            new WatcherEventLogger(),
            TimeProvider.System)
    {
    }

    internal WatcherLivenessCoordinator(
        IControllerLivenessProbe probe,
        Func<bool> appAlreadyRunning,
        Action startApp,
        IWatcherDelay delay,
        TimeSpan pollingInterval,
        WatcherEventLogger? logger,
        TimeProvider timeProvider)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _appAlreadyRunning = appAlreadyRunning
            ?? throw new ArgumentNullException(nameof(appAlreadyRunning));
        _startApp = startApp ?? throw new ArgumentNullException(nameof(startApp));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        if (pollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollingInterval));
        _pollingInterval = pollingInterval;
        _logger = logger;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _requestSignal.Release();
        _worker = RunAsync();
    }

    public void RequestCheck()
    {
        lock (_sync)
        {
            if (_disposeStarted)
                return;

            try
            {
                _requestSignal.Release();
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
                await WaitForRequestOrTickAsync(_shutdown.Token).ConfigureAwait(false);
                if (_shutdown.IsCancellationRequested)
                    return;

                if (AppIsRunning())
                    continue;

                ControllerLivenessProbeResult liveness;
                try
                {
                    liveness = await _probe
                        .ProbeAsync(ControllerIdentity.UsbDualSense, _shutdown.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    LogFailure(error);
                    continue;
                }

                LogTransition(liveness);
                if (!WatcherDecision.ShouldStart(liveness, AppIsRunning()))
                    continue;

                Exception? launchError = null;
                lock (_sync)
                {
                    if (_disposeStarted)
                        return;

                    try
                    {
                        _startApp();
                    }
                    catch (Exception error)
                    {
                        launchError = error;
                    }
                }

                if (launchError is not null)
                    LogFailure(launchError);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            LogFailure(error);
        }
    }

    private bool AppIsRunning()
    {
        try
        {
            return _appAlreadyRunning();
        }
        catch (Exception error)
        {
            LogFailure(error);
            return true;
        }
    }

    private async Task WaitForRequestOrTickAsync(CancellationToken cancellationToken)
    {
        if (_requestSignal.Wait(0))
        {
            while (_requestSignal.Wait(0))
            {
            }
            return;
        }

        using var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var request = _requestSignal.WaitAsync(wake.Token);
        var tick = _delay.WaitAsync(_pollingInterval, wake.Token);
        _ = await Task.WhenAny(request, tick).ConfigureAwait(false);
        wake.Cancel();
        await IgnoreExpectedCancellationAsync(request, wake.Token).ConfigureAwait(false);
        await IgnoreExpectedCancellationAsync(tick, wake.Token).ConfigureAwait(false);
        while (_requestSignal.Wait(0))
        {
        }
    }

    private static async Task IgnoreExpectedCancellationAsync(
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

    private void LogTransition(ControllerLivenessProbeResult liveness)
    {
        if (liveness == ControllerLivenessProbeResult.AdapterAbsent)
        {
            _adapterPresent = false;
            _lastControllerState = null;
            return;
        }

        if (!_adapterPresent)
        {
            SafeLog("adapter.present");
            _adapterPresent = true;
        }

        if (liveness == ControllerLivenessProbeResult.Progressing
            && _lastControllerState != ControllerLivenessProbeResult.Progressing)
        {
            SafeLog("controller.live");
        }
        else if (liveness == ControllerLivenessProbeResult.Stale
                 && _lastControllerState != ControllerLivenessProbeResult.Stale)
        {
            SafeLog("controller.stale");
        }

        if (liveness is ControllerLivenessProbeResult.Progressing
            or ControllerLivenessProbeResult.Stale)
        {
            _lastControllerState = liveness;
        }
    }

    private void LogFailure(Exception error)
    {
        var now = _timeProvider.GetUtcNow();
        if (_lastFailureLog is not null && now - _lastFailureLog < FailureLogInterval)
            return;

        if (SafeLog("liveness.failure", error))
            _lastFailureLog = now;
    }

    private bool SafeLog(string eventName, Exception? error = null)
    {
        if (_logger is null)
            return true;

        try
        {
            _logger.Log(eventName, error);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync() => new(DisposeCoreAsync());

    private async Task DisposeCoreAsync()
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
            await _disposeCompleted.Task.ConfigureAwait(false);
            return;
        }

        _shutdown.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _requestSignal.Dispose();
                _shutdown.Dispose();
            }
            finally
            {
                _disposeCompleted.TrySetResult();
            }
        }
    }
}
