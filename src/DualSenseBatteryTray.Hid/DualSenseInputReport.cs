using System.Buffers.Binary;

namespace DualSenseBatteryTray.Hid;

// Wire layout documented by Sony's Linux hid-playstation driver:
// USB: ID 0x01 + common payload; Bluetooth: ID 0x31 + tag + payload + CRC32.
internal static class DualSenseInputReport
{
    internal const int UsbLength = 64;
    internal const int BluetoothLength = 78;

    internal static bool TryGetPayloadOffset(ReadOnlySpan<byte> report, out int offset, bool bluetooth = false)
    {
        offset = 0;
        if (!bluetooth && report.Length == UsbLength && report[0] == 0x01)
        {
            offset = 1;
            return true;
        }
        if (report.Length != BluetoothLength || report[0] != 0x31)
            return false;

        if (CalculateBluetoothCrc(report[..^4]) != BinaryPrimitives.ReadUInt32LittleEndian(report[^4..]))
            return false;
        offset = 2;
        return true;
    }

    internal static uint CalculateBluetoothCrc(ReadOnlySpan<byte> data)
    {
        var crc = UpdateCrc(uint.MaxValue, 0xA1);
        foreach (var value in data)
            crc = UpdateCrc(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        return crc;
    }
}
