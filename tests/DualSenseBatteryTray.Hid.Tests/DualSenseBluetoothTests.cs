using System.Buffers.Binary;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class DualSenseBluetoothTests
{
    [Fact]
    public void Bluetooth_crc_matches_an_independently_computed_fixed_vector()
    {
        var report = Report(5, 100, 5);
        Assert.Equal(0xF548FEF7u, BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(74, 4)));
    }

    [Theory]
    [InlineData(0x05, 55, ConnectionState.Discharging)]
    [InlineData(0x14, 45, ConnectionState.Charging)]
    [InlineData(0x20, 100, ConnectionState.Full)]
    public void Bluetooth_uses_its_own_payload_offset(byte battery, int percentage, ConnectionState state)
    {
        var report = Report(5, 100, battery);
        // Adjacent USB offset must not be interpreted as Bluetooth battery data.
        report[53] = 0xF0;
        Sign(report);
        Assert.True(new HidReportProcessor().TryProcess(report, out var actual));
        Assert.Equal(new BatteryState(percentage, state), actual);
    }

    [Fact]
    public void Corrupted_bluetooth_data_does_not_update_battery_or_liveness()
    {
        var report = Report(5, 100, 0x05);
        report[54] ^= 1;
        Assert.False(new HidReportProcessor().TryProcess(report, out _));
        Assert.Equal(ControllerLivenessObservation.Insufficient,
            new DualSenseReportLivenessObserver().Observe(report));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(64)]
    [InlineData(78)]
    public void Minimal_bluetooth_reports_are_not_battery_reports_even_when_padded(int length)
    {
        var report = new byte[length];
        report[0] = 0x01;
        Assert.False(new HidReportProcessor(bluetooth: true).TryProcess(report, out _));
        Assert.Equal(ControllerLivenessObservation.Insufficient,
            new DualSenseReportLivenessObserver(bluetooth: true).Observe(report));
    }

    [Fact]
    public void Bluetooth_liveness_uses_shifted_sequence_and_timestamp_and_handles_wrap()
    {
        var observer = new DualSenseReportLivenessObserver();
        Assert.Equal(ControllerLivenessObservation.Insufficient, observer.Observe(Report(255, uint.MaxValue, 5)));
        Assert.Equal(ControllerLivenessObservation.Progressing, observer.Observe(Report(0, 0, 5)));
        Assert.Equal(ControllerLivenessObservation.Stale, observer.Observe(Report(0, 0, 5)));
    }

    [Fact]
    public void Usb_never_requests_a_feature_report()
    {
        Assert.True(DualSenseBluetoothInitialization.Initialize(64, 64, _ => throw new InvalidOperationException()));
    }

    [Theory]
    [InlineData(0, 41)]
    [InlineData(64, 64)]
    public void Bluetooth_requests_calibration_once_with_the_advertised_buffer(int advertised, int expected)
    {
        var calls = new List<int>();
        Assert.True(DualSenseBluetoothInitialization.Initialize(78, advertised, length => { calls.Add(length); return true; }));
        Assert.Equal(new[] { expected }, calls);
    }

    [Fact]
    public void Bluetooth_initialization_failure_is_reported_without_retrying()
    {
        var calls = 0;
        Assert.False(DualSenseBluetoothInitialization.Initialize(78, 41, _ => { calls++; return false; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Watcher_detects_bluetooth_progress_without_usb_reconnect()
    {
        var probe = new ControllerLivenessProbe(new BluetoothSessions(
            new QueueReportStream(Report(1, 1, 5), Report(2, 2, 5))), TimeSpan.FromSeconds(1));
        Assert.Equal(ControllerLivenessProbeResult.Progressing,
            await probe.ProbeAsync(ControllerIdentity.UsbDualSense, CancellationToken.None));
    }

    [Fact]
    public async Task Reader_emits_bluetooth_battery_and_stops_when_reports_freeze()
    {
        var time = new ControlledAsyncDelay();
        await using var reader = new DualSenseHidReader(new BluetoothSessions(
            new QueueThenBlockingStream(Report(1, 1, 5))), time, TimeSpan.FromSeconds(2));
        await using var reads = reader.ReadStatesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reads.MoveNextAsync());
        Assert.Equal(new BatteryState(55, ConnectionState.Discharging), reads.Current);
        var pending = reads.MoveNextAsync().AsTask();
        time.Complete(0);
        await Assert.ThrowsAsync<ControllerReportsStaleException>(() => pending);
    }

    internal static byte[] Report(byte sequence, uint timestamp, byte battery)
    {
        var data = new byte[78];
        data[0] = 0x31;
        data[8] = sequence;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(29, 4), timestamp);
        data[54] = battery;
        Sign(data);
        return data;
    }

    private static void Sign(byte[] data) => BinaryPrimitives.WriteUInt32LittleEndian(
        data.AsSpan(74, 4), DualSenseInputReport.CalculateBluetoothCrc(data.AsSpan(0, 74)));

    private sealed class BluetoothSessions(Stream stream) : IHidInputReportSessionFactory
    {
        public ValueTask<HidInputReportSession?> OpenAsync(ControllerIdentity identity, CancellationToken cancellationToken)
            => ValueTask.FromResult<HidInputReportSession?>(new HidInputReportSession(stream, 78));
    }
}
