using DualSenseBatteryTray.App.Tray;

namespace DualSenseBatteryTray.App.Tests;

public sealed class WindowsTrayThemeProviderTests
{
    [Theory]
    [InlineData(1, (int)TrayTheme.LightTaskbar)]
    [InlineData(0, (int)TrayTheme.DarkTaskbar)]
    [InlineData(null, (int)TrayTheme.DarkTaskbar)]
    public void ParseSystemUsesLightTheme_maps_registry_value(object? value, int expected) =>
        Assert.Equal((TrayTheme)expected, WindowsTrayThemeProvider.ParseSystemUsesLightTheme(value));

    [Fact]
    public void Refresh_notifies_once_when_the_taskbar_theme_changes()
    {
        object? value = 1;
        using var source = new FakeUserPreferenceChangeSource();
        using var provider = new WindowsTrayThemeProvider(() => value, source);
        var changes = new List<TrayTheme>();
        provider.ThemeChanged += changes.Add;

        value = 0;
        source.RaiseChanged();

        Assert.Equal(TrayTheme.DarkTaskbar, provider.Current);
        Assert.Equal([TrayTheme.DarkTaskbar], changes);
    }

    [Fact]
    public void Refresh_does_not_notify_when_the_taskbar_theme_is_unchanged()
    {
        object? value = 1;
        using var source = new FakeUserPreferenceChangeSource();
        using var provider = new WindowsTrayThemeProvider(() => value, source);
        var changes = new List<TrayTheme>();
        provider.ThemeChanged += changes.Add;

        source.RaiseChanged();

        Assert.Equal(TrayTheme.LightTaskbar, provider.Current);
        Assert.Empty(changes);
    }

    [Fact]
    public void Dispose_stops_responding_to_preference_changes()
    {
        object? value = 1;
        var source = new FakeUserPreferenceChangeSource();
        var provider = new WindowsTrayThemeProvider(() => value, source);
        var changes = new List<TrayTheme>();
        provider.ThemeChanged += changes.Add;

        provider.Dispose();
        value = 0;
        source.RaiseChanged();

        Assert.Equal(TrayTheme.LightTaskbar, provider.Current);
        Assert.Empty(changes);
        Assert.True(source.IsDisposed);
    }

    [Fact]
    public void Refresh_keeps_the_last_valid_theme_when_registry_read_fails()
    {
        object? value = 1;
        var throws = false;
        using var source = new FakeUserPreferenceChangeSource();
        using var provider = new WindowsTrayThemeProvider(() =>
        {
            if (throws)
                throw new InvalidOperationException("Registry unavailable");
            return value;
        }, source);
        var changes = new List<TrayTheme>();
        provider.ThemeChanged += changes.Add;

        throws = true;
        source.RaiseChanged();

        Assert.Equal(TrayTheme.LightTaskbar, provider.Current);
        Assert.Empty(changes);
    }

    [Fact]
    public void Refresh_does_not_notify_when_disposed_during_an_in_flight_theme_read()
    {
        object? value = 1;
        var disposeDuringRead = false;
        using var source = new FakeUserPreferenceChangeSource();
        WindowsTrayThemeProvider? provider = null;
        provider = new WindowsTrayThemeProvider(() =>
        {
            if (disposeDuringRead)
                provider!.Dispose();
            return value;
        }, source);
        var changes = new List<TrayTheme>();
        provider.ThemeChanged += changes.Add;

        value = 0;
        disposeDuringRead = true;
        source.RaiseChanged();

        Assert.Equal(TrayTheme.LightTaskbar, provider.Current);
        Assert.Empty(changes);
        Assert.True(source.IsDisposed);
    }

    private sealed class FakeUserPreferenceChangeSource : IUserPreferenceChangeSource
    {
        public event Action? Changed;

        public bool IsDisposed { get; private set; }

        public void RaiseChanged() => Changed?.Invoke();

        public void Dispose() => IsDisposed = true;
    }
}
