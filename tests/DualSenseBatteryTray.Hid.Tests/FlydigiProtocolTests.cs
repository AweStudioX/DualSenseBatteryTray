using System.Text;
using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class FlydigiProtocolTests
{
    [Fact]
    public void Service_response_parser_reads_Vader_identity_connection_and_battery_level()
    {
        var device = Message(
            TextField(1, "Flydigi Vader 4 Pro"),
            VarintField(3, 2),
            TextField(4, "f4"),
            VarintField(8, 1),
            VarintField(9, 4));
        var container = BytesField(1, device);
        var response = Message(VarintField(1, 4097), BytesField(5, container));

        var parsed = FlydigiServiceClient.ParseResponse(response);
        var controller = Assert.Single(parsed.Devices);

        Assert.Equal(4097, parsed.Command);
        Assert.Equal("Flydigi Vader 4 Pro", controller.Name);
        Assert.Equal("f4", controller.DeviceCode);
        Assert.True(controller.Connected);
        Assert.Equal(4, controller.BatteryLevel);
        Assert.Equal(2, controller.ConnectionType);
    }

    [Theory]
    [InlineData(0, 5, ConnectionState.Flat)]
    [InlineData(1, 20, ConnectionState.Discharging)]
    [InlineData(2, 40, ConnectionState.Discharging)]
    [InlineData(3, 60, ConnectionState.Discharging)]
    [InlineData(4, 80, ConnectionState.Discharging)]
    [InlineData(5, 100, ConnectionState.Discharging)]
    public void Flydigi_levels_are_exposed_as_six_coarse_percentages(
        int level,
        int percentage,
        ConnectionState state)
    {
        var result = FlydigiBatteryReader.Normalize(
            new FlydigiDeviceState("Vader", "f4", true, level, 2));

        Assert.Equal(percentage, result.Percentage);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Charging_does_not_invent_a_percentage()
    {
        var result = FlydigiBatteryReader.Normalize(
            new FlydigiDeviceState("Vader", "f4", true, 6, 1));

        Assert.Null(result.Percentage);
        Assert.Equal(ConnectionState.Charging, result.State);
    }

    private static byte[] Message(params byte[][] fields) => fields.SelectMany(x => x).ToArray();

    private static byte[] TextField(int number, string value) =>
        BytesField(number, Encoding.UTF8.GetBytes(value));

    private static byte[] BytesField(int number, byte[] value) =>
        [.. Varint((ulong)(number << 3 | 2)), .. Varint((ulong)value.Length), .. value];

    private static byte[] VarintField(int number, ulong value) =>
        [.. Varint((ulong)(number << 3)), .. Varint(value)];

    private static byte[] Varint(ulong value)
    {
        var result = new List<byte>();
        do
        {
            var current = (byte)(value & 0x7F);
            value >>= 7;
            result.Add(value == 0 ? current : (byte)(current | 0x80));
        } while (value != 0);
        return result.ToArray();
    }
}
