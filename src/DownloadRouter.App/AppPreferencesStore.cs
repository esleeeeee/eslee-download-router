using System.Text.Json;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App;

public sealed record AppPreferences(WindowCloseBehavior CloseBehavior = WindowCloseBehavior.MinimizeToTray);

public sealed class AppPreferencesStore
{
    private readonly string configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "eslee",
        "DownloadRouter",
        "config.local.json");

    public AppPreferences Load()
    {
        try
        {
            return File.Exists(configPath)
                ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(configPath), ProtocolJson.Options)
                    ?? new AppPreferences()
                : new AppPreferences();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences preferences)
    {
        var directory = Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporary = configPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, ProtocolJson.Options));
        File.Move(temporary, configPath, overwrite: true);
    }
}
