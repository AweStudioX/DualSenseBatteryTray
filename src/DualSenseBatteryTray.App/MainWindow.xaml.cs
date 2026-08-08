using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using DualSenseBatteryTray.App.Tray;
using DualSenseBatteryTray.Core.Battery;

namespace DualSenseBatteryTray.App;

public partial class MainWindow : Window
{
    private static readonly System.Windows.Media.ImageSource ApplicationIcon =
        BatteryIconRenderer.RenderApplicationIcon();

    private readonly MainWindowViewModel _viewModel = new();
    private bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Icon = ApplicationIcon;
        TaskbarItemInfo.Overlay = null;
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
    private string _percentage = "?";
    private string _chargeState = "Unknown";

    public string DeviceName => "DualSense Wireless Controller";
    public string ConnectionType => "USB";

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
        Percentage = state.Percentage is int level ? $"{level}%" : "?";
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
