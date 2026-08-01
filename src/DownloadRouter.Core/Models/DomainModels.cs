using System.Text.Json;

namespace DownloadRouter.Core.Models;

public enum BrowserKind
{
    Unknown,
    Whale,
    Edge,
    Chrome,
    Brave,
    Vivaldi,
    Opera,
}

public enum RuleMatchType
{
    DomainAndSubdomains,
    ExactHost,
    UrlContains,
}

public enum RuleMatchTarget
{
    InitiatingPage,
    FileUrl,
    Either,
}

public enum StorageMode
{
    Automatic,
    SelectSubfolder,
}

public enum WindowCloseBehavior
{
    MinimizeToTray,
    ExitApplication,
}

public enum AppThemePreference
{
    System,
    Light,
    Dark,
}

public enum DownloadJobStatus
{
    Detected,
    WaitingForDownload,
    WaitingForSelection,
    ReadyToMove,
    Moving,
    RetryPending,
    Completed,
    Failed,
    Cancelled,
    Interrupted,
    Skipped,
}

public enum BrowserTransferState
{
    InProgress,
    Complete,
    Cancelled,
    Interrupted,
}

public enum RoutingState
{
    WaitingForSelection,
    SelectionReady,
    Moving,
    RetryPending,
    Completed,
    Skipped,
    Failed,
    NotRequired,
}

/// <summary>
/// Tracks whether the automatic folder selection window was already offered for a job.
/// This is persisted so a restart of the app, the browser, or Windows cannot replay a
/// prompt the user already answered or postponed.
/// </summary>
public enum SelectionPromptState
{
    NeverShown,
    Shown,
    Deferred,
    Resolved,
}

public sealed record DownloadRule(
    Guid Id,
    string Name,
    bool IsEnabled,
    RuleMatchType MatchType,
    string MatchValue,
    RuleMatchTarget MatchTarget,
    string StorageRoot,
    StorageMode StorageMode,
    int Priority,
    int ListOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DownloadMetadata(
    BrowserKind Browser,
    string BrowserDownloadId,
    string OriginalFileName,
    string? CurrentFilePath,
    string? InitiatingPageUrl,
    string? InitialUrl,
    string? FinalUrl,
    string? ReferrerUrl,
    DateTimeOffset StartedAt);

public sealed record RuleMatchResult(
    DownloadRule Rule,
    string MatchedUrl,
    string SourceField);

public sealed record DownloadJob(
    Guid Id,
    BrowserKind Browser,
    string BrowserDownloadId,
    string OriginalFileName,
    string CurrentFileName,
    string? InitiatingPageUrl,
    string? InitialUrl,
    string? FinalUrl,
    string? ReferrerUrl,
    string? SanitizedSource,
    Guid RuleId,
    string? OriginalPath,
    string? FinalPath,
    string? SelectedRelativeFolder,
    BrowserTransferState BrowserState,
    RoutingState RoutingState,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastBrowserEventAt = null,
    bool IsBrowserRecordStale = false,
    SelectionPromptState SelectionPromptState = SelectionPromptState.NeverShown)
{
    public DownloadJobStatus Status
        => BrowserState switch
        {
            BrowserTransferState.Cancelled => DownloadJobStatus.Cancelled,
            BrowserTransferState.Interrupted => DownloadJobStatus.Interrupted,
            _ => RoutingState switch
            {
                RoutingState.WaitingForSelection => DownloadJobStatus.WaitingForSelection,
                RoutingState.SelectionReady when BrowserState == BrowserTransferState.InProgress => DownloadJobStatus.WaitingForDownload,
                RoutingState.SelectionReady => DownloadJobStatus.ReadyToMove,
                RoutingState.Moving => DownloadJobStatus.Moving,
                RoutingState.RetryPending => DownloadJobStatus.RetryPending,
                RoutingState.Completed => DownloadJobStatus.Completed,
                RoutingState.Skipped => DownloadJobStatus.Skipped,
                RoutingState.Failed => DownloadJobStatus.Failed,
                RoutingState.NotRequired when BrowserState == BrowserTransferState.InProgress => DownloadJobStatus.WaitingForDownload,
                RoutingState.NotRequired => DownloadJobStatus.ReadyToMove,
                _ => DownloadJobStatus.Detected,
            },
        };

    public bool IsSelectionPending
        => BrowserState is BrowserTransferState.InProgress or BrowserTransferState.Complete
            && RoutingState == RoutingState.WaitingForSelection;

    public bool IsTerminal
        => BrowserState is BrowserTransferState.Cancelled or BrowserTransferState.Interrupted
            || RoutingState is RoutingState.Completed or RoutingState.Skipped or RoutingState.Failed;
}

public sealed record AgentCommand(
    int Version,
    Guid RequestId,
    string Command,
    JsonElement Payload);

public sealed record AgentResponse(
    int Version,
    Guid RequestId,
    bool Success,
    string? ErrorCode,
    string? Message,
    JsonElement? Data)
{
    public static AgentResponse Ok(Guid requestId, object? data = null, string? message = null)
        => new(ProtocolConstants.CurrentVersion, requestId, true, null, message, ProtocolJson.ToElement(data));

    public static AgentResponse Error(Guid requestId, string code, string message)
        => new(ProtocolConstants.CurrentVersion, requestId, false, code, message, null);
}

public sealed record DownloadStartedPayload(
    string Browser,
    string DownloadId,
    string FileName,
    string? FilePath,
    string? InitiatingPageUrl,
    string? InitialUrl,
    string? FinalUrl,
    string? ReferrerUrl);

public sealed record DownloadChangedPayload(
    string Browser,
    string DownloadId,
    string State,
    string? FilePath,
    string? Error,
    string? FileName = null,
    bool IsReconciliation = false);

public sealed record DownloadMetadataChangedPayload(
    string Browser,
    string DownloadId,
    string? FilePath,
    string? FileName);

public sealed record SelectionCompletedPayload(
    IReadOnlyList<Guid> JobIds,
    string RelativeFolder);

public sealed record SelectionSkippedPayload(Guid JobId);

public sealed record SelectionsSkippedPayload(IReadOnlyList<Guid> JobIds);

public sealed record SelectionPromptStatePayload(
    IReadOnlyList<Guid> JobIds,
    SelectionPromptState State);

public sealed record JobRouteChangePayload(
    Guid JobId,
    string RelativeFolder,
    bool ConfirmCompletedMove = false);

public sealed record JobsDeletePayload(IReadOnlyList<Guid> JobIds);

public sealed record RuleDeletePayload(Guid RuleId);

public sealed record ActiveDownloadsPayload(string Browser);

public sealed record ActiveBrowserDownload(Guid JobId, string DownloadId);

public sealed record DashboardCounts(
    int DownloadsInProgress,
    int WaitingForSelection,
    int RecentlyCompleted,
    int CancelledOrInterrupted,
    int RetryOrFailed);

public static class ProtocolConstants
{
    public const int CurrentVersion = 1;
    public const int MaximumMessageBytes = 1024 * 1024;
    public const string AgentPipeName = "eslee.download-router.agent.v1";
    public const string AppMutexName = "Local\\eslee.DownloadRouter.App";
    public const string AppActivationEventName = "Local\\eslee.DownloadRouter.App.Activate";
    public const string AppShutdownEventName = "Local\\eslee.DownloadRouter.App.Shutdown";
    public const string AgentShutdownEventName = "Local\\eslee.DownloadRouter.Agent.Shutdown";

    public static readonly ISet<string> AllowedCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "ping",
        "download.started",
        "download.metadata",
        "download.changed",
        "download.cancelled",
        "download.interrupted",
        "rules.list",
        "rules.upsert",
        "rules.delete",
        "jobs.list",
        "jobs.delete",
        "downloads.active",
        "selection.complete",
        "selection.skip",
        "selection.skip-many",
        "selection.prompt-state",
        "route.change",
        "job.retry",
        "diagnostics.status",
    };
}

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public static JsonElement? ToElement(object? value)
        => value is null ? null : JsonSerializer.SerializeToElement(value, Options);
}
