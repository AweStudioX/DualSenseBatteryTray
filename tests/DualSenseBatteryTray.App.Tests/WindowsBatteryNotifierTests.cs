using DualSenseBatteryTray.App.Notifications;

namespace DualSenseBatteryTray.App.Tests;

public sealed class WindowsBatteryNotifierTests
{
    [Theory]
    [InlineData(20, "20%", "準備")]
    [InlineData(10, "10%", "立即")]
    public void Notify_shows_the_expected_Chinese_low_battery_message(
        int threshold,
        string percentage,
        string urgency)
    {
        var sink = new RecordingBalloonTipSink();
        var notifier = new WindowsBatteryNotifier(sink);

        notifier.Notify(threshold);

        Assert.Contains("電量", sink.Title, StringComparison.Ordinal);
        Assert.Contains(percentage, sink.Message, StringComparison.Ordinal);
        Assert.Contains(urgency, sink.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingBalloonTipSink : IBalloonTipSink
    {
        public string Title { get; private set; } = string.Empty;
        public string Message { get; private set; } = string.Empty;

        public void Show(string title, string message)
        {
            Title = title;
            Message = message;
        }
    }
}
