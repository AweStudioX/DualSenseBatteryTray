using System.Runtime.CompilerServices;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid;

public sealed class ControllerReportsStaleException()
    : IOException("The physical DualSense input report stopped progressing.");

public sealed class DualSenseHidReader : IBatteryReportSource, IDisposable, IAsyncDisposable
{
    public static readonly TimeSpan DefaultStaleTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly IHidInputReportSessionFactory _sessions;
    private readonly IAsyncDelay _delay;
    private readonly IAsyncTaskRace _taskRace;
    private readonly TimeSpan _staleTimeout;
    private Stream? _activeStream;
    private bool _disposed;

    public DualSenseHidReader()
        : this(
            new HidInputReportStreamFactory(),
            new SystemAsyncDelay(),
            new SystemAsyncTaskRace(),
            DefaultStaleTimeout)
    {
    }

    internal DualSenseHidReader(IHidInputReportSessionFactory sessions)
        : this(sessions, new SystemAsyncDelay(), new SystemAsyncTaskRace(), DefaultStaleTimeout)
    {
    }

    internal DualSenseHidReader(
        IHidInputReportSessionFactory sessions,
        IAsyncDelay delay,
        TimeSpan staleTimeout)
        : this(sessions, delay, new SystemAsyncTaskRace(), staleTimeout)
    {
    }

    internal DualSenseHidReader(
        IHidInputReportSessionFactory sessions,
        IAsyncDelay delay,
        IAsyncTaskRace taskRace,
        TimeSpan staleTimeout)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(taskRace);
        if (staleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(staleTimeout));

        _sessions = sessions;
        _delay = delay;
        _taskRace = taskRace;
        _staleTimeout = staleTimeout;
    }

    public async IAsyncEnumerable<BatteryState> ReadStatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await using var session = await _sessions
            .OpenAsync(ControllerIdentity.UsbDualSense, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
            yield break;

        var stream = session.Stream;

        Activate(stream);

        try
        {
            var buffer = new byte[session.ReportLength];
            var processor = new HidReportProcessor();
            var failures = new ConsecutiveReadFailureTracker();
            var liveness = new DualSenseReportLivenessObserver();
            var deadlineCancellation = new CancellationTokenSource();
            var deadline = _delay.WaitAsync(_staleTimeout, deadlineCancellation.Token);

            try
            {
                while (true)
                {
                    int bytesRead;
                    if (deadline.IsCompleted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsDisposed())
                            break;

                        DisposeStreamAfterStaleDeadline(stream);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsDisposed())
                            break;

                        throw new ControllerReportsStaleException();
                    }

                    using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    var read = ReadReportAsync(
                        stream, buffer.AsMemory(), readCancellation.Token);
                    var completed = await _taskRace
                        .WhenAnyAsync(deadline, read)
                        .ConfigureAwait(false);
                    if (ReferenceEquals(completed, deadline))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsDisposed())
                            break;

                        readCancellation.Cancel();
                        DisposeStreamAfterStaleDeadline(stream);
                        ObserveReadFailure(read);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsDisposed())
                            break;

                        throw new ControllerReportsStaleException();
                    }

                    try
                    {
                        bytesRead = await read.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (ObjectDisposedException) when (IsDisposed())
                    {
                        break;
                    }
                    catch (IOException error)
                    {
                        failures.RecordFailure(error);
                        continue;
                    }

                    RecordReadOutcome(bytesRead, failures);
                    var observation = liveness.Observe(buffer.AsSpan(0, bytesRead));
                    if (observation == ControllerLivenessObservation.Progressing)
                    {
                        deadlineCancellation.Cancel();
                        deadlineCancellation.Dispose();
                        deadlineCancellation = new CancellationTokenSource();
                        deadline = _delay.WaitAsync(
                            _staleTimeout, deadlineCancellation.Token);
                    }

                    if (bytesRead <= BatteryParser.BatteryOffset + 1)
                        continue;

                    if (processor.TryProcess(buffer.AsSpan(0, bytesRead), out var state))
                        yield return state!;
                }
            }
            finally
            {
                deadlineCancellation.Cancel();
                deadlineCancellation.Dispose();
            }
        }
        finally
        {
            Deactivate(stream);
        }
    }

    public void Dispose()
    {
        Stream? stream;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            stream = _activeStream;
            _activeStream = null;
        }

        stream?.Dispose();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal static void RecordReadOutcome(
        int bytesRead,
        ConsecutiveReadFailureTracker failures)
    {
        if (bytesRead == 0)
        {
            failures.RecordFailure(
                new EndOfStreamException("The DualSense HID input stream ended."));
            return;
        }

        failures.RecordSuccess();
    }

    private void Activate(Stream stream)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeStream is not null)
                throw new InvalidOperationException("This reader already has an active HID stream.");

            _activeStream = stream;
        }
    }

    private void Deactivate(Stream stream)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeStream, stream))
                _activeStream = null;
        }
    }

    private bool IsDisposed()
    {
        lock (_gate)
            return _disposed;
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void DisposeStreamAfterStaleDeadline(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void ObserveReadFailure(Task<int> read)
    {
        _ = read.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static async Task<int> ReadReportAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken) =>
        await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
}

internal interface IAsyncDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

internal interface IAsyncTaskRace
{
    Task<Task> WhenAnyAsync(Task first, Task second);
}

internal sealed class SystemAsyncTaskRace : IAsyncTaskRace
{
    public Task<Task> WhenAnyAsync(Task first, Task second) => Task.WhenAny(first, second);
}

internal sealed class HidReportProcessor
{
    private BatteryState? _lastState;

    public bool TryProcess(ReadOnlySpan<byte> report, out BatteryState? state)
    {
        if (report.Length <= BatteryParser.BatteryOffset + 1)
        {
            state = null;
            return false;
        }

        var parsed = BatteryParser.Parse(report[1..]);
        if (parsed == _lastState)
        {
            state = null;
            return false;
        }

        _lastState = parsed;
        state = parsed;
        return true;
    }
}

internal sealed class ConsecutiveReadFailureTracker
{
    private const int MaximumConsecutiveFailures = 5;
    private int _failureCount;

    public void RecordFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        _failureCount++;
        if (_failureCount >= MaximumConsecutiveFailures)
        {
            throw new IOException(
                $"HID input failed {MaximumConsecutiveFailures} consecutive times.",
                failure);
        }
    }

    public void RecordSuccess() => _failureCount = 0;
}
