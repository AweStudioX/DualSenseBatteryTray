using Microsoft.Win32;

namespace DualSenseBatteryTray.App.Tray;

internal interface ITrayThemeProvider : IDisposable
{
    TrayTheme Current { get; }

    event Action<TrayTheme>? ThemeChanged;

    void Refresh();
}

internal interface IUserPreferenceChangeSource : IDisposable
{
    event Action? Changed;
}

internal sealed class WindowsTrayThemeProvider : ITrayThemeProvider
{
    private const string PersonalizeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightThemeValueName = "SystemUsesLightTheme";

    private readonly Func<object?> _readSystemUsesLightTheme;
    private readonly IUserPreferenceChangeSource _userPreferenceChangeSource;
    private readonly object _sync = new();
    private bool _disposed;

    public WindowsTrayThemeProvider()
        : this(ReadSystemUsesLightTheme, new SystemEventsUserPreferenceChangeSource())
    {
    }

    internal WindowsTrayThemeProvider(
        Func<object?> readSystemUsesLightTheme,
        IUserPreferenceChangeSource userPreferenceChangeSource)
    {
        _readSystemUsesLightTheme = readSystemUsesLightTheme ??
            throw new ArgumentNullException(nameof(readSystemUsesLightTheme));
        _userPreferenceChangeSource = userPreferenceChangeSource ??
            throw new ArgumentNullException(nameof(userPreferenceChangeSource));

        Current = ReadThemeOrKeep(TrayTheme.DarkTaskbar);
        _userPreferenceChangeSource.Changed += OnUserPreferenceChanged;
    }

    public TrayTheme Current { get; private set; }

    public event Action<TrayTheme>? ThemeChanged;

    public void Refresh()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            var next = ReadThemeOrKeep(Current);
            if (_disposed || next == Current)
                return;

            Current = next;
            var themeChanged = ThemeChanged;
            themeChanged?.Invoke(next);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _userPreferenceChangeSource.Changed -= OnUserPreferenceChanged;
            _userPreferenceChangeSource.Dispose();
        }
    }

    internal static TrayTheme ParseSystemUsesLightTheme(object? value) =>
        value is int integerValue && integerValue == 1
            ? TrayTheme.LightTaskbar
            : TrayTheme.DarkTaskbar;

    private static object? ReadSystemUsesLightTheme()
    {
        using var personalizeKey = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
        return personalizeKey?.GetValue(SystemUsesLightThemeValueName);
    }

    private TrayTheme ReadThemeOrKeep(TrayTheme fallback)
    {
        try
        {
            return ParseSystemUsesLightTheme(_readSystemUsesLightTheme());
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private void OnUserPreferenceChanged() => Refresh();
}

internal sealed class SystemEventsUserPreferenceChangeSource : IUserPreferenceChangeSource
{
    private bool _disposed;

    public SystemEventsUserPreferenceChangeSource() =>
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    public event Action? Changed;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs) =>
        Changed?.Invoke();
}
