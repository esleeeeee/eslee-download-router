using DownloadRouter.App.Services;

namespace DownloadRouter.App.Views;

/// <summary>A page hosted in the main window's navigation frame.</summary>
internal interface IAppView
{
    /// <summary>Called each time the page becomes visible. <paramref name="parameter"/> carries a deep link (filter, job).</summary>
    void OnShown(object? parameter);

    /// <summary>Called on the UI thread after each agent poll while the page is visible.</summary>
    void OnLiveUpdate(LiveSnapshot snapshot, bool changed);
}

/// <summary>Deep link into the downloads page.</summary>
internal sealed record DownloadsNavigation(DownloadFilter? Filter = null, Guid? JobId = null, string? Search = null);

internal enum DownloadFilter
{
    All,
    Pending,
    InProgress,
    Completed,
    Skipped,
    Failed,
    CancelledOrInterrupted,
}

/// <summary>Deep link into the rules page.</summary>
internal sealed record RulesNavigation(bool AttentionOnly = false, Guid? RuleId = null, string? NewRuleStorageRoot = null);
