using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Core.Tests;

public sealed class BatteryParserTests
{
    [Theory]
    [InlineData(0x00, 5, ConnectionState.Discharging)]
    [InlineData(0x09, 95, ConnectionState.Discharging)]
    [InlineData(0x0F, 100, ConnectionState.Discharging)]
    [InlineData(0x13, 35, ConnectionState.Charging)]
    [InlineData(0x20, 100, ConnectionState.Full)]
    [InlineData(0xF0, 0, ConnectionState.Flat)]
    public void Parse_maps_known_status(byte batteryByte, int percentage, ConnectionState state)
    {
        var report = new byte[53];
        report[52] = batteryByte;
        Assert.Equal(new BatteryState(percentage, state), BatteryParser.Parse(report));
    }

    [Fact]
    public void Parse_maps_unknown_status_without_percentage()
    {
        var report = new byte[53];
        report[52] = 0xB4;
        Assert.Equal(new BatteryState(null, ConnectionState.Unknown), BatteryParser.Parse(report));
    }

    [Fact]
    public void Parse_rejects_short_report()
    {
        Assert.Throws<ArgumentException>(() => BatteryParser.Parse(new byte[52]));
    }
}
