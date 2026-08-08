using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DualSenseBatteryTray.Hid.Native;

internal static class SetupApiNative
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;

    internal static IReadOnlyList<string> GetPresentDevicePaths(Guid interfaceClassGuid)
    {
        using var deviceInfoSet = SetupDiGetClassDevsW(
            ref interfaceClassGuid,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate HID devices.");

        var paths = new List<string>();
        for (uint index = 0; ; index++)
        {
            var interfaceData = DeviceInterfaceData.Create();
            if (!SetupDiEnumDeviceInterfaces(
                    deviceInfoSet,
                    IntPtr.Zero,
                    ref interfaceClassGuid,
                    index,
                    ref interfaceData))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreItems)
                    break;

                throw new Win32Exception(error, "Could not enumerate a HID device interface.");
            }

            paths.Add(GetDevicePath(deviceInfoSet, ref interfaceData));
        }

        return paths;
    }

    private static string GetDevicePath(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref DeviceInterfaceData interfaceData)
    {
        _ = SetupDiGetDeviceInterfaceDetailW(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out var requiredSize,
            IntPtr.Zero);

        var error = Marshal.GetLastWin32Error();
        if (requiredSize == 0 || error != ErrorInsufficientBuffer)
            throw new Win32Exception(error, "Could not determine the HID device path length.");

        var detailData = Marshal.AllocHGlobal(checked((int)requiredSize));
        try
        {
            Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetailW(
                    deviceInfoSet,
                    ref interfaceData,
                    detailData,
                    requiredSize,
                    out _,
                    IntPtr.Zero))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not read a HID device path.");
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detailData, sizeof(uint)))
                ?? throw new IOException("SetupAPI returned an empty HID device path.");
        }
        finally
        {
            Marshal.FreeHGlobal(detailData);
        }
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeDeviceInfoSetHandle SetupDiGetClassDevsW(
        ref Guid classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        SafeDeviceInfoSetHandle deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref DeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref DeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;

        public static DeviceInterfaceData Create() => new()
        {
            Size = checked((uint)Marshal.SizeOf<DeviceInterfaceData>())
        };
    }

    private sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeDeviceInfoSetHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }
}
