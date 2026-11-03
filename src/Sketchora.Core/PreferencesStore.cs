using System.Text.Json;

namespace Sketchora.Core;

public sealed class PreferencesStore(string path)
{
    public AppPreferences Load()
    {
        try
        {
            return (JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path))
                ?? new AppPreferences()).Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences preferences)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences.Normalize(),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
