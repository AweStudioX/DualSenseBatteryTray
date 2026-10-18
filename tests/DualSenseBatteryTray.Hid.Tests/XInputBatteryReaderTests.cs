using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class XInputBatteryReaderTests
{
    [Theory]
    [InlineData(0, 0, ConnectionState.Flat)]
    [InlineData(1, 25, ConnectionState.Discharging)]
    [InlineData(2, 50, ConnectionState.Discharging)]
    [InlineData(3, 100, ConnectionState.Discharging)]
    public void Normalize_maps_the_four_XInput_battery_buckets(
        byte level,
        int percentage,
        ConnectionState state)
    {
        var result = XInputBatteryReader.Normalize(new XInputBattery(2, level));

        Assert.Equal(percentage, result.Percentage);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Normalize_does_not_invent_a_percentage_for_a_wired_controller()
    {
        var result = XInputBatteryReader.Normalize(new XInputBattery(1, 3));

        Assert.Null(result.Percentage);
        Assert.Equal(ConnectionState.Unknown, result.State);
    }

    [Fact]
    public async Task Reader_emits_changes_only_and_ends_when_the_controller_disconnects()
    {
        var api = new SequenceXInputApi(
            new XInputBattery(2, 3),
            new XInputBattery(2, 3),
            new XInputBattery(2, 2),
            null);
        var reader = new XInputBatteryReader(
            0,
            api,
            new ImmediateDelay(),
            TimeSpan.FromMilliseconds(1));

        var states = new List<BatteryState>();
        await foreach (var state in reader.ReadStatesAsync(CancellationToken.None))
            states.Add(state);

        Assert.Equal(
            [new BatteryState(100, ConnectionState.Discharging),
             new BatteryState(50, ConnectionState.Discharging)],
            states);
    }

    private sealed class SequenceXInputApi(params XInputBattery?[] values) : IXInputApi
    {
        private readonly Queue<XInputBattery?> _values = new(values);

        public bool TryGetBattery(int userIndex, out XInputBattery battery)
        {
            var value = _values.Dequeue();
            battery = value ?? default;
            return value.HasValue;
        }
    }

    private sealed class ImmediateDelay : IXInputDelay
    {
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
