using System.IO;
using System.Security;
using System.Text;

namespace DualSenseBatteryTray.App.Tray;

internal enum TrayLayoutMode
{
    Compact,
    TwoIcons
}

internal interface ITrayLayoutPreferenceStore
{
    TrayLayoutMode Load();

    void Save(TrayLayoutMode mode);
}

internal sealed class TrayLayoutPreferenceStore : ITrayLayoutPreferenceStore
{
    private const string PreferenceFileName = "tray-layout.txt";

    public TrayLayoutPreferenceStore(string? directory = null)
    {
        var preferenceDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DualSenseBatteryTray");
        ArgumentException.ThrowIfNullOrWhiteSpace(preferenceDirectory);

        PreferencePath = Path.Combine(preferenceDirectory, PreferenceFileName);
    }

    public string PreferencePath { get; }

    public TrayLayoutMode Load()
    {
        try
        {
            var value = File.ReadAllText(PreferencePath);
            return Enum.TryParse<TrayLayoutMode>(value, ignoreCase: false, out var mode)
                && Enum.IsDefined(mode)
                ? mode
                : TrayLayoutMode.TwoIcons;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return TrayLayoutMode.TwoIcons;
        }
    }

    public void Save(TrayLayoutMode mode)
    {
        var directory = Path.GetDirectoryName(PreferencePath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{PreferencePath}.tmp";
        using (var stream = new FileStream(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None))
        {
            var content = Encoding.UTF8.GetBytes(mode.ToString());
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, PreferencePath, overwrite: true);
    }
}
