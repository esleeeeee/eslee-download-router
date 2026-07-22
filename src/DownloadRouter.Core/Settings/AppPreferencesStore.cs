using System.Text.Json;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Settings;

public sealed record AppPreferences(
    WindowCloseBehavior CloseBehavior = WindowCloseBehavior.MinimizeToTray,
    string Theme = "System")
{
    public AppThemePreference ThemePreference => AppThemePolicy.Parse(Theme);
}

public static class AppThemePolicy
{
    public static AppThemePreference Parse(string? value)
        => Enum.TryParse<AppThemePreference>(value, ignoreCase: true, out var result)
            && Enum.IsDefined(result)
                ? result
                : AppThemePreference.System;

    public static string ToStorageValue(AppThemePreference preference)
        => Enum.IsDefined(preference) ? preference.ToString() : AppThemePreference.System.ToString();

    public static string ToElementThemeName(AppThemePreference preference)
        => preference switch
        {
            AppThemePreference.Light => "Light",
            AppThemePreference.Dark => "Dark",
            _ => "Default",
        };
}

public sealed class AppPreferencesStore
{
    private readonly string configPath;

    public AppPreferencesStore(string? configPath = null)
    {
        this.configPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "eslee",
            "DownloadRouter",
            "config.local.json");
    }

    public string ConfigPath => configPath;

    public AppPreferences Load()
    {
        try
        {
            var loaded = File.Exists(configPath)
                ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(configPath), ProtocolJson.Options)
                    ?? new AppPreferences()
                : new AppPreferences();
            return loaded with { Theme = AppThemePolicy.ToStorageValue(loaded.ThemePreference) };
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
        var normalized = preferences with
        {
            Theme = AppThemePolicy.ToStorageValue(preferences.ThemePreference),
        };
        var temporary = configPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(normalized, ProtocolJson.Options));
        File.Move(temporary, configPath, overwrite: true);
    }
}
