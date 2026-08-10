using System.Text.Json;
using System.Text.Json.Nodes;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Settings;

public sealed record AppPreferences(
    WindowCloseBehavior CloseBehavior = WindowCloseBehavior.MinimizeToTray,
    string Theme = "System",
    string? LastUpdateCheckAt = null,
    string? LastKnownLatestVersion = null,
    string? LastKnownReleaseUrl = null)
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
        // Each field is read on its own so one wrong-typed value (the file is shared with
        // the agent and user-editable) cannot silently reset the others to defaults.
        try
        {
            if (File.Exists(configPath)
                && JsonNode.Parse(File.ReadAllText(configPath)) is JsonObject document)
            {
                var loaded = new AppPreferences(
                    CloseBehavior: ReadCloseBehavior(document),
                    Theme: ReadString(document, "theme") ?? "System",
                    LastUpdateCheckAt: ReadString(document, "lastUpdateCheckAt"),
                    LastKnownLatestVersion: ReadString(document, "lastKnownLatestVersion"),
                    LastKnownReleaseUrl: ReadString(document, "lastKnownReleaseUrl"));
                return loaded with { Theme = AppThemePolicy.ToStorageValue(loaded.ThemePreference) };
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new AppPreferences();
    }

    private static string? ReadString(JsonObject document, string key)
        => document[key] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static WindowCloseBehavior ReadCloseBehavior(JsonObject document)
        => document["closeBehavior"] is JsonValue value
            && value.TryGetValue<int>(out var stored)
            && Enum.IsDefined((WindowCloseBehavior)stored)
                ? (WindowCloseBehavior)stored
                : WindowCloseBehavior.MinimizeToTray;

    public void Save(AppPreferences preferences)
    {
        var directory = Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var normalized = preferences with
        {
            Theme = AppThemePolicy.ToStorageValue(preferences.ThemePreference),
        };
        JsonObject document;
        try
        {
            document = File.Exists(configPath)
                ? JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            document = [];
        }

        document["closeBehavior"] = (int)normalized.CloseBehavior;
        document["theme"] = normalized.Theme;
        WriteOrRemove(document, "lastUpdateCheckAt", normalized.LastUpdateCheckAt);
        WriteOrRemove(document, "lastKnownLatestVersion", normalized.LastKnownLatestVersion);
        WriteOrRemove(document, "lastKnownReleaseUrl", normalized.LastKnownReleaseUrl);
        var temporary = configPath + ".tmp";
        File.WriteAllText(temporary, document.ToJsonString(ProtocolJson.Options));
        File.Move(temporary, configPath, overwrite: true);
    }

    private static void WriteOrRemove(JsonObject document, string key, string? value)
    {
        if (value is null)
        {
            document.Remove(key);
        }
        else
        {
            document[key] = value;
        }
    }
}
