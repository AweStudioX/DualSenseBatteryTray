namespace DualSenseBatteryTray.Hid.Tests;

public sealed class HidOpenContractTests
{
    [Fact]
    public void Native_layer_exposes_read_only_shared_flags()
    {
        Assert.Equal(0x80000000u, Native.HidNative.GenericRead);
        Assert.Equal(0x00000001u, Native.HidNative.FileShareRead);
        Assert.Equal(0x00000002u, Native.HidNative.FileShareWrite);
        Assert.Equal(0u, Native.HidNative.RequestedWriteAccess);
    }

    [Fact]
    public void Native_open_methods_use_the_safe_access_and_share_parameters()
    {
        var sharedRead = Native.HidNative.ReadOnlySharedOpenParameters;
        Assert.Equal(Native.HidNative.GenericRead, sharedRead.DesiredAccess);
        Assert.Equal(
            Native.HidNative.FileShareRead | Native.HidNative.FileShareWrite,
            sharedRead.ShareMode);
        Assert.Equal(3u, sharedRead.CreationDisposition);
        Assert.Equal(0x40000000u, sharedRead.FlagsAndAttributes);

        var attributeProbe = Native.HidNative.AttributeProbeOpenParameters;
        Assert.Equal(Native.HidNative.RequestedWriteAccess, attributeProbe.DesiredAccess);
        Assert.Equal(
            Native.HidNative.FileShareRead | Native.HidNative.FileShareWrite,
            attributeProbe.ShareMode);
        Assert.Equal(3u, attributeProbe.CreationDisposition);
        Assert.Equal(0u, attributeProbe.FlagsAndAttributes);
    }
}
