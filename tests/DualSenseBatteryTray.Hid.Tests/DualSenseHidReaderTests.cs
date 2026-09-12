using System.Buffers.Binary;
using DualSenseBatteryTray.Core.Devices;
using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.Hid.Tests;

public sealed class DualSenseHidReaderTests
{
    [Fact]
    public void Injectable_constructor_requires_a_positive_stale_timeout()
    {
        var sessions = new FakeSessionFactory(stream: null);
        var time = new ControlledAsyncDelay();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DualSenseHidReader(sessions, time, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DualSenseHidReader(sessions, time, TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public async Task Frozen_reports_end_with_stale_after_two_seconds()
    {
        var frozen = Report(3, 500, 0x29);
        var stream = new QueueThenBlockingStream(frozen, frozen, frozen);
        var time = new ControlledAsyncDelay();
        await using var reader = new DualSenseHidReader(
            new FakeSessionFactory(stream), time, DualSenseHidReader.DefaultStaleTimeout);
        await using var read = reader.ReadStatesAsync(CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await read.MoveNextAsync());
        var pending = read.MoveNextAsync().AsTask();
        await time.WaitForRequestCountAsync(1);
        time.Complete(0);

        await Assert.ThrowsAsync<ControllerReportsStaleException>(() => pending);
        Assert.Equal(TimeSpan.FromSeconds(2), time.Requests[0].Delay);
    }

    [Fact]
    public async Task Completed_deadline_wins_over_an_infinite_synchronous_frozen_stream()
    {
        using var safetyCancellation = new CancellationTokenSource();
        var frozen = Report(3, 500, 0x29);
        var time = new ControlledAsyncDelay();
        var stream = new SynchronouslyRepeatingReportStream(frozen, readCount =>
        {
            if (readCount == 2)
                time.Complete(0);
            if (readCount == 10_000)
                safetyCancellation.Cancel();
        });
        await using var reader = new DualSenseHidReader(
            new FakeSessionFactory(stream),
            time,
            DualSenseHidReader.DefaultStaleTimeout);
        await using var read = reader.ReadStatesAsync(safetyCancellation.Token).GetAsyncEnumerator();

        Assert.True(await read.MoveNextAsync());
        await Assert.ThrowsAsync<ControllerReportsStaleException>(() =>
            read.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task Stale_does_not_wait_for_a_pending_read_that_ignores_cancellation()
    {
        var stream = new CancellationIgnoringReadStream(Report(3, 500, 0x11));
        var time = new ControlledAsyncDelay();
        await using var reader = new DualSenseHidReader(
            new FakeSessionFactory(stream), time, DualSenseHidReader.DefaultStaleTimeout);
        await using var read = reader.ReadStatesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await read.MoveNextAsync());

        var pending = read.MoveNextAsync().AsTask();
        await stream.WaitUntilBlockedAsync();
        time.Complete(0);

        await Assert.ThrowsAsync<ControllerReportsStaleException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Progressive_reports_reset_the_stale_deadline()
    {
        var stream = new QueueThenBlockingStream(
            Report(3, 500, 0x11),
            Report(4, 501, 0x11));
        var time = new ControlledAsyncDelay();
        await using var reader = new DualSenseHidReader(
            new FakeSessionFactory(stream), time, DualSenseHidReader.DefaultStaleTimeout);
        await using var read = reader.ReadStatesAsync(CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await read.MoveNextAsync());
        var pending = read.MoveNextAsync().AsTask();
        await time.WaitForRequestCountAsync(2);

        Assert.True(time.Requests[0].Canceled);
        Assert.False(pending.IsCompleted);
        time.Complete(1);
        await Assert.ThrowsAsync<ControllerReportsStaleException>(() => pending);
    }

    [Fact]
    public async Task Progressive_read_that_wins_before_deadline_is_not_discarded_by_late_continuation()
    {
        var stream = new ControlledProgressStream(
            Report(3, 500, 0x11),
            Report(4, 501, 0x11));
        var time = new ControlledAsyncDelay();
        var race = new HeldContinuationTaskRace();
        await using var reader = new DualSenseHidReader(
            new FakeSessionFactory(stream),
            time,
            race,
            DualSenseHidReader.DefaultStaleTimeout);
        var read = reader.ReadStatesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await read.MoveNextAsync());

        var pending = read.MoveNextAsync().AsTask();
        await stream.WaitForProgressReadAsync();
        stream.CompleteProgressRead();
        await race.WaitForWinnerAsync();
        time.Complete(0);
        race.ReleaseContinuation();

        await time.WaitForRequestCountAsync(2);
        Assert.False(pending.IsCompleted);
        time.Complete(1);
        await Assert.ThrowsAsync<ControllerReportsStaleException>(() => pending);
    }

    [Fact]
    public async Task Duplicate_battery_values_are_emitted_once_while_liveness_advances()
    {
        var stream = new QueueThenBlockingStream(
            Report(3, 500, 0x11),
            Report(4, 501, 0x11),
            Report(5, 502, 0x11));
        var time = new ControlledAsyncDelay();
        await using var reader = new DualSenseHidReader(
            new FakeSessionFactory(stream), time, DualSenseHidReader.DefaultStaleTimeout);
        await using var read = reader.ReadStatesAsync(CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await read.MoveNextAsync());
        Assert.Equal(new BatteryState(15, ConnectionState.Charging), read.Current);
        var pending = read.MoveNextAsync().AsTask();
        await time.WaitForRequestCountAsync(3);
        time.Complete(2);

        await Assert.ThrowsAsync<ControllerReportsStaleException>(() => pending);
    }

    [Fact]
    public async Task Cancellation_wins_over_stale_classification()
    {
        using var cancellation = new CancellationTokenSource();
        var time = new ControlledAsyncDelay();
        await using var reader = new DualSenseHidReader(
            new FakeSessionFactory(new QueueThenBlockingStream(Report(3, 500, 0x11))),
            time,
            DualSenseHidReader.DefaultStaleTimeout);
        await using var read = reader.ReadStatesAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await read.MoveNextAsync());

        var pending = read.MoveNextAsync().AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Disposal_does_not_report_stale()
    {
        var time = new ControlledAsyncDelay();
        var reader = new DualSenseHidReader(
            new FakeSessionFactory(new DisposeAwareBlockingStream(Report(3, 500, 0x11))),
            time,
            DualSenseHidReader.DefaultStaleTimeout);
        await using var read = reader.ReadStatesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await read.MoveNextAsync());

        var pending = read.MoveNextAsync().AsTask();
        reader.Dispose();

        Assert.False(await pending);
    }

    [Fact]
    public async Task Five_consecutive_io_failures_retain_the_existing_failure_policy()
    {
        var time = new ControlledAsyncDelay();
        await using var reader = new DualSenseHidReader(
            new FakeSessionFactory(new ThrowingReadStream(new IOException("read failed"))),
            time,
            DualSenseHidReader.DefaultStaleTimeout);
        await using var read = reader.ReadStatesAsync(CancellationToken.None).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<IOException>(() => read.MoveNextAsync().AsTask());

        Assert.IsNotType<ControllerReportsStaleException>(error);
        Assert.Equal("HID input failed 5 consecutive times.", error.Message);
    }

    [Fact]
    public void RecordReadOutcome_resets_failures_after_a_positive_short_read()
    {
        var tracker = new ConsecutiveReadFailureTracker();
        var failure = new IOException("Native read failed.");
        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);

        DualSenseHidReader.RecordReadOutcome(bytesRead: 1, tracker);

        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);
    }

    [Fact]
    public void RecordReadOutcome_preserves_end_of_stream_as_the_fifth_failure()
    {
        var tracker = new ConsecutiveReadFailureTracker();
        var failure = new IOException("Native read failed.");
        for (var attempt = 1; attempt < 5; attempt++)
            tracker.RecordFailure(failure);

        var error = Assert.Throws<IOException>(
            () => DualSenseHidReader.RecordReadOutcome(bytesRead: 0, tracker));

        Assert.IsType<EndOfStreamException>(error.InnerException);
    }

    [Fact]
    public void Reader_implements_the_battery_report_source_contract()
    {
        using var reader = new DualSenseHidReader();

        Assert.IsAssignableFrom<IBatteryReportSource>(reader);
    }

    [Fact]
    public async Task ReadStatesAsync_honors_cancellation_before_device_access()
    {
        using var reader = new DualSenseHidReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in reader.ReadStatesAsync(cancellation.Token))
            {
            }
        });
    }

    [Fact]
    public async Task ReadStatesAsync_rejects_use_after_disposal()
    {
        var reader = new DualSenseHidReader();
        reader.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in reader.ReadStatesAsync(CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task Reader_uses_the_injected_shared_session_factory()
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[BatteryParser.BatteryOffset + 1] = 0x11;
        var sessions = new FakeSessionFactory(new QueueReportStream(report));
        await using var reader = new DualSenseHidReader(sessions);

        await foreach (var _ in reader.ReadStatesAsync(CancellationToken.None))
            break;

        Assert.Equal(new[] { ControllerIdentity.UsbDualSense }, sessions.Identities);
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

internal sealed class ControlledAsyncDelay : IAsyncDelay
{
    private readonly List<Request> _requests = new();
    internal IReadOnlyList<Request> Requests => _requests;

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var request = new Request(delay, cancellationToken);
        lock (_requests)
            _requests.Add(request);
        return request.Task;
    }

    internal async Task WaitForRequestCountAsync(int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (_requests)
            {
                if (_requests.Count >= count)
                    return;
            }
            await Task.Yield();
        }
        throw new TimeoutException("Delay request was not created.");
    }

    internal void Complete(int index) => _requests[index].Complete();

    internal sealed class Request
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        internal Request(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delay = delay;
            _registration = cancellationToken.Register(() =>
            {
                Canceled = true;
                _completion.TrySetCanceled(cancellationToken);
            });
        }

        internal TimeSpan Delay { get; }
        internal bool Canceled { get; private set; }
        internal Task Task => _completion.Task;
        internal void Complete()
        {
            _registration.Dispose();
            _completion.TrySetResult();
        }
    }
}

internal sealed class HeldContinuationTaskRace : IAsyncTaskRace
{
    private int _calls;
    private readonly TaskCompletionSource _winnerSelected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _continuation =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Task> WhenAnyAsync(Task first, Task second)
    {
        var winner = await Task.WhenAny(first, second);
        if (Interlocked.Increment(ref _calls) == 1)
            return winner;

        _winnerSelected.TrySetResult();
        await _continuation.Task;
        return winner;
    }

    internal Task WaitForWinnerAsync() =>
        _winnerSelected.Task.WaitAsync(TimeSpan.FromSeconds(5));

    internal void ReleaseContinuation() => _continuation.TrySetResult();
}

internal sealed class SynchronouslyRepeatingReportStream(
    byte[] report,
    Action<int> onRead) : Stream
{
    private int _readCount;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        report.CopyTo(buffer);
        onRead(++_readCount);
        return ValueTask.FromResult(report.Length);
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class CancellationIgnoringReadStream(byte[] firstReport) : Stream
{
    private bool _first = true;
    private readonly TaskCompletionSource _blocked =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _never =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool Disposed { get; private set; }
    internal Task WaitUntilBlockedAsync() => _blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_first)
        {
            _first = false;
            firstReport.CopyTo(buffer);
            return ValueTask.FromResult(firstReport.Length);
        }

        _blocked.TrySetResult();
        return new ValueTask<int>(_never.Task);
    }
    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class ControlledProgressStream : Stream
{
    private readonly byte[] _firstReport;
    private readonly byte[] _progressReport;
    private readonly TaskCompletionSource _progressReadStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _progressRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Memory<byte> _progressBuffer;
    private int _readCount;

    internal ControlledProgressStream(byte[] firstReport, byte[] progressReport)
    {
        _firstReport = firstReport;
        _progressReport = progressReport;
    }

    internal Task WaitForProgressReadAsync() =>
        _progressReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

    internal void CompleteProgressRead()
    {
        _progressReport.CopyTo(_progressBuffer);
        _progressRead.TrySetResult(_progressReport.Length);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_readCount++ == 0)
        {
            _firstReport.CopyTo(buffer);
            return _firstReport.Length;
        }

        if (_readCount == 2)
        {
            _progressBuffer = buffer;
            _progressReadStarted.TrySetResult();
            return await _progressRead.Task;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal class QueueThenBlockingStream(params byte[][] reports) : Stream
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
        if (_reports.TryDequeue(out var report))
        {
            report.CopyTo(buffer);
            return report.Length;
        }
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class DisposeAwareBlockingStream(params byte[][] reports)
    : QueueThenBlockingStream(reports)
{
    private readonly CancellationTokenSource _disposed = new();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposed.Token);
        try
        {
            return await base.ReadAsync(buffer, linked.Token);
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(DisposeAwareBlockingStream));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _disposed.Cancel();
        base.Dispose(disposing);
    }
}
