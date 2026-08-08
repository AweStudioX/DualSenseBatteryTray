using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class HidReportProcessorTests
{
    [Fact]
    public void TryProcess_discards_a_report_without_the_battery_byte()
    {
        var processor = new HidReportProcessor();

        var accepted = processor.TryProcess(
            new byte[BatteryParser.BatteryOffset + 1],
            out var state);

        Assert.False(accepted);
        Assert.Null(state);
    }

    [Fact]
    public void TryProcess_skips_the_report_id_and_parses_a_valid_report()
    {
        var processor = new HidReportProcessor();
        var report = ReportWithBatteryByte(0x14);
        report[0] = 0x01;
        report[BatteryParser.BatteryOffset] = 0xF0;

        var accepted = processor.TryProcess(report, out var state);

        Assert.True(accepted);
        Assert.Equal(new BatteryState(45, ConnectionState.Charging), state);
    }

    [Fact]
    public void TryProcess_suppresses_duplicate_states()
    {
        var processor = new HidReportProcessor();
        var report = ReportWithBatteryByte(0x07);

        Assert.True(processor.TryProcess(report, out _));
        Assert.False(processor.TryProcess(report, out var duplicate));
        Assert.Null(duplicate);
    }

    private static byte[] ReportWithBatteryByte(byte batteryByte)
    {
        var report = new byte[BatteryParser.BatteryOffset + 2];
        report[BatteryParser.BatteryOffset + 1] = batteryByte;
        return report;
    }
}

public sealed class ConsecutiveReadFailureTrackerTests
{
    [Fact]
    public void RecordFailure_throws_an_io_exception_on_the_fifth_consecutive_failure()
    {
        var tracker = new ConsecutiveReadFailureTracker();
        var failure = new IOException("Native read failed.");

        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);

        var error = Assert.Throws<IOException>(() => tracker.RecordFailure(failure));
        Assert.Same(failure, error.InnerException);
    }

    [Fact]
    public void RecordSuccess_resets_the_consecutive_failure_count()
    {
        var tracker = new ConsecutiveReadFailureTracker();
        var failure = new IOException("Native read failed.");

        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);

        tracker.RecordSuccess();

        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);
    }
}
