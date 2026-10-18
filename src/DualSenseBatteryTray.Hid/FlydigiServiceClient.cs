using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace DualSenseBatteryTray.Hid;

internal sealed record FlydigiDeviceState(
    string Name,
    string DeviceCode,
    bool Connected,
    int BatteryLevel,
    int ConnectionType);

internal interface IFlydigiServiceClient
{
    Task<FlydigiDeviceState?> QueryControllerAsync(CancellationToken cancellationToken);
}

internal sealed class FlydigiServiceClient : IFlydigiServiceClient
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("FDG_PROTOCOL\n");
    private static readonly byte[] GetConnectedDevicesRequest = [0x08, 0x81, 0x20, 0x1A, 0x00];
    private const int GetConnectedDevicesCommand = 4097;
    private const int MaximumPayloadLength = 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    public async Task<FlydigiDeviceState?> QueryControllerAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                "fcs.sock",
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

            var frame = new byte[Header.Length + sizeof(int) + GetConnectedDevicesRequest.Length];
            Header.CopyTo(frame, 0);
            BinaryPrimitives.WriteInt32LittleEndian(
                frame.AsSpan(Header.Length, sizeof(int)),
                GetConnectedDevicesRequest.Length);
            GetConnectedDevicesRequest.CopyTo(frame, Header.Length + sizeof(int));
            await pipe.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);

            for (var message = 0; message < 4; message++)
            {
                var payload = await ReadMessageAsync(pipe, timeout.Token).ConfigureAwait(false);
                var result = ParseResponse(payload);
                if (result.Command == GetConnectedDevicesCommand)
                    return result.Devices.FirstOrDefault(device => device.Connected);
            }

            return null;
        }
        catch (Exception error) when (
            error is IOException
                or TimeoutException
                or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadMessageAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var receivedHeader = new byte[Header.Length];
        await stream.ReadExactlyAsync(receivedHeader, cancellationToken).ConfigureAwait(false);
        if (!receivedHeader.AsSpan().SequenceEqual(Header))
            throw new InvalidDataException("Flydigi service returned an unexpected header.");

        var lengthBytes = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length < 0 || length > MaximumPayloadLength)
            throw new InvalidDataException("Flydigi service returned an invalid payload length.");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    internal static (int Command, IReadOnlyList<FlydigiDeviceState> Devices) ParseResponse(
        ReadOnlySpan<byte> payload)
    {
        var command = 0;
        var devices = new List<FlydigiDeviceState>();
        foreach (var field in ProtobufFields.Parse(payload))
        {
            if (field.Number == 1 && field.Integer is ulong commandValue)
                command = checked((int)commandValue);
            else if (field.Number == 5 && field.Bytes is { } container)
                ParseDeviceContainer(container, devices);
            else if (field.Number == 6 && field.Bytes is { } changed)
                ParseDeviceContainer(changed, devices);
        }
        return (command, devices);
    }

    private static void ParseDeviceContainer(
        byte[] payload,
        ICollection<FlydigiDeviceState> devices)
    {
        foreach (var field in ProtobufFields.Parse(payload))
        {
            if (field.Number != 1 || field.Bytes is null)
                continue;
            devices.Add(ParseDevice(field.Bytes));
        }
    }

    private static FlydigiDeviceState ParseDevice(ReadOnlySpan<byte> payload)
    {
        var name = string.Empty;
        var code = string.Empty;
        var connected = false;
        var battery = -1;
        var connectionType = 0;
        foreach (var field in ProtobufFields.Parse(payload))
        {
            switch (field.Number)
            {
                case 1 when field.Bytes is not null:
                    name = Encoding.UTF8.GetString(field.Bytes);
                    break;
                case 3 when field.Integer is ulong connection:
                    connectionType = checked((int)connection);
                    break;
                case 4 when field.Bytes is not null:
                    code = Encoding.UTF8.GetString(field.Bytes);
                    break;
                case 8 when field.Integer is ulong online:
                    connected = online != 0;
                    break;
                case 9 when field.Integer is ulong level:
                    battery = checked((int)level);
                    break;
            }
        }
        return new FlydigiDeviceState(name, code, connected, battery, connectionType);
    }
}

internal readonly record struct ProtobufField(int Number, ulong? Integer, byte[]? Bytes);

internal static class ProtobufFields
{
    internal static IReadOnlyList<ProtobufField> Parse(ReadOnlySpan<byte> payload)
    {
        var result = new List<ProtobufField>();
        var offset = 0;
        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var number = checked((int)(tag >> 3));
            var wireType = (int)(tag & 7);
            switch (wireType)
            {
                case 0:
                    result.Add(new ProtobufField(number, ReadVarint(payload, ref offset), null));
                    break;
                case 2:
                    var length = checked((int)ReadVarint(payload, ref offset));
                    if (length < 0 || length > payload.Length - offset)
                        throw new InvalidDataException("Invalid protobuf length.");
                    result.Add(new ProtobufField(number, null, payload.Slice(offset, length).ToArray()));
                    offset += length;
                    break;
                case 1:
                    offset = checked(offset + 8);
                    break;
                case 5:
                    offset = checked(offset + 4);
                    break;
                default:
                    throw new InvalidDataException("Unsupported protobuf wire type.");
            }
            if (offset > payload.Length)
                throw new InvalidDataException("Truncated protobuf field.");
        }
        return result;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> payload, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64 && offset < payload.Length; shift += 7)
        {
            var current = payload[offset++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return value;
        }
        throw new InvalidDataException("Invalid protobuf varint.");
    }
}
