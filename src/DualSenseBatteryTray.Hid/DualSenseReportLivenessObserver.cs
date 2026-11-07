using System.Buffers.Binary;

namespace DualSenseBatteryTray.Hid;

public enum ControllerLivenessObservation
{
    Insufficient,
    Progressing,
    Stale,
}

public sealed class DualSenseReportLivenessObserver
{
    public const byte UsbReportId = 0x01;
    public const int SequenceOffset = 7;
    public const int SensorTimestampOffset = 28;
    public const int MinimumReportLength = SensorTimestampOffset + sizeof(uint);

    private readonly int _requiredObservations;
    private readonly bool _bluetooth;
    private int _validObservations;
    private byte _lastSequence;
    private uint _lastTimestamp;
    private bool _hasPrevious;

    public DualSenseReportLivenessObserver(int requiredObservations = 3, bool bluetooth = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredObservations, 2);
        _requiredObservations = requiredObservations;
        _bluetooth = bluetooth;
    }

    public ControllerLivenessObservation Observe(ReadOnlySpan<byte> report)
    {
        if (!DualSenseInputReport.TryGetPayloadOffset(report, out var payloadOffset, _bluetooth))
            return ControllerLivenessObservation.Insufficient;

        var sequence = report[payloadOffset + SequenceOffset - 1];
        var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(
            report.Slice(payloadOffset + SensorTimestampOffset - 1, sizeof(uint)));
        _validObservations++;

        var progressed = _hasPrevious
            && (sequence != _lastSequence || timestamp != _lastTimestamp);
        _hasPrevious = true;
        _lastSequence = sequence;
        _lastTimestamp = timestamp;

        if (progressed)
            return ControllerLivenessObservation.Progressing;

        return _validObservations >= _requiredObservations
            ? ControllerLivenessObservation.Stale
            : ControllerLivenessObservation.Insufficient;
    }
}
