using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DualSenseBatteryTray.App.Shell;
using DualSenseBatteryTray.App.Tray;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;
using DualSenseBatteryTray.Hid;
using Forms = System.Windows.Forms;

namespace DualSenseBatteryTray.App.Tests;

public sealed class ApplicationShellTests
{
    [Theory]
    [InlineData(typeof(ControllerReportsStaleException), "controller.stale")]
    [InlineData(typeof(IOException), "reader.failure")]
    public void Reader_end_event_distinguishes_stale_from_failure(Type type, string expected)
    {
        var error = (Exception)Activator.CreateInstance(type)!;

        Assert.Equal(expected, App.ReaderEndEventName(error));
    }

    [Fact]
    public void Reader_end_event_maps_clean_completion()
    {
        Assert.Equal("reader.ended", App.ReaderEndEventName(null));
    }

    [Fact]
    public void WindowSmallIconController_applies_once_and_owns_icon_until_disposed()
    {
        var created = 0;
        var applied = new List<(nint Window, nint Icon)>();
        using var expected = BatteryIconRenderer.RenderWindowSmallIcon(16);
        using var controller = new WindowSmallIconController(
            () => { created++; return (System.Drawing.Icon)expected.Clone(); },
            (window, icon) => applied.Add((window, icon)));

        controller.Apply((nint)42);
        controller.Apply((nint)42);

        Assert.Equal(1, created);
        Assert.Single(applied);
        Assert.Equal((nint)42, applied[0].Window);
        Assert.NotEqual(nint.Zero, applied[0].Icon);
    }

    [Fact]
    public void WindowSmallIconController_disposes_new_icon_when_apply_fails()
    {
        using var icon = BatteryIconRenderer.RenderWindowSmallIcon(16);
        var disposed = 0;
        var controller = new WindowSmallIconController(
            () => icon,
            (_, _) => throw new InvalidOperationException("apply failed"),
            _ => disposed++);

        Assert.Throws<InvalidOperationException>(() => controller.Apply((nint)42));
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void SingleInstanceGuard_allows_only_one_current_user_instance()
    {
        var first = SingleInstanceGuard.TryAcquire();
        Assert.NotNull(first);

        using var second = SingleInstanceGuard.TryAcquire();
        Assert.Null(second);

        first.Dispose();
        using var replacement = SingleInstanceGuard.TryAcquire();
        Assert.NotNull(replacement);
    }

    [Fact]
    public void MainWindowViewModel_maps_the_four_status_values()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Update(new BatteryState(75, ConnectionState.Charging));

        Assert.Equal("DualSense Wireless Controller", viewModel.DeviceName);
        Assert.Equal("USB", viewModel.ConnectionType);
        Assert.Equal("75%", viewModel.Percentage);
        Assert.Equal("Charging", viewModel.ChargeState);
    }

    [Fact]
    public void TrayApplicationContext_updates_only_the_notification_area_icon_for_battery_changes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                var expectedLargeIcon = BatteryIconRenderer.RenderApplicationIcon();
                Assert.Same(expectedLargeIcon, window.Icon);
                Assert.Equal(
                    ComputeHash(CopyPixels(expectedLargeIcon)),
                    ComputeHash(CopyPixels(window.Icon)));
                using var context = new TrayApplicationContext(
                    window,
                    () => { },
                    () => { },
                    new AvailableWatcherTaskService(),
                    new FakeTrayThemeProvider(TrayTheme.DarkTaskbar),
                    new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.Compact));
                var notifyIcon = Assert.Single(context.ActiveIcons);
                window.TaskbarItemInfo.Overlay = BitmapSource.Create(
                    16,
                    16,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    new byte[16 * 16 * 4],
                    16 * 4);

                context.UpdateState(new BatteryState(75, ConnectionState.Discharging));
                var normalIcon = notifyIcon.Icon;
                context.UpdateState(new BatteryState(20, ConnectionState.Discharging));
                var lowState = new BatteryState(20, ConnectionState.Discharging);
                var lowIcon = notifyIcon.Icon;

                Assert.NotSame(normalIcon, lowIcon);
                Assert.Equal(
                    ComputeHash(BatteryIconRenderer.RenderCompact(lowState, TrayTheme.DarkTaskbar, 32)),
                    ComputeHash(DecodeFrames(lowIcon)[32]));
                Assert.Equal(
                    ComputeHash(CopyPixels(expectedLargeIcon)),
                    ComputeHash(CopyPixels(window.Icon)));
                Assert.Null(window.TaskbarItemInfo.Overlay);
                window.BeginShutdown();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF test thread did not finish.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void TrayApplicationContext_assigns_compact_frames_to_one_NotifyIcon()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                using var context = new TrayApplicationContext(
                    window,
                    () => { },
                    () => { },
                    new AvailableWatcherTaskService(),
                    new FakeTrayThemeProvider(TrayTheme.DarkTaskbar),
                    new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.Compact));
                var notifyIcon = Assert.Single(context.ActiveIcons);
                var state = new BatteryState(100, ConnectionState.Full);

                context.UpdateState(state);

                var frames = DecodeFrames(notifyIcon.Icon);
                Assert.Equal([16, 24, 32, 48], frames.Keys.Order().ToArray());
                Assert.Equal(Forms.SystemInformation.SmallIconSize, notifyIcon.Icon?.Size);
                foreach (var size in frames.Keys)
                {
                    Assert.Equal(
                        ComputeHash(BatteryIconRenderer.RenderCompact(state, TrayTheme.DarkTaskbar, size)),
                        ComputeHash(frames[size]));
                }
                Assert.Contains("100%", notifyIcon.Text, StringComparison.Ordinal);

                window.BeginShutdown();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF test thread did not finish.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Compact_mode_owns_one_visible_icon_and_two_icons_mode_owns_two()
    {
        RunOnStaThread(() =>
        {
            var window = new MainWindow();
            using var theme = new FakeTrayThemeProvider(TrayTheme.DarkTaskbar);
            var preferences = new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.Compact);
            using var context = new TrayApplicationContext(
                window, () => { }, () => { }, new AvailableWatcherTaskService(),
                theme, preferences);

            Assert.Equal(TrayLayoutMode.Compact, context.LayoutMode);
            Assert.Single(context.ActiveIcons.Where(icon => icon.Visible));

            context.ChangeLayout(TrayLayoutMode.TwoIcons);

            Assert.Equal(TrayLayoutMode.TwoIcons, context.LayoutMode);
            Assert.Equal(2, context.ActiveIcons.Count(icon => icon.Visible));
            Assert.Single(context.ActiveIcons.Select(icon => icon.Text).Distinct());
            Assert.Equal(TrayLayoutMode.TwoIcons, preferences.Value);
            window.BeginShutdown();
        });
    }

    [Fact]
    public void Two_icons_preference_shows_two_visible_icons_with_a_synchronized_tooltip()
    {
        RunOnStaThread(() =>
        {
            var window = new MainWindow();
            using var context = new TrayApplicationContext(
                window, () => { }, () => { }, new AvailableWatcherTaskService(),
                new FakeTrayThemeProvider(TrayTheme.DarkTaskbar),
                new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.TwoIcons));

            Assert.Equal(TrayLayoutMode.TwoIcons, context.LayoutMode);
            Assert.Equal(2, context.ActiveIcons.Count(icon => icon.Visible));
            Assert.Single(context.ActiveIcons.Select(icon => icon.Text).Distinct());
            window.BeginShutdown();
        });
    }

    [Fact]
    public void Persisted_two_icons_mode_is_selected_and_exposed_as_radio_menu_choices()
    {
        RunOnStaThread(() =>
        {
            var window = new MainWindow();
            using var theme = new FakeTrayThemeProvider(TrayTheme.DarkTaskbar);
            var preferences = new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.TwoIcons);
            using var context = new TrayApplicationContext(
                window, () => { }, () => { }, new AvailableWatcherTaskService(),
                theme, preferences);
            var menu = Assert.IsType<Forms.ContextMenuStrip>(context.ActiveIcons[0].ContextMenuStrip);
            var layoutMenu = Assert.IsType<Forms.ToolStripMenuItem>(menu.Items
                .Cast<Forms.ToolStripItem>()
                .Single(item => item.Text == "Tray layout"));
            var choices = layoutMenu.DropDownItems
                .Cast<Forms.ToolStripItem>()
                .Select(item => Assert.IsType<Forms.ToolStripMenuItem>(item))
                .ToArray();

            Assert.Equal(TrayLayoutMode.TwoIcons, context.LayoutMode);
            Assert.Equal(2, context.ActiveIcons.Count(icon => icon.Visible));
            Assert.Equal(["Compact", "Two icons"], choices.Select(choice => choice.Text));
            Assert.All(choices, choice => Assert.True(choice.CheckOnClick));
            Assert.False(choices[0].Checked);
            Assert.True(choices[1].Checked);
            window.BeginShutdown();
        });
    }

    [Fact]
    public void Expanded_icons_share_tooltip_menu_and_left_click_behavior()
    {
        RunOnStaThread(() =>
        {
            var window = new MainWindow();
            using var context = new TrayApplicationContext(
                window, () => { }, () => { }, new AvailableWatcherTaskService(),
                new FakeTrayThemeProvider(TrayTheme.DarkTaskbar),
                new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.TwoIcons));
            var state = new BatteryState(75, ConnectionState.Charging);
            context.UpdateState(state);
            using var expectedController = BatteryIconRenderer.RenderControllerTrayIcon(
                state,
                TrayTheme.DarkTaskbar);
            using var expectedPercentage = BatteryIconRenderer.RenderPercentageTrayIcon(
                state,
                TrayTheme.DarkTaskbar);

            Assert.Equal(2, context.ActiveIcons.Count);
            Assert.Single(context.ActiveIcons.Select(icon => icon.Text).Distinct());
            Assert.All(context.ActiveIcons, icon => Assert.Contains("75%", icon.Text));
            Assert.Same(context.ActiveIcons[0].ContextMenuStrip, context.ActiveIcons[1].ContextMenuStrip);
            Assert.Equal(
                ComputeHash(DecodeFrames(expectedController)[32]),
                ComputeHash(DecodeFrames(context.ActiveIcons[0].Icon)[32]));
            Assert.Equal(
                ComputeHash(DecodeFrames(expectedPercentage)[32]),
                ComputeHash(DecodeFrames(context.ActiveIcons[1].Icon)[32]));
            Assert.Equal(
                GetMouseClickHandler(context.ActiveIcons[0]),
                GetMouseClickHandler(context.ActiveIcons[1]));

            window.BeginShutdown();
        });
    }

    [Fact]
    public void Changing_layout_does_not_request_a_reader_refresh()
    {
        RunOnStaThread(() =>
        {
            var refreshRequests = 0;
            var window = new MainWindow();
            using var context = new TrayApplicationContext(
                window, () => refreshRequests++, () => { }, new AvailableWatcherTaskService(),
                new FakeTrayThemeProvider(TrayTheme.DarkTaskbar),
                new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.Compact));

            context.ChangeLayout(TrayLayoutMode.TwoIcons);
            context.ChangeLayout(TrayLayoutMode.Compact);

            Assert.Equal(0, refreshRequests);
            window.BeginShutdown();
        });
    }

    [Fact]
    public void Failed_second_icon_assignment_retains_the_compact_icon_and_preference()
    {
        RunOnStaThread(() =>
        {
            var assignment = 0;
            var failSecondAssignment = false;
            var newlyAssignedIcons = new List<System.Drawing.Icon>();
            var window = new MainWindow();
            var preferences = new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.Compact);
            using var context = new TrayApplicationContext(
                window, () => { }, () => { }, new AvailableWatcherTaskService(),
                new FakeTrayThemeProvider(TrayTheme.DarkTaskbar), preferences,
                (notifyIcon, icon) =>
                {
                    notifyIcon.Icon = icon;
                    if (!failSecondAssignment || icon is null)
                        return;

                    newlyAssignedIcons.Add(icon);
                    if (++assignment == 2)
                        throw new InvalidOperationException("second assignment failed");
                });
            var compactNotifyIcon = Assert.Single(context.ActiveIcons);
            var compactIcon = compactNotifyIcon.Icon;
            failSecondAssignment = true;

            Assert.Throws<InvalidOperationException>(() =>
                context.ChangeLayout(TrayLayoutMode.TwoIcons));

            Assert.Equal(TrayLayoutMode.Compact, context.LayoutMode);
            Assert.Equal(TrayLayoutMode.Compact, preferences.Value);
            Assert.Same(compactNotifyIcon, Assert.Single(context.ActiveIcons));
            Assert.Same(compactIcon, compactNotifyIcon.Icon);
            Assert.True(compactNotifyIcon.Visible);
            Assert.NotEqual(IntPtr.Zero, compactIcon?.Handle);
            Assert.Equal(2, newlyAssignedIcons.Count);
            Assert.All(newlyAssignedIcons, icon =>
                Assert.Throws<ObjectDisposedException>(() => _ = icon.Handle));
            window.BeginShutdown();
        });
    }

    [Fact]
    public void Theme_changes_are_marshaled_to_the_window_dispatcher_and_refresh_both_icons()
    {
        RunOnStaThread(() =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var assignmentThreads = new List<int>();
            var window = new MainWindow();
            using var theme = new FakeTrayThemeProvider(TrayTheme.DarkTaskbar);
            using var context = new TrayApplicationContext(
                window, () => { }, () => { }, new AvailableWatcherTaskService(),
                theme, new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.TwoIcons),
                (notifyIcon, icon) =>
                {
                    assignmentThreads.Add(Environment.CurrentManagedThreadId);
                    notifyIcon.Icon = icon;
                });
            var darkHashes = context.ActiveIcons
                .Select(icon => ComputeHash(DecodeFrames(icon.Icon)[32]))
                .ToArray();
            assignmentThreads.Clear();

            var themeThread = new Thread(() => theme.Set(TrayTheme.LightTaskbar));
            themeThread.Start();
            Assert.True(themeThread.Join(TimeSpan.FromSeconds(5)), "Theme thread did not finish.");
            PumpDispatcherUntil(() => assignmentThreads.Count == 2);

            Assert.All(assignmentThreads, thread => Assert.Equal(uiThread, thread));
            Assert.NotEqual(
                darkHashes,
                context.ActiveIcons.Select(icon => ComputeHash(DecodeFrames(icon.Icon)[32])).ToArray());
            window.BeginShutdown();
        });
    }

    [Fact]
    public void Theme_change_during_initial_snapshot_is_applied_after_initial_reconciliation()
    {
        RunOnStaThread(() =>
        {
            var window = new MainWindow();
            using var theme = new InterleavingTrayThemeProvider();
            using var context = new TrayApplicationContext(
                window, () => { }, () => { }, new AvailableWatcherTaskService(),
                theme, new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.Compact));
            var state = new BatteryState(null, ConnectionState.Unknown);

            Assert.Equal(TrayTheme.LightTaskbar, theme.Current);
            Assert.Equal(
                ComputeHash(BatteryIconRenderer.RenderCompact(
                    state,
                    TrayTheme.LightTaskbar,
                    32)),
                ComputeHash(DecodeFrames(Assert.Single(context.ActiveIcons).Icon)[32]));
            window.BeginShutdown();
        });
    }

    [Fact]
    public void TrayApplicationContext_dispose_makes_every_icon_invisible()
    {
        RunOnStaThread(() =>
        {
            var window = new MainWindow();
            var context = new TrayApplicationContext(
                window, () => { }, () => { }, new AvailableWatcherTaskService(),
                new FakeTrayThemeProvider(TrayTheme.DarkTaskbar),
                new MemoryTrayLayoutPreferenceStore(TrayLayoutMode.TwoIcons));
            var icons = context.ActiveIcons.ToArray();

            context.Dispose();

            Assert.All(icons, icon => Assert.False(icon.Visible));
            window.BeginShutdown();
        });
    }

    [Fact]
    public void MainWindow_instances_share_one_static_controller_only_application_icon()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var first = new MainWindow();
                var second = new MainWindow();

                Assert.Same(first.Icon, second.Icon);
                Assert.Null(first.TaskbarItemInfo.Overlay);
                Assert.Null(second.TaskbarItemInfo.Overlay);

                first.BeginShutdown();
                second.BeginShutdown();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF test thread did not finish.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void FormatToolTip_includes_device_percentage_and_charge_state()
    {
        var toolTip = TrayApplicationContext.FormatToolTip(
            new BatteryState(75, ConnectionState.Charging));

        Assert.Contains("DualSense", toolTip, StringComparison.Ordinal);
        Assert.Contains("75%", toolTip, StringComparison.Ordinal);
        Assert.Contains("Charging", toolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatteryReaderSession_refresh_disposes_the_old_source_and_creates_a_fresh_one()
    {
        var sources = new List<BlockingSource>();
        var statesSeen = 0;
        using var firstStateSeen = new SemaphoreSlim(0);
        using var secondStateSeen = new SemaphoreSlim(0);
        await using var session = new BatteryReaderSession(
            () =>
            {
                var source = new BlockingSource();
                sources.Add(source);
                return source;
            },
            _ =>
            {
                var count = Interlocked.Increment(ref statesSeen);
                (count == 1 ? firstStateSeen : secondStateSeen).Release();
            },
            _ => { });

        session.Start();
        Assert.True(await firstStateSeen.WaitAsync(TimeSpan.FromSeconds(5)));

        await session.RefreshAsync();
        Assert.True(await secondStateSeen.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(2, sources.Count);
        Assert.True(sources[0].Disposed);
        Assert.False(sources[1].Disposed);
    }

    [Fact]
    public async Task BatteryReaderSession_reports_a_clean_end_for_device_removal()
    {
        var ended = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new BatteryReaderSession(
            () => new EmptySource(),
            _ => { },
            error => ended.TrySetResult(error));

        session.Start();

        Assert.Null(await ended.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task BatteryReaderSession_disposal_is_idempotent_during_application_exit()
    {
        var session = new BatteryReaderSession(
            () => new EmptySource(),
            _ => { },
            _ => { });
        session.Start();

        var firstDisposal = session.DisposeAsync().AsTask();
        var repeatedDisposal = session.DisposeAsync().AsTask();

        Assert.Same(firstDisposal, repeatedDisposal);
        await Task.WhenAll(firstDisposal, repeatedDisposal);
        Assert.Same(firstDisposal, session.DisposeAsync().AsTask());
    }

    private sealed class BlockingSource : IBatteryReportSource, IDisposable
    {
        public bool Disposed { get; private set; }

        public async IAsyncEnumerable<BatteryState> ReadStatesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new BatteryState(75, ConnectionState.Discharging);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class EmptySource : IBatteryReportSource
    {
        public async IAsyncEnumerable<BatteryState> ReadStatesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield break;
        }
    }

    private static Delegate GetMouseClickHandler(Forms.NotifyIcon notifyIcon)
    {
        var eventKey = typeof(Forms.NotifyIcon)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(field => field.Name.Contains("mouseClick", StringComparison.OrdinalIgnoreCase))
            .GetValue(null);
        var eventList = typeof(System.ComponentModel.Component).GetProperty(
            "Events", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(notifyIcon)
            as System.ComponentModel.EventHandlerList;
        Assert.NotNull(eventKey);
        Assert.NotNull(eventList);
        return Assert.IsAssignableFrom<Delegate>(eventList[eventKey]);
    }

    private static void PumpDispatcherUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        Assert.True(condition(), "Dispatcher work did not complete before the timeout.");
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF test thread did not finish.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IReadOnlyDictionary<int, BitmapFrame> DecodeFrames(System.Drawing.Icon? icon)
    {
        Assert.NotNull(icon);
        using var stream = new MemoryStream();
        icon.Save(stream);
        stream.Position = 0;
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames.ToDictionary(frame => frame.PixelWidth);
    }

    private static byte[] CopyPixels(ImageSource? source)
    {
        var bitmap = Assert.IsAssignableFrom<BitmapSource>(source);
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    private static string ComputeHash(BitmapSource source) =>
        ComputeHash(CopyPixels(source));

    private static string ComputeHash(byte[] pixels) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));

    private sealed class AvailableWatcherTaskService : DualSenseBatteryTray.App.Startup.IWatcherTaskService
    {
        public Task<DualSenseBatteryTray.App.Startup.WatcherTaskStatus> QueryAsync() =>
            Task.FromResult(DualSenseBatteryTray.App.Startup.WatcherTaskStatus.Enabled);

        public Task<DualSenseBatteryTray.App.Startup.WatcherTaskActionResult> EnableAndStartAsync() =>
            Task.FromResult(DualSenseBatteryTray.App.Startup.WatcherTaskActionResult.Succeeded);

        public Task<DualSenseBatteryTray.App.Startup.WatcherTaskActionResult> DisableAsync() =>
            Task.FromResult(DualSenseBatteryTray.App.Startup.WatcherTaskActionResult.Succeeded);
    }

    private sealed class FakeTrayThemeProvider(TrayTheme current) : ITrayThemeProvider
    {
        public TrayTheme Current { get; private set; } = current;
        public event Action<TrayTheme>? ThemeChanged;
        public void Set(TrayTheme theme) { Current = theme; ThemeChanged?.Invoke(theme); }
        public void Refresh() { }
        public void Dispose() { }
    }

    private sealed class InterleavingTrayThemeProvider : ITrayThemeProvider
    {
        private TrayTheme _current = TrayTheme.DarkTaskbar;
        private bool _switchDuringFirstRead = true;

        public TrayTheme Current
        {
            get
            {
                var snapshot = _current;
                if (_switchDuringFirstRead)
                {
                    _switchDuringFirstRead = false;
                    _current = TrayTheme.LightTaskbar;
                    ThemeChanged?.Invoke(_current);
                }

                return snapshot;
            }
        }

        public event Action<TrayTheme>? ThemeChanged;

        public void Refresh() { }

        public void Dispose() { }
    }

    private sealed class MemoryTrayLayoutPreferenceStore(TrayLayoutMode value)
        : ITrayLayoutPreferenceStore
    {
        public TrayLayoutMode Value { get; private set; } = value;
        public TrayLayoutMode Load() => Value;
        public void Save(TrayLayoutMode mode) => Value = mode;
    }
}
