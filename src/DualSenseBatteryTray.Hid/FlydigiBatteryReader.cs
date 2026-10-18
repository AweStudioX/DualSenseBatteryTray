using System.Runtime.CompilerServices;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid;

public sealed class FlydigiBatteryReader : IBatteryReportSource
{
    private static readonly int[] Percentages = [5, 20, 40, 60, 80, 100];
    private readonly int _xinputSlot;
    private readonly IFlydigiServiceClient _service;
    private readonly IXInputApi _xinput;
    private readonly IXInputDelay _delay;

    public FlydigiBatteryReader(int xinputSlot)
        : this(
            xinputSlot,
            new FlydigiServiceClient(),
            new WindowsXInputApi(),
            SystemXInputDelay.Instance)
    {
    }

    internal FlydigiBatteryReader(
        int xinputSlot,
        IFlydigiServiceClient service,
        IXInputApi xinput,
        IXInputDelay delay)
    {
        _xinputSlot = xinputSlot;
        _service = service;
        _xinput = xinput;
        _delay = delay;
    }

    public async IAsyncEnumerable<BatteryState> ReadStatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        BatteryState? previous = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_xinput.TryGetBattery(_xinputSlot, out _))
                yield break;

            var device = await _service.QueryControllerAsync(cancellationToken).ConfigureAwait(false);
            var state = Normalize(device);
            if (state != previous)
            {
                previous = state;
                yield return state;
            }
            await _delay.WaitAsync(
                XInputBatteryReader.DefaultPollingInterval,
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal static BatteryState Normalize(FlydigiDeviceState? device)
    {
        if (device is null || !device.Connected)
            return new BatteryState(null, ConnectionState.Unknown);
        if (device.BatteryLevel == 6)
            return new BatteryState(null, ConnectionState.Charging);
        if ((uint)device.BatteryLevel < Percentages.Length)
            return new BatteryState(
                Percentages[device.BatteryLevel],
                device.BatteryLevel == 0 ? ConnectionState.Flat : ConnectionState.Discharging);
        return new BatteryState(null, ConnectionState.Unknown);
    }
}
