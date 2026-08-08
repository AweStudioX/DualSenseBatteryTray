using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Core.Devices;

public interface IBatteryReportSource
{
    IAsyncEnumerable<BatteryState> ReadStatesAsync(CancellationToken cancellationToken);
}
