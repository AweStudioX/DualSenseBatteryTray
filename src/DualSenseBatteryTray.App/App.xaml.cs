using System.Windows;
using DualSenseBatteryTray.App.Logging;
using DualSenseBatteryTray.App.Startup;
using DualSenseBatteryTray.App.Tray;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Notifications;
using DualSenseBatteryTray.Hid;

namespace DualSenseBatteryTray.App;

public partial class App : System.Windows.Application
{
    private readonly LowBatteryAlertTracker _alertTracker = new();
    private SingleInstanceGuard? _singleInstanceGuard;
    private BoundedFileLogger? _logger;
    private MainWindow? _mainWindow;
    private TrayApplicationContext? _trayContext;
    private BatteryReaderSession? _readerSession;
    private Task? _readerDisposalTask;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        _singleInstanceGuard = SingleInstanceGuard.TryAcquire();
        if (_singleInstanceGuard is null)
        {
            Shutdown(0);
            return;
        }

        _logger = new BoundedFileLogger();
        _logger.Log("app.start", null);
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _trayContext = new TrayApplicationContext(
            _mainWindow,
            () => _ = RefreshReaderAsync(),
            () => _ = ShutdownApplicationAsync(),
            new WatcherTaskService(),
            new WindowsTrayThemeProvider(),
            new TrayLayoutPreferenceStore());
        _readerSession = new BatteryReaderSession(
            () => new DualSenseHidReader(),
            OnBatteryState,
            OnReaderEnded);

        _mainWindow.Show();
        _readerSession.Start();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _isShuttingDown = true;
        try
        {
            DisposeReaderOnceAsync().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            _logger?.Log("reader.dispose_failure", error);
        }

        _trayContext?.Dispose();
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        base.OnExit(eventArgs);
    }

    private void OnBatteryState(BatteryState state)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_isShuttingDown || _trayContext is null)
                return;

            _trayContext.UpdateState(state);
            if (_alertTracker.Observe(state) is int threshold)
                _trayContext.NotifyLowBattery(threshold);
        });
    }

    private void OnReaderEnded(Exception? error)
    {
        _logger?.Log(error is null ? "reader.ended" : "reader.failure", error);
        _ = Dispatcher.InvokeAsync(() => _ = ShutdownApplicationAsync());
    }

    private async Task RefreshReaderAsync()
    {
        if (_isShuttingDown || _readerSession is null)
            return;

        try
        {
            await _readerSession.RefreshAsync();
            _logger?.Log("reader.refreshed", null);
        }
        catch (ObjectDisposedException) when (_isShuttingDown)
        {
        }
        catch (Exception error)
        {
            _logger?.Log("reader.refresh_failure", error);
            await ShutdownApplicationAsync();
        }
    }

    private async Task ShutdownApplicationAsync()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        var readerDisposal = DisposeReaderOnceAsync();
        _trayContext?.Dispose();
        _trayContext = null;
        _mainWindow?.BeginShutdown();
        try
        {
            await readerDisposal;
        }
        catch (Exception error)
        {
            _logger?.Log("reader.dispose_failure", error);
        }

        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        Shutdown(0);
    }

    private Task DisposeReaderOnceAsync() =>
        _readerDisposalTask ??= DisposeReaderCoreAsync();

    private async Task DisposeReaderCoreAsync()
    {
        var readerSession = _readerSession;
        _readerSession = null;
        if (readerSession is null)
            return;

        readerSession.Cancel();
        await readerSession.DisposeAsync().ConfigureAwait(false);
    }
}
