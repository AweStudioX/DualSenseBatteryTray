using System.Buffers.Binary;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class ControllerLivenessProbeTests
{
    [Fact]
    public async Task Progressive_reports_return_progressing_even_at_full_battery()
    {
        var sessions = new FakeSessionFactory(new QueueReportStream(
            Report(9, 100, 0x29), Report(10, 101, 0x29)));
        var probe = new ControllerLivenessProbe(sessions, TimeSpan.FromSeconds(1.5));

        var result = await probe.ProbeAsync(
            ControllerIdentity.UsbDualSense, CancellationToken.None);

        Assert.Equal(ControllerLivenessProbeResult.Progressing, result);
        Assert.Equal(new[] { ControllerIdentity.UsbDualSense }, sessions.Identities);
    }

    [Fact]
    public async Task Three_frozen_reports_return_stale()
    {
        var frozen = Report(9, 100, 0x29);
        var probe = new ControllerLivenessProbe(
            new FakeSessionFactory(new QueueReportStream(frozen, frozen, frozen)),
            TimeSpan.FromSeconds(1.5));

        Assert.Equal(
            ControllerLivenessProbeResult.Stale,
            await probe.ProbeAsync(ControllerIdentity.UsbDualSense, CancellationToken.None));
    }

    [Fact]
    public async Task No_matching_path_returns_adapter_absent()
    {
        var probe = new ControllerLivenessProbe(
            new FakeSessionFactory(stream: null), TimeSpan.FromSeconds(1.5));

        Assert.Equal(
            ControllerLivenessProbeResult.AdapterAbsent,
            await probe.ProbeAsync(ControllerIdentity.UsbDualSense, CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_reports_until_deadline_return_insufficient()
    {
        var probe = new ControllerLivenessProbe(
            new FakeSessionFactory(new RepeatingReportStream(new byte[] { 0x02 })),
            TimeSpan.FromMilliseconds(30));

        Assert.Equal(
            ControllerLivenessProbeResult.Insufficient,
            await probe.ProbeAsync(ControllerIdentity.UsbDualSense, CancellationToken.None));
    }

    [Fact]
    public async Task Repeated_probes_share_one_blocking_open_and_dispose_one_late_session()
    {
        var factory = new BlockingOpenSessionFactory();
        var probe = new ControllerLivenessProbe(factory, TimeSpan.FromMilliseconds(30));

        var first = probe.ProbeAsync(
            ControllerIdentity.UsbDualSense, CancellationToken.None);
        await factory.OpenStarted.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            Assert.Equal(
                ControllerLivenessProbeResult.Insufficient,
                await first.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(
                ControllerLivenessProbeResult.Insufficient,
                await probe.ProbeAsync(
                    ControllerIdentity.UsbDualSense,
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(
                ControllerLivenessProbeResult.Insufficient,
                await probe.ProbeAsync(
                    ControllerIdentity.UsbDualSense,
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            factory.ReleaseOpen();
        }

        await factory.SessionDisposed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, factory.OpenCallCount);
        Assert.Equal(1, factory.MaximumConcurrency);
        Assert.Equal(1, factory.DisposedSessionCount);

        Assert.Equal(
            ControllerLivenessProbeResult.Insufficient,
            await probe.ProbeAsync(
                ControllerIdentity.UsbDualSense,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(2, factory.OpenCallCount);
        Assert.Equal(1, factory.MaximumConcurrency);
        Assert.Equal(2, factory.DisposedSessionCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Open_or_read_errors_cross_probe_boundary_as_sanitized_failures(
        bool failDuringOpen)
    {
        const string privateDetail = "path=private report=0102";
        var factory = failDuringOpen
            ? new FakeSessionFactory(new IOException(privateDetail))
            : new FakeSessionFactory(new ThrowingReadStream(new IOException(privateDetail)));
        var probe = new ControllerLivenessProbe(factory, TimeSpan.FromSeconds(1.5));

        var error = await Assert.ThrowsAsync<ControllerLivenessProbeException>(() =>
            probe.ProbeAsync(ControllerIdentity.UsbDualSense, CancellationToken.None));

        Assert.DoesNotContain(privateDetail, error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task External_cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new ControllerLivenessProbe(
            new FakeSessionFactory(new BlockingReadStream()), TimeSpan.FromSeconds(1.5));
        var task = probe.ProbeAsync(ControllerIdentity.UsbDualSense, cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Caller_cancellation_wins_when_open_fault_is_already_available()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellation.Token);
        callerCancellation.Cancel();
        var open = Task.FromException<HidInputReportSession?>(
            new IOException("path=private"));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ControllerLivenessProbe.AwaitOpenAsync(
                open,
                deadline.Token,
                callerCancellation.Token));

        Assert.Equal(callerCancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void Injectable_constructor_requires_a_positive_timeout()
    {
        var sessions = new FakeSessionFactory(stream: null);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ControllerLivenessProbe(sessions, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ControllerLivenessProbe(sessions, TimeSpan.FromTicks(-1)));
    }

    private static byte[] Report(byte sequence, uint timestamp, byte battery)
    {
        var report = new byte[64];
        report[0] = DualSenseReportLivenessObserver.UsbReportId;
        report[DualSenseReportLivenessObserver.SequenceOffset] = sequence;
        BinaryPrimitives.WriteUInt32LittleEndian(
            report.AsSpan(DualSenseReportLivenessObserver.SensorTimestampOffset, sizeof(uint)),
            timestamp);
        report[BatteryParser.BatteryOffset + 1] = battery;
        return report;
    }
}

internal sealed class BlockingOpenSessionFactory : IHidInputReportSessionFactory
{
    private readonly ManualResetEventSlim _release = new(initialState: false);
    private readonly TaskCompletionSource _openStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _sessionDisposed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private int _disposedSessionCount;
    private int _maximumConcurrency;
    private int _openCallCount;

    internal Task OpenStarted => _openStarted.Task;
    internal Task SessionDisposed => _sessionDisposed.Task;
    internal int DisposedSessionCount => Volatile.Read(ref _disposedSessionCount);
    internal int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);
    internal int OpenCallCount => Volatile.Read(ref _openCallCount);

    public ValueTask<HidInputReportSession?> OpenAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _openCallCount);
        var active = Interlocked.Increment(ref _active);
        InterlockedExtensions.Max(ref _maximumConcurrency, active);
        _openStarted.TrySetResult();
        try
        {
            _release.Wait();
            return ValueTask.FromResult<HidInputReportSession?>(
                new HidInputReportSession(
                    new DisposalTrackingStream(
                        _sessionDisposed,
                        () => Interlocked.Increment(ref _disposedSessionCount)),
                    64));
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    internal void ReleaseOpen() => _release.Set();
}

internal sealed class DisposalTrackingStream(
    TaskCompletionSource disposed,
    Action onDisposed) : MemoryStream
{
    private int _disposed;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        onDisposed();
        disposed.TrySetResult();
    }
}

internal static class InterlockedExtensions
{
    internal static void Max(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }
}

internal sealed class FakeSessionFactory : IHidInputReportSessionFactory
{
    private readonly Stream? _stream;
    private readonly IOException? _openError;

    internal FakeSessionFactory(Stream? stream) => _stream = stream;
    internal FakeSessionFactory(IOException openError) => _openError = openError;

    internal List<ControllerIdentity> Identities { get; } = new();

    public ValueTask<HidInputReportSession?> OpenAsync(
        ControllerIdentity identity,
        CancellationToken cancellationToken)
    {
        Identities.Add(identity);
        cancellationToken.ThrowIfCancellationRequested();
        if (_openError is not null)
            throw _openError;

        return ValueTask.FromResult(
            _stream is null ? null : new HidInputReportSession(_stream, 64));
    }
}

internal sealed class QueueReportStream(params byte[][] reports) : Stream
{
    private readonly Queue<byte[]> _reports = new(reports);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (_reports.Count == 0)
            return 0;
        var report = _reports.Dequeue();
        report.CopyTo(buffer);
        return report.Length;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class RepeatingReportStream(byte[] report) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        report.CopyTo(buffer);
        return report.Length;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class ThrowingReadStream(IOException error) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw error;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(error);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class BlockingReadStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
