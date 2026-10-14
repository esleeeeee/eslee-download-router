using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Startup;

namespace DownloadRouter.Core.Settings;

public sealed record AppPreferences(
    WindowCloseBehavior CloseBehavior = WindowCloseBehavior.MinimizeToTray,
    string Theme = "System",
    string? LastUpdateCheckAt = null,
    string? LastKnownLatestVersion = null,
    string? LastKnownReleaseUrl = null,
    string Language = "System",
    bool NotifyOnMoveCompleted = false)
{
    public const int MaximumFavoriteFolders = 20;

    /// <summary>
    /// Absolute folder paths the user pinned. The store keeps at most
    /// <see cref="MaximumFavoriteFolders"/>, de-duplicated without regard to case.
    /// Record equality compares this list by reference, never by content.
    /// </summary>
    public IReadOnlyList<string> FavoriteFolders { get; init; } = [];

    public AppThemePreference ThemePreference => AppThemePolicy.Parse(Theme);

    public AppLanguage LanguagePreference => AppText.ParseLanguage(Language);
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

/// <summary>
/// Reads and writes the app's <c>config.local.json</c>. The app owns the file; the agent
/// only reads <c>language</c> from it at startup. Writes are serialized across processes
/// with a <c>.lock</c> file and replace the file atomically, and keys this version does not
/// know are kept so an older or newer build never loses another build's settings.
/// </summary>
public sealed class AppPreferencesStore
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(25);

    private readonly string configPath;

    public AppPreferencesStore(string? configPath = null)
    {
        this.configPath = configPath ?? AppInstance.Current.ConfigPath;
    }

    public string ConfigPath => configPath;

    private string LockPath => configPath + ".lock";

    /// <summary>Read-only helper for processes that only need the display language.</summary>
    public static AppLanguage ReadLanguage(string configPath)
        => new AppPreferencesStore(configPath).Load().LanguagePreference;

    public AppPreferences Load()
    {
        try
        {
            return FromDocument(ReadDocument());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppPreferences();
        }
    }

    /// <summary>Replaces the stored preferences under the cross-process lock.</summary>
    public void Save(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _ = Update(_ => preferences);
    }

    /// <summary>
    /// Reads the current file, applies <paramref name="change"/>, and writes the result while
    /// holding the cross-process lock, so concurrent writers never overwrite each other.
    /// Returns the preferences as stored.
    /// </summary>
    public AppPreferences Update(Func<AppPreferences, AppPreferences> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var directory = Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        Directory.CreateDirectory(directory);

        using var fileLock = AcquireLock();
        var document = ReadDocumentForUpdate();
        var current = FromDocument(document);
        var changed = change(current)
            ?? throw new InvalidOperationException("The preferences update returned no value.");
        var normalized = Normalize(changed);
        WriteDocument(document, normalized);
        var temporary = configPath + ".tmp";
        File.WriteAllText(temporary, document.ToJsonString(ProtocolJson.Options));
        MoveWithRetry(temporary, configPath);
        return normalized;
    }

    public static IReadOnlyList<string> NormalizeFavoriteFolders(IEnumerable<string?>? folders)
    {
        var result = new List<string>();
        if (folders is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (result.Count >= AppPreferences.MaximumFavoriteFolders)
            {
                break;
            }

            var candidate = folder?.Trim();
            if (string.IsNullOrEmpty(candidate) || !Path.IsPathFullyQualified(candidate))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(candidate);
                var trimmed = Path.TrimEndingDirectorySeparator(full);
                full = Path.IsPathFullyQualified(trimmed) ? trimmed : full;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (seen.Add(full))
            {
                result.Add(full);
            }
        }

        return result;
    }

    private static AppPreferences Normalize(AppPreferences preferences)
        => preferences with
        {
            Theme = AppThemePolicy.ToStorageValue(preferences.ThemePreference),
            Language = preferences.LanguagePreference.ToString(),
            FavoriteFolders = NormalizeFavoriteFolders(preferences.FavoriteFolders),
        };

    private JsonObject ReadDocument()
    {
        try
        {
            // File.Exists also returns false for inaccessible existing files.
            using var stream = new FileStream(
                configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return JsonNode.Parse(reader.ReadToEnd()) as JsonObject
                ?? throw new JsonException("The settings document must be an object.");
        }
        catch (FileNotFoundException)
        {
            return [];
        }
    }

    private JsonObject ReadDocumentForUpdate()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return ReadDocument();
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(LockRetryDelay);
            }
            catch (JsonException)
            {
                // A failed backup leaves the existing settings untouched.
                var backup = configPath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ",
                    System.Globalization.CultureInfo.InvariantCulture);
                File.Copy(configPath, backup, overwrite: false);
                return [];
            }
        }
    }

    private static AppPreferences FromDocument(JsonObject document)
    {
        var loaded = new AppPreferences(
            CloseBehavior: ReadCloseBehavior(document),
            Theme: ReadString(document, "theme") ?? "System",
            LastUpdateCheckAt: ReadString(document, "lastUpdateCheckAt"),
            LastKnownLatestVersion: ReadString(document, "lastKnownLatestVersion"),
            LastKnownReleaseUrl: ReadString(document, "lastKnownReleaseUrl"),
            Language: ReadString(document, "language") ?? "System",
            NotifyOnMoveCompleted: document["notifyOnMoveCompleted"] is JsonValue notify
                && notify.TryGetValue<bool>(out var enabled)
                && enabled)
        {
            FavoriteFolders = NormalizeFavoriteFolders(ReadStringArray(document, "favoriteFolders")),
        };
        return loaded with
        {
            Theme = AppThemePolicy.ToStorageValue(loaded.ThemePreference),
            Language = loaded.LanguagePreference.ToString(),
        };
    }

    private static void WriteDocument(JsonObject document, AppPreferences preferences)
    {
        document["closeBehavior"] = (int)preferences.CloseBehavior;
        document["theme"] = preferences.Theme;
        WriteOrRemove(document, "lastUpdateCheckAt", preferences.LastUpdateCheckAt);
        WriteOrRemove(document, "lastKnownLatestVersion", preferences.LastKnownLatestVersion);
        WriteOrRemove(document, "lastKnownReleaseUrl", preferences.LastKnownReleaseUrl);
        document["language"] = preferences.Language;
        document["notifyOnMoveCompleted"] = preferences.NotifyOnMoveCompleted;
        var favorites = new JsonArray();
        foreach (var folder in preferences.FavoriteFolders)
        {
            favorites.Add(folder);
        }

        document["favoriteFolders"] = favorites;
    }

    private static string? ReadString(JsonObject document, string key)
        => document[key] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static IEnumerable<string?> ReadStringArray(JsonObject document, string key)
        => document[key] is JsonArray array
            ? array.Select(static item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
            : [];

    private static WindowCloseBehavior ReadCloseBehavior(JsonObject document)
        => document["closeBehavior"] is JsonValue value
            && value.TryGetValue<int>(out var stored)
            && Enum.IsDefined((WindowCloseBehavior)stored)
                ? (WindowCloseBehavior)stored
                : WindowCloseBehavior.MinimizeToTray;

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

    private FileStream AcquireLock()
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (stopwatch.Elapsed < LockTimeout)
            {
                Thread.Sleep(LockRetryDelay);
            }
            catch (UnauthorizedAccessException) when (stopwatch.Elapsed < LockTimeout)
            {
                // A lock file that is being deleted or recreated reports access denied briefly.
                Thread.Sleep(LockRetryDelay);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    AppText.Pick(
                        "다른 창이 설정 파일을 사용하고 있어 설정을 저장하지 못했습니다. 잠시 후 다시 시도하세요.",
                        "Settings could not be saved because another window is using the settings file. Try again in a moment."),
                    exception);
            }
        }
    }

    private static void MoveWithRetry(string source, string destination)
    {
        // A reader without delete sharing (for example an editor) can briefly block the replace.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < 20)
            {
                Thread.Sleep(LockRetryDelay);
            }
        }
    }
}
