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
/// <summary>
/// Narrows a rule to certain file types. Stored as an integer, so append new members only.
/// </summary>
public enum FileTypeFilterMode
{
    AllFiles,
    OnlyListed,
    ExceptListed,
}

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
    DateTimeOffset UpdatedAt,
    FileTypeFilterMode FileTypeMode = FileTypeFilterMode.AllFiles,
    string FileExtensions = "");

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

/// <param name="IsTentative">
/// True when the file name is not known yet and a matching rule filters by file type, so the
/// winner may change once the browser reports the real name.
/// </param>
public sealed record RuleMatchResult(
    DownloadRule Rule,
    string MatchedUrl,
    string SourceField,
    bool IsTentative = false);

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
    SelectionPromptState SelectionPromptState = SelectionPromptState.NeverShown,
    string? SelectedDestinationFolder = null,
    string? SelectedFileName = null,
    DateTimeOffset? BrowserStartedAt = null,
    bool IsRuleTentative = false)
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
    string? ReferrerUrl,
    /// <summary>Browser transfer state at creation. Absent on unsupported extension builds.</summary>
    string? State = null,
    /// <summary>Browser-reported start time. Past values indicate a replayed history item.</summary>
    DateTimeOffset? StartedAt = null,
    /// <summary>Identifies the extension code that sent this request.</summary>
    string? ExtensionBuild = null);

public sealed record DownloadChangedPayload(
    string Browser,
    string DownloadId,
    string State,
    string? FilePath,
    string? Error,
    string? FileName = null,
    bool IsReconciliation = false,
    DateTimeOffset? StartedAt = null,
    Guid? JobId = null);

public sealed record DownloadMetadataChangedPayload(
    string Browser,
    string DownloadId,
    string? FilePath,
    string? FileName,
    DateTimeOffset? StartedAt = null);

public sealed record SelectionCompletedPayload(
    IReadOnlyList<Guid> JobIds,
    string RelativeFolder,
    string? DestinationFolder = null,
    string? FileName = null,
    bool UseForTenMinutes = false);

public sealed record TemporaryFolderCancelPayload(Guid RuleId);

public sealed record ActiveTemporaryFolder(Guid RuleId, string RuleName, string DestinationFolder, DateTimeOffset ExpiresAt);

public sealed record TemporaryFolderChoice(
    string DestinationFolder,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RuleUpdatedAt)
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);

    public bool IsActive(DownloadRule rule, DateTimeOffset now)
        => rule.IsEnabled && rule.StorageMode == StorageMode.SelectSubfolder
            && rule.UpdatedAt == RuleUpdatedAt
            && now >= CreatedAt && now < ExpiresAt
            && ExpiresAt - CreatedAt == Duration;
}

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

/// <summary>
/// Whether new downloads are currently left alone. <paramref name="PausedUntil"/> is null for
/// a pause that lasts until the user resumes routing.
/// </summary>
public sealed record RoutingPauseStatus(bool IsPaused, DateTimeOffset? PausedUntil);

/// <summary>Minutes to pause (1 to 1440), or null to pause until resumed.</summary>
public sealed record RoutingPausePayload(int? Minutes);

/// <summary>Days of finished history to keep. 0 keeps everything.</summary>
public sealed record HistoryRetentionStatus(int Days);

public sealed record HistoryRetentionPayload(int Days);

public sealed record HistoryRetentionResult(int Days, int Deleted);

public sealed record RuleDeletePayload(Guid RuleId);

public sealed record ActiveDownloadsPayload(string Browser);

/// <summary>
/// Sent once when the extension's service worker starts so the agent can warn about a
/// stale build before the user tries to download anything.
/// </summary>
public sealed record ExtensionHelloPayload(string Browser, string? ExtensionBuild);

public sealed record ActiveBrowserDownload(Guid JobId, string DownloadId, DateTimeOffset? StartedAt = null);

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

    /// <summary>
    /// Fixed Chromium extension ID. It is part of the local messaging identity and must
    /// never change; it is declared once here so every surface shows the same value.
    /// </summary>
    public const string ExtensionId = "gilicenlclaemgiijcjjejilikbooggj";

    public const string AgentPipeName = "eslee.download-router.agent.v1";
    public const string AppMutexName = "Local\\eslee.DownloadRouter.App";
    public const string AppActivationEventName = "Local\\eslee.DownloadRouter.App.Activate";
    public const string AppShutdownEventName = "Local\\eslee.DownloadRouter.App.Shutdown";
    public const string AgentShutdownEventName = "Local\\eslee.DownloadRouter.Agent.Shutdown";
    public const string AgentMutexName = "Local\\eslee.DownloadRouter.Agent";

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
        "extension.hello",
        "selection.complete",
        "temporary-folder.list",
        "temporary-folder.cancel",
        "selection.skip",
        "selection.skip-many",
        "selection.prompt-state",
        "route.change",
        "job.retry",
        "diagnostics.status",
        "routing.status",
        "routing.pause",
        "routing.resume",
        "history.retention.get",
        "history.retention.set",
    };

    /// <summary>
    /// Commands only the app may send. The Native Host refuses them, so a browser can never
    /// pause routing or delete history.
    /// </summary>
    public static readonly ISet<string> AppOnlyCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "routing.status",
        "routing.pause",
        "routing.resume",
        "history.retention.get",
        "history.retention.set",
    };

    /// <summary>Commands the Native Host forwards from the browser extension.</summary>
    public static bool IsBrowserForwardable(string? command)
        => command is not null && AllowedCommands.Contains(command) && !AppOnlyCommands.Contains(command);
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
