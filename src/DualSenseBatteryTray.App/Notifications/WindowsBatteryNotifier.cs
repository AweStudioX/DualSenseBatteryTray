using System.Windows.Forms;

namespace DualSenseBatteryTray.App.Notifications;

internal interface IBalloonTipSink
{
    void Show(string title, string message);
}

internal sealed class WindowsBatteryNotifier
{
    private readonly IBalloonTipSink _sink;

    public WindowsBatteryNotifier(NotifyIcon notifyIcon)
        : this(new NotifyIconBalloonTipSink(notifyIcon))
    {
    }

    internal WindowsBatteryNotifier(IBalloonTipSink sink) => _sink = sink;

    public void Notify(int threshold)
    {
        var message = threshold switch
        {
            20 => "DualSense 電量剩餘 20%，請準備連接 USB 充電。",
            10 => "DualSense 電量只剩 10%，請立即連接 USB 充電。",
            _ => throw new ArgumentOutOfRangeException(nameof(threshold)),
        };

        _sink.Show("DualSense 電量提醒", message);
    }

    private sealed class NotifyIconBalloonTipSink(NotifyIcon notifyIcon) : IBalloonTipSink
    {
        public void Show(string title, string message) =>
            notifyIcon.ShowBalloonTip(5_000, title, message, ToolTipIcon.Warning);
    }
}
