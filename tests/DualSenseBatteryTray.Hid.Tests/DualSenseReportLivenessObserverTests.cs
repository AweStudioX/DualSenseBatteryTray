using System.Buffers.Binary;
using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class DualSenseReportLivenessObserverTests
{
    [Fact]
    public void Three_identical_valid_reports_become_stale()
    {
        var observer = new DualSenseReportLivenessObserver(requiredObservations: 3);

        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
        Assert.Equal(ControllerLivenessObservation.Stale, observer.Observe(Report(4, 99)));
    }

    [Theory]
    [InlineData(4, 99u, 5, 99u)]
    [InlineData(4, 99u, 4, 100u)]
    [InlineData(255, uint.MaxValue, 0, 0u)]
    public void Either_changed_field_is_progress(
        byte firstSequence,
        uint firstTimestamp,
        byte nextSequence,
        uint nextTimestamp)
    {
        var observer = new DualSenseReportLivenessObserver(3);
        _ = observer.Observe(Report(firstSequence, firstTimestamp));

        Assert.Equal(
            ControllerLivenessObservation.Progressing,
            observer.Observe(Report(nextSequence, nextTimestamp)));
    }

    [Fact]
    public void Truncated_reports_are_insufficient_and_do_not_count_as_valid_observations()
    {
        var observer = new DualSenseReportLivenessObserver(3);

        Assert.Equal(
            ControllerLivenessObservation.Insufficient,
            observer.Observe(new byte[DualSenseReportLivenessObserver.MinimumReportLength - 1]));
        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
    }

    [Fact]
    public void Unsupported_report_ids_are_insufficient_and_do_not_count_as_valid_observations()
    {
        var observer = new DualSenseReportLivenessObserver(3);
        var unsupported = Report(4, 99);
        unsupported[0] = 0x31;

        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(unsupported));
        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
    }

    [Fact]
    public void Two_valid_observations_remain_insufficient()
    {
        var observer = new DualSenseReportLivenessObserver(3);

        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(4, 99)));
    }

    [Fact]
    public void Progressive_full_battery_reports_are_progressing()
    {
        var observer = new DualSenseReportLivenessObserver(3);

        _ = observer.Observe(Report(4, 99, batteryByte: 0x29));

        Assert.Equal(
            ControllerLivenessObservation.Progressing,
            observer.Observe(Report(5, 100, batteryByte: 0x29)));
    }

    [Fact]
    public void Three_frozen_full_battery_reports_are_stale()
    {
        var observer = new DualSenseReportLivenessObserver(3);

        Assert.Equal(
            ControllerLivenessObservation.Insufficient,
            observer.Observe(Report(4, 99, batteryByte: 0x29)));
        Assert.Equal(
            ControllerLivenessObservation.Insufficient,
            observer.Observe(Report(4, 99, batteryByte: 0x29)));
        Assert.Equal(
            ControllerLivenessObservation.Stale,
            observer.Observe(Report(4, 99, batteryByte: 0x29)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Required_observations_must_be_at_least_two(int requiredObservations)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DualSenseReportLivenessObserver(requiredObservations));
    }

    private static byte[] Report(byte sequence, uint timestamp, byte? batteryByte = null)
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[7] = sequence;
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(28, 4), timestamp);
        if (batteryByte is not null)
            report[BatteryParser.BatteryOffset + 1] = batteryByte.Value;

        return report;
    }
}
