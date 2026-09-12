using System.Text;

namespace DualSenseBatteryTray.Watcher;

public sealed class WatcherEventLogger
{
    internal const long MaximumLogBytes = 1024 * 1024;

    private static readonly HashSet<string> SupportedEvents =
    [
        "adapter.present",
        "controller.live",
        "controller.stale",
        "liveness.failure",
    ];

    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();

    public WatcherEventLogger()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DualSenseBatteryTray",
                "watcher.log"),
            TimeProvider.System)
    {
    }

    internal WatcherEventLogger(string path, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _path = path;
    }

    public void Log(string eventName, Exception? error = null)
    {
        if (!SupportedEvents.Contains(eventName))
            throw new ArgumentOutOfRangeException(nameof(eventName));

        var line = $"timestamp={_timeProvider.GetUtcNow():O} event={eventName}";
        if (error is not null)
            line += $" error={error.GetType().Name}";
        line += Environment.NewLine;

        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
            File.AppendAllText(_path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private void RotateIfNeeded(int pendingBytes)
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length + pendingBytes <= MaximumLogBytes)
            return;

        File.Move(_path, _path + ".1", overwrite: true);
    }
}
