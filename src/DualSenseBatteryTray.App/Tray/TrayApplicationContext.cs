using System.Drawing;
using System.Windows;
using DualSenseBatteryTray.App.Notifications;
using DualSenseBatteryTray.App.Startup;
using DualSenseBatteryTray.Core.Battery;
using Forms = System.Windows.Forms;

namespace DualSenseBatteryTray.App.Tray;

internal sealed class TrayApplicationContext : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly Action _refreshRequested;
    private readonly Action _exitRequested;
    private readonly WatcherListenerController _watcherListenerController;
    private readonly ITrayThemeProvider _trayThemeProvider;
    private readonly ITrayLayoutPreferenceStore _layoutPreferenceStore;
    private readonly Action<Forms.NotifyIcon, Icon?> _assignIcon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _listenerMenuItem;
    private readonly Forms.ToolStripMenuItem _compactLayoutMenuItem;
    private readonly Forms.ToolStripMenuItem _twoIconsLayoutMenuItem;
    private readonly List<Forms.NotifyIcon> _activeIcons = [];
    private readonly List<Icon> _currentIcons = [];
    private readonly object _themeInitializationSync = new();
    private WindowsBatteryNotifier? _notifier;
    private BatteryState? _currentState;
    private TrayTheme _currentTheme;
    private TrayTheme? _pendingTheme;
    private bool _initializingTheme = true;
    private bool _disposed;

    internal TrayApplicationContext(
        MainWindow mainWindow,
        Action refreshRequested,
        Action exitRequested,
        IWatcherTaskService watcherTaskService,
        ITrayThemeProvider trayThemeProvider,
        ITrayLayoutPreferenceStore layoutPreferenceStore,
        Action<Forms.NotifyIcon, Icon?>? assignIcon = null)
    {
        _mainWindow = mainWindow;
        _refreshRequested = refreshRequested;
        _exitRequested = exitRequested;
        _watcherListenerController = new WatcherListenerController(watcherTaskService);
        _trayThemeProvider = trayThemeProvider;
        _layoutPreferenceStore = layoutPreferenceStore;
        _assignIcon = assignIcon ?? AssignIcon;

        var refreshItem = new Forms.ToolStripMenuItem("Refresh");
        refreshItem.Click += (_, _) => _refreshRequested();
        _listenerMenuItem = new Forms.ToolStripMenuItem("Listen after Windows sign-in");
        _listenerMenuItem.Click += async (_, _) => await ToggleListenerAsync();
        _compactLayoutMenuItem = CreateLayoutMenuItem("Compact", TrayLayoutMode.Compact);
        _twoIconsLayoutMenuItem = CreateLayoutMenuItem("Two icons", TrayLayoutMode.TwoIcons);
        var layoutMenu = new Forms.ToolStripMenuItem("Tray layout");
        layoutMenu.DropDownItems.AddRange([_compactLayoutMenuItem, _twoIconsLayoutMenuItem]);
        var aboutItem = new Forms.ToolStripMenuItem("About");
        aboutItem.Click += (_, _) => ShowAbout();
        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => _exitRequested();
        _menu.Items.AddRange(
        [
            refreshItem,
            _listenerMenuItem,
            layoutMenu,
            new Forms.ToolStripSeparator(),
            aboutItem,
            exitItem,
        ]);

        _watcherListenerController.StateChanged += ApplyListenerState;
        ApplyListenerState(_watcherListenerController.State);

        try
        {
            var initialState = new BatteryState(null, ConnectionState.Unknown);
            var initialLayout = NormalizeLayout(_layoutPreferenceStore.Load());
            _trayThemeProvider.ThemeChanged += OnThemeChanged;
            Reconcile(initialState, _trayThemeProvider.Current, initialLayout);
            CompleteThemeInitialization();
            _notifier = new WindowsBatteryNotifier(_activeIcons[0]);
            _ = _watcherListenerController.RefreshAsync();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal TrayLayoutMode LayoutMode { get; private set; }

    internal IReadOnlyList<Forms.NotifyIcon> ActiveIcons => _activeIcons;

    public void UpdateState(BatteryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state == _currentState)
            return;

        Reconcile(state, _currentTheme, LayoutMode);
    }

    internal void ChangeLayout(TrayLayoutMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        if (mode == LayoutMode)
            return;

        Reconcile(_currentState!, _currentTheme, mode);
        _layoutPreferenceStore.Save(mode);
    }

    public void NotifyLowBattery(int threshold) => _notifier!.Notify(threshold);

    internal static string FormatToolTip(BatteryState state)
    {
        var percentage = state.Percentage is int level ? $"{level}%" : "?";
        var chargeState = state.State switch
        {
            ConnectionState.Discharging => "Discharging",
            ConnectionState.Charging => "Charging",
            ConnectionState.Full => "Full",
            ConnectionState.Flat => "Flat",
            _ => "Unknown",
        };
        return $"DualSense USB · {percentage} · {chargeState}";
    }

    public void Dispose()
    {
        lock (_themeInitializationSync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _initializingTheme = false;
            _pendingTheme = null;
        }
        _trayThemeProvider.ThemeChanged -= OnThemeChanged;
        _trayThemeProvider.Dispose();
        _watcherListenerController.StateChanged -= ApplyListenerState;
        foreach (var notifyIcon in _activeIcons)
        {
            notifyIcon.Visible = false;
            notifyIcon.MouseClick -= ShowWindowOnLeftClick;
            notifyIcon.Dispose();
        }
        _activeIcons.Clear();
        foreach (var icon in _currentIcons)
            icon.Dispose();
        _currentIcons.Clear();
        _menu.Dispose();
    }

    private Forms.ToolStripMenuItem CreateLayoutMenuItem(string text, TrayLayoutMode mode)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            CheckOnClick = true,
        };
        item.Click += (_, _) =>
        {
            try
            {
                ChangeLayout(mode);
            }
            finally
            {
                UpdateLayoutMenuChecks();
            }
        };
        return item;
    }

    private void Reconcile(BatteryState state, TrayTheme theme, TrayLayoutMode layout)
    {
        var nextIcons = RenderIconSet(state, theme, layout);
        var desiredCount = nextIcons.Count;
        var nextNotifyIcons = new List<Forms.NotifyIcon>(desiredCount);
        var newlyCreatedNotifyIcons = new List<Forms.NotifyIcon>();
        var previousAssignedIcons = new Icon?[desiredCount];
        var previousTexts = new string[desiredCount];
        var previousVisibility = new bool[desiredCount];

        try
        {
            for (var index = 0; index < desiredCount; index++)
            {
                var notifyIcon = index < _activeIcons.Count
                    ? _activeIcons[index]
                    : CreateNotifyIcon();
                if (index >= _activeIcons.Count)
                    newlyCreatedNotifyIcons.Add(notifyIcon);
                nextNotifyIcons.Add(notifyIcon);
                previousAssignedIcons[index] = notifyIcon.Icon;
                previousTexts[index] = notifyIcon.Text;
                previousVisibility[index] = notifyIcon.Visible;
            }

            var tooltip = FormatToolTip(state);
            for (var index = 0; index < desiredCount; index++)
            {
                _assignIcon(nextNotifyIcons[index], nextIcons[index]);
                nextNotifyIcons[index].Text = tooltip;
            }

            _mainWindow.Update(state);
            foreach (var notifyIcon in nextNotifyIcons)
                notifyIcon.Visible = true;
        }
        catch
        {
            RollBackAssignments(
                nextNotifyIcons,
                previousAssignedIcons,
                previousTexts,
                previousVisibility);
            DisposeNewNotifyIcons(newlyCreatedNotifyIcons);
            ReconcileFailedOwnership(nextIcons);
            throw;
        }

        var obsoleteNotifyIcons = _activeIcons.Skip(desiredCount).ToArray();
        var previousOwnedIcons = _currentIcons.ToArray();
        foreach (var notifyIcon in obsoleteNotifyIcons)
        {
            notifyIcon.Visible = false;
            notifyIcon.MouseClick -= ShowWindowOnLeftClick;
            notifyIcon.Dispose();
        }

        _activeIcons.Clear();
        _activeIcons.AddRange(nextNotifyIcons);
        _currentIcons.Clear();
        _currentIcons.AddRange(nextIcons);
        foreach (var icon in previousOwnedIcons)
        {
            if (!nextIcons.Any(next => ReferenceEquals(next, icon)))
                icon.Dispose();
        }

        _currentState = state;
        _currentTheme = theme;
        LayoutMode = layout;
        UpdateLayoutMenuChecks();
    }

    private static List<Icon> RenderIconSet(
        BatteryState state,
        TrayTheme theme,
        TrayLayoutMode layout)
    {
        var icons = new List<Icon>(layout == TrayLayoutMode.Compact ? 1 : 2);
        try
        {
            switch (layout)
            {
                case TrayLayoutMode.Compact:
                    icons.Add(BatteryIconRenderer.RenderCompactTrayIcon(state, theme));
                    break;
                case TrayLayoutMode.TwoIcons:
                    icons.Add(BatteryIconRenderer.RenderControllerTrayIcon(state, theme));
                    icons.Add(BatteryIconRenderer.RenderPercentageTrayIcon(state, theme));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout), layout, null);
            }

            return icons;
        }
        catch
        {
            foreach (var icon in icons)
                icon.Dispose();
            throw;
        }
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Text = "DualSense USB · ? · Unknown",
            Visible = false,
        };
        notifyIcon.MouseClick += ShowWindowOnLeftClick;
        return notifyIcon;
    }

    private static void AssignIcon(Forms.NotifyIcon notifyIcon, Icon? icon) =>
        notifyIcon.Icon = icon;

    private static void RollBackAssignments(
        IReadOnlyList<Forms.NotifyIcon> notifyIcons,
        IReadOnlyList<Icon?> previousIcons,
        IReadOnlyList<string> previousTexts,
        IReadOnlyList<bool> previousVisibility)
    {
        for (var index = notifyIcons.Count - 1; index >= 0; index--)
        {
            try
            {
                notifyIcons[index].Icon = previousIcons[index];
            }
            catch
            {
                // Disposal below preserves any newly assigned handle still in use.
            }

            try
            {
                notifyIcons[index].Text = previousTexts[index];
                notifyIcons[index].Visible = previousVisibility[index];
            }
            catch
            {
                // Preserve the original reconciliation failure.
            }
        }
    }

    private void DisposeNewNotifyIcons(IEnumerable<Forms.NotifyIcon> notifyIcons)
    {
        foreach (var notifyIcon in notifyIcons)
        {
            notifyIcon.Visible = false;
            notifyIcon.MouseClick -= ShowWindowOnLeftClick;
            notifyIcon.Dispose();
        }
    }

    private void ReconcileFailedOwnership(IEnumerable<Icon> candidateIcons)
    {
        var assignedIcons = _activeIcons
            .Select(notifyIcon => notifyIcon.Icon)
            .Where(icon => icon is not null)
            .Cast<Icon>()
            .ToArray();
        foreach (var icon in _currentIcons)
        {
            if (!assignedIcons.Any(assigned => ReferenceEquals(assigned, icon)))
                icon.Dispose();
        }
        foreach (var icon in candidateIcons)
        {
            if (!assignedIcons.Any(assigned => ReferenceEquals(assigned, icon)))
                icon.Dispose();
        }

        _currentIcons.Clear();
        _currentIcons.AddRange(assignedIcons);
    }

    private void OnThemeChanged(TrayTheme theme)
    {
        lock (_themeInitializationSync)
        {
            if (_disposed)
                return;
            if (_initializingTheme)
            {
                _pendingTheme = theme;
                return;
            }
        }

        if (_mainWindow.Dispatcher.CheckAccess())
        {
            ApplyTheme(theme);
            return;
        }

        _ = _mainWindow.Dispatcher.InvokeAsync(() => ApplyTheme(theme));
    }

    private void CompleteThemeInitialization()
    {
        while (true)
        {
            TrayTheme? pendingTheme;
            lock (_themeInitializationSync)
            {
                pendingTheme = _pendingTheme;
                _pendingTheme = null;
                if (pendingTheme is null)
                {
                    _initializingTheme = false;
                    return;
                }
            }

            ApplyTheme(pendingTheme.Value);
        }
    }

    private void ApplyTheme(TrayTheme theme)
    {
        if (_disposed || theme == _currentTheme)
            return;

        Reconcile(_currentState!, theme, LayoutMode);
    }

    private void UpdateLayoutMenuChecks()
    {
        _compactLayoutMenuItem.Checked = LayoutMode == TrayLayoutMode.Compact;
        _twoIconsLayoutMenuItem.Checked = LayoutMode == TrayLayoutMode.TwoIcons;
    }

    private void ShowWindowOnLeftClick(object? sender, Forms.MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == Forms.MouseButtons.Left)
            _mainWindow.ShowFromTray();
    }

    private async Task ToggleListenerAsync()
    {
        var result = await _watcherListenerController.ToggleAsync();
        if (_disposed)
            return;

        switch (result)
        {
            case WatcherTaskActionResult.Succeeded:
                break;
            case WatcherTaskActionResult.TaskMissing:
                System.Windows.MessageBox.Show(
                    "啟動監聽工作尚未安裝。完成安裝後即可使用此選項。",
                    "DualSense Battery",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                break;
            default:
                System.Windows.MessageBox.Show(
                    "無法更新 Windows 登入後監聽設定。",
                    "DualSense Battery",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                break;
        }
    }

    private void ApplyListenerState(WatcherListenerState state)
    {
        if (_disposed)
            return;

        _listenerMenuItem.Enabled = state.CanToggle;
        _listenerMenuItem.Checked = state.IsEnabled;
    }

    private static TrayLayoutMode NormalizeLayout(TrayLayoutMode layout) =>
        Enum.IsDefined(layout) ? layout : TrayLayoutMode.Compact;

    private static void ShowAbout() => System.Windows.MessageBox.Show(
        "DualSense Battery Tray\nUSB read-only battery monitor",
        "About",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
}
