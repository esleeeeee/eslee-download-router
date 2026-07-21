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
    DownloadJobStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

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
    string? Error);

public sealed record SelectionCompletedPayload(
    IReadOnlyList<Guid> JobIds,
    string RelativeFolder);

public static class ProtocolConstants
{
    public const int CurrentVersion = 1;
    public const int MaximumMessageBytes = 1024 * 1024;
    public const string AgentPipeName = "eslee.download-router.agent.v1";

    public static readonly ISet<string> AllowedCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "ping",
        "download.started",
        "download.changed",
        "rules.list",
        "rules.upsert",
        "jobs.list",
        "selection.complete",
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
