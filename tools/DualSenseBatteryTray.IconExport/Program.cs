using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DualSenseBatteryTray.App.Tray;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Pass an output directory.");

        Directory.CreateDirectory(args[0]);
        using (var small = BatteryIconRenderer.RenderPreviewWindowSmallIcon())
        using (var stream = File.Create(Path.Combine(args[0], "window-small.ico")))
            small.Save(stream);
        using (var disconnectedWindow = BatteryIconRenderer.RenderDisconnectedWindowIcon())
        using (var stream = File.Create(Path.Combine(args[0], "window-dualsense-disconnected-light.ico")))
            disconnectedWindow.Save(stream);
        var state = new BatteryState(50, ConnectionState.Discharging);
        foreach (var theme in Enum.GetValues<TrayTheme>())
        {
            var themeName = theme == TrayTheme.DarkTaskbar ? "dark" : "light";
            for (var level = 0; level <= 100; level += 5)
            {
                var battery = new BatteryState(level, ConnectionState.Discharging);
                var percentage = Path.Combine(args[0], $"percentage-{themeName}-{level}.ico");
                using var percentageIcon = BatteryIconRenderer.RenderPercentageTrayIcon(battery, theme);
                using var percentageStream = File.Create(percentage);
                percentageIcon.Save(percentageStream);
            }
            var unknownPercentage = Path.Combine(args[0], $"percentage-{themeName}-unknown.ico");
            using (var unknownIcon = BatteryIconRenderer.RenderPercentageTrayIcon(
                new BatteryState(null, ConnectionState.Unknown), theme))
            using (var unknownStream = File.Create(unknownPercentage))
                unknownIcon.Save(unknownStream);
            using (var disconnected = BatteryIconRenderer.RenderDisconnectedControllerTrayIcon(theme))
            using (var stream = File.Create(Path.Combine(args[0], $"disconnected-{themeName}.ico")))
                disconnected.Save(stream);
            foreach (var kind in Enum.GetValues<ControllerIconKind>())
            {
                var kindName = kind switch
                {
                    ControllerIconKind.DualSense => "dualsense",
                    ControllerIconKind.FlydigiApex => "flydigi-apex",
                    _ => "generic",
                };
                var output = Path.Combine(args[0], $"{kindName}-connected-{themeName}.ico");
                using (var icon = BatteryIconRenderer.RenderReferenceConnectedTrayIcon(theme, kind, false))
                using (var stream = File.Create(output))
                    icon.Save(stream);
                var chargingOutput = Path.Combine(args[0], $"{kindName}-charging-{themeName}.ico");
                using (var icon = BatteryIconRenderer.RenderReferenceConnectedTrayIcon(theme, kind, true))
                using (var stream = File.Create(chargingOutput))
                    icon.Save(stream);

                var windowOutput = Path.Combine(args[0], $"window-{kindName}-connected-light.ico");
                using (var windowIcon = BatteryIconRenderer.RenderConnectedWindowIcon(kind))
                using (var windowStream = File.Create(windowOutput))
                    windowIcon.Save(windowStream);
                for (var level = 0; level <= 100; level += 5)
                {
                    var battery = new BatteryState(level, ConnectionState.Discharging);
                    var compact = Path.Combine(args[0], $"compact-{kindName}-{themeName}-{level}.ico");
                    using var compactIcon = BatteryIconRenderer.RenderCompactTrayIcon(battery, theme, kind);
                    using var compactStream = File.Create(compact);
                    compactIcon.Save(compactStream);
                    var charging = new BatteryState(level, ConnectionState.Charging);
                    var chargingPath = Path.Combine(args[0], $"compact-{kindName}-{themeName}-{level}-charging.ico");
                    using var chargingIcon = BatteryIconRenderer.RenderCompactTrayIcon(charging, theme, kind);
                    using var chargingStream = File.Create(chargingPath);
                    chargingIcon.Save(chargingStream);
                }
                var unknown = Path.Combine(args[0], $"compact-{kindName}-{themeName}-unknown.ico");
                using var unknownIcon = BatteryIconRenderer.RenderCompactTrayIcon(
                    new BatteryState(null, ConnectionState.Unknown), theme, kind);
                using var unknownStream = File.Create(unknown);
                unknownIcon.Save(unknownStream);
                var unknownCharging = Path.Combine(args[0], $"compact-{kindName}-{themeName}-unknown-charging.ico");
                using var unknownChargingIcon = BatteryIconRenderer.RenderCompactTrayIcon(
                    new BatteryState(null, ConnectionState.Charging), theme, kind);
                using var unknownChargingStream = File.Create(unknownCharging);
                unknownChargingIcon.Save(unknownChargingStream);
            }
        }
        LucideIconRenderer.Export(args[0]);
        if (Environment.GetEnvironmentVariable("CONTROLLER_ICON_QA") == "1")
            SaveIconQa(args[0]);
    }

    private static void SaveIconQa(string directory)
    {
        string[] files =
        [
            "dualsense-disconnected.ico",
            "window-small.ico",
            "disconnected-light.ico",
            "dualsense-connected-light.ico",
            "dualsense-charging-light.ico",
            "generic-connected-light.ico",
            "disconnected-dark.ico",
            "dualsense-connected-dark.ico",
            "dualsense-charging-dark.ico",
            "generic-connected-dark.ico",
        ];
        using var canvas = new Bitmap(files.Length * 144, 176);
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.FromArgb(242, 245, 245));
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        for (var index = 0; index < files.Length; index++)
        {
            using var icon = new Icon(Path.Combine(directory, files[index]), 16, 16);
            using var bitmap = icon.ToBitmap();
            var x = index * 144 + 8;
            graphics.FillRectangle(files[index].EndsWith("-dark.ico", StringComparison.Ordinal)
                ? Brushes.Black : Brushes.White, x, 8, 128, 128);
            graphics.DrawImage(bitmap, x, 8, 128, 128);
            graphics.DrawString(files[index].Replace(".ico", ""), SystemFonts.DefaultFont, Brushes.Black,
                new RectangleF(x, 142, 130, 30));
        }
        canvas.Save(Path.Combine(directory, "icon-qa.png"), ImageFormat.Png);

        using var offline = new Icon(Path.Combine(directory, "window-dualsense-disconnected-light.ico"), 256, 256);
        using var connected = new Icon(Path.Combine(directory, "window-dualsense-connected-light.ico"), 256, 256);
        using var comparison = new Bitmap(512, 256);
        using var comparisonGraphics = Graphics.FromImage(comparison);
        using var offlineBitmap = offline.ToBitmap();
        using var connectedBitmap = connected.ToBitmap();
        comparisonGraphics.Clear(Color.White);
        comparisonGraphics.DrawImage(offlineBitmap, 0, 0, 256, 256);
        comparisonGraphics.DrawImage(connectedBitmap, 256, 0, 256, 256);
        comparison.Save(Path.Combine(directory, "window-icon-qa.png"), ImageFormat.Png);

        string[] batteries = ["battery", "battery-low", "battery-medium", "battery-full", "battery-charging"];
        using var batterySheet = new Bitmap(batteries.Length * 160, 178);
        using var batteryGraphics = Graphics.FromImage(batterySheet);
        batteryGraphics.Clear(Color.White);
        batteryGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        for (var index = 0; index < batteries.Length; index++)
        {
            using var icon = new Icon(Path.Combine(directory, $"lucide-{batteries[index]}.ico"), 30, 30);
            using var bitmap = icon.ToBitmap();
            batteryGraphics.DrawImage(bitmap, index * 160 + 5, 5, 150, 150);
            batteryGraphics.DrawString(batteries[index], SystemFonts.DefaultFont, Brushes.Black,
                new RectangleF(index * 160 + 5, 158, 150, 18));
        }
        batterySheet.Save(Path.Combine(directory, "battery-icon-qa.png"), ImageFormat.Png);
    }
}
