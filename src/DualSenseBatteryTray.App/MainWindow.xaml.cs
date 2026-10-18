using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using DualSenseBatteryTray.App.Shell;
using DualSenseBatteryTray.App.Tray;
using DualSenseBatteryTray.Core.Battery;
using DualSenseBatteryTray.Core.Devices;

namespace DualSenseBatteryTray.App;

public partial class MainWindow : Window
{
    private readonly System.Windows.Media.ImageSource ApplicationIcon =
        BatteryIconRenderer.RenderApplicationIcon();

    private readonly WindowSmallIconController _smallIcon = new(
        BatteryIconRenderer.RenderWindowSmallIcon);
    private readonly MainWindowViewModel _viewModel;
    private bool _isShuttingDown;

    public MainWindow(ControllerProfile? profile = null)
    {
        _viewModel = new MainWindowViewModel(profile ?? ControllerProfile.DualSenseUsb);
        InitializeComponent();
        DataContext = _viewModel;
        Icon = ApplicationIcon;
        TaskbarItemInfo.Overlay = null;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => _smallIcon.Dispose();
        Closing += OnClosing;
    }

    internal void Update(BatteryState state)
    {
        _viewModel.Update(state);
        TaskbarItemInfo.Overlay = null;
    }

    internal void ShowFromTray()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
    }

    internal void BeginShutdown()
    {
        _isShuttingDown = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs) =>
        _smallIcon.Apply(new WindowInteropHelper(this).Handle);

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_isShuttingDown)
            return;

        eventArgs.Cancel = true;
        Hide();
    }
}

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ControllerProfile _profile;
    private string _percentage = "?";
    private string _chargeState = "Unknown";

    public MainWindowViewModel()
        : this(ControllerProfile.DualSenseUsb)
    {
    }

    public MainWindowViewModel(ControllerProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public string DeviceName => _profile.DisplayName;
    public string ConnectionType => _profile.ConnectionType;

    public string Percentage
    {
        get => _percentage;
        private set => SetField(ref _percentage, value);
    }

    public string ChargeState
    {
        get => _chargeState;
        private set => SetField(ref _chargeState, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(BatteryState state)
    {
        Percentage = state.Percentage is int level
            ? _profile.HasPrecisePercentage ? $"{level}%" : $"≈ {level}%"
            : "?";
        ChargeState = state.State switch
        {
            ConnectionState.Discharging => "Discharging",
            ConnectionState.Charging => "Charging",
            ConnectionState.Full => "Fully charged",
            ConnectionState.Flat => "Flat",
            _ => "Unknown",
        };
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
