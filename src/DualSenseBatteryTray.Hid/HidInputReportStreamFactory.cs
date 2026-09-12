using System.ComponentModel;
using System.Runtime.InteropServices;
using DualSenseBatteryTray.Core.Devices;
using DualSenseBatteryTray.Hid.Native;

namespace DualSenseBatteryTray.Hid;

internal interface IHidInputReportSessionFactory
{
    ValueTask<HidInputReportSession?> OpenAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken);
}

internal sealed class HidInputReportSession : IAsyncDisposable
{
    internal HidInputReportSession(Stream stream, int reportLength)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reportLength);
        Stream = stream;
        ReportLength = reportLength;
    }

    internal Stream Stream { get; }
    internal int ReportLength { get; }

    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

internal sealed class HidInputReportStreamFactory : IHidInputReportSessionFactory
{
    private readonly HidDeviceEnumerator _enumerator = new();

    public ValueTask<HidInputReportSession?> OpenAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var path = _enumerator.FindPaths(identity).FirstOrDefault();
        cancellationToken.ThrowIfCancellationRequested();
        if (path is null)
            return ValueTask.FromResult<HidInputReportSession?>(null);

        var handle = HidNative.OpenReadOnlyShared(path);
        if (handle.IsInvalid)
        {
            var nativeError = new Win32Exception(Marshal.GetLastWin32Error());
            handle.Dispose();
            throw new IOException(
                "Could not open the DualSense HID device for shared reading.",
                nativeError);
        }

        FileStream? stream = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputReportByteLength = HidNative.GetInputReportByteLength(handle);
            cancellationToken.ThrowIfCancellationRequested();
            stream = new FileStream(
                handle,
                FileAccess.Read,
                bufferSize: inputReportByteLength,
                isAsync: true);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<HidInputReportSession?>(
                new HidInputReportSession(stream, inputReportByteLength));
        }
        catch
        {
            if (stream is null)
                handle.Dispose();
            else
                stream.Dispose();
            throw;
        }
    }
}
