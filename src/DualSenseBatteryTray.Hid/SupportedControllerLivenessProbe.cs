using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid;

public sealed class SupportedControllerLivenessProbe : IControllerLivenessProbe
{
    private readonly IXInputApi _xinput;
    private readonly IControllerLivenessProbe _dualSense;

    public SupportedControllerLivenessProbe()
        : this(new WindowsXInputApi(), new ControllerLivenessProbe())
    {
    }

    internal SupportedControllerLivenessProbe(
        IXInputApi xinput,
        IControllerLivenessProbe dualSense)
    {
        _xinput = xinput;
        _dualSense = dualSense;
    }

    public Task<ControllerLivenessProbeResult> ProbeAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Enumerable.Range(0, 4).Any(index => _xinput.TryGetBattery(index, out _)))
            return Task.FromResult(ControllerLivenessProbeResult.Progressing);

        return _dualSense.ProbeAsync(identity, cancellationToken);
    }
}
