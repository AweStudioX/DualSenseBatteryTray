using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DualSenseBatteryTray.Core.Battery;
using DrawingColor = System.Drawing.Color;
using DrawingIcon = System.Drawing.Icon;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;

namespace DualSenseBatteryTray.App.Tray;

internal enum ControllerGlyphDetail { Minimal, Full }

public static class BatteryIconRenderer
{
    internal readonly record struct PercentageLayers(BitmapSource Number, BitmapSource Percent);

    private const string FluentGames16FilledPath = "M5.50195 3C3.01667 3 1.00195 5.01472 1.00195 7.5C1.00195 9.98528 3.01667 12 5.50195 12H10.5104C12.9957 12 15.0104 9.98528 15.0104 7.5C15.0104 5.01472 12.9957 3 10.5104 3H5.50195ZM3.50391 7.5C3.50391 7.22386 3.72776 7 4.00391 7H5.00391V6C5.00391 5.72386 5.22776 5.5 5.50391 5.5C5.78005 5.5 6.00391 5.72386 6.00391 6V7H7.00391C7.28005 7 7.50391 7.22386 7.50391 7.5C7.50391 7.77614 7.28005 8 7.00391 8H6.00391V9C6.00391 9.27614 5.78005 9.5 5.50391 9.5C5.22776 9.5 5.00391 9.27614 5.00391 9V8H4.00391C3.72776 8 3.50391 7.77614 3.50391 7.5ZM11 9C11 9.55228 10.5523 10 10 10C9.44772 10 9 9.55228 9 9C9 8.44772 9.44772 8 10 8C10.5523 8 11 8.44772 11 9ZM11 7C10.4477 7 10 6.55228 10 6C10 5.44772 10.4477 5 11 5C11.5523 5 12 5.44772 12 6C12 6.55228 11.5523 7 11 7Z";
    private const string FluentGames16RegularOuterShellPath =
        "M1.00098 7.5C1.00098 5.01472 3.0157 3 5.50098 3H10.5094C12.9947 3 15.0094 5.01472 15.0094 7.5C15.0094 9.98528 12.9947 12 10.5094 12H5.50098C3.0157 12 1.00098 9.98528 1.00098 7.5Z " +
        "M5.50098 4C3.56798 4 2.00098 5.567 2.00098 7.5C2.00098 9.433 3.56798 11 5.50098 11H10.5094C12.4424 11 14.0094 9.433 14.0094 7.5C14.0094 5.567 12.4424 4 10.5094 4H5.50098Z";
    private const string ChargingPath = "M8.25 4.5 6.5 7.75H8L7.25 10.75 10 7H8.5L9.5 4.5Z";
    private const string MergedChargingPath =
        "M3.25 11 1.75 12.75H3L2.5 14.5 4.75 12H3.5L4.25 11Z";
    private const string RefinedDualSenseShellPath =
        "M3.25 4.15 C2.7 4.3 2.4 5 2.1 5.8 " +
        "L1.15 9.25 C0.73 10.9 0.97 12.3 2.05 12.7 " +
        "C2.87 13 3.5 12.5 3.9 11.55 L4.82 9.5 " +
        "C5.02 9.05 5.37 8.85 5.92 8.85 H10.08 " +
        "C10.63 8.85 10.98 9.05 11.18 9.5 L12.1 11.55 " +
        "C12.5 12.5 13.13 13 13.95 12.7 " +
        "C15.03 12.3 15.27 10.9 14.85 9.25 L13.9 5.8 " +
        "C13.6 5 13.3 4.3 12.75 4.15 L10.7 3.8 H5.3 Z";
    private const string RefinedDualSenseShouldersPath =
        "M3.25 4.25 L3.5 3.3 L5.05 3.05 L5.2 3.9 " +
        "M10.8 3.9 L10.95 3.05 L12.5 3.3 L12.75 4.25";
    private const string RefinedDualSenseTouchpadPath =
        "M5.35 3.85 H10.65 L10.3 6.1 " +
        "C10.22 6.65 9.88 6.92 9.35 6.92 H6.65 " +
        "C6.12 6.92 5.78 6.65 5.7 6.1 Z";
    private static readonly int[] NativeIconSizes = [16, 24, 32, 48];
    private static readonly DrawingColor Outline = DrawingColor.FromArgb(255, 20, 22, 27);
    private static readonly DrawingColor White = DrawingColor.FromArgb(255, 255, 255, 255);
    private static readonly DrawingColor Green = DrawingColor.FromArgb(255, 46, 204, 113);
    private static readonly DrawingColor Amber = DrawingColor.FromArgb(255, 243, 156, 18);
    private static readonly DrawingColor Red = DrawingColor.FromArgb(255, 231, 76, 60);
    private static readonly DrawingColor Unknown = DrawingColor.FromArgb(255, 189, 195, 199);
    private static readonly DrawingColor Lightning = DrawingColor.FromArgb(255, 255, 213, 74);
    private static readonly MediaColor OutlineMedia = MediaColor.FromArgb(255, 20, 22, 27);
    private static readonly MediaColor LightningMedia = MediaColor.FromArgb(255, 255, 213, 74);
    private static readonly MediaBrush GreenBrush = CreateFrozenBrush(46, 204, 113);
    private static readonly MediaBrush AmberBrush = CreateFrozenBrush(243, 156, 18);
    private static readonly MediaBrush RedBrush = CreateFrozenBrush(231, 76, 60);
    private static readonly MediaBrush UnknownBrush = CreateFrozenBrush(189, 195, 199);
    private static readonly Geometry FluentGamesGeometry =
        CreateFrozenGeometry(FluentGames16FilledPath);
    private static readonly Geometry FluentGamesRegularOuterShell =
        CreateFrozenGeometry(FluentGames16RegularOuterShellPath);
    private static readonly Geometry ChargingGeometry =
        CreateFrozenGeometry(ChargingPath);
    private static readonly Geometry MergedChargingGeometry =
        CreateFrozenGeometry(MergedChargingPath);
    private static readonly Geometry RefinedDualSenseShell =
        CreateFrozenGeometry(RefinedDualSenseShellPath);
    private static readonly Geometry RefinedDualSenseShoulders =
        CreateFrozenGeometry(RefinedDualSenseShouldersPath);
    private static readonly Geometry RefinedDualSenseTouchpad =
        CreateFrozenGeometry(RefinedDualSenseTouchpadPath);
    private static readonly Lazy<BitmapSource> ConnectedLightController = new(
        () => LoadFrozenResourceBitmap(
            "/DualSenseBatteryTray.App;component/Assets/Tray/dualsense-connected-light.png"));
    private static readonly Lazy<BitmapSource> ConnectedDarkController = new(
        () => LoadFrozenResourceBitmap(
            "/DualSenseBatteryTray.App;component/Assets/Tray/dualsense-connected-dark.png"));
    private static readonly ThreadLocal<IconBitmapDecoder> DisconnectedApplicationIcon = new(
        LoadDisconnectedApplicationIcon);

    private static readonly string[] Controller16 =
    [
        ".####.",
        "#o##o#",
        "######",
        ".#..#.",
    ];

    private static readonly string[] Controller24 =
    [
        "..#####..",
        ".#######.",
        "#########",
        "##o###o##",
        "#########",
        ".##...##.",
        "..#...#..",
    ];

    private static readonly string[] Controller32 =
    [
        "....#####....",
        "..#########..",
        ".###########.",
        "#############",
        "##o#######o##",
        "#############",
        "#############",
        ".####...####.",
        "..###...###..",
        "..###...###..",
        "..##.....##..",
    ];

    private static readonly string[] Controller48 =
    [
        "......#######......",
        "....###########....",
        "..###############..",
        ".#################.",
        "###################",
        "###o###########o###",
        "###################",
        "###################",
        ".######.....######.",
        "..#####.....#####..",
        "...####.....####...",
        "...###.......###...",
        "...###.......###...",
    ];

    private static readonly string[] LightningMask =
    [
        ".##",
        "##.",
        ".#.",
        "#..",
    ];

    public static BitmapSource RenderController(BatteryState state, int size)
    {
        ValidateStateAndSize(state, size);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var scale = size / 16d;
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawGeometry(
                MediaBrushes.White,
                new MediaPen(new SolidColorBrush(OutlineMedia), 0.55),
                FluentGamesGeometry);
            if (state.IsCharging)
                context.DrawGeometry(new SolidColorBrush(LightningMedia), null, ChargingGeometry);
            context.Pop();
        }
        return RenderVisual(visual, size);
    }

    internal static ControllerGlyphDetail SelectControllerGlyphDetail(int size) => size switch
    {
        16 => ControllerGlyphDetail.Minimal,
        24 or 32 or 48 => ControllerGlyphDetail.Full,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    internal static BitmapSource RenderControllerDetailLayer(int size)
    {
        _ = SelectControllerGlyphDetail(size);
        var visual = new DrawingVisual();
        if (size == 16)
            return RenderVisual(visual, size);

        using (var context = visual.RenderOpen())
        {
            var scale = size / 16d;
            context.PushTransform(new ScaleTransform(scale, scale));
            DrawFullControllerDetails(context, new SolidColorBrush(OutlineMedia), scale);
            context.Pop();
        }
        return RenderVisual(visual, size);
    }

    internal static BitmapSource RenderDisconnectedWindowGlyph(int size) =>
        RenderControllerGlyphCore(
            size,
            MediaBrushes.White,
            new SolidColorBrush(OutlineMedia),
            new SolidColorBrush(OutlineMedia),
            new SolidColorBrush(OutlineMedia));

    internal static BitmapSource RenderConnectedControllerGlyph(TrayTheme theme, int size)
    {
        var (body, details) = theme switch
        {
            TrayTheme.DarkTaskbar => (MediaBrushes.White, new SolidColorBrush(OutlineMedia)),
            TrayTheme.LightTaskbar => (new SolidColorBrush(OutlineMedia), MediaBrushes.White),
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
        };
        return RenderControllerGlyphCore(
            size,
            body,
            body,
            CreateFrozenBrush(87, 151, 246),
            details);
    }

    private static BitmapSource RenderControllerGlyphCore(
        int size,
        MediaBrush body,
        MediaBrush outline,
        MediaBrush touchpad,
        MediaBrush details)
    {
        var detail = SelectControllerGlyphDetail(size);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var scale = (size - 2d) / 16d;
            context.PushTransform(new TranslateTransform(1, 1));
            context.PushTransform(new ScaleTransform(scale, scale));
            var shellPen = new MediaPen(outline, 1d / scale);
            var touchpadPen = new MediaPen(outline, 0.5d / scale);

            context.DrawGeometry(body, shellPen, RefinedDualSenseShell);
            context.DrawGeometry(null, shellPen, RefinedDualSenseShoulders);
            context.DrawGeometry(touchpad, touchpadPen, RefinedDualSenseTouchpad);
            if (detail == ControllerGlyphDetail.Full)
                DrawFullControllerDetails(context, details, scale);

            context.Pop();
            context.Pop();
        }
        return RenderVisual(visual, size);
    }

    private static void DrawFullControllerDetails(
        DrawingContext context,
        MediaBrush brush,
        double scale)
    {
        var pen = new MediaPen(brush, 1d / scale);

        context.DrawLine(pen, new System.Windows.Point(3.65, 7.15), new System.Windows.Point(3.65, 9.35));
        context.DrawLine(pen, new System.Windows.Point(2.55, 8.25), new System.Windows.Point(4.75, 8.25));

        context.DrawEllipse(brush, null, new System.Windows.Point(11.8, 7.2), 0.34, 0.34);
        context.DrawEllipse(brush, null, new System.Windows.Point(12.75, 8.15), 0.34, 0.34);
        context.DrawEllipse(brush, null, new System.Windows.Point(11.8, 9.1), 0.34, 0.34);
        context.DrawEllipse(brush, null, new System.Windows.Point(10.85, 8.15), 0.34, 0.34);

        context.DrawEllipse(null, pen, new System.Windows.Point(6.45, 9.65), 0.55, 0.55);
        context.DrawEllipse(null, pen, new System.Windows.Point(9.55, 9.65), 0.55, 0.55);
    }

    internal static BitmapSource RenderCompact(BatteryState state, TrayTheme theme, int size)
    {
        ValidateStateAndSize(state, size);
        var controller = SelectConnectedController(theme);
        var numberLayer = RenderCompactNumberLayer(state, size);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(controller, GetConnectedControllerBounds(size));
            context.DrawImage(numberLayer, new System.Windows.Rect(0, 0, size, size));
        }

        return RenderVisual(visual, size);
    }

    internal static BitmapSource RenderCompactNumberLayer(BatteryState state, int size)
    {
        ValidateStateAndSize(state, size);
        return RenderFittedTextLayer(
            FormatIconNumber(state),
            new System.Windows.Rect(size * 0.28, size * 0.15, size * 0.44, size * 0.30),
            size,
            MediaBrushes.White,
            new MediaPen(new SolidColorBrush(OutlineMedia), 0.5),
            fillPasses: 2);
    }

    internal static BitmapSource RenderPercentage(BatteryState state, TrayTheme theme, int size)
    {
        var layers = RenderPercentageLayers(state, theme, size);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(layers.Number, new System.Windows.Rect(0, 0, size, size));
            context.DrawImage(layers.Percent, new System.Windows.Rect(0, 0, size, size));
        }
        return RenderVisual(visual, size);
    }

    internal static PercentageLayers RenderPercentageLayers(BatteryState state, TrayTheme theme, int size)
    {
        ValidateStateAndSize(state, size);
        var (fill, outlineColor) = theme switch
        {
            TrayTheme.DarkTaskbar => (MediaBrushes.White, OutlineMedia),
            TrayTheme.LightTaskbar => (CreateFrozenBrush(20, 22, 27), MediaColor.FromArgb(255, 255, 255, 255)),
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
        };
        var outline = new MediaPen(new SolidColorBrush(outlineColor), 0.5);
        var number = state.Percentage is int value
            ? value.ToString(CultureInfo.InvariantCulture)
            : "?";
        var numberRegionRight = Math.Floor(size * 0.78);
        var numberLayer = RenderFittedTextLayer(
            number,
            new System.Windows.Rect(1, 1, numberRegionRight - 1, size - 2),
            size,
            fill,
            outline);
        var percentLayer = RenderPercentLayer(
            new System.Windows.Rect(
                numberRegionRight,
                1,
                size - numberRegionRight - 1,
                Math.Ceiling(size * 0.42)),
            size,
            fill,
            outline);
        return new PercentageLayers(numberLayer, percentLayer);
    }

    private static BitmapSource RenderFittedTextLayer(
        string text,
        System.Windows.Rect targetBounds,
        int size,
        MediaBrush fill,
        MediaPen outline,
        int fillPasses = 1)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            size,
            fill,
            1d);
        var geometry = formatted.BuildGeometry(new System.Windows.Point(0, 0));
        var bounds = geometry.Bounds;
        var availableWidth = targetBounds.Width - outline.Thickness;
        var availableHeight = targetBounds.Height - outline.Thickness;
        var scale = Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height);
        var renderedWidth = bounds.Width * scale;
        var renderedHeight = bounds.Height * scale;
        var targetX = targetBounds.X + (outline.Thickness / 2d) +
            ((availableWidth - renderedWidth) / 2d);
        var targetY = targetBounds.Y + (outline.Thickness / 2d) +
            ((availableHeight - renderedHeight) / 2d);
        var transformed = geometry.Clone();
        transformed.Transform = new MatrixTransform(new Matrix(
            scale,
            0d,
            0d,
            scale,
            targetX - (bounds.X * scale),
            targetY - (bounds.Y * scale)));
        transformed.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawGeometry(null, outline, transformed);
            for (var pass = 0; pass < fillPasses; pass++)
                context.DrawGeometry(fill, null, transformed);
        }
        return RenderVisual(visual, size);
    }

    private static BitmapSource RenderPercentLayer(
        System.Windows.Rect targetBounds,
        int size,
        MediaBrush fill,
        MediaPen outline)
    {
        if (size == 16)
            return RenderSmallPercentLayer(fill, outline);

        var horizontalInset = Math.Min(0.75, targetBounds.Width * 0.13333333333333333d);
        var glyphHeight = Math.Min(targetBounds.Height, size * 0.28d);
        var verticalInset = Math.Min(0.75, glyphHeight * 0.09d);
        var glyphBounds = new System.Windows.Rect(
            targetBounds.X + horizontalInset,
            targetBounds.Y + verticalInset,
            targetBounds.Width - (horizontalInset * 2d),
            glyphHeight - (verticalInset * 2d));
        var dotSize = Math.Min(glyphBounds.Width * 0.8, glyphBounds.Height * 0.4);
        var topDot = new EllipseGeometry(new System.Windows.Rect(
            glyphBounds.Right - dotSize,
            glyphBounds.Top,
            dotSize,
            dotSize));
        var bottomDot = new EllipseGeometry(new System.Windows.Rect(
            glyphBounds.Left,
            glyphBounds.Bottom - dotSize,
            dotSize,
            dotSize));
        var slash = new StreamGeometry();
        using (var slashContext = slash.Open())
        {
            var width = Math.Max(dotSize * 0.55, 0.6);
            slashContext.BeginFigure(
                new System.Windows.Point(glyphBounds.Left, glyphBounds.Bottom - (dotSize * 0.1)),
                isFilled: true,
                isClosed: true);
            slashContext.LineTo(
                new System.Windows.Point(glyphBounds.Left + width, glyphBounds.Bottom),
                isStroked: true,
                isSmoothJoin: false);
            slashContext.LineTo(
                new System.Windows.Point(glyphBounds.Right, glyphBounds.Top + (dotSize * 0.1)),
                isStroked: true,
                isSmoothJoin: false);
            slashContext.LineTo(
                new System.Windows.Point(glyphBounds.Right - width, glyphBounds.Top),
                isStroked: true,
                isSmoothJoin: false);
        }
        slash.Freeze();

        var percent = new GeometryGroup();
        percent.Children.Add(topDot);
        percent.Children.Add(bottomDot);
        percent.Children.Add(slash);
        percent.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawGeometry(null, outline, percent);
            context.DrawGeometry(fill, null, percent);
        }
        return RenderVisual(visual, size);
    }

    private static BitmapSource RenderSmallPercentLayer(MediaBrush fill, MediaPen outline)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            DrawSmallPercentCell(context, fill, outline, 13, 2);
            DrawSmallPercentCell(context, fill, outline, 13, 3);
            DrawSmallPercentCell(context, fill, outline, 12, 4);
            DrawSmallPercentCell(context, fill, outline, 12, 5);
        }
        return RenderVisual(visual, 16);
    }

    private static void DrawSmallPercentCell(
        DrawingContext context,
        MediaBrush fill,
        MediaPen outline,
        int x,
        int y)
    {
        var cell = new System.Windows.Rect(x, y, 1, 1);
        context.DrawRectangle(null, outline, cell);
        context.DrawRectangle(fill, null, cell);
    }

    public static BitmapSource RenderMerged(BatteryState state, int size)
    {
        ValidateStateAndSize(state, size);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var scale = size / 16d;
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawGeometry(MediaBrushes.White, null, FluentGamesRegularOuterShell);
            DrawFittedIconNumber(context, FormatIconNumber(state));
            if (state.State == ConnectionState.Charging)
                context.DrawGeometry(new SolidColorBrush(LightningMedia), null, MergedChargingGeometry);
            context.Pop();
        }
        return RenderVisual(visual, size);
    }

    public static BitmapSource Render(BatteryState state, int size)
    {
        ArgumentNullException.ThrowIfNull(state);
        var layout = GetLayout(size, state.IsCharging);
        var canvas = new PixelCanvas(size, size);

        canvas.DrawOutlinedPattern(
            layout.Controller,
            layout.ControllerX,
            layout.ControllerY,
            1,
            1,
            layout.ControllerClip,
            Outline,
            White,
            '#');

        if (state.IsCharging)
        {
            canvas.DrawOutlinedPattern(
                LightningMask,
                layout.LightningX,
                layout.LightningY,
                layout.LightningScale,
                layout.LightningScale,
                layout.ControllerClip,
                Outline,
                Lightning,
                '#');
        }

        DrawText(
            canvas,
            FormatPercentage(state),
            layout,
            SelectPercentageColor(state.Percentage));

        return canvas.ToBitmapSource();
    }

    public static DrawingIcon RenderTrayIcon(BatteryState state)
        => RenderTrayIcon(state, Render);

    public static DrawingIcon RenderControllerTrayIcon(BatteryState state) =>
        RenderTrayIcon(state, RenderController);

    internal static DrawingIcon RenderControllerTrayIcon(BatteryState state, TrayTheme theme) =>
        RenderTrayIcon(
            state,
            (frameState, size) => RenderConnectedController(frameState, theme, size));

    internal static DrawingIcon RenderCompactTrayIcon(BatteryState state, TrayTheme theme) =>
        RenderTrayIcon(state, (frameState, size) => RenderCompact(frameState, theme, size));

    internal static DrawingIcon RenderPercentageTrayIcon(BatteryState state, TrayTheme theme) =>
        RenderTrayIcon(state, (frameState, size) => RenderPercentage(frameState, theme, size));

    public static DrawingIcon RenderMergedTrayIcon(BatteryState state) =>
        RenderTrayIcon(state, RenderMerged);

    private static DrawingIcon RenderTrayIcon(
        BatteryState state,
        Func<BatteryState, int, BitmapSource> renderFrame)
    {
        ArgumentNullException.ThrowIfNull(state);
        var smallIconSize = System.Windows.Forms.SystemInformation.SmallIconSize;
        var nativeSize = NativeIconSizes
            .OrderBy(size => Math.Abs(size - smallIconSize.Width))
            .ThenByDescending(size => size)
            .First();
        return RenderTrayIcon(state, nativeSize, renderFrame);
    }

    internal static DrawingIcon RenderTrayIcon(BatteryState state, int nativeSize)
        => RenderTrayIcon(state, nativeSize, Render);

    internal static DrawingIcon RenderWindowSmallIcon()
    {
        var requested = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
        var nativeSize = NativeIconSizes
            .OrderBy(size => Math.Abs(size - requested))
            .ThenByDescending(size => size)
            .First();
        return RenderWindowSmallIcon(nativeSize);
    }

    internal static DrawingIcon RenderWindowSmallIcon(int nativeSize)
    {
        if (!NativeIconSizes.Contains(nativeSize))
            throw new ArgumentOutOfRangeException(nameof(nativeSize), nativeSize, null);

        var bytes = EncodeIcon(RenderDisconnectedWindowGlyph);
        using var stream = new MemoryStream(bytes, writable: false);
        using var icon = new DrawingIcon(stream, nativeSize, nativeSize);
        return (DrawingIcon)icon.Clone();
    }

    private static DrawingIcon RenderTrayIcon(
        BatteryState state,
        int nativeSize,
        Func<BatteryState, int, BitmapSource> renderFrame)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(renderFrame);
        if (!NativeIconSizes.Contains(nativeSize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nativeSize),
                nativeSize,
                "Tray HICONs are available only at 16, 24, 32, or 48 pixels.");
        }

        var iconBytes = EncodeIcon(state, renderFrame);
        using var stream = new MemoryStream(iconBytes, writable: false);
        using var icon = new DrawingIcon(stream, nativeSize, nativeSize);
        return (DrawingIcon)icon.Clone();
    }

    public static BitmapFrame RenderApplicationIcon()
    {
        return DisconnectedApplicationIcon.Value!.Frames.Single(frame => frame.PixelWidth == 256);
    }

    private static IconBitmapDecoder LoadDisconnectedApplicationIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri(
                "/DualSenseBatteryTray.App;component/assets/app/dualsense-disconnected.ico",
                UriKind.Relative)) ??
            throw new InvalidOperationException("Embedded disconnected application icon was not found.");
        using var stream = resource.Stream;
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        foreach (var frame in decoder.Frames)
            frame.Freeze();
        return decoder;
    }

    private static byte[] EncodeIcon(
        BatteryState state,
        Func<BatteryState, int, BitmapSource> renderFrame)
        => EncodeIconFrames(NativeIconSizes.Select(size => (size, renderFrame(state, size))));

    private static byte[] EncodeIcon(Func<int, BitmapSource> renderFrame) =>
        EncodeIconFrames(NativeIconSizes.Select(size => (size, renderFrame(size))));

    private static byte[] EncodeIconFrames(
        IEnumerable<(int Size, BitmapSource Frame)> frames)
    {
        var items = frames
            .Select(item => (item.Size, Data: EncodePng(item.Frame)))
            .ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)items.Length);

        var imageOffset = 6 + (items.Length * 16);
        foreach (var item in items)
        {
            writer.Write((byte)item.Size);
            writer.Write((byte)item.Size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)item.Data.Length);
            writer.Write((uint)imageOffset);
            imageOffset += item.Data.Length;
        }

        foreach (var item in items)
            writer.Write(item.Data);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource RenderConnectedController(
        BatteryState state,
        TrayTheme theme,
        int size)
    {
        ValidateStateAndSize(state, size);
        return RenderConnectedControllerGlyph(theme, size);
    }

    private static System.Windows.Rect GetConnectedControllerBounds(int size) =>
        new(1, 1, size - 2, size - 2);

    private static BitmapSource SelectConnectedController(TrayTheme theme) => theme switch
    {
        TrayTheme.LightTaskbar => ConnectedLightController.Value,
        TrayTheme.DarkTaskbar => ConnectedDarkController.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    private static BitmapSource LoadFrozenResourceBitmap(string resourceUri)
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri(resourceUri, UriKind.Relative)) ??
            throw new InvalidOperationException($"Embedded tray asset was not found: {resourceUri}");
        using var stream = resource.Stream;
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var bitmap = decoder.Frames[0];
        bitmap.Freeze();
        return bitmap;
    }

    internal static BitmapSource RenderTextMask(string text, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var canvas = new PixelCanvas(width, height);
        var style = height >= 32 ? GlyphStyle.Large : GlyphStyle.Small;
        var pattern = ComposeTextPattern(text, style, spacing: 1);
        var scaleX = Math.Max(1, width / Math.Max(1, pattern[0].Length + 2));
        var scaleY = Math.Max(1, Math.Min(height / pattern.Length, scaleX * 2));
        var renderedWidth = pattern[0].Length * scaleX;
        var renderedHeight = pattern.Length * scaleY;
        var clip = new PixelBounds(0, 0, width - 1, height - 1);
        canvas.DrawOutlinedPattern(
            pattern,
            Math.Max(0, (width - renderedWidth) / 2),
            Math.Max(0, (height - renderedHeight) / 2),
            scaleX,
            scaleY,
            clip,
            Outline,
            White,
            '1');
        return canvas.ToBitmapSource();
    }

    internal static string FormatPercentage(BatteryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Percentage is int percentage
            ? $"{percentage.ToString(CultureInfo.InvariantCulture)}%"
            : "?%";
    }

    internal static string FormatIconNumber(BatteryState state) =>
        state.Percentage is int percentage
            ? percentage.ToString(CultureInfo.InvariantCulture)
            : "?";

    private static void DrawFittedIconNumber(DrawingContext context, string text)
    {
        var brush = MediaBrushes.White;
        var outline = new MediaPen(new SolidColorBrush(OutlineMedia), 0.35);
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            16d,
            brush,
            1d);
        var geometry = formatted.BuildGeometry(new System.Windows.Point(0, 0));
        var bounds = geometry.Bounds;
        var innerBox = new System.Windows.Rect(2d, 4d, 12d, 7d);
        var strokeInset = outline.Thickness / 2d;
        var availableWidth = innerBox.Width - outline.Thickness;
        var availableHeight = innerBox.Height - outline.Thickness;
        var scale = Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height);
        var renderedWidth = bounds.Width * scale;
        var renderedHeight = bounds.Height * scale;
        var targetX = innerBox.X + strokeInset + ((availableWidth - renderedWidth) / 2d);
        var targetY = innerBox.Y + strokeInset + ((availableHeight - renderedHeight) / 2d);
        var transformed = geometry.Clone();
        transformed.Transform = new MatrixTransform(new Matrix(
            scale,
            0d,
            0d,
            scale,
            targetX - (bounds.X * scale),
            targetY - (bounds.Y * scale)));
        transformed.Freeze();
        context.DrawGeometry(brush, outline, transformed);
    }

    private static void DrawText(
        PixelCanvas canvas,
        string text,
        IconLayout layout,
        DrawingColor color)
    {
        var pattern = ComposeTextPattern(text, layout.GlyphStyle, layout.GlyphSpacing);
        var renderedWidth = pattern[0].Length * layout.GlyphScaleX;
        var renderedHeight = pattern.Length * layout.GlyphScaleY;
        var x = layout.TextClip.Left + ((layout.TextClip.Width - renderedWidth) / 2);
        var y = (canvas.Height - renderedHeight) / 2;
        canvas.DrawOutlinedPattern(
            pattern,
            x,
            y,
            layout.GlyphScaleX,
            layout.GlyphScaleY,
            layout.TextClip,
            Outline,
            color,
            '1');
    }

    private static string[] ComposeTextPattern(string text, GlyphStyle style, int spacing)
    {
        var glyphs = text.Select(character => GetGlyph(character, style)).ToArray();
        var height = glyphs[0].Length;
        var rows = new string[height];
        var separator = new string('.', spacing);
        for (var row = 0; row < height; row++)
            rows[row] = string.Join(separator, glyphs.Select(glyph => glyph[row]));
        return rows;
    }

    private static string[] GetGlyph(char character, GlyphStyle style) => style switch
    {
        GlyphStyle.Small => character switch
        {
            '0' => ["11", "10", "10", "01", "11"],
            '1' => ["1", "1", "1", "1", "1"],
            '2' => ["11", "01", "11", "10", "11"],
            '3' => ["11", "01", "11", "01", "11"],
            '4' => ["10", "10", "11", "01", "01"],
            '5' => ["11", "10", "11", "01", "11"],
            '6' => ["11", "10", "11", "11", "11"],
            '7' => ["11", "01", "01", "01", "01"],
            '8' => ["11", "11", "11", "11", "11"],
            '9' => ["11", "11", "11", "01", "11"],
            '?' => ["11", "01", "01", "00", "01"],
            '%' => ["110", "110", "010", "011", "011"],
            _ => throw new ArgumentOutOfRangeException(nameof(character)),
        },
        _ => character switch
        {
            '0' => ["111", "101", "101", "101", "101", "101", "111"],
            '1' => ["010", "110", "010", "010", "010", "010", "111"],
            '2' => ["111", "001", "001", "111", "100", "100", "111"],
            '3' => ["111", "001", "001", "111", "001", "001", "111"],
            '4' => ["101", "101", "101", "111", "001", "001", "001"],
            '5' => ["111", "100", "100", "111", "001", "001", "111"],
            '6' => ["111", "100", "100", "111", "101", "101", "111"],
            '7' => ["111", "001", "001", "010", "010", "100", "100"],
            '8' => ["111", "101", "101", "111", "101", "101", "111"],
            '9' => ["111", "101", "101", "111", "001", "001", "111"],
            '?' => ["111", "001", "001", "011", "010", "000", "010"],
            '%' => ["11000", "11001", "00010", "00100", "01000", "10011", "00011"],
            _ => throw new ArgumentOutOfRangeException(nameof(character)),
        },
    };

    private static IconLayout GetLayout(int size, bool charging) => size switch
    {
        16 => new IconLayout(
            Controller16,
            0,
            charging ? 3 : 6,
            new PixelBounds(0, 0, 5, 15),
            new PixelBounds(7, 0, 14, 15),
            GlyphStyle.Small,
            1,
            1,
            0,
            1,
            11,
            1),
        24 => new IconLayout(
            Controller24,
            0,
            charging ? 4 : 8,
            new PixelBounds(0, 0, 8, 23),
            new PixelBounds(10, 0, 22, 23),
            GlyphStyle.Small,
            1,
            2,
            1,
            1,
            15,
            2),
        32 => new IconLayout(
            Controller32,
            0,
            charging ? 3 : 8,
            new PixelBounds(0, 0, 12, 31),
            new PixelBounds(14, 0, 30, 31),
            GlyphStyle.Large,
            1,
            2,
            1,
            3,
            19,
            2),
        48 => new IconLayout(
            Controller48,
            0,
            charging ? 5 : 12,
            new PixelBounds(0, 0, 18, 47),
            new PixelBounds(21, 0, 46, 47),
            GlyphStyle.Large,
            1,
            3,
            2,
            5,
            29,
            3),
        _ => throw new ArgumentOutOfRangeException(
            nameof(size),
            size,
            "Battery icons are rendered only at 16, 24, 32, or 48 pixels."),
    };

    private static DrawingColor SelectPercentageColor(int? percentage) => percentage switch
    {
        null => Unknown,
        <= 10 => Red,
        <= 20 => Amber,
        _ => Green,
    };

    private static MediaBrush SelectPercentageBrush(int? percentage) => percentage switch
    {
        null => UnknownBrush,
        <= 10 => RedBrush,
        <= 20 => AmberBrush,
        _ => GreenBrush,
    };

    private static BitmapSource FitAndRenderGeometry(
        Geometry geometry,
        MediaBrush fill,
        MediaPen outline,
        int size,
        double inset)
    {
        var bounds = geometry.Bounds;
        var renderInset = inset + outline.Thickness;
        var available = size - (renderInset * 2);
        var scale = Math.Min(available / bounds.Width, available / bounds.Height);
        var renderedWidth = bounds.Width * scale;
        var renderedHeight = bounds.Height * scale;
        var targetX = renderInset + ((available - renderedWidth) / 2);
        var targetY = renderInset + ((available - renderedHeight) / 2);
        var matrix = new Matrix(
            scale,
            0,
            0,
            scale,
            targetX - (bounds.X * scale),
            targetY - (bounds.Y * scale));
        var transformed = geometry.Clone();
        transformed.Transform = new MatrixTransform(matrix);
        transformed.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawGeometry(fill, outline, transformed);
        return RenderVisual(visual, size);
    }

    private static BitmapSource RenderVisual(DrawingVisual visual, int size)
    {
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(
            target,
            PixelFormats.Bgra32,
            null,
            0);
        converted.Freeze();
        return converted;
    }

    private static Geometry CreateFrozenGeometry(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private static MediaBrush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(MediaColor.FromArgb(255, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static void ValidateStateAndSize(BatteryState state, int size)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!NativeIconSizes.Contains(size))
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "Battery icons are rendered only at 16, 24, 32, or 48 pixels.");
        }
    }

    private enum GlyphStyle
    {
        Small,
        Large,
    }

    private readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left + 1;
        public bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    private sealed record IconLayout(
        string[] Controller,
        int ControllerX,
        int ControllerY,
        PixelBounds ControllerClip,
        PixelBounds TextClip,
        GlyphStyle GlyphStyle,
        int GlyphScaleX,
        int GlyphScaleY,
        int GlyphSpacing,
        int LightningX,
        int LightningY,
        int LightningScale);

    private sealed class PixelCanvas(int width, int height)
    {
        private readonly byte[] _pixels = new byte[width * height * 4];

        public int Width { get; } = width;
        public int Height { get; } = height;

        public void DrawOutlinedPattern(
            string[] pattern,
            int originX,
            int originY,
            int scaleX,
            int scaleY,
            PixelBounds clip,
            DrawingColor outline,
            DrawingColor fill,
            char fillCharacter)
        {
            for (var row = 0; row < pattern.Length; row++)
            {
                for (var column = 0; column < pattern[row].Length; column++)
                {
                    if (pattern[row][column] == '.')
                        continue;

                    var left = originX + (column * scaleX);
                    var top = originY + (row * scaleY);
                    for (var y = top - 1; y <= top + scaleY; y++)
                    {
                        for (var x = left - 1; x <= left + scaleX; x++)
                        {
                            if (clip.Contains(x, y))
                                SetPixel(x, y, outline);
                        }
                    }
                }
            }

            for (var row = 0; row < pattern.Length; row++)
            {
                for (var column = 0; column < pattern[row].Length; column++)
                {
                    if (pattern[row][column] != fillCharacter)
                        continue;

                    var left = originX + (column * scaleX);
                    var top = originY + (row * scaleY);
                    for (var y = top; y < top + scaleY; y++)
                    {
                        for (var x = left; x < left + scaleX; x++)
                        {
                            if (clip.Contains(x, y))
                                SetPixel(x, y, fill);
                        }
                    }
                }
            }
        }

        public BitmapSource ToBitmapSource()
        {
            var stride = Width * 4;
            var source = BitmapSource.Create(
                Width,
                Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                _pixels,
                stride);
            source.Freeze();
            return source;
        }

        private void SetPixel(int x, int y, DrawingColor color)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return;

            var offset = ((y * Width) + x) * 4;
            _pixels[offset] = color.B;
            _pixels[offset + 1] = color.G;
            _pixels[offset + 2] = color.R;
            _pixels[offset + 3] = color.A;
        }
    }
}
