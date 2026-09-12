using System.Reflection;
using DualSenseBatteryTray.App.Logging;
using DualSenseBatteryTray.Hid;

namespace DualSenseBatteryTray.App.Tests;

public sealed class BoundedFileLoggerTests
{
    private const long MaximumLogBytes = 1_048_576;

    [Fact]
    public void Stale_entry_contains_only_the_fixed_generic_failure_description()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var logger = new BoundedFileLogger(directory);

            logger.Log("controller.stale", new ControllerReportsStaleException());

            var entry = File.ReadAllText(Path.Combine(directory, "app.log"));
            Assert.Contains("event=controller.stale", entry, StringComparison.Ordinal);
            Assert.Contains(
                "The physical DualSense input report stopped progressing.",
                entry,
                StringComparison.Ordinal);
            Assert.DoesNotContain("VID_", entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PID_", entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("report=", entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("button=", entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("path=", entry, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Log_rotates_at_one_mibibyte_and_retains_exactly_one_backup()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var logger = new BoundedFileLogger(directory);
            var error = new InvalidOperationException(new string('e', 16 * 1024));

            for (var index = 0; index < 128; index++)
                logger.Log("reader.failure", error);

            var activePath = Path.Combine(directory, "app.log");
            var backupPath = Path.Combine(directory, "app.log.1");

            Assert.InRange(new FileInfo(activePath).Length, 1, MaximumLogBytes);
            Assert.True(File.Exists(backupPath));
            Assert.Single(Directory.EnumerateFiles(directory, "app.log.1"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Log_rejects_an_axis_like_event_payload()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var logger = new BoundedFileLogger(directory);

            Assert.Throws<ArgumentException>(() => logger.Log("axisX=32767", null));

            var logMethods = typeof(BoundedFileLogger)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == nameof(BoundedFileLogger.Log));
            var method = Assert.Single(logMethods);
            Assert.Collection(
                method.GetParameters(),
                parameter => Assert.Equal(typeof(string), parameter.ParameterType),
                parameter => Assert.Equal(typeof(Exception), parameter.ParameterType));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"DualSenseBatteryTray.App.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
