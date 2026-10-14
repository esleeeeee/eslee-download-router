using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Settings;
using DownloadRouter.Core.Startup;

namespace DownloadRouter.App.Services;

/// <summary>
/// Process-wide services created once in <see cref="App.OnLaunched"/>. Views read them instead of
/// receiving long constructor lists; everything here is touched on the UI thread unless noted.
/// </summary>
internal static class AppServices
{
    public static AppInstance Instance { get; set; } = AppInstance.Current;

    public static AgentClient Agent { get; } = new();

    public static AppPreferencesStore PreferencesStore { get; set; } = null!;

    /// <summary>Last saved preferences. Replace only through <see cref="UpdatePreferencesAsync"/>.</summary>
    public static AppPreferences Preferences { get; private set; } = new();

    public static ThemeService Theme { get; set; } = null!;

    public static LiveMonitor Live { get; set; } = null!;

    public static NotificationService Notifications { get; set; } = null!;

    public static RootHealthMonitor RootHealth { get; } = new();

    public static MainWindow? Main { get; set; }

    public static PathTokenResolver PathResolver { get; } = new(new WindowsKnownPathProvider());

    public static void InitializePreferences(AppPreferencesStore store, AppPreferences loaded)
    {
        PreferencesStore = store;
        Preferences = loaded;
    }

    /// <summary>
    /// Read-modify-write of config.local.json under the store's cross-process lock, off the UI
    /// thread. The cached copy is refreshed from what was actually written.
    /// </summary>
    public static async Task<AppPreferences> UpdatePreferencesAsync(Func<AppPreferences, AppPreferences> change)
    {
        var store = PreferencesStore;
        var updated = await Task.Run(() => store.Update(change)).ConfigureAwait(true);
        Preferences = updated;
        return updated;
    }

    public static string ResolveForDisplay(string configuredPath)
    {
        try
        {
            return PathResolver.Resolve(configuredPath.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return configuredPath.Trim();
        }
    }

    public static void WriteDiagnostic(string fileName, string message)
    {
        try
        {
            Directory.CreateDirectory(Instance.LogsDirectory);
            File.AppendAllText(
                Path.Combine(Instance.LogsDirectory, fileName),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
