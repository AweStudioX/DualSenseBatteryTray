namespace DualSenseBatteryTray.Core.Battery;

public static class BatteryParser
{
    public const int BatteryOffset = 52;

    public static BatteryState Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length <= BatteryOffset)
            throw new ArgumentException("DualSense report must contain byte 52.", nameof(report));

        var value = report[BatteryOffset];
        var charge = value & 0x0F;
        var status = value >> 4;
        var percentage = Math.Min(charge * 10 + 5, 100);

        return status switch
        {
            0 => new(percentage, ConnectionState.Discharging),
            1 => new(percentage, ConnectionState.Charging),
            2 => new(100, ConnectionState.Full),
            15 => new(0, ConnectionState.Flat),
            _ => new(null, ConnectionState.Unknown)
        };
    }
}
