using System.Diagnostics;
using DualSenseBatteryTray.App.Startup;

namespace DualSenseBatteryTray.App.Tests;

public sealed class WatcherProcessInfrastructureTests
{
    [Fact]
    public void StopInside_reports_path_inspection_failure()
    {
        var process = new FakeWatcherProcess { PathError = new UnauthorizedAccessException() };
        var stopper = new WatcherProcessStopper(new FakeWatcherProcessCatalog(process));

        var result = stopper.StopInside(@"C:\App");

        Assert.Equal(WatcherProcessStopResult.Failed, result);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public void StopInside_reports_timeout_when_target_does_not_exit()
    {
        var process = new FakeWatcherProcess
        {
            Path = @"C:\App\DualSenseBatteryTray.Watcher.exe",
            WaitResult = false,
        };
        var timeout = TimeSpan.FromMilliseconds(250);
        var stopper = new WatcherProcessStopper(
            new FakeWatcherProcessCatalog(process),
            timeout);

        var result = stopper.StopInside(@"C:\App");

        Assert.Equal(WatcherProcessStopResult.Failed, result);
        Assert.True(process.KillCalled);
        Assert.Equal(timeout, Assert.Single(process.WaitTimeouts));
    }

    [Fact]
    public void StopInside_reports_kill_access_failure()
    {
        var process = new FakeWatcherProcess
        {
            Path = @"C:\App\DualSenseBatteryTray.Watcher.exe",
            KillError = new UnauthorizedAccessException(),
        };
        var stopper = new WatcherProcessStopper(new FakeWatcherProcessCatalog(process));

        Assert.Equal(WatcherProcessStopResult.Failed, stopper.StopInside(@"C:\App"));
        Assert.True(process.KillCalled);
    }

    [Fact]
    public void StopInside_reports_failure_when_wait_returns_but_process_is_still_alive()
    {
        var process = new FakeWatcherProcess
        {
            Path = @"C:\App\DualSenseBatteryTray.Watcher.exe",
            WaitResult = true,
            HasExitedValue = false,
        };
        var stopper = new WatcherProcessStopper(new FakeWatcherProcessCatalog(process));

        Assert.Equal(WatcherProcessStopResult.Failed, stopper.StopInside(@"C:\App"));
        Assert.True(process.HasExitedRead);
    }

    [Fact]
    public void StopInside_verifies_successful_target_exit()
    {
        var process = new FakeWatcherProcess
        {
            Path = @"C:\App\DualSenseBatteryTray.Watcher.exe",
            WaitResult = true,
            HasExitedValue = true,
        };
        var stopper = new WatcherProcessStopper(new FakeWatcherProcessCatalog(process));

        var result = stopper.StopInside(@"C:\App");

        Assert.Equal(WatcherProcessStopResult.Succeeded, result);
        Assert.True(process.KillCalled);
        Assert.True(process.HasExitedRead);
    }

    [Fact]
    public async Task DisableAsync_returns_failed_when_real_stopper_cannot_confirm_exit()
    {
        var process = new FakeWatcherProcess
        {
            Path = @"C:\App\DualSenseBatteryTray.Watcher.exe",
            WaitResult = false,
        };
        var stopper = new WatcherProcessStopper(new FakeWatcherProcessCatalog(process));
        var runner = new QueueCommandRunner(
            new ScheduledTaskCommandResult(
                0,
                "<Task><Settings><Enabled>true</Enabled></Settings></Task>",
                TaskMissing: false),
            new ScheduledTaskCommandResult(0, string.Empty, TaskMissing: false));
        var service = new WatcherTaskService(runner, stopper, @"C:\App");

        Assert.Equal(WatcherTaskActionResult.Failed, await service.DisableAsync());
    }

    [Fact]
    public void ScheduledTaskRunner_uses_absolute_system32_path()
    {
        var process = new FakeScheduledTaskProcess(waitResults: [true]);
        var factory = new RecordingScheduledTaskProcessFactory(process);
        var runner = new ScheduledTaskCommandRunner(factory, @"C:\Windows");

        runner.Run("/Query", "/TN", WatcherTaskService.TaskName, "/XML");

        var startInfo = Assert.IsType<ProcessStartInfo>(factory.StartInfo);
        Assert.Equal(@"C:\Windows\System32\schtasks.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            ["/Query", "/TN", WatcherTaskService.TaskName, "/XML"],
            startInfo.ArgumentList.Cast<string>());
    }

    [Fact]
    public void ScheduledTaskRunner_uses_only_bounded_waits_after_timeout()
    {
        var process = new FakeScheduledTaskProcess(waitResults: [false, false]);
        var factory = new RecordingScheduledTaskProcessFactory(process);
        var commandTimeout = TimeSpan.FromMilliseconds(500);
        var killTimeout = TimeSpan.FromMilliseconds(200);
        var runner = new ScheduledTaskCommandRunner(
            factory,
            @"C:\Windows",
            commandTimeout,
            killTimeout);

        Assert.Throws<TimeoutException>(() => runner.Run("/Query"));

        Assert.True(process.KillTreeCalled);
        Assert.Equal(2, process.WaitTimeouts.Count);
        Assert.InRange(process.WaitTimeouts[0], TimeSpan.Zero, commandTimeout);
        Assert.Equal(killTimeout, process.WaitTimeouts[1]);
    }

    [Fact]
    public void ScheduledTaskRunner_reports_failure_when_kill_wait_returns_but_process_is_alive()
    {
        var process = new FakeScheduledTaskProcess(
            waitResults: [false, true],
            hasExited: false);
        var factory = new RecordingScheduledTaskProcessFactory(process);
        var commandTimeout = TimeSpan.FromMilliseconds(500);
        var killTimeout = TimeSpan.FromMilliseconds(200);
        var runner = new ScheduledTaskCommandRunner(
            factory,
            @"C:\Windows",
            commandTimeout,
            killTimeout);

        Assert.Throws<TimeoutException>(() => runner.Run("/Query"));

        Assert.True(process.KillTreeCalled);
        Assert.Equal(2, process.WaitTimeouts.Count);
        Assert.InRange(process.WaitTimeouts[0], TimeSpan.Zero, commandTimeout);
        Assert.Equal(killTimeout, process.WaitTimeouts[1]);
        Assert.True(process.HasExitedRead);
    }

    [Fact]
    public async Task ScheduledTaskRunner_times_out_when_redirected_output_never_completes()
    {
        var output = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new FakeScheduledTaskProcess(
            waitResults: [true],
            standardOutputTask: output.Task);
        var factory = new RecordingScheduledTaskProcessFactory(process);
        var commandTimeout = TimeSpan.FromMilliseconds(75);
        var runner = new ScheduledTaskCommandRunner(
            factory,
            @"C:\Windows",
            commandTimeout,
            TimeSpan.FromMilliseconds(25));
        var stopwatch = Stopwatch.StartNew();

        var runTask = Task.Run(() => Record.Exception(() => runner.Run("/Query")));
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(1)));
        var elapsed = stopwatch.Elapsed;
        output.TrySetResult(string.Empty);
        var error = await runTask;

        Assert.Same(runTask, completed);
        Assert.IsType<TimeoutException>(error);
        Assert.True(elapsed <= commandTimeout + TimeSpan.FromMilliseconds(500));
        Assert.InRange(Assert.Single(process.WaitTimeouts), TimeSpan.Zero, commandTimeout);
        Assert.True(process.DisposeCalled);
    }

    private sealed class FakeWatcherProcessCatalog(params IWatcherProcess[] processes)
        : IWatcherProcessCatalog
    {
        public IReadOnlyList<IWatcherProcess> FindByName(string processName) => processes;
    }

    private sealed class FakeWatcherProcess : IWatcherProcess
    {
        public string? Path { get; init; }
        public Exception? PathError { get; init; }
        public Exception? KillError { get; init; }
        public bool WaitResult { get; init; } = true;
        public bool HasExitedValue { get; init; } = true;
        public bool KillCalled { get; private set; }
        public bool HasExitedRead { get; private set; }
        public List<TimeSpan> WaitTimeouts { get; } = [];

        public string? ExecutablePath =>
            PathError is null ? Path : throw PathError;

        public bool HasExited
        {
            get
            {
                HasExitedRead = true;
                return HasExitedValue;
            }
        }

        public void Kill()
        {
            KillCalled = true;
            if (KillError is not null)
                throw KillError;
        }

        public bool WaitForExit(TimeSpan timeout)
        {
            WaitTimeouts.Add(timeout);
            return WaitResult;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingScheduledTaskProcessFactory(IScheduledTaskProcess process)
        : IScheduledTaskProcessFactory
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public IScheduledTaskProcess Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return process;
        }
    }

    private sealed class FakeScheduledTaskProcess(
        IEnumerable<bool> waitResults,
        bool hasExited = true,
        Task<string>? standardOutputTask = null,
        Task<string>? standardErrorTask = null)
        : IScheduledTaskProcess
    {
        private readonly Queue<bool> _waitResults = new(waitResults);

        public int ExitCode => 0;
        public bool HasExited
        {
            get
            {
                HasExitedRead = true;
                return hasExited;
            }
        }
        public bool HasExitedRead { get; private set; }
        public bool KillTreeCalled { get; private set; }
        public bool DisposeCalled { get; private set; }
        public List<TimeSpan> WaitTimeouts { get; } = [];

        public Task<string> ReadStandardOutputAsync() =>
            standardOutputTask ?? Task.FromResult(string.Empty);

        public Task<string> ReadStandardErrorAsync() =>
            standardErrorTask ?? Task.FromResult(string.Empty);

        public bool WaitForExit(TimeSpan timeout)
        {
            WaitTimeouts.Add(timeout);
            return _waitResults.Dequeue();
        }

        public void KillTree() => KillTreeCalled = true;

        public void Dispose()
        {
            DisposeCalled = true;
        }
    }

    private sealed class QueueCommandRunner(params ScheduledTaskCommandResult[] results)
        : IScheduledTaskCommandRunner
    {
        private readonly Queue<ScheduledTaskCommandResult> _results = new(results);

        public ScheduledTaskCommandResult Run(params string[] arguments) => _results.Dequeue();
    }
}
