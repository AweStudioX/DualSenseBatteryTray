using System.ComponentModel;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid;

public enum ControllerLivenessProbeResult
{
    AdapterAbsent,
    Insufficient,
    Stale,
    Progressing,
}

public interface IControllerLivenessProbe
{
    Task<ControllerLivenessProbeResult> ProbeAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken);
}

internal sealed class ControllerLivenessProbeException()
    : IOException("The controller liveness probe failed.");

public sealed class ControllerLivenessProbe : IControllerLivenessProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1.5);
    public const int RequiredObservations = 3;

    private readonly IHidInputReportSessionFactory _sessions;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _openGate = new(1, 1);

    public ControllerLivenessProbe()
        : this(new HidInputReportStreamFactory(), DefaultTimeout)
    {
    }

    internal ControllerLivenessProbe(
        IHidInputReportSessionFactory sessions,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        _sessions = sessions;
        _timeout = timeout;
    }

    public async Task<ControllerLivenessProbeResult> ProbeAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        try
        {
            await using var session = await OpenWithinDeadlineAsync(
                    identity,
                    deadline.Token,
                    cancellationToken)
                .ConfigureAwait(false);
            if (session is null)
                return ControllerLivenessProbeResult.AdapterAbsent;

            var observer = new DualSenseReportLivenessObserver(RequiredObservations);
            var buffer = new byte[session.ReportLength];

            while (true)
            {
                var count = await session.Stream
                    .ReadAsync(buffer.AsMemory(), deadline.Token)
                    .ConfigureAwait(false);
                if (count == 0)
                    return ControllerLivenessProbeResult.Insufficient;

                var observation = observer.Observe(buffer.AsSpan(0, count));
                if (observation == ControllerLivenessObservation.Progressing)
                    return ControllerLivenessProbeResult.Progressing;
                if (observation == ControllerLivenessObservation.Stale)
                    return ControllerLivenessProbeResult.Stale;
            }
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return ControllerLivenessProbeResult.Insufficient;
        }
        catch (Exception error) when (error is IOException or Win32Exception)
        {
            throw new ControllerLivenessProbeException();
        }
    }

    private async Task<HidInputReportSession?> OpenWithinDeadlineAsync(
        ControllerIdentity identity,
        CancellationToken deadlineToken,
        CancellationToken callerToken)
    {
        await _openGate.WaitAsync(deadlineToken).ConfigureAwait(false);
        var releaseGate = true;

        try
        {
            var openTask = Task.Run(
                async () => await _sessions
                    .OpenAsync(identity, deadlineToken)
                    .ConfigureAwait(false),
                CancellationToken.None);

            try
            {
                return await AwaitOpenAsync(openTask, deadlineToken, callerToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadlineToken.IsCancellationRequested)
            {
                releaseGate = false;
                _ = DisposeLateSessionAndReleaseGateAsync(openTask);
                throw;
            }
        }
        finally
        {
            if (releaseGate)
                _openGate.Release();
        }
    }

    internal static async Task<T> AwaitOpenAsync<T>(
        Task<T> openTask,
        CancellationToken deadlineToken,
        CancellationToken callerToken)
    {
        try
        {
            return await openTask.WaitAsync(deadlineToken).ConfigureAwait(false);
        }
        catch (Exception) when (callerToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(callerToken);
        }
        catch (OperationCanceledException) when (
            deadlineToken.IsCancellationRequested && openTask.IsFaulted)
        {
            return await openTask.ConfigureAwait(false);
        }
    }

    private async Task DisposeLateSessionAndReleaseGateAsync(
        Task<HidInputReportSession?> openTask)
    {
        try
        {
            var session = await openTask.ConfigureAwait(false);
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            _openGate.Release();
        }
    }
}
