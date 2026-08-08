using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class DualSenseHidReaderTests
{
    [Fact]
    public void RecordReadOutcome_resets_failures_after_a_positive_short_read()
    {
        var tracker = new ConsecutiveReadFailureTracker();
        var failure = new IOException("Native read failed.");
        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);

        DualSenseHidReader.RecordReadOutcome(bytesRead: 1, tracker);

        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);
    }

    [Fact]
    public void RecordReadOutcome_preserves_end_of_stream_as_the_fifth_failure()
    {
        var tracker = new ConsecutiveReadFailureTracker();
        var failure = new IOException("Native read failed.");
        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);

        var error = Assert.Throws<IOException>(
            () => DualSenseHidReader.RecordReadOutcome(bytesRead: 0, tracker));

        Assert.IsType<EndOfStreamException>(error.InnerException);
    }

    [Fact]
    public void Reader_implements_the_battery_report_source_contract()
    {
        using var reader = new DualSenseHidReader();

        Assert.IsAssignableFrom<IBatteryReportSource>(reader);
    }

    [Fact]
    public async Task ReadStatesAsync_honors_cancellation_before_device_access()
    {
        using var reader = new DualSenseHidReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in reader.ReadStatesAsync(cancellation.Token))
            {
            }
        });
    }

    [Fact]
    public async Task ReadStatesAsync_rejects_use_after_disposal()
    {
        var reader = new DualSenseHidReader();
        reader.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in reader.ReadStatesAsync(CancellationToken.None))
            {
            }
        });
    }
}
