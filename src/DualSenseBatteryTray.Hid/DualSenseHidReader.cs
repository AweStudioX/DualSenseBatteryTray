using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;
using DualSenseBatteryTray.Hid.Native;

namespace DualSenseBatteryTray.Hid;

public sealed class DualSenseHidReader : IBatteryReportSource, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HidDeviceEnumerator _enumerator = new();
    private FileStream? _activeStream;
    private bool _disposed;

    public async IAsyncEnumerable<BatteryState> ReadStatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var path = _enumerator.FindPaths(ControllerIdentity.UsbDualSense).FirstOrDefault();
        if (path is null)
            yield break;

        var handle = HidNative.OpenReadOnlyShared(path);
        if (handle.IsInvalid)
        {
            var nativeError = new Win32Exception(Marshal.GetLastWin32Error());
            handle.Dispose();
            throw new IOException("Could not open the DualSense HID device for shared reading.", nativeError);
        }

        FileStream stream;
        int inputReportByteLength;
        try
        {
            inputReportByteLength = HidNative.GetInputReportByteLength(handle);
            stream = new FileStream(
                handle,
                FileAccess.Read,
                bufferSize: inputReportByteLength,
                isAsync: true);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        try
        {
            Activate(stream);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            var buffer = new byte[inputReportByteLength];
            var processor = new HidReportProcessor();
            var failures = new ConsecutiveReadFailureTracker();

            while (true)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream
                        .ReadAsync(buffer.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ObjectDisposedException) when (IsDisposed())
                {
                    break;
                }
                catch (IOException error)
                {
                    failures.RecordFailure(error);
                    continue;
                }

                RecordReadOutcome(bytesRead, failures);
                if (bytesRead <= BatteryParser.BatteryOffset + 1)
                    continue;

                if (processor.TryProcess(buffer.AsSpan(0, bytesRead), out var state))
                    yield return state!;
            }
        }
        finally
        {
            Deactivate(stream);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        FileStream? stream;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            stream = _activeStream;
            _activeStream = null;
        }

        stream?.Dispose();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal static void RecordReadOutcome(
        int bytesRead,
        ConsecutiveReadFailureTracker failures)
    {
        if (bytesRead == 0)
        {
            failures.RecordFailure(
                new EndOfStreamException("The DualSense HID input stream ended."));
            return;
        }

        failures.RecordSuccess();
    }

    private void Activate(FileStream stream)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeStream is not null)
                throw new InvalidOperationException("This reader already has an active HID stream.");

            _activeStream = stream;
        }
    }

    private void Deactivate(FileStream stream)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeStream, stream))
                _activeStream = null;
        }
    }

    private bool IsDisposed()
    {
        lock (_gate)
            return _disposed;
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal sealed class HidReportProcessor
{
    private BatteryState? _lastState;

    public bool TryProcess(ReadOnlySpan<byte> report, out BatteryState? state)
    {
        if (report.Length <= BatteryParser.BatteryOffset + 1)
        {
            state = null;
            return false;
        }

        var parsed = BatteryParser.Parse(report[1..]);
        if (parsed == _lastState)
        {
            state = null;
            return false;
        }

        _lastState = parsed;
        state = parsed;
        return true;
    }
}

internal sealed class ConsecutiveReadFailureTracker
{
    private const int MaximumConsecutiveFailures = 5;
    private int _failureCount;

    public void RecordFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        _failureCount++;
        if (_failureCount >= MaximumConsecutiveFailures)
        {
            throw new IOException(
                $"HID input failed {MaximumConsecutiveFailures} consecutive times.",
                failure);
        }
    }

    public void RecordSuccess() => _failureCount = 0;
}
