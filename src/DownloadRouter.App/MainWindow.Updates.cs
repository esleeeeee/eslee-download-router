using DownloadRouter.App.Services;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Updates;
using Microsoft.UI.Xaml;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Re-evaluates due-ness every few hours while the tray app runs; the actual network
    /// request still happens at most once per <see cref="UpdateCheckPolicy.AutomaticCheckInterval"/>.
    /// </summary>
    private readonly DispatcherTimer updateCheckTimer = new() { Interval = TimeSpan.FromHours(6) };

    /// <summary>
    /// The in-flight check, shared so a manual click during the automatic startup check
    /// awaits the same result instead of reporting stale state. Touched only on the UI thread.
    /// </summary>
    private Task<UpdateCheckOutcome>? updateCheckTask;

    private void StartUpdateChecks()
    {
        updateCheckTimer.Tick += (_, _) => _ = RunAutomaticUpdateCheckAsync();
        updateCheckTimer.Start();
        Closed += (_, _) => updateCheckTimer.Stop();
        _ = RunAutomaticUpdateCheckAsync();
    }

    private async Task RunAutomaticUpdateCheckAsync()
    {
        if (!UpdateCheckPolicy.IsAutomaticCheckDue(AppServices.Preferences.LastUpdateCheckAt, DateTimeOffset.Now))
        {
            return;
        }

        await RunUpdateCheckAsync();
    }

    /// <summary>
    /// Runs one check and persists the outcome, or joins the check already in flight.
    /// Failures only mark the attempt time so an offline start does not retry on every
    /// launch; the known latest version is kept.
    /// </summary>
    internal Task<UpdateCheckOutcome> RunUpdateCheckAsync()
    {
        if (updateCheckTask is { IsCompleted: false } running)
        {
            return running;
        }

        var task = RunUpdateCheckCoreAsync();
        updateCheckTask = task;
        return task;
    }

    private static async Task<UpdateCheckOutcome> RunUpdateCheckCoreAsync()
    {
        var outcome = await UpdateCheckService.CheckAsync();
        var checkedAt = DateTimeOffset.Now.ToString("O");
        try
        {
            await AppServices.UpdatePreferencesAsync(preferences => outcome.Succeeded && outcome.Latest is not null
                ? preferences with
                {
                    LastUpdateCheckAt = checkedAt,
                    LastKnownLatestVersion = UpdateCheckPolicy.ToDisplayVersion(outcome.Latest.Version),
                    LastKnownReleaseUrl = outcome.Latest.ReleaseUrl,
                }
                : preferences with { LastUpdateCheckAt = checkedAt });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
        }

        if (!outcome.Succeeded)
        {
            WriteAppDiagnostic("update-check failed; downloads are unaffected");
        }

        return outcome;
    }

    /// <summary>Cached state from the last completed check, shown without a new request.</summary>
    internal static (UpdateStatus Status, LatestReleaseInfo? Latest) ReadCachedUpdateState(string currentVersion)
    {
        var preferences = AppServices.Preferences;
        if (UpdateCheckPolicy.TryParseVersion(preferences.LastKnownLatestVersion) is not Version known)
        {
            return (UpdateStatus.Unknown, null);
        }

        var latest = new LatestReleaseInfo(
            known,
            preferences.LastKnownLatestVersion!,
            UpdateCheckPolicy.NormalizeReleaseUrl(preferences.LastKnownReleaseUrl));
        return (UpdateCheckPolicy.Evaluate(currentVersion, latest), latest);
    }

    /// <summary>The release page to open; the persisted value always goes through the normaliser.</summary>
    internal static string ReleasePageUrl()
        => UpdateCheckPolicy.NormalizeReleaseUrl(AppServices.Preferences.LastKnownReleaseUrl);

    internal static ProductVersionInfo CurrentVersion()
        => ProductVersionInfo.Read(typeof(App).Assembly, AppContext.BaseDirectory);
}
