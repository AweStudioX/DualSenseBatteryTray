using DualSenseBatteryTray.App.Tray;

namespace DualSenseBatteryTray.App.Tests;

public sealed class TrayLayoutPreferenceStoreTests
{
    [Fact]
    public void Missing_or_invalid_preference_defaults_to_two_icons()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var store = new TrayLayoutPreferenceStore(directory);

            Assert.Equal(TrayLayoutMode.TwoIcons, store.Load());

            File.WriteAllText(store.PreferencePath, "broken");

            Assert.Equal(TrayLayoutMode.TwoIcons, store.Load());

            File.WriteAllText(store.PreferencePath, "999");

            Assert.Equal(TrayLayoutMode.TwoIcons, store.Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Inaccessible_preference_defaults_to_two_icons()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var store = new TrayLayoutPreferenceStore(directory);
            Directory.CreateDirectory(store.PreferencePath);

            Assert.Equal(TrayLayoutMode.TwoIcons, store.Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData((int)TrayLayoutMode.Compact)]
    [InlineData((int)TrayLayoutMode.TwoIcons)]
    public void Valid_preference_round_trips(int modeValue)
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var store = new TrayLayoutPreferenceStore(directory);

            var mode = (TrayLayoutMode)modeValue;

            store.Save(mode);

            var reloadedStore = new TrayLayoutPreferenceStore(directory);
            Assert.Equal(mode, reloadedStore.Load());
            Assert.False(File.Exists($"{store.PreferencePath}.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"DualSenseBatteryTray.App.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
