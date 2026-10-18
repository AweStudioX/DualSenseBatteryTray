using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using DualSenseBatteryTray.App.Tray;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.App.Tests;

public sealed class MultiControllerUiTests
{
    [Fact]
    public void Apex4_window_marks_XInput_battery_as_an_estimate()
    {
        var viewModel = new MainWindowViewModel(ControllerProfile.FlydigiApex4Receiver);

        viewModel.Update(new BatteryState(100, ConnectionState.Discharging));

        Assert.Equal("Flydigi APEX 4 (八爪鱼 4)", viewModel.DeviceName);
        Assert.Equal("≈ 100%", viewModel.Percentage);
    }

    [Fact]
    public void Apex4_tooltip_uses_the_selected_controller_name()
    {
        var tooltip = TrayApplicationContext.FormatToolTip(
            new BatteryState(50, ConnectionState.Discharging),
            ControllerProfile.FlydigiApex4Receiver);

        Assert.Contains("Flydigi APEX 4", tooltip);
        Assert.Contains("≈50%", tooltip);
    }

    [Fact]
    public void Apex4_and_generic_profiles_render_distinct_controller_glyphs()
    {
        var state = new BatteryState(50, ConnectionState.Discharging);
        var vader = BatteryIconRenderer.RenderConnectedControllerGlyph(
            TrayTheme.DarkTaskbar,
            32,
            ControllerIconKind.FlydigiApex);
        var generic = BatteryIconRenderer.RenderConnectedControllerGlyph(
            TrayTheme.DarkTaskbar,
            32,
            ControllerIconKind.GenericGamepad);

        Assert.NotEqual(Hash(vader), Hash(generic));
    }

    private static string Hash(BitmapSource source)
    {
        var stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
}
