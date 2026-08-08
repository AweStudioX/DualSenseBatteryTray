using DualSenseBatteryTray.App.Startup;

namespace DualSenseBatteryTray.App.Tests;

public sealed class WatcherListenerControllerTests
{
    [Fact]
    public async Task ToggleAsync_disables_menu_and_refreshes_status_after_partial_failure()
    {
        var operation = new TaskCompletionSource<WatcherTaskActionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ControlledWatcherTaskService(
            queryResults: [WatcherTaskStatus.Disabled, WatcherTaskStatus.Enabled],
            enableTask: operation.Task);
        var controller = new WatcherListenerController(service);
        await controller.RefreshAsync();

        var toggleTask = controller.ToggleAsync();

        Assert.True(controller.State.IsBusy);
        Assert.False(controller.State.CanToggle);
        operation.SetResult(WatcherTaskActionResult.Failed);

        Assert.Equal(WatcherTaskActionResult.Failed, await toggleTask);
        Assert.False(controller.State.IsBusy);
        Assert.True(controller.State.CanToggle);
        Assert.Equal(WatcherTaskStatus.Enabled, controller.State.Status);
        Assert.True(controller.State.IsEnabled);
        Assert.Equal(["Query", "Enable", "Query"], service.Calls);
    }

    [Fact]
    public async Task RefreshAsync_contains_service_failure_as_unavailable()
    {
        var controller = new WatcherListenerController(new ThrowingWatcherTaskService());

        await controller.RefreshAsync();

        Assert.False(controller.State.IsBusy);
        Assert.True(controller.State.CanToggle);
        Assert.Equal(WatcherTaskStatus.Unavailable, controller.State.Status);
    }

    private sealed class ControlledWatcherTaskService(
        IEnumerable<WatcherTaskStatus> queryResults,
        Task<WatcherTaskActionResult> enableTask) : IWatcherTaskService
    {
        private readonly Queue<WatcherTaskStatus> _queryResults = new(queryResults);

        public List<string> Calls { get; } = [];

        public Task<WatcherTaskStatus> QueryAsync()
        {
            Calls.Add("Query");
            return Task.FromResult(_queryResults.Dequeue());
        }

        public Task<WatcherTaskActionResult> EnableAndStartAsync()
        {
            Calls.Add("Enable");
            return enableTask;
        }

        public Task<WatcherTaskActionResult> DisableAsync()
        {
            Calls.Add("Disable");
            return Task.FromResult(WatcherTaskActionResult.Succeeded);
        }
    }

    private sealed class ThrowingWatcherTaskService : IWatcherTaskService
    {
        public Task<WatcherTaskStatus> QueryAsync() =>
            Task.FromException<WatcherTaskStatus>(new InvalidOperationException());

        public Task<WatcherTaskActionResult> EnableAndStartAsync() =>
            Task.FromException<WatcherTaskActionResult>(new InvalidOperationException());

        public Task<WatcherTaskActionResult> DisableAsync() =>
            Task.FromException<WatcherTaskActionResult>(new InvalidOperationException());
    }
}
