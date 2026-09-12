using System.Reflection;
using DualSenseBatteryTray.Core.Devices;
using DualSenseBatteryTray.Hid;

namespace DualSenseBatteryTray.Watcher.Tests;

public sealed class WatcherLivenessCoordinatorTests
{
    [Fact]
    public async Task Constructor_checks_immediately_then_retries_after_two_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), WatcherLivenessCoordinator.DefaultPollingInterval);
        var probe = new ControlledProbe();
        probe.Enqueue(ControllerLivenessProbeResult.AdapterAbsent);
        probe.Enqueue(ControllerLivenessProbeResult.Insufficient);
        var delays = new ControlledWatcherDelay();
        await using var coordinator = Create(probe, delays: delays);

        await probe.WaitForCallsAsync(1);
        using var retry = await delays.NextAsync();
        Assert.Equal(TimeSpan.FromSeconds(2), retry.Delay);
        retry.Complete();

        await probe.WaitForCallsAsync(2);
        Assert.All(probe.Identities, identity => Assert.Same(ControllerIdentity.UsbDualSense, identity));
    }

    [Fact]
    public async Task Progressing_result_launches_once_and_probes_are_single_flight()
    {
        var probe = new ControlledProbe();
        probe.Enqueue(ControllerLivenessProbeResult.Stale);
        probe.Enqueue(ControllerLivenessProbeResult.Progressing);
        var launchCount = 0;
        await using var coordinator = Create(probe, startApp: () => launchCount++);

        await probe.WaitForCallsAsync(1);
        coordinator.RequestCheck();
        await probe.WaitForCallsAsync(2);

        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref launchCount) == 1,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(1, Volatile.Read(ref launchCount));
        Assert.Equal(1, probe.MaximumConcurrency);
    }

    [Fact]
    public async Task App_mutex_suppresses_probe_until_a_later_tick_after_it_disappears()
    {
        var running = true;
        var probe = new ControlledProbe();
        probe.Enqueue(ControllerLivenessProbeResult.Progressing);
        var delays = new ControlledWatcherDelay();
        var launches = 0;
        await using var coordinator = Create(
            probe,
            appAlreadyRunning: () => Volatile.Read(ref running),
            startApp: () => launches++,
            delays: delays);

        using var retry = await delays.NextAsync();
        Assert.Equal(0, probe.CallCount);
        running = false;
        retry.Complete();
        await probe.WaitForCallsAsync(1);

        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task Timer_and_arrivals_during_active_probe_coalesce_to_one_follow_up()
    {
        var probe = new ControlledProbe();
        var first = probe.EnqueueGate();
        probe.Enqueue(ControllerLivenessProbeResult.Stale);
        await using var coordinator = Create(probe);
        await probe.WaitForCallsAsync(1);

        coordinator.RequestCheck();
        coordinator.RequestCheck();
        coordinator.RequestCheck();
        first.Complete(ControllerLivenessProbeResult.Insufficient);
        await probe.WaitForCallsAsync(2);

        await Task.Delay(50);
        Assert.Equal(2, probe.CallCount);
        Assert.Equal(1, probe.MaximumConcurrency);
    }

    [Theory]
    [InlineData(ControllerLivenessProbeResult.AdapterAbsent)]
    [InlineData(ControllerLivenessProbeResult.Insufficient)]
    [InlineData(ControllerLivenessProbeResult.Stale)]
    public async Task Non_live_results_keep_worker_alive(ControllerLivenessProbeResult result)
    {
        var probe = new ControlledProbe();
        probe.Enqueue(result);
        probe.Enqueue(ControllerLivenessProbeResult.Progressing);
        var launches = 0;
        await using var coordinator = Create(probe, startApp: () => launches++);
        await probe.WaitForCallsAsync(1);

        coordinator.RequestCheck();
        await probe.WaitForCallsAsync(2);

        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task Probe_failure_is_rate_limited_and_worker_continues()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"watcher-{Guid.NewGuid():N}.log");
        var time = new ManualTimeProvider();
        var logger = new WatcherEventLogger(logPath, time);
        var probe = new ControlledProbe();
        probe.Enqueue(new IOException("path=private"));
        probe.Enqueue(new InvalidOperationException("report=private"));
        probe.Enqueue(ControllerLivenessProbeResult.Progressing);
        var launches = 0;
        await using var coordinator = Create(
            probe,
            startApp: () => launches++,
            logger: logger,
            timeProvider: time);
        await probe.WaitForCallsAsync(1);

        coordinator.RequestCheck();
        await probe.WaitForCallsAsync(2);
        coordinator.RequestCheck();
        await probe.WaitForCallsAsync(3);

        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref launches) == 1,
            TimeSpan.FromSeconds(2)));
        var text = File.ReadAllText(logPath);
        Assert.Equal(1, Count(text, "event=liveness.failure"));
        Assert.DoesNotContain("path=private", text, StringComparison.Ordinal);
        Assert.DoesNotContain("report=private", text, StringComparison.Ordinal);
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task Real_probe_failures_are_sanitized_logged_once_and_worker_continues()
    {
        const string privateDetail = "VID_054C path=private report=0102";
        var logPath = Path.Combine(Path.GetTempPath(), $"watcher-{Guid.NewGuid():N}.log");
        var time = new ManualTimeProvider();
        var logger = new WatcherEventLogger(logPath, time);
        var sessions = new ProbeSessionFactory();
        sessions.Enqueue(new IOException(privateDetail));
        sessions.Enqueue(new ThrowingProbeReadStream(new IOException(privateDetail)));
        sessions.Enqueue(new ProgressingProbeReadStream());
        var probe = new ControllerLivenessProbe(sessions, TimeSpan.FromSeconds(1));
        var launches = 0;
        await using var coordinator = new WatcherLivenessCoordinator(
            probe,
            () => false,
            () => launches++,
            new ControlledWatcherDelay(),
            WatcherLivenessCoordinator.DefaultPollingInterval,
            logger,
            time);

        await sessions.WaitForCallsAsync(1);
        coordinator.RequestCheck();
        await sessions.WaitForCallsAsync(2);
        coordinator.RequestCheck();
        await sessions.WaitForCallsAsync(3);

        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref launches) == 1,
            TimeSpan.FromSeconds(2)));
        var text = File.ReadAllText(logPath);
        Assert.Equal(1, Count(text, "event=liveness.failure"));
        Assert.Contains("error=ControllerLivenessProbeException", text, StringComparison.Ordinal);
        Assert.DoesNotContain(privateDetail, text, StringComparison.Ordinal);
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task Disposal_cancels_active_probe_joins_worker_and_prevents_launch()
    {
        var probe = new ControlledProbe();
        _ = probe.EnqueueGate();
        var launches = 0;
        var coordinator = Create(probe, startApp: () => launches++);
        await probe.WaitForCallsAsync(1);

        await coordinator.DisposeAsync();
        coordinator.RequestCheck();

        Assert.True(probe.LastCancellation.IsCancellationRequested);
        Assert.Equal(0, launches);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task Concurrent_disposal_callers_all_wait_for_the_worker_to_finish()
    {
        var probe = new ControlledProbe();
        var cancellationGate = probe.EnqueueCancellationGate();
        var coordinator = Create(probe);
        await probe.WaitForCallsAsync(1);

        var first = coordinator.DisposeAsync().AsTask();
        await cancellationGate.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(2));
        var second = coordinator.DisposeAsync().AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        cancellationGate.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Logger_rejects_unbounded_events_and_exposes_no_data_parameter()
    {
        var path = Path.Combine(Path.GetTempPath(), $"watcher-{Guid.NewGuid():N}.log");
        var logger = new WatcherEventLogger(path, TimeProvider.System);

        Assert.Throws<ArgumentOutOfRangeException>(() => logger.Log("device.path"));
        var method = typeof(WatcherEventLogger).GetMethod(nameof(WatcherEventLogger.Log));
        Assert.NotNull(method);
        Assert.Equal([typeof(string), typeof(Exception)],
            method!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void Logger_writes_only_event_timestamp_and_exception_type()
    {
        var path = Path.Combine(Path.GetTempPath(), $"watcher-{Guid.NewGuid():N}.log");
        var logger = new WatcherEventLogger(path, TimeProvider.System);

        logger.Log("liveness.failure", new IOException("VID_054C path=secret report=0102"));
        var text = File.ReadAllText(path);

        Assert.Contains("event=liveness.failure", text, StringComparison.Ordinal);
        Assert.Contains("error=IOException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("VID_054C", text, StringComparison.Ordinal);
        Assert.DoesNotContain("path=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("report=", text, StringComparison.Ordinal);
    }

    private static WatcherLivenessCoordinator Create(
        ControlledProbe probe,
        Func<bool>? appAlreadyRunning = null,
        Action? startApp = null,
        ControlledWatcherDelay? delays = null,
        WatcherEventLogger? logger = null,
        TimeProvider? timeProvider = null) =>
        new(
            probe,
            appAlreadyRunning ?? (() => false),
            startApp ?? (() => { }),
            delays ?? new ControlledWatcherDelay(),
            WatcherLivenessCoordinator.DefaultPollingInterval,
            logger,
            timeProvider ?? TimeProvider.System);

    private static int Count(string value, string needle) =>
        value.Split(needle, StringSplitOptions.None).Length - 1;

    private sealed class ControlledProbe : IControllerLivenessProbe
    {
        private readonly object _sync = new();
        private readonly Queue<Func<CancellationToken, Task<ControllerLivenessProbeResult>>> _results = new();
        private readonly SemaphoreSlim _calls = new(0);
        private int _active;

        public int CallCount { get; private set; }
        public int MaximumConcurrency { get; private set; }
        public CancellationToken LastCancellation { get; private set; }
        public List<ControllerIdentity> Identities { get; } = [];

        public void Enqueue(ControllerLivenessProbeResult result) =>
            Enqueue(_ => Task.FromResult(result));

        public void Enqueue(Exception error) =>
            Enqueue(_ => Task.FromException<ControllerLivenessProbeResult>(error));

        public ProbeGate EnqueueGate()
        {
            var gate = new ProbeGate();
            Enqueue(token => gate.WaitAsync(token));
            return gate;
        }

        public CancellationProbeGate EnqueueCancellationGate()
        {
            var gate = new CancellationProbeGate();
            Enqueue(token => gate.WaitAsync(token));
            return gate;
        }

        private void Enqueue(Func<CancellationToken, Task<ControllerLivenessProbeResult>> result)
        {
            lock (_sync)
                _results.Enqueue(result);
        }

        public async Task<ControllerLivenessProbeResult> ProbeAsync(
            ControllerIdentity identity,
            CancellationToken cancellationToken)
        {
            Func<CancellationToken, Task<ControllerLivenessProbeResult>> result;
            lock (_sync)
            {
                result = _results.Dequeue();
                CallCount++;
                Identities.Add(identity);
                LastCancellation = cancellationToken;
            }

            var active = Interlocked.Increment(ref _active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            _calls.Release();
            try
            {
                return await result(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (CallCount < count)
                Assert.True(await _calls.WaitAsync(TimeSpan.FromSeconds(2)));
        }
    }

    private sealed class ProbeSessionFactory : IHidInputReportSessionFactory
    {
        private readonly object _sync = new();
        private readonly Queue<Func<HidInputReportSession?>> _results = new();
        private readonly SemaphoreSlim _calls = new(0);
        private int _callCount;

        public void Enqueue(IOException error) => Enqueue(() => throw error);

        public void Enqueue(Stream stream) =>
            Enqueue(() => new HidInputReportSession(stream, 64));

        private void Enqueue(Func<HidInputReportSession?> result)
        {
            lock (_sync)
                _results.Enqueue(result);
        }

        public ValueTask<HidInputReportSession?> OpenAsync(
            ControllerIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Func<HidInputReportSession?> result;
            lock (_sync)
            {
                result = _results.Dequeue();
                _callCount++;
            }

            _calls.Release();
            return ValueTask.FromResult(result());
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (Volatile.Read(ref _callCount) < count)
                Assert.True(await _calls.WaitAsync(TimeSpan.FromSeconds(2)));
        }
    }

    private sealed class ThrowingProbeReadStream(IOException error) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw error;
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) => ValueTask.FromException<int>(error);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ProgressingProbeReadStream : Stream
    {
        private byte _sequence;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Span.Clear();
            buffer.Span[0] = DualSenseReportLivenessObserver.UsbReportId;
            buffer.Span[DualSenseReportLivenessObserver.SequenceOffset] = ++_sequence;
            buffer.Span[DualSenseReportLivenessObserver.SensorTimestampOffset] = _sequence;
            return ValueTask.FromResult(64);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ProbeGate
    {
        private readonly TaskCompletionSource<ControllerLivenessProbeResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(ControllerLivenessProbeResult result) => _completion.TrySetResult(result);

        public Task<ControllerLivenessProbeResult> WaitAsync(CancellationToken cancellationToken) =>
            _completion.Task.WaitAsync(cancellationToken);
    }

    private sealed class CancellationProbeGate
    {
        private readonly TaskCompletionSource _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CancellationObserved => _cancellationObserved.Task;
        public void Release() => _release.TrySetResult();

        public async Task<ControllerLivenessProbeResult> WaitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cancellationObserved.TrySetResult();
            }

            await _release.Task;
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class ControlledWatcherDelay : IWatcherDelay
    {
        private readonly Queue<DelayRequest> _requests = new();
        private readonly SemaphoreSlim _available = new(0);

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            lock (_requests)
                _requests.Enqueue(new DelayRequest(delay, cancellationToken));
            _available.Release();
            lock (_requests)
                return _requests.Last().Completion;
        }

        public async Task<DelayRequest> NextAsync()
        {
            Assert.True(await _available.WaitAsync(TimeSpan.FromSeconds(2)));
            lock (_requests)
                return _requests.Dequeue();
        }
    }

    private sealed class DelayRequest : IDisposable
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public DelayRequest(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delay = delay;
            _registration = cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        }

        public TimeSpan Delay { get; }
        public Task Completion => _completion.Task;
        public void Complete() => _completion.TrySetResult();
        public void Dispose() => _registration.Dispose();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
