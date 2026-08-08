using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.App;

internal sealed class BatteryReaderSession : IAsyncDisposable
{
    private readonly Func<IBatteryReportSource> _sourceFactory;
    private readonly Action<BatteryState> _stateChanged;
    private readonly Action<Exception?> _readerEnded;
    private readonly object _disposalGate = new();
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private CancellationTokenSource? _currentCancellation;
    private ReaderLease? _currentLease;
    private Task? _currentTask;
    private Task? _disposalTask;
    private bool _disposed;

    public BatteryReaderSession(
        Func<IBatteryReportSource> sourceFactory,
        Action<BatteryState> stateChanged,
        Action<Exception?> readerEnded)
    {
        _sourceFactory = sourceFactory;
        _stateChanged = stateChanged;
        _readerEnded = readerEnded;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_currentTask is not null)
            throw new InvalidOperationException("A battery reader is already active.");

        StartNewReader();
    }

    public async Task RefreshAsync()
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopCurrentReaderAsync().ConfigureAwait(false);
            StartNewReader();
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public void Cancel()
    {
        _currentCancellation?.Cancel();
        _currentLease?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposalGate)
            return new ValueTask(_disposalTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            await StopCurrentReaderAsync().ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private void StartNewReader()
    {
        var cancellation = new CancellationTokenSource();
        var lease = new ReaderLease(_sourceFactory());
        _currentCancellation = cancellation;
        _currentLease = lease;
        _currentTask = ConsumeAsync(lease, cancellation.Token);
    }

    private async Task StopCurrentReaderAsync()
    {
        var cancellation = _currentCancellation;
        var lease = _currentLease;
        var task = _currentTask;
        _currentCancellation = null;
        _currentLease = null;
        _currentTask = null;

        if (cancellation is null)
            return;

        cancellation.Cancel();
        lease?.Dispose();
        if (task is not null)
            await task.ConfigureAwait(false);
        cancellation.Dispose();
    }

    private async Task ConsumeAsync(ReaderLease lease, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var state in lease.Source
                .ReadStatesAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                _stateChanged(state);
            }

            if (!cancellationToken.IsCancellationRequested)
                _readerEnded(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!cancellationToken.IsCancellationRequested)
                _readerEnded(error);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private sealed class ReaderLease(IBatteryReportSource source) : IDisposable
    {
        private int _disposed;

        public IBatteryReportSource Source { get; } = source;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (Source is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
