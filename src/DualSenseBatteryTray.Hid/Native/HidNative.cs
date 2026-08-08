using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DualSenseBatteryTray.Hid.Native;

public static class HidNative
{
    public const uint GenericRead = 0x80000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint RequestedWriteAccess = 0;

    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int HidPStatusSuccess = 0x00110000;

    internal static HidOpenParameters ReadOnlySharedOpenParameters { get; } = new(
        GenericRead,
        FileShareRead | FileShareWrite,
        OpenExisting,
        FileFlagOverlapped);

    internal static HidOpenParameters AttributeProbeOpenParameters { get; } = new(
        RequestedWriteAccess,
        FileShareRead | FileShareWrite,
        OpenExisting,
        0);

    public static SafeFileHandle OpenReadOnlyShared(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Open(path, ReadOnlySharedOpenParameters);
    }

    internal static SafeFileHandle OpenForAttributes(string path) =>
        Open(path, AttributeProbeOpenParameters);

    private static SafeFileHandle Open(string path, HidOpenParameters parameters) =>
        CreateFileW(
            path,
            parameters.DesiredAccess,
            parameters.ShareMode,
            IntPtr.Zero,
            parameters.CreationDisposition,
            parameters.FlagsAndAttributes,
            IntPtr.Zero);

    internal static Guid GetHidClassGuid()
    {
        HidD_GetHidGuid(out var hidGuid);
        return hidGuid;
    }

    internal static bool TryGetAttributes(
        SafeFileHandle handle,
        out ushort vendorId,
        out ushort productId)
    {
        var attributes = new HidAttributes
        {
            Size = checked((uint)Marshal.SizeOf<HidAttributes>())
        };

        if (!HidD_GetAttributes(handle, ref attributes))
        {
            vendorId = 0;
            productId = 0;
            return false;
        }

        vendorId = attributes.VendorId;
        productId = attributes.ProductId;
        return true;
    }

    internal static int GetInputReportByteLength(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsedData))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read HID preparsed data.");

        try
        {
            var capabilities = HidPCaps.Create();
            var status = HidP_GetCaps(preparsedData, ref capabilities);
            if (status != HidPStatusSuccess)
                throw new IOException($"Could not read HID capabilities (status 0x{status:X8}).");

            if (capabilities.InputReportByteLength == 0)
                throw new IOException("The HID device reports a zero-length input report.");

            return capabilities.InputReportByteLength;
        }
        finally
        {
            _ = HidD_FreePreparsedData(preparsedData);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("hid.dll", ExactSpelling = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(
        SafeFileHandle hidDeviceObject,
        ref HidAttributes attributes);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(
        SafeFileHandle hidDeviceObject,
        out IntPtr preparsedData);

    [DllImport("hid.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", ExactSpelling = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, ref HidPCaps capabilities);

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public uint Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidPCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;

        public static HidPCaps Create() => new()
        {
            Reserved = new ushort[17]
        };
    }
}

internal readonly record struct HidOpenParameters(
    uint DesiredAccess,
    uint ShareMode,
    uint CreationDisposition,
    uint FlagsAndAttributes);
