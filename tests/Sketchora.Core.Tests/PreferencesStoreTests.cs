using Sketchora.Core;
using Xunit;

namespace Sketchora.Core.Tests;

public sealed class PreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Sketchora.Tests", Guid.NewGuid().ToString("N"));
    private string PathToPreferences => Path.Combine(_directory, "preferences.json");

    [Fact]
    public void FirstLaunchUsesDefaults()
    {
        Assert.Equal(new AppPreferences(), new PreferencesStore(PathToPreferences).Load());
    }

    [Fact]
    public void MalformedOrNullSettingsDoNotPreventLaunch()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathToPreferences, "{ broken");
        Assert.Equal(new AppPreferences(), new PreferencesStore(PathToPreferences).Load());
        File.WriteAllText(PathToPreferences, "null");
        Assert.Equal(new AppPreferences(), new PreferencesStore(PathToPreferences).Load());
    }

    [Fact]
    public void InvalidColorAndWidthAreNormalized()
    {
        var preferences = new AppPreferences { Color = "bad", Thickness = double.PositiveInfinity };
        Assert.Equal("#6366F1", preferences.Normalize().Color);
        Assert.Equal(4, preferences.Normalize().Thickness);
        Assert.Equal(20, (preferences with { Thickness = 400 }).Normalize().Thickness);
    }

    [Fact]
    public void SaveRoundTripsAndCanReplaceExistingPreferences()
    {
        var store = new PreferencesStore(PathToPreferences);
        store.Save(new AppPreferences());
        var expected = new AppPreferences { Color = "#ef4444", Thickness = 9, PointerHalo = false, ToolbarLeft = 120 };
        store.Save(expected);
        Assert.Equal(expected.Normalize(), store.Load());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
