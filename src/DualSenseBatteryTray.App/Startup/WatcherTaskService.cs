using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace DualSenseBatteryTray.App.Startup;

internal enum WatcherTaskStatus { Missing, Disabled, Enabled, Unavailable }

internal enum WatcherTaskActionResult { Succeeded, TaskMissing, Failed }

internal interface IWatcherTaskService
{
    Task<WatcherTaskStatus> QueryAsync();
    Task<WatcherTaskActionResult> EnableAndStartAsync();
    Task<WatcherTaskActionResult> DisableAsync();
}

internal sealed class WatcherTaskService : IWatcherTaskService
{
    public const string TaskName = "DualSenseBatteryTray-DeviceWatcher";

    private readonly IScheduledTaskCommandRunner _commandRunner;
    private readonly IWatcherProcessStopper _processStopper;
    private readonly string _installDirectory;

    public WatcherTaskService()
        : this(new ScheduledTaskCommandRunner(), new WatcherProcessStopper(), AppContext.BaseDirectory)
    {
    }

    internal WatcherTaskService(
        IScheduledTaskCommandRunner commandRunner,
        IWatcherProcessStopper processStopper,
        string installDirectory)
    {
        _commandRunner = commandRunner;
        _processStopper = processStopper;
        _installDirectory = installDirectory;
    }

    public Task<WatcherTaskStatus> QueryAsync() => Task.Run(QueryCore);

    public Task<WatcherTaskActionResult> EnableAndStartAsync() =>
        Task.Run(EnableAndStartCore);

    public Task<WatcherTaskActionResult> DisableAsync() => Task.Run(DisableCore);

    private WatcherTaskStatus QueryCore()
    {
        try
        {
            var result = _commandRunner.Run("/Query", "/TN", TaskName, "/XML");
            if (result.ExitCode != 0)
            {
                return result.TaskMissing
                    ? WatcherTaskStatus.Missing
                    : WatcherTaskStatus.Unavailable;
            }

            var document = XDocument.Parse(result.Output);
            var root = document.Root;
            if (root is null)
                return WatcherTaskStatus.Unavailable;

            var taskNamespace = root.Name.Namespace;
            var enabled = root
                .Element(taskNamespace + "Settings")
                ?.Element(taskNamespace + "Enabled")
                ?.Value;
            return bool.TryParse(enabled, out var isEnabled)
                ? isEnabled ? WatcherTaskStatus.Enabled : WatcherTaskStatus.Disabled
                : WatcherTaskStatus.Unavailable;
        }
        catch (Exception)
        {
            return WatcherTaskStatus.Unavailable;
        }
    }

    private WatcherTaskActionResult EnableAndStartCore()
    {
        try
        {
            var status = QueryCore();
            if (status == WatcherTaskStatus.Missing)
                return WatcherTaskActionResult.TaskMissing;
            if (status == WatcherTaskStatus.Unavailable)
                return WatcherTaskActionResult.Failed;

            var enable = _commandRunner.Run("/Change", "/TN", TaskName, "/ENABLE");
            if (enable.ExitCode != 0)
                return WatcherTaskActionResult.Failed;

            var start = _commandRunner.Run("/Run", "/TN", TaskName);
            return start.ExitCode == 0
                ? WatcherTaskActionResult.Succeeded
                : WatcherTaskActionResult.Failed;
        }
        catch (Exception)
        {
            return WatcherTaskActionResult.Failed;
        }
    }

    private WatcherTaskActionResult DisableCore()
    {
        try
        {
            var status = QueryCore();
            if (status == WatcherTaskStatus.Missing)
                return WatcherTaskActionResult.TaskMissing;
            if (status == WatcherTaskStatus.Unavailable)
                return WatcherTaskActionResult.Failed;

            var disable = _commandRunner.Run("/Change", "/TN", TaskName, "/DISABLE");
            if (disable.ExitCode != 0)
                return WatcherTaskActionResult.Failed;

            return _processStopper.StopInside(_installDirectory) == WatcherProcessStopResult.Succeeded
                ? WatcherTaskActionResult.Succeeded
                : WatcherTaskActionResult.Failed;
        }
        catch (Exception)
        {
            return WatcherTaskActionResult.Failed;
        }
    }
}

internal sealed record ScheduledTaskCommandResult(
    int ExitCode,
    string Output,
    bool TaskMissing);

internal interface IScheduledTaskCommandRunner
{
    ScheduledTaskCommandResult Run(params string[] arguments);
}

internal sealed class ScheduledTaskCommandRunner : IScheduledTaskCommandRunner
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultKillWaitTimeout = TimeSpan.FromSeconds(2);
    private readonly IScheduledTaskProcessFactory _processFactory;
    private readonly string _executablePath;
    private readonly TimeSpan _commandTimeout;
    private readonly TimeSpan _killWaitTimeout;

    public ScheduledTaskCommandRunner()
        : this(
            new ScheduledTaskProcessFactory(),
            Environment.GetEnvironmentVariable("SystemRoot") ??
                Environment.GetFolderPath(Environment.SpecialFolder.Windows))
    {
    }

    internal ScheduledTaskCommandRunner(
        IScheduledTaskProcessFactory processFactory,
        string windowsDirectory,
        TimeSpan? commandTimeout = null,
        TimeSpan? killWaitTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDirectory);
        if (!Path.IsPathFullyQualified(windowsDirectory))
            throw new ArgumentException("The Windows directory must be absolute.", nameof(windowsDirectory));

        _processFactory = processFactory;
        _executablePath = Path.GetFullPath(
            Path.Combine(windowsDirectory, "System32", "schtasks.exe"));
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        _killWaitTimeout = killWaitTimeout ?? DefaultKillWaitTimeout;
    }

    public ScheduledTaskCommandResult Run(params string[] arguments)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var startInfo = new ProcessStartInfo(_executablePath)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = _processFactory.Start(startInfo);
        var outputTask = process.ReadStandardOutputAsync();
        var errorTask = process.ReadStandardErrorAsync();
        if (!process.WaitForExit(RemainingCommandTime(startedAt)))
        {
            process.KillTree();
            if (!process.WaitForExit(_killWaitTimeout) || !process.HasExited)
            {
                throw new TimeoutException(
                    "schtasks.exe timed out and did not exit after bounded termination.");
            }

            throw new TimeoutException("schtasks.exe did not finish before the command deadline.");
        }
        if (!process.HasExited)
            throw new InvalidOperationException("schtasks.exe reported completion but is still running.");

        var drainTask = Task.WhenAll(outputTask, errorTask);
        if (!WaitForCompletion(drainTask, RemainingCommandTime(startedAt)))
        {
            throw new TimeoutException(
                "schtasks.exe exited but redirected output did not drain before the command deadline.");
        }

        // Completion was established within the remaining deadline, so result access cannot block.
        var streams = drainTask.GetAwaiter().GetResult();
        var output = streams[0];
        var error = streams[1];
        return new ScheduledTaskCommandResult(
            process.ExitCode,
            output,
            process.ExitCode != 0 && IndicatesMissingTask(error));
    }

    private TimeSpan RemainingCommandTime(long startedAt)
    {
        var remaining = _commandTimeout - Stopwatch.GetElapsedTime(startedAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static bool WaitForCompletion(Task task, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            return false;
        if (task.IsCompleted)
            return true;

        return Task.WaitAny([task], timeout) == 0;
    }

    private static bool IndicatesMissingTask(string error) =>
        error.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("找不到", StringComparison.Ordinal);
}

internal interface IScheduledTaskProcessFactory
{
    IScheduledTaskProcess Start(ProcessStartInfo startInfo);
}

internal interface IScheduledTaskProcess : IDisposable
{
    int ExitCode { get; }
    bool HasExited { get; }
    Task<string> ReadStandardOutputAsync();
    Task<string> ReadStandardErrorAsync();
    bool WaitForExit(TimeSpan timeout);
    void KillTree();
}

internal sealed class ScheduledTaskProcessFactory : IScheduledTaskProcessFactory
{
    public IScheduledTaskProcess Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Could not start schtasks.exe.");

            return new ScheduledTaskProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}

internal sealed class ScheduledTaskProcess(Process process) : IScheduledTaskProcess
{
    public int ExitCode => process.ExitCode;
    public bool HasExited => process.HasExited;

    public Task<string> ReadStandardOutputAsync() => process.StandardOutput.ReadToEndAsync();
    public Task<string> ReadStandardErrorAsync() => process.StandardError.ReadToEndAsync();
    public bool WaitForExit(TimeSpan timeout) => process.WaitForExit(ToMilliseconds(timeout));
    public void KillTree() => process.Kill(entireProcessTree: true);
    public void Dispose() => process.Dispose();

    private static int ToMilliseconds(TimeSpan timeout) => checked((int)Math.Ceiling(timeout.TotalMilliseconds));
}

internal enum WatcherProcessStopResult { Succeeded, Failed }

internal interface IWatcherProcessStopper
{
    WatcherProcessStopResult StopInside(string installDirectory);
}

internal sealed class WatcherProcessStopper : IWatcherProcessStopper
{
    private const string WatcherProcessName = "DualSenseBatteryTray.Watcher";
    private const string WatcherExecutableName = "DualSenseBatteryTray.Watcher.exe";
    private static readonly TimeSpan DefaultExitTimeout = TimeSpan.FromSeconds(2);
    private readonly IWatcherProcessCatalog _processCatalog;
    private readonly TimeSpan _exitTimeout;

    public WatcherProcessStopper()
        : this(new WatcherProcessCatalog(), DefaultExitTimeout)
    {
    }

    internal WatcherProcessStopper(
        IWatcherProcessCatalog processCatalog,
        TimeSpan? exitTimeout = null)
    {
        _processCatalog = processCatalog;
        _exitTimeout = exitTimeout ?? DefaultExitTimeout;
    }

    public WatcherProcessStopResult StopInside(string installDirectory)
    {
        IReadOnlyList<IWatcherProcess> processes;
        try
        {
            processes = _processCatalog.FindByName(WatcherProcessName);
        }
        catch (Exception)
        {
            return WatcherProcessStopResult.Failed;
        }

        var failed = false;
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var executablePath = process.ExecutablePath;
                    if (executablePath is null)
                    {
                        failed = true;
                        continue;
                    }
                    if (!IsWatcherInsideInstallDirectory(executablePath, installDirectory))
                        continue;

                    process.Kill();
                    if (!process.WaitForExit(_exitTimeout) || !process.HasExited)
                        failed = true;
                }
                catch (Exception)
                {
                    failed = true;
                }
            }
        }

        return failed ? WatcherProcessStopResult.Failed : WatcherProcessStopResult.Succeeded;
    }

    internal static bool IsWatcherInsideInstallDirectory(
        string executablePath,
        string installDirectory)
    {
        var fullExecutablePath = Path.GetFullPath(executablePath);
        var fullInstallDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installDirectory));
        return string.Equals(
                Path.GetFileName(fullExecutablePath),
                WatcherExecutableName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Path.GetDirectoryName(fullExecutablePath),
                fullInstallDirectory,
                StringComparison.OrdinalIgnoreCase);
    }
}

internal interface IWatcherProcessCatalog
{
    IReadOnlyList<IWatcherProcess> FindByName(string processName);
}

internal interface IWatcherProcess : IDisposable
{
    string? ExecutablePath { get; }
    bool HasExited { get; }
    void Kill();
    bool WaitForExit(TimeSpan timeout);
}

internal sealed class WatcherProcessCatalog : IWatcherProcessCatalog
{
    public IReadOnlyList<IWatcherProcess> FindByName(string processName) =>
        Process.GetProcessesByName(processName)
            .Select(process => (IWatcherProcess)new WatcherProcess(process))
            .ToArray();
}

internal sealed class WatcherProcess(Process process) : IWatcherProcess
{
    public string? ExecutablePath => process.MainModule?.FileName;
    public bool HasExited => process.HasExited;
    public void Kill() => process.Kill(entireProcessTree: false);
    public bool WaitForExit(TimeSpan timeout) =>
        process.WaitForExit(checked((int)Math.Ceiling(timeout.TotalMilliseconds)));
    public void Dispose() => process.Dispose();
}
