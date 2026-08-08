namespace DualSenseBatteryTray.App.Startup;

internal sealed record WatcherListenerState(WatcherTaskStatus Status, bool IsBusy)
{
    public bool IsEnabled => Status == WatcherTaskStatus.Enabled;
    public bool CanToggle => !IsBusy;
}

internal sealed class WatcherListenerController
{
    private readonly IWatcherTaskService _watcherTaskService;

    public WatcherListenerController(IWatcherTaskService watcherTaskService) =>
        _watcherTaskService = watcherTaskService;

    public WatcherListenerState State { get; private set; } =
        new(WatcherTaskStatus.Unavailable, IsBusy: false);

    public event Action<WatcherListenerState>? StateChanged;

    public async Task RefreshAsync()
    {
        if (State.IsBusy)
            return;

        SetState(State with { IsBusy = true });
        WatcherTaskStatus status;
        try
        {
            status = await _watcherTaskService.QueryAsync();
        }
        catch (Exception)
        {
            status = WatcherTaskStatus.Unavailable;
        }

        SetState(new WatcherListenerState(status, IsBusy: false));
    }

    public async Task<WatcherTaskActionResult> ToggleAsync()
    {
        if (State.IsBusy)
            return WatcherTaskActionResult.Failed;

        var disable = State.IsEnabled;
        SetState(State with { IsBusy = true });
        var result = WatcherTaskActionResult.Failed;
        try
        {
            result = disable
                ? await _watcherTaskService.DisableAsync()
                : await _watcherTaskService.EnableAndStartAsync();
        }
        catch (Exception)
        {
        }

        WatcherTaskStatus status;
        try
        {
            status = await _watcherTaskService.QueryAsync();
        }
        catch (Exception)
        {
            status = WatcherTaskStatus.Unavailable;
        }

        SetState(new WatcherListenerState(status, IsBusy: false));
        return result;
    }

    private void SetState(WatcherListenerState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
