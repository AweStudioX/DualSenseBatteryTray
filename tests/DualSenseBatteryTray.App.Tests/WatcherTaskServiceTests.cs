using DualSenseBatteryTray.App.Startup;

namespace DualSenseBatteryTray.App.Tests;

public sealed class WatcherTaskServiceTests
{
    [Fact]
    public async Task Query_returns_missing_when_task_7_has_not_registered_the_task()
    {
        var runner = new RecordingCommandRunner(
            new ScheduledTaskCommandResult(1, string.Empty, TaskMissing: true));
        var service = new WatcherTaskService(runner, new RecordingProcessStopper(), @"C:\App");

        var status = await service.QueryAsync();

        Assert.Equal(WatcherTaskStatus.Missing, status);
        Assert.Equal(
            ["/Query", "/TN", WatcherTaskService.TaskName, "/XML"],
            runner.Calls.Single());
    }

    [Fact]
    public async Task EnableAndStart_enables_and_starts_only_the_exact_task_name()
    {
        var runner = new RecordingCommandRunner(
            new ScheduledTaskCommandResult(
                0,
                "<Task><Settings><Enabled>false</Enabled></Settings></Task>",
                TaskMissing: false),
            new ScheduledTaskCommandResult(0, string.Empty, TaskMissing: false),
            new ScheduledTaskCommandResult(0, string.Empty, TaskMissing: false));
        var service = new WatcherTaskService(runner, new RecordingProcessStopper(), @"C:\App");

        var result = await service.EnableAndStartAsync();

        Assert.Equal(WatcherTaskActionResult.Succeeded, result);
        Assert.Equal(
            ["/Change", "/TN", WatcherTaskService.TaskName, "/ENABLE"],
            runner.Calls[1]);
        Assert.Equal(
            ["/Run", "/TN", WatcherTaskService.TaskName],
            runner.Calls[2]);
    }

    [Fact]
    public async Task Disable_stops_only_the_install_directory_watcher_after_disabling_the_task()
    {
        var runner = new RecordingCommandRunner(
            new ScheduledTaskCommandResult(
                0,
                "<Task><Settings><Enabled>true</Enabled></Settings></Task>",
                TaskMissing: false),
            new ScheduledTaskCommandResult(0, string.Empty, TaskMissing: false));
        var stopper = new RecordingProcessStopper();
        var service = new WatcherTaskService(runner, stopper, @"C:\App");

        var result = await service.DisableAsync();

        Assert.Equal(WatcherTaskActionResult.Succeeded, result);
        Assert.Equal(
            ["/Change", "/TN", WatcherTaskService.TaskName, "/DISABLE"],
            runner.Calls[1]);
        Assert.Equal(@"C:\App", Assert.Single(stopper.InstallDirectories));
    }

    [Fact]
    public async Task Query_reads_task_settings_instead_of_trigger_enabled_state()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
              <Settings><Enabled>false</Enabled></Settings>
            </Task>
            """;
        var runner = new RecordingCommandRunner(
            new ScheduledTaskCommandResult(0, xml, TaskMissing: false));
        var service = new WatcherTaskService(runner, new RecordingProcessStopper(), @"C:\App");

        Assert.Equal(WatcherTaskStatus.Disabled, await service.QueryAsync());
    }

    [Fact]
    public async Task Query_reports_non_missing_command_failure_as_unavailable()
    {
        var runner = new RecordingCommandRunner(
            new ScheduledTaskCommandResult(1, string.Empty, TaskMissing: false));
        var service = new WatcherTaskService(runner, new RecordingProcessStopper(), @"C:\App");

        Assert.Equal(WatcherTaskStatus.Unavailable, await service.QueryAsync());
    }

    [Fact]
    public async Task EnableAndStart_contains_command_failures()
    {
        var service = new WatcherTaskService(
            new ThrowingAfterQueryRunner(),
            new RecordingProcessStopper(),
            @"C:\App");

        Assert.Equal(WatcherTaskActionResult.Failed, await service.EnableAndStartAsync());
    }

    [Fact]
    public async Task Disable_contains_process_stop_failures()
    {
        var runner = new RecordingCommandRunner(
            new ScheduledTaskCommandResult(
                0,
                "<Task><Settings><Enabled>true</Enabled></Settings></Task>",
                TaskMissing: false),
            new ScheduledTaskCommandResult(0, string.Empty, TaskMissing: false));
        var service = new WatcherTaskService(runner, new ThrowingProcessStopper(), @"C:\App");

        Assert.Equal(WatcherTaskActionResult.Failed, await service.DisableAsync());
    }

    [Fact]
    public async Task QueryAsync_does_not_block_the_calling_thread()
    {
        using var runner = new BlockingCommandRunner();
        var service = new WatcherTaskService(runner, new RecordingProcessStopper(), @"C:\App");
        var callerThreadId = Environment.CurrentManagedThreadId;

        var queryTask = service.QueryAsync();
        Assert.True(runner.Started.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(queryTask.IsCompleted);

        runner.Release.Set();
        Assert.Equal(WatcherTaskStatus.Disabled, await queryTask);
        Assert.NotEqual(callerThreadId, runner.ExecutionThreadId);
    }

    [Theory]
    [InlineData(@"C:\App\DualSenseBatteryTray.Watcher.exe", @"C:\App", true)]
    [InlineData(@"C:\App-Evil\DualSenseBatteryTray.Watcher.exe", @"C:\App", false)]
    [InlineData(@"C:\App\Other.exe", @"C:\App", false)]
    public void Watcher_path_check_is_bounded_to_the_install_directory(
        string executablePath,
        string installDirectory,
        bool expected)
    {
        Assert.Equal(
            expected,
            WatcherProcessStopper.IsWatcherInsideInstallDirectory(executablePath, installDirectory));
    }

    private sealed class RecordingCommandRunner(params ScheduledTaskCommandResult[] results)
        : IScheduledTaskCommandRunner
    {
        private readonly Queue<ScheduledTaskCommandResult> _results = new(results);

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public ScheduledTaskCommandResult Run(params string[] arguments)
        {
            Calls.Add(arguments);
            return _results.Dequeue();
        }
    }

    private sealed class RecordingProcessStopper : IWatcherProcessStopper
    {
        public List<string> InstallDirectories { get; } = [];

        public WatcherProcessStopResult StopInside(string installDirectory)
        {
            InstallDirectories.Add(installDirectory);
            return WatcherProcessStopResult.Succeeded;
        }
    }

    private sealed class ThrowingAfterQueryRunner : IScheduledTaskCommandRunner
    {
        private int _callCount;

        public ScheduledTaskCommandResult Run(params string[] arguments)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return new ScheduledTaskCommandResult(
                    0,
                    "<Task><Settings><Enabled>false</Enabled></Settings></Task>",
                    TaskMissing: false);
            }

            throw new InvalidOperationException("Task Scheduler unavailable.");
        }
    }

    private sealed class ThrowingProcessStopper : IWatcherProcessStopper
    {
        public WatcherProcessStopResult StopInside(string installDirectory) =>
            throw new InvalidOperationException("Process inventory unavailable.");
    }

    private sealed class BlockingCommandRunner : IScheduledTaskCommandRunner, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int ExecutionThreadId { get; private set; }

        public ScheduledTaskCommandResult Run(params string[] arguments)
        {
            ExecutionThreadId = Environment.CurrentManagedThreadId;
            Started.Set();
            Release.Wait(TimeSpan.FromSeconds(5));
            return new ScheduledTaskCommandResult(
                0,
                "<Task><Settings><Enabled>false</Enabled></Settings></Task>",
                TaskMissing: false);
        }

        public void Dispose()
        {
            Started.Dispose();
            Release.Dispose();
        }
    }
}
