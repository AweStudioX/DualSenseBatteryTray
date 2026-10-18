using System.Runtime.CompilerServices;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid;

internal interface IXInputDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemXInputDelay : IXInputDelay
{
    internal static SystemXInputDelay Instance { get; } = new();

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class XInputBatteryReader : IBatteryReportSource
{
    public static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(2);

    private readonly int _userIndex;
    private readonly IXInputApi _api;
    private readonly IXInputDelay _delay;
    private readonly TimeSpan _pollingInterval;

    public XInputBatteryReader(int userIndex)
        : this(userIndex, new WindowsXInputApi(), SystemXInputDelay.Instance, DefaultPollingInterval)
    {
    }

    internal XInputBatteryReader(
        int userIndex,
        IXInputApi api,
        IXInputDelay delay,
        TimeSpan pollingInterval)
    {
        if ((uint)userIndex >= 4)
            throw new ArgumentOutOfRangeException(nameof(userIndex));
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(delay);
        if (pollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollingInterval));

        _userIndex = userIndex;
        _api = api;
        _delay = delay;
        _pollingInterval = pollingInterval;
    }

    public async IAsyncEnumerable<BatteryState> ReadStatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        BatteryState? previous = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_api.TryGetBattery(_userIndex, out var battery))
                yield break;

            var state = Normalize(battery);
            if (state != previous)
            {
                previous = state;
                yield return state;
            }

            await _delay.WaitAsync(_pollingInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static BatteryState Normalize(XInputBattery battery)
    {
        if (battery.Type == WindowsXInputApi.BatteryTypeWired)
            return new BatteryState(null, ConnectionState.Unknown);

        return battery.Level switch
        {
            WindowsXInputApi.BatteryLevelEmpty => new BatteryState(0, ConnectionState.Flat),
            WindowsXInputApi.BatteryLevelLow => new BatteryState(25, ConnectionState.Discharging),
            WindowsXInputApi.BatteryLevelMedium => new BatteryState(50, ConnectionState.Discharging),
            WindowsXInputApi.BatteryLevelFull => new BatteryState(100, ConnectionState.Discharging),
            _ => new BatteryState(null, ConnectionState.Unknown),
        };
    }
}
