using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using DualSenseBatteryTray.App.Tray;
using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.App.Tests;

public sealed class BatteryIconRendererTests
{
    private static readonly Pixel Outline = new(20, 22, 27, 255);
    private static readonly Pixel Green = new(46, 204, 113, 255);
    private static readonly Pixel Amber = new(243, 156, 18, 255);
    private static readonly Pixel Red = new(231, 76, 60, 255);
    private static readonly Pixel Lightning = new(255, 213, 74, 255);
    private static readonly Pixel White = new(255, 255, 255, 255);

    public static TheoryData<int?, int> CompactValuesAndSizes
    {
        get
        {
            var data = new TheoryData<int?, int>();
            foreach (var percentage in new int?[] { 10, 55, 100, null })
            {
                foreach (var size in new[] { 16, 24, 32, 48 })
                    data.Add(percentage, size);
            }

            return data;
        }
    }

    [Theory]
    [InlineData(10, "10")]
    [InlineData(75, "75")]
    [InlineData(100, "100")]
    [InlineData(null, "?")]
    public void FormatIconNumber_omits_the_percent_sign(int? percentage, string expected)
    {
        Assert.Equal(expected, BatteryIconRenderer.FormatIconNumber(
            new BatteryState(percentage, ConnectionState.Unknown)));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void RenderMerged_contains_a_white_controller_and_number(int size)
    {
        var pixels = CopyPixels(BatteryIconRenderer.RenderMerged(
            new BatteryState(75, ConnectionState.Discharging), size));
        Assert.Contains(pixels, IsWhiteTextPixel);
        Assert.DoesNotContain(pixels, pixel => pixel == Green);
        Assert.DoesNotContain(pixels, pixel => pixel == Amber);
        Assert.DoesNotContain(pixels, pixel => pixel == Red);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(null)]
    public void RenderMerged_fits_each_number_inside_the_native_frame(int? percentage)
    {
        const int size = 16;
        var pixels = CopyPixels(BatteryIconRenderer.RenderMerged(
            new BatteryState(percentage, ConnectionState.Unknown), size));
        Assert.Contains(pixels, pixel => pixel.Alpha != 0);
        Assert.Equal(0, CountPixels(pixels, size, 0, 1, pixel => pixel.Alpha != 0));
        Assert.Equal(0, CountPixels(pixels, size, size - 1, size, pixel => pixel.Alpha != 0));
    }

    [Fact]
    public void RenderMerged_charging_adds_yellow_without_changing_the_number_to_100()
    {
        var charging = CopyPixels(BatteryIconRenderer.RenderMerged(
            new BatteryState(75, ConnectionState.Charging), 48));
        var full = CopyPixels(BatteryIconRenderer.RenderMerged(
            new BatteryState(100, ConnectionState.Full), 48));
        Assert.Contains(charging, pixel => pixel == Lightning);
        Assert.DoesNotContain(full, pixel => pixel == Lightning);
        Assert.NotEqual(ComputeHash(charging), ComputeHash(full));
    }

    [Fact]
    public void RenderMergedTrayIcon_contains_all_native_frames()
    {
        using var icon = BatteryIconRenderer.RenderMergedTrayIcon(
            new BatteryState(75, ConnectionState.Discharging));
        Assert.Equal([16, 24, 32, 48], DecodeFrames(icon).Keys.Order().ToArray());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void RenderController_uses_the_Fluent_shape_at_every_native_size(int size)
    {
        var pixels = CopyPixels(BatteryIconRenderer.RenderController(
            new BatteryState(75, ConnectionState.Discharging), size));
        var bounds = GetBounds(pixels, size, pixel => pixel == White);
        Assert.True(bounds.Width >= (int)Math.Floor(size * 0.75));
        Assert.True(bounds.Height >= (int)Math.Floor(size * 0.45));
        Assert.True(bounds.Width > bounds.Height);
    }

    [Theory]
    [InlineData((int)TrayTheme.DarkTaskbar, 255)]
    [InlineData((int)TrayTheme.LightTaskbar, 20)]
    public void RenderPercentage_uses_theme_foreground(int themeValue, byte expected)
    {
        var theme = (TrayTheme)themeValue;
        var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
            new BatteryState(55, ConnectionState.Discharging), theme, 48));
        Assert.Contains(pixels, p => p.Alpha > 0 && Math.Abs(p.Red - expected) < 12);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void RenderPercentage_places_a_small_percent_mark_at_upper_right(int size)
    {
        var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
            new BatteryState(55, ConnectionState.Discharging), TrayTheme.DarkTaskbar, size));
        var numberRegion = CountPixels(
            pixels, size, 0, (int)Math.Ceiling(size * 0.78), IsWhiteTextPixel);
        var percentRegion = CountPixels(
            pixels, size, (int)Math.Floor(size * 0.78), size, IsWhiteTextPixel);
        Assert.True(
            numberRegion > percentRegion * 2,
            $"Expected number pixels to dominate; number={numberRegion}, percent={percentRegion}.");
        Assert.True(
            percentRegion > 0,
            $"Expected a visible percent mark; percent={percentRegion}.");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(55)]
    [InlineData(100)]
    [InlineData(null)]
    public void RenderPercentage_keeps_number_and_percent_inside_16px(int? percentage)
    {
        const int size = 16;
        var pixels = CopyPixels(BatteryIconRenderer.RenderPercentage(
            new BatteryState(percentage, ConnectionState.Unknown), TrayTheme.DarkTaskbar, size));
        Assert.Contains(pixels, IsWhiteTextPixel);
        Assert.Equal(0, CountPixels(pixels, size, 0, 1, pixel => pixel.Alpha != 0));
        Assert.Equal(0, CountPixels(pixels, size, size - 1, size, pixel => pixel.Alpha != 0));
    }

    [Fact]
    public void RenderPercentage_numeric_glyphs_are_taller_than_the_percent_glyph()
    {
        var layers = BatteryIconRenderer.RenderPercentageLayers(
            new BatteryState(55, ConnectionState.Discharging), TrayTheme.DarkTaskbar, 48);

        Assert.Contains(CopyPixels(layers.Number), IsWhiteTextPixel);
        Assert.Contains(CopyPixels(layers.Percent), IsWhiteTextPixel);
        Assert.Contains(CopyPixels(layers.Number), IsDarkOutlinePixel);
        Assert.Contains(CopyPixels(layers.Percent), IsDarkOutlinePixel);
        Assert.True(GetOpaqueBounds(layers.Number).Height > GetOpaqueBounds(layers.Percent).Height * 1.7);
    }

    [Fact]
    public void Independent_tray_icons_contain_all_native_frames()
    {
        var state = new BatteryState(75, ConnectionState.Charging);
        using var controller = BatteryIconRenderer.RenderControllerTrayIcon(state);
        using var percentage = BatteryIconRenderer.RenderPercentageTrayIcon(state, TrayTheme.DarkTaskbar);
        Assert.Equal([16, 24, 32, 48], DecodeFrames(controller).Keys.Order().ToArray());
        Assert.Equal([16, 24, 32, 48], DecodeFrames(percentage).Keys.Order().ToArray());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void RenderCompact_preserves_controller_blue_and_number(int size)
    {
        var image = BatteryIconRenderer.RenderCompact(
            new BatteryState(55, ConnectionState.Discharging),
            TrayTheme.DarkTaskbar,
            size);
        var pixels = CopyPixels(image);
        var numberPixels = CopyPixels(BatteryIconRenderer.RenderCompactNumberLayer(
            new BatteryState(55, ConnectionState.Discharging),
            size));

        Assert.Contains(pixels, IsConnectedBluePixel);
        Assert.True(GetOpaqueBounds(image).Width >= size * 0.75);
        Assert.Contains(numberPixels, IsLightDigitPixel);
    }

    [Theory]
    [InlineData((int)TrayTheme.LightTaskbar, 16)]
    [InlineData((int)TrayTheme.LightTaskbar, 24)]
    [InlineData((int)TrayTheme.LightTaskbar, 32)]
    [InlineData((int)TrayTheme.LightTaskbar, 48)]
    [InlineData((int)TrayTheme.DarkTaskbar, 16)]
    [InlineData((int)TrayTheme.DarkTaskbar, 24)]
    [InlineData((int)TrayTheme.DarkTaskbar, 32)]
    [InlineData((int)TrayTheme.DarkTaskbar, 48)]
    public void RenderCompact_keeps_the_controller_inside_the_native_frame(int themeValue, int size)
    {
        var pixels = CopyPixels(BatteryIconRenderer.RenderCompact(
            new BatteryState(55, ConnectionState.Discharging),
            (TrayTheme)themeValue,
            size));

        for (var coordinate = 0; coordinate < size; coordinate++)
        {
            Assert.Equal(0, pixels[coordinate].Alpha);
            Assert.Equal(0, pixels[((size - 1) * size) + coordinate].Alpha);
            Assert.Equal(0, pixels[coordinate * size].Alpha);
            Assert.Equal(0, pixels[(coordinate * size) + size - 1].Alpha);
        }
    }

    [Theory]
    [MemberData(nameof(CompactValuesAndSizes))]
    public void RenderCompact_fits_each_number_without_clipping(int? percentage, int size)
    {
        var image = BatteryIconRenderer.RenderCompact(
            new BatteryState(percentage, ConnectionState.Unknown),
            TrayTheme.LightTaskbar,
            size);
        var bounds = GetOpaqueBounds(image);

        Assert.Equal(size, image.PixelWidth);
        Assert.Equal(size, image.PixelHeight);
        Assert.True(bounds.Left > 0);
        Assert.True(bounds.Top > 0);
        Assert.True(bounds.Left + bounds.Width < size);
        Assert.True(bounds.Top + bounds.Height < size);
    }

    [Theory]
    [MemberData(nameof(CompactValuesAndSizes))]
    public void RenderCompactNumberLayer_fits_each_number_inside_the_touchpad(int? percentage, int size)
    {
        var layer = BatteryIconRenderer.RenderCompactNumberLayer(
            new BatteryState(percentage, ConnectionState.Unknown),
            size);
        var pixels = CopyPixels(layer);
        var bounds = GetOpaqueBounds(layer);
        var (left, top, right, bottom) = GetTouchpadRegion(size);

        Assert.Contains(pixels, IsLightDigitPixel);
        Assert.True(bounds.Left >= left);
        Assert.True(bounds.Top >= top);
        Assert.True(bounds.Left + bounds.Width <= right);
        Assert.True(bounds.Top + bounds.Height <= bottom);
    }

    [Fact]
    public void RenderCompactNumberLayer_omits_the_percent_glyph()
    {
        const int size = 48;
        var pixels = CopyPixels(BatteryIconRenderer.RenderCompactNumberLayer(
            new BatteryState(55, ConnectionState.Discharging),
            size));
        var digitBounds = GetBounds(pixels, size, IsLightDigitPixel);

        Assert.True(
            digitBounds.Height >= digitBounds.Width * 0.55,
            $"Expected only two fitted digits, got {digitBounds.Width}x{digitBounds.Height}.");
    }

    [Theory]
    [InlineData((int)TrayTheme.LightTaskbar, false)]
    [InlineData((int)TrayTheme.DarkTaskbar, true)]
    public void RenderCompact_uses_theme_specific_outer_outline(int themeValue, bool expectLightOutline)
    {
        const int size = 48;
        var pixels = CopyPixels(BatteryIconRenderer.RenderCompact(
            new BatteryState(55, ConnectionState.Discharging),
            (TrayTheme)themeValue,
            size));

        Assert.True(HasOpaqueEdgeColor(pixels, size, expectLightOutline));
    }

    [Fact]
    public void Compact_and_controller_tray_icons_contain_all_native_frames()
    {
        var state = new BatteryState(55, ConnectionState.Discharging);
        using var compact = BatteryIconRenderer.RenderCompactTrayIcon(state, TrayTheme.DarkTaskbar);
        using var controller = BatteryIconRenderer.RenderControllerTrayIcon(state, TrayTheme.LightTaskbar);

        Assert.Equal([16, 24, 32, 48], DecodeFrames(compact).Keys.Order().ToArray());
        Assert.Equal([16, 24, 32, 48], DecodeFrames(controller).Keys.Order().ToArray());
    }

    [Fact]
    public void Render_draws_each_supported_size_independently()
    {
        int[] sizes = [16, 24, 32, 48];
        var hashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var size in sizes)
        {
            var image = BatteryIconRenderer.Render(
                new BatteryState(50, ConnectionState.Discharging),
                size);
            var pixels = CopyPixels(image);

            Assert.Equal(size, image.PixelWidth);
            Assert.Equal(size, image.PixelHeight);
            Assert.Contains(pixels, pixel => pixel.Alpha != 0);
            Assert.True(hashes.Add(ComputeHash(pixels)), $"Duplicate output for {size}px.");
        }
    }

    [Fact]
    public void RenderTrayIcon_returns_an_owned_SystemDrawing_icon_with_all_native_frames()
    {
        var state = new BatteryState(50, ConnectionState.Discharging);

        using var icon = BatteryIconRenderer.RenderTrayIcon(state);
        using var stream = new MemoryStream();
        icon.Save(stream);
        stream.Position = 0;
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var framesBySize = decoder.Frames.ToDictionary(frame => frame.PixelWidth);

        Assert.Equal([16, 24, 32, 48], framesBySize.Keys.Order().ToArray());
        foreach (var size in framesBySize.Keys)
        {
            Assert.Equal(
                ComputeHash(CopyPixels(BatteryIconRenderer.Render(state, size))),
                ComputeHash(CopyPixels(framesBySize[size])));
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void RenderTrayIcon_selects_the_requested_native_frame_for_its_HICON(int size)
    {
        var state = new BatteryState(50, ConnectionState.Discharging);

        using var icon = BatteryIconRenderer.RenderTrayIcon(state, size);
        using var bitmap = icon.ToBitmap();

        Assert.Equal(new System.Drawing.Size(size, size), icon.Size);
        Assert.Equal(
            ComputeHash(CopyPixels(BatteryIconRenderer.Render(state, size))),
            ComputeHash(CopyPixels(bitmap)));
    }

    [Fact]
    public void RenderApplicationIcon_uses_unconnected_multi_frame_asset()
    {
        var icon = BatteryIconRenderer.RenderApplicationIcon();
        var decoder = Assert.IsType<IconBitmapDecoder>(icon.Decoder);

        Assert.True(icon.IsFrozen);
        Assert.Equal([16, 24, 32, 48, 256],
            decoder.Frames.Select(frame => frame.PixelWidth).Order().ToArray());
        foreach (var frame in decoder.Frames)
        {
            var pixels = CopyPixels(frame);
            Assert.Equal(0, pixels[0].Alpha);
            Assert.DoesNotContain(pixels, IsConnectedBluePixel);
            Assert.Contains(pixels, IsLightControllerPixel);
            Assert.Contains(pixels, IsDarkOutlinePixel);
        }
    }

    [Fact]
    public void RenderApplicationIcon_can_be_read_from_multiple_dispatcher_threads()
    {
        _ = BatteryIconRenderer.RenderApplicationIcon().Decoder.Frames.Count;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var icon = BatteryIconRenderer.RenderApplicationIcon();
                Assert.Equal(5, icon.Decoder.Frames.Count);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Icon decoding thread did not finish.");

        Assert.Null(failure);
    }

    [Fact]
    public void Connected_controller_tray_icon_keeps_its_blue_touchpad()
    {
        using var icon = BatteryIconRenderer.RenderControllerTrayIcon(
            new BatteryState(55, ConnectionState.Discharging),
            TrayTheme.DarkTaskbar);
        var frame = DecodeFrames(icon)[48];

        Assert.Contains(CopyPixels(frame), IsConnectedBluePixel);
    }

    [Fact]
    public void Executable_metadata_contains_a_visible_application_icon()
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "DualSenseBatteryTray.App.exe");

        Assert.True(File.Exists(executablePath), $"Application executable not found: {executablePath}");
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
        Assert.NotNull(icon);
        using var bitmap = icon.ToBitmap();
        Assert.Contains(CopyPixels(bitmap), pixel => pixel.Alpha != 0);
    }

    public static TheoryData<int, int> SupportedSizesAndSeparators => new()
    {
        { 16, 6 },
        { 24, 9 },
        { 32, 13 },
        { 48, 19 },
    };

    [Theory]
    [MemberData(nameof(SupportedSizesAndSeparators))]
    public void Render_uses_a_transparent_canvas_with_a_visible_separator_gap(
        int size,
        int separatorX)
    {
        var pixels = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            size));

        Assert.Equal(0, pixels[0].Alpha);
        Assert.Equal(0, pixels[size - 1].Alpha);
        Assert.Equal(0, pixels[(size - 1) * size].Alpha);
        Assert.Equal(0, pixels[(size * size) - 1].Alpha);
        for (var y = 0; y < size; y++)
            Assert.Equal(0, pixels[(y * size) + separatorX].Alpha);
        Assert.True(pixels.Count(pixel => pixel.Alpha == 0) > pixels.Length / 2);
    }

    [Fact]
    public void Render_uses_the_golden_16px_gamepad_mask()
    {
        var pixels = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            16));

        Assert.Equal(
        [
            ".####.",
            "#.##.#",
            "######",
            ".#..#.",
        ],
            ExtractMask(pixels, 16, 0, 6, 6, 4, White));
    }

    [Fact]
    public void Render_uses_the_golden_24px_gamepad_mask()
    {
        var pixels = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            24));

        Assert.Equal(
        [
            "..#####..",
            ".#######.",
            "#########",
            "##.###.##",
            "#########",
            ".##...##.",
            "..#...#..",
        ],
            ExtractMask(pixels, 24, 0, 8, 9, 7, White));
    }

    [Fact]
    public void Render_uses_the_golden_16px_100_percent_mask()
    {
        var pixels = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(100, ConnectionState.Discharging),
            16));

        Assert.Equal(
        [
            "1111111.",
            "11.1.11.",
            "11.1..1.",
            "1.1.1.11",
            "11111.11",
        ],
            ExtractBinaryMask(pixels, 16, 7, 5, 8, 5, Green));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void Render_controller_is_wider_than_tall_with_two_grips_at_every_size(int size)
    {
        var pixels = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            size));
        var bounds = GetBounds(pixels, size, pixel => pixel == White);

        Assert.True(bounds.Width >= bounds.Height + 2, $"Expected a wide gamepad, got {bounds.Width}x{bounds.Height}.");
        var bottomRows = ExtractMask(
            pixels,
            size,
            bounds.Left,
            bounds.Top + (bounds.Height / 2),
            bounds.Width,
            bounds.Height - (bounds.Height / 2),
            White);
        Assert.Contains(bottomRows, row => row.Contains("..", StringComparison.Ordinal));
        Assert.True(CountControlHoles(pixels, size, bounds) >= 2);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void Render_has_a_dark_theme_safe_outline_around_controller_and_text(int size)
    {
        var pixels = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            size));

        Assert.Contains(pixels, pixel => pixel == Outline);
        Assert.True(HasAdjacentPair(pixels, size, White, Outline));
        Assert.True(HasAdjacentPair(pixels, size, Green, Outline));
    }

    [Fact]
    public void Render_populates_both_regions_and_keeps_the_white_controller_on_the_left()
    {
        const int size = 48;
        var image = BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            size);
        var pixels = CopyPixels(image);
        var split = (int)Math.Ceiling(size * 0.4);

        Assert.True(CountPixels(pixels, size, 0, split, pixel => pixel.Alpha != 0) > 0);
        Assert.True(CountPixels(pixels, size, split, size, pixel => pixel.Alpha != 0) > 0);

        var whiteOnLeft = CountPixels(pixels, size, 0, split, pixel => pixel == White);
        var whiteOnRight = CountPixels(pixels, size, split, size, pixel => pixel == White);
        Assert.True(whiteOnLeft >= 20, $"Expected a visible controller, found {whiteOnLeft} white pixels.");
        Assert.True(whiteOnLeft > whiteOnRight * 4);
    }

    [Theory]
    [InlineData(50, 46, 204, 113)]
    [InlineData(20, 243, 156, 18)]
    [InlineData(10, 231, 76, 60)]
    public void Render_colors_the_percentage_by_battery_threshold(
        int percentage,
        byte red,
        byte green,
        byte blue)
    {
        const int size = 48;
        var image = BatteryIconRenderer.Render(
            new BatteryState(percentage, ConnectionState.Discharging),
            size);
        var pixels = CopyPixels(image);
        var expected = new Pixel(red, green, blue, 255);

        Assert.True(
            CountPixels(pixels, size, 19, size, pixel => pixel == expected) > 5,
            $"Expected exact percentage color RGB({red}, {green}, {blue}) in the right region.");
    }

    [Theory]
    [MemberData(nameof(SupportedSizesAndSeparators))]
    public void Render_colors_threshold_text_at_every_native_size(int size, int _)
    {
        Assert.Contains(
            CopyPixels(BatteryIconRenderer.Render(
                new BatteryState(50, ConnectionState.Discharging),
                size)),
            pixel => pixel == Green);
        Assert.Contains(
            CopyPixels(BatteryIconRenderer.Render(
                new BatteryState(20, ConnectionState.Discharging),
                size)),
            pixel => pixel == Amber);
        Assert.Contains(
            CopyPixels(BatteryIconRenderer.Render(
                new BatteryState(10, ConnectionState.Discharging),
                size)),
            pixel => pixel == Red);
    }

    [Fact]
    public void Render_fits_100_percent_without_colored_pixels_touching_the_right_edge()
    {
        const int size = 48;
        var image = BatteryIconRenderer.Render(
            new BatteryState(100, ConnectionState.Full),
            size);
        var pixels = CopyPixels(image);

        Assert.True(CountPixels(pixels, size, 19, size - 1, pixel => pixel == Green) > 5);
        Assert.Equal(0, CountPixels(pixels, size, size - 1, size, pixel => pixel == Green));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void Render_fits_100_percent_without_clipping_at_every_size(int size)
    {
        var pixels = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(100, ConnectionState.Discharging),
            size));

        Assert.Contains(pixels, pixel => pixel == Green);
        Assert.Equal(0, CountPixels(pixels, size, size - 1, size, pixel => pixel == Green));
    }

    [Fact]
    public void Render_keeps_100_percent_legible_at_16px()
    {
        const int size = 16;
        var image = BatteryIconRenderer.Render(
            new BatteryState(100, ConnectionState.Discharging),
            size);
        var pixels = CopyPixels(image);
        var coloredCoordinates = pixels
            .Select((pixel, index) => (pixel, x: index % size, y: index / size))
            .Where(item => item.pixel == Green)
            .ToArray();

        Assert.True(coloredCoordinates.Length >= 15);
        Assert.True(coloredCoordinates.Select(item => item.x).Distinct().Count() >= 7);
        Assert.True(coloredCoordinates.Select(item => item.y).Distinct().Count() >= 5);
        Assert.DoesNotContain(coloredCoordinates, item => item.x == size - 1);
    }

    [Fact]
    public void RenderTextMask_percent_sign_contributes_pixels_beyond_the_numeric_mask()
    {
        var numericMask = CopyPixels(BatteryIconRenderer.RenderTextMask("50", 29, 48));
        var percentMask = CopyPixels(BatteryIconRenderer.RenderTextMask("50%", 29, 48));

        Assert.NotEqual(ComputeHash(numericMask), ComputeHash(percentMask));
        Assert.True(
            numericMask.Zip(percentMask).Count(pair => pair.First != pair.Second) > 10,
            "Expected the percent glyph to make a visible contribution to the fitted text mask.");
    }

    [Fact]
    public void Render_charging_adds_a_distinct_mark_in_the_lower_left_region()
    {
        const int size = 48;
        var discharging = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            size));
        var charging = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Charging),
            size));
        var split = (int)Math.Ceiling(size * 0.4);
        var firstLowerRow = (int)Math.Floor(size * 0.58);
        var differences = 0;

        for (var y = firstLowerRow; y < size; y++)
        {
            for (var x = 0; x < split; x++)
            {
                var index = (y * size) + x;
                if (discharging[index] != charging[index])
                    differences++;
            }
        }

        Assert.True(differences >= 5, $"Expected a visible charging mark, found {differences} changed pixels.");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void Render_charging_uses_yellow_lightning_only_in_the_lower_left(int size)
    {
        var charging = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Charging),
            size));
        var discharging = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            size));
        var yellowCoordinates = charging
            .Select((pixel, index) => (pixel, x: index % size, y: index / size))
            .Where(item => item.pixel == Lightning)
            .ToArray();

        Assert.NotEmpty(yellowCoordinates);
        Assert.All(yellowCoordinates, item =>
        {
            Assert.True(item.x < Math.Ceiling(size * 0.4));
            Assert.True(item.y >= size / 2);
        });
        Assert.DoesNotContain(discharging, pixel => pixel == Lightning);
    }

    [Theory]
    [InlineData(16, 1, 11, 3, 4, 6)]
    [InlineData(24, 1, 15, 6, 8, 24)]
    [InlineData(32, 3, 19, 6, 8, 24)]
    [InlineData(48, 5, 29, 9, 12, 54)]
    public void Render_charging_uses_the_exact_native_lightning_location_and_weight(
        int size,
        int left,
        int top,
        int width,
        int height,
        int pixelCount)
    {
        var pixels = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Charging),
            size));

        Assert.Equal(
            new Bounds(left, top, width, height),
            GetBounds(pixels, size, pixel => pixel == Lightning));
        Assert.Equal(pixelCount, pixels.Count(pixel => pixel == Lightning));
    }

    [Theory]
    [InlineData(10, "10%")]
    [InlineData(50, "50%")]
    [InlineData(100, "100%")]
    [InlineData(null, "?%")]
    public void FormatPercentage_keeps_the_exact_value_and_percent_sign(int? percentage, string expected)
    {
        Assert.Equal(
            expected,
            BatteryIconRenderer.FormatPercentage(
                new BatteryState(percentage, ConnectionState.Unknown)));
    }

    [Fact]
    public void Render_unknown_state_is_distinct_from_a_known_percentage()
    {
        var unknown = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(null, ConnectionState.Unknown),
            48));
        var known = CopyPixels(BatteryIconRenderer.Render(
            new BatteryState(50, ConnectionState.Discharging),
            48));

        Assert.NotEqual(ComputeHash(known), ComputeHash(unknown));
    }

    private static Pixel[] CopyPixels(BitmapSource source)
    {
        var stride = source.PixelWidth * 4;
        var bytes = new byte[stride * source.PixelHeight];
        source.CopyPixels(bytes, stride, 0);
        var pixels = new Pixel[source.PixelWidth * source.PixelHeight];

        for (var index = 0; index < pixels.Length; index++)
        {
            var offset = index * 4;
            pixels[index] = new Pixel(
                bytes[offset + 2],
                bytes[offset + 1],
                bytes[offset],
                bytes[offset + 3]);
        }

        return pixels;
    }

    private static Pixel[] CopyPixels(System.Drawing.Bitmap source)
    {
        var pixels = new Pixel[source.Width * source.Height];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                pixels[(y * source.Width) + x] = new Pixel(
                    color.R,
                    color.G,
                    color.B,
                    color.A);
            }
        }

        return pixels;
    }

    private static IReadOnlyDictionary<int, BitmapFrame> DecodeFrames(System.Drawing.Icon icon)
    {
        using var stream = new MemoryStream();
        icon.Save(stream);
        stream.Position = 0;
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames.ToDictionary(frame => frame.PixelWidth);
    }

    private static int CountPixels(
        Pixel[] pixels,
        int width,
        int minimumX,
        int maximumX,
        Func<Pixel, bool> predicate)
    {
        var count = 0;
        for (var y = 0; y < pixels.Length / width; y++)
        {
            for (var x = minimumX; x < maximumX; x++)
            {
                if (predicate(pixels[(y * width) + x]))
                    count++;
            }
        }

        return count;
    }

    private static string[] ExtractMask(
        Pixel[] pixels,
        int canvasWidth,
        int left,
        int top,
        int width,
        int height,
        Pixel expected) => ExtractBinaryMask(
            pixels,
            canvasWidth,
            left,
            top,
            width,
            height,
            expected)
        .Select(row => row.Replace('1', '#'))
        .ToArray();

    private static string[] ExtractBinaryMask(
        Pixel[] pixels,
        int canvasWidth,
        int left,
        int top,
        int width,
        int height,
        Pixel expected)
    {
        var rows = new string[height];
        for (var y = 0; y < height; y++)
        {
            var row = new char[width];
            for (var x = 0; x < width; x++)
                row[x] = pixels[((top + y) * canvasWidth) + left + x] == expected ? '1' : '.';
            rows[y] = new string(row);
        }

        return rows;
    }

    private static Bounds GetBounds(
        Pixel[] pixels,
        int width,
        Func<Pixel, bool> predicate)
    {
        var coordinates = pixels
            .Select((pixel, index) => (pixel, x: index % width, y: index / width))
            .Where(item => predicate(item.pixel))
            .ToArray();
        Assert.NotEmpty(coordinates);
        var left = coordinates.Min(item => item.x);
        var right = coordinates.Max(item => item.x);
        var top = coordinates.Min(item => item.y);
        var bottom = coordinates.Max(item => item.y);
        return new Bounds(left, top, right - left + 1, bottom - top + 1);
    }

    private static Bounds GetOpaqueBounds(BitmapSource source) =>
        GetBounds(CopyPixels(source), source.PixelWidth, pixel => pixel.Alpha != 0);

    private static bool IsWhiteTextPixel(Pixel pixel) =>
        pixel.Alpha != 0 &&
        pixel.Red == pixel.Green &&
        pixel.Green == pixel.Blue &&
        pixel.Red >= 96;

    private static bool IsDarkOutlinePixel(Pixel pixel) =>
        pixel.Alpha != 0 && pixel.Red < 80 && pixel.Green < 80 && pixel.Blue < 80;

    private static bool IsConnectedBluePixel(Pixel pixel) =>
        pixel.Alpha > 0 &&
        pixel.Blue >= 150 &&
        pixel.Blue > pixel.Red * 1.3 &&
        pixel.Blue > pixel.Green * 1.05;

    private static bool IsLightControllerPixel(Pixel pixel) =>
        pixel.Alpha != 0 && pixel.Red >= 180 && pixel.Green >= 180 && pixel.Blue >= 180;

    private static bool IsLightDigitPixel(Pixel pixel) =>
        pixel.Alpha > 0 && pixel.Red >= 220 && pixel.Green >= 220 && pixel.Blue >= 220;

    private static (int Left, int Top, int Right, int Bottom) GetTouchpadRegion(int size) =>
        (
            (int)Math.Floor(size * 0.28),
            (int)Math.Floor(size * 0.15),
            (int)Math.Ceiling(size * 0.72),
            (int)Math.Ceiling(size * 0.45));

    private static bool HasOpaqueEdgeColor(Pixel[] pixels, int size, bool expectLight)
    {
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var pixel = pixels[(y * size) + x];
                var expectedColor = expectLight
                    ? IsLightDigitPixel(pixel)
                    : IsDarkOutlinePixel(pixel);
                if (!expectedColor)
                    continue;

                if ((x > 0 && pixels[(y * size) + x - 1].Alpha == 0) ||
                    (x + 1 < size && pixels[(y * size) + x + 1].Alpha == 0) ||
                    (y > 0 && pixels[((y - 1) * size) + x].Alpha == 0) ||
                    (y + 1 < size && pixels[((y + 1) * size) + x].Alpha == 0))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountControlHoles(Pixel[] pixels, int width, Bounds bounds)
    {
        var count = 0;
        for (var y = bounds.Top + 1; y < bounds.Top + bounds.Height - 1; y++)
        {
            for (var x = bounds.Left + 1; x < bounds.Left + bounds.Width - 1; x++)
            {
                if (pixels[(y * width) + x] != Outline)
                    continue;
                if (pixels[(y * width) + x - 1] == White && pixels[(y * width) + x + 1] == White)
                    count++;
            }
        }

        return count;
    }

    private static bool HasAdjacentPair(Pixel[] pixels, int width, Pixel first, Pixel second)
    {
        for (var y = 0; y < pixels.Length / width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width) + x] != first)
                    continue;
                if (x > 0 && pixels[(y * width) + x - 1] == second)
                    return true;
                if (x + 1 < width && pixels[(y * width) + x + 1] == second)
                    return true;
                if (y > 0 && pixels[((y - 1) * width) + x] == second)
                    return true;
                if (y + 1 < pixels.Length / width && pixels[((y + 1) * width) + x] == second)
                    return true;
            }
        }

        return false;
    }

    private static string ComputeHash(Pixel[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (var index = 0; index < pixels.Length; index++)
        {
            var offset = index * 4;
            bytes[offset] = pixels[index].Red;
            bytes[offset + 1] = pixels[index].Green;
            bytes[offset + 2] = pixels[index].Blue;
            bytes[offset + 3] = pixels[index].Alpha;
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private readonly record struct Pixel(byte Red, byte Green, byte Blue, byte Alpha);
    private readonly record struct Bounds(int Left, int Top, int Width, int Height);
}
