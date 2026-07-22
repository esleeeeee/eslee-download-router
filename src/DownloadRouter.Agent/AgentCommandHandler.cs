using System.Text.Json;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Privacy;
using DownloadRouter.Core.Rules;
using DownloadRouter.Infrastructure.Files;
using DownloadRouter.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DownloadRouter.Agent;

public sealed class AgentCommandHandler(
    DownloadRouterRepository repository,
    RuleMatcher matcher,
    UrlSanitizer sanitizer,
    PathTokenResolver pathTokenResolver,
    PathBoundaryValidator boundaryValidator,
    DownloadJobStateMachine stateMachine,
    FileMoveService fileMoveService,
    ISelectionUiLauncher selectionUiLauncher,
    AppPaths paths,
    ILogger<AgentCommandHandler> logger)
{
    public async Task<AgentResponse> HandleAsync(AgentCommand request, CancellationToken cancellationToken)
    {
        if (request.Version != ProtocolConstants.CurrentVersion)
        {
            return AgentResponse.Error(request.RequestId, "protocol.unsupported-version", "Unsupported protocol version.");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AgentResponse.Error(Guid.Empty, "protocol.invalid-request-id", "A non-empty request ID is required.");
        }

        if (!ProtocolConstants.AllowedCommands.Contains(request.Command))
        {
            return AgentResponse.Error(request.RequestId, "protocol.command-not-allowed", "The requested command is not allowed.");
        }

        try
        {
            return request.Command switch
            {
                "ping" => AgentResponse.Ok(request.RequestId, new
                {
                    agent = "DownloadRouter.Agent",
                    protocolVersion = ProtocolConstants.CurrentVersion,
                    timestamp = DateTimeOffset.UtcNow,
                }),
                "download.started" => await HandleDownloadStartedAsync(request, cancellationToken).ConfigureAwait(false),
                "download.changed" => await HandleDownloadChangedAsync(request, cancellationToken).ConfigureAwait(false),
                "rules.list" => AgentResponse.Ok(request.RequestId, await repository.GetRulesAsync(cancellationToken).ConfigureAwait(false)),
                "rules.upsert" => await HandleRuleUpsertAsync(request, cancellationToken).ConfigureAwait(false),
                "jobs.list" => AgentResponse.Ok(request.RequestId, await repository.GetRecentJobsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)),
                "jobs.delete" => await HandleJobsDeleteAsync(request, cancellationToken).ConfigureAwait(false),
                "downloads.active" => await HandleActiveDownloadsAsync(request, cancellationToken).ConfigureAwait(false),
                "selection.complete" => await HandleSelectionCompletedAsync(request, cancellationToken).ConfigureAwait(false),
                "selection.skip" => await HandleSelectionSkippedAsync(request, cancellationToken).ConfigureAwait(false),
                "job.retry" => await HandleRetryAsync(request, cancellationToken).ConfigureAwait(false),
                "diagnostics.status" => AgentResponse.Ok(request.RequestId, new
                {
                    dataDirectory = paths.RootDirectory,
                    databaseExists = File.Exists(paths.DatabasePath),
                    protocolVersion = ProtocolConstants.CurrentVersion,
                    processId = Environment.ProcessId,
                }),
                _ => AgentResponse.Error(request.RequestId, "protocol.command-not-allowed", "The requested command is not allowed."),
            };
        }
        catch (JsonException exception)
        {
            return AgentResponse.Error(request.RequestId, "protocol.invalid-json", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return AgentResponse.Error(request.RequestId, "validation.invalid-argument", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Agent rejected a request at a security boundary");
            return AgentResponse.Error(request.RequestId, "security.path-boundary", exception.Message);
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Agent database operation failed");
            return AgentResponse.Error(request.RequestId, "database.unavailable", "The local rule database is temporarily unavailable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Agent command {Command} failed", request.Command);
            return AgentResponse.Error(request.RequestId, "agent.unexpected", "The agent could not process this request.");
        }
    }

    private async Task<AgentResponse> HandleDownloadStartedAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<DownloadStartedPayload>(request.Payload);
        if (!TryParseBrowser(payload.Browser, out var browser)
            || string.IsNullOrWhiteSpace(payload.DownloadId)
            || string.IsNullOrWhiteSpace(payload.FileName))
        {
            return AgentResponse.Error(request.RequestId, "download.invalid-metadata", "Browser, download ID, and file name are required.");
        }

        var existing = await repository.GetJobAsync(browser, payload.DownloadId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return AgentResponse.Ok(request.RequestId, new { tracked = true, jobId = existing.Id, existing = true });
        }

        var metadata = new DownloadMetadata(
            browser,
            payload.DownloadId,
            Path.GetFileName(payload.FileName),
            payload.FilePath,
            payload.InitiatingPageUrl,
            payload.InitialUrl,
            payload.FinalUrl,
            payload.ReferrerUrl,
            DateTimeOffset.UtcNow);
        var rules = await repository.GetRulesAsync(cancellationToken).ConfigureAwait(false);
        var matched = matcher.Match(rules, metadata);
        if (matched is null)
        {
            logger.LogInformation("No enabled rule matched download {BrowserDownloadId}; browser behavior remains unchanged", payload.DownloadId);
            return AgentResponse.Ok(request.RequestId, new { tracked = false, failOpen = true });
        }

        var routingState = matched.Rule.StorageMode == StorageMode.SelectSubfolder
            ? RoutingState.WaitingForSelection
            : RoutingState.NotRequired;
        var job = new DownloadJob(
            Guid.NewGuid(),
            browser,
            payload.DownloadId,
            Path.GetFileName(payload.FileName),
            Path.GetFileName(payload.FileName),
            sanitizer.Sanitize(payload.InitiatingPageUrl),
            sanitizer.Sanitize(payload.InitialUrl),
            sanitizer.Sanitize(payload.FinalUrl),
            sanitizer.Sanitize(payload.ReferrerUrl),
            sanitizer.Sanitize(matched.MatchedUrl),
            matched.Rule.Id,
            NormalizeOptionalSourcePath(payload.FilePath),
            null,
            null,
            BrowserTransferState.InProgress,
            routingState,
            null,
            null,
            DateTimeOffset.UtcNow,
            null);

        await repository.CreateJobAsync(job, cancellationToken).ConfigureAwait(false);
        var selectionUiRequested = matched.Rule.StorageMode == StorageMode.SelectSubfolder
            && selectionUiLauncher.RequestSelectionUi();
        logger.LogInformation(
            "Rule {RuleId} matched download {DownloadId} using {SourceField}",
            matched.Rule.Id,
            payload.DownloadId,
            matched.SourceField);
        return AgentResponse.Ok(request.RequestId, new
        {
            tracked = true,
            jobId = job.Id,
            storageMode = matched.Rule.StorageMode.ToString(),
            requiresSelection = matched.Rule.StorageMode == StorageMode.SelectSubfolder,
            selectionUiRequested,
        });
    }

    private async Task<AgentResponse> HandleDownloadChangedAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<DownloadChangedPayload>(request.Payload);
        if (!TryParseBrowser(payload.Browser, out var browser))
        {
            return AgentResponse.Error(request.RequestId, "download.unknown-browser", "The browser is not supported.");
        }

        var job = await repository.GetJobAsync(browser, payload.DownloadId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return AgentResponse.Ok(request.RequestId, new { tracked = false, failOpen = true });
        }

        var state = payload.State.Trim().ToLowerInvariant();
        if (state is "interrupted" or "cancelled")
        {
            var cancelled = string.Equals(payload.Error, "USER_CANCELED", StringComparison.OrdinalIgnoreCase)
                || state == "cancelled";
            var next = cancelled ? BrowserTransferState.Cancelled : BrowserTransferState.Interrupted;
            if (job.BrowserState != BrowserTransferState.InProgress)
            {
                return AgentResponse.Ok(request.RequestId, JobState(job));
            }

            stateMachine.EnsureCanTransition(job.BrowserState, next);
            var routing = cancelled ? RoutingState.NotRequired : RoutingState.Failed;
            stateMachine.EnsureCanTransition(job.RoutingState, routing);
            var reportedFileName = string.IsNullOrWhiteSpace(payload.FilePath)
                ? job.CurrentFileName
                : Path.GetFileName(NormalizeOptionalSourcePath(payload.FilePath));
            job = job with
            {
                BrowserState = next,
                RoutingState = routing,
                CurrentFileName = reportedFileName,
                ErrorCode = cancelled ? "download.cancelled" : "download.interrupted." + NormalizeBrowserError(payload.Error),
                ErrorMessage = cancelled
                    ? "The user cancelled this download in the browser."
                    : "The browser interrupted this download before completion.",
                CompletedAt = DateTimeOffset.UtcNow,
            };
            await repository.UpdateJobAsync(job, cancelled ? "download.cancelled" : "download.interrupted", cancellationToken).ConfigureAwait(false);
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        if (state != "complete")
        {
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        if (job.BrowserState != BrowserTransferState.InProgress)
        {
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        stateMachine.EnsureCanTransition(job.BrowserState, BrowserTransferState.Complete);
        var sourcePath = NormalizeOptionalSourcePath(payload.FilePath)
            ?? throw new ArgumentException("A completed download must include its final local path.");
        job = job with
        {
            OriginalPath = sourcePath,
            CurrentFileName = Path.GetFileName(sourcePath),
            BrowserState = BrowserTransferState.Complete,
            ErrorCode = null,
            ErrorMessage = null,
        };

        var rule = await repository.GetRuleAsync(job.RuleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The matched rule no longer exists.");
        if (job.RoutingState == RoutingState.WaitingForSelection)
        {
            await repository.UpdateJobAsync(job, "download.completed-selection-pending", cancellationToken).ConfigureAwait(false);
            return AgentResponse.Ok(request.RequestId, new
            {
                tracked = true,
                status = job.Status.ToString(),
                browserState = job.BrowserState.ToString(),
                routingState = job.RoutingState.ToString(),
                requiresSelection = true,
            });
        }

        await repository.UpdateJobAsync(job, "download.completed", cancellationToken).ConfigureAwait(false);
        var moved = await MoveJobAsync(job, rule, cancellationToken).ConfigureAwait(false);
        return AgentResponse.Ok(request.RequestId, new
        {
            tracked = true,
            status = moved.Status.ToString(),
            browserState = moved.BrowserState.ToString(),
            routingState = moved.RoutingState.ToString(),
            destinationPath = moved.FinalPath,
            errorCode = moved.ErrorCode,
        });
    }

    private async Task<AgentResponse> HandleRuleUpsertAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var rule = Deserialize<DownloadRule>(request.Payload);
        _ = pathTokenResolver.Resolve(rule.StorageRoot);
        await repository.UpsertRuleAsync(rule with { UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
        return AgentResponse.Ok(request.RequestId, new { ruleId = rule.Id });
    }

    private async Task<AgentResponse> HandleSelectionCompletedAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<SelectionCompletedPayload>(request.Payload);
        if (payload.JobIds.Count is < 1 or > 1000)
        {
            return AgentResponse.Error(request.RequestId, "selection.invalid-count", "Select between 1 and 1000 queued jobs.");
        }

        var results = new List<object>();
        foreach (var jobId in payload.JobIds.Distinct())
        {
            var job = await repository.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (job is null || !job.IsSelectionPending)
            {
                continue;
            }

            var rule = await repository.GetRuleAsync(job.RuleId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The matched rule no longer exists.");
            var root = pathTokenResolver.Resolve(rule.StorageRoot);
            _ = boundaryValidator.ValidateRelativeFolder(root, payload.RelativeFolder);

            stateMachine.EnsureCanTransition(job.RoutingState, RoutingState.SelectionReady);
            job = job with
            {
                SelectedRelativeFolder = payload.RelativeFolder,
                RoutingState = RoutingState.SelectionReady,
                ErrorCode = null,
                ErrorMessage = null,
            };
            await repository.UpdateJobAsync(job, "selection.completed", cancellationToken).ConfigureAwait(false);

            if (job.BrowserState == BrowserTransferState.Complete && job.OriginalPath is not null)
            {
                job = await MoveJobAsync(job, rule, cancellationToken).ConfigureAwait(false);
            }

            results.Add(new
            {
                jobId = job.Id,
                status = job.Status.ToString(),
                browserState = job.BrowserState.ToString(),
                routingState = job.RoutingState.ToString(),
                destinationPath = job.FinalPath,
            });
        }

        return AgentResponse.Ok(request.RequestId, results);
    }

    private async Task<AgentResponse> HandleSelectionSkippedAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<SelectionSkippedPayload>(request.Payload);
        var job = await repository.GetJobAsync(payload.JobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return AgentResponse.Error(request.RequestId, "job.not-found", "The download job was not found.");
        }

        if (!job.IsSelectionPending)
        {
            return AgentResponse.Error(request.RequestId, "selection.not-pending", "This download is no longer waiting for a folder selection.");
        }

        stateMachine.EnsureCanTransition(job.RoutingState, RoutingState.Skipped);
        job = job with
        {
            RoutingState = RoutingState.Skipped,
            ErrorCode = null,
            ErrorMessage = null,
            CompletedAt = DateTimeOffset.UtcNow,
        };
        await repository.UpdateJobAsync(job, "selection.skipped", cancellationToken).ConfigureAwait(false);
        return AgentResponse.Ok(request.RequestId, JobState(job));
    }

    private async Task<AgentResponse> HandleJobsDeleteAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<JobsDeletePayload>(request.Payload);
        if (payload.JobIds.Count is < 1 or > 1000)
        {
            return AgentResponse.Error(request.RequestId, "jobs.invalid-count", "Delete between 1 and 1000 history items.");
        }

        var deleted = await repository.DeleteJobsAsync(payload.JobIds, cancellationToken).ConfigureAwait(false);
        return AgentResponse.Ok(request.RequestId, new { deleted, filesDeleted = 0 });
    }

    private async Task<AgentResponse> HandleActiveDownloadsAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<ActiveDownloadsPayload>(request.Payload);
        if (!TryParseBrowser(payload.Browser, out var browser))
        {
            return AgentResponse.Error(request.RequestId, "download.unknown-browser", "The browser is not supported.");
        }

        var downloads = await repository.GetActiveBrowserDownloadsAsync(browser, cancellationToken).ConfigureAwait(false);
        return AgentResponse.Ok(request.RequestId, downloads);
    }

    private async Task<AgentResponse> HandleRetryAsync(AgentCommand request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<JobRetryPayload>(ProtocolJson.Options)
            ?? throw new JsonException("Retry payload is required.");
        var job = await repository.GetJobAsync(payload.JobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return AgentResponse.Error(request.RequestId, "job.not-found", "The download job was not found.");
        }

        if (job.OriginalPath is null)
        {
            return AgentResponse.Error(request.RequestId, "file.source-missing", "The original download path is unknown.");
        }

        if (job.BrowserState != BrowserTransferState.Complete)
        {
            return AgentResponse.Error(request.RequestId, "download.not-complete", "Only completed browser downloads can be retried.");
        }

        stateMachine.EnsureCanTransition(job.RoutingState, RoutingState.RetryPending);
        job = job with { RoutingState = RoutingState.RetryPending, ErrorCode = null, ErrorMessage = null };
        await repository.UpdateJobAsync(job, "job.retry-requested", cancellationToken).ConfigureAwait(false);
        var rule = await repository.GetRuleAsync(job.RuleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The matched rule no longer exists.");
        job = await MoveJobAsync(job, rule, cancellationToken).ConfigureAwait(false);
        return AgentResponse.Ok(request.RequestId, new { jobId = job.Id, status = job.Status.ToString(), destinationPath = job.FinalPath });
    }

    private async Task<DownloadJob> MoveJobAsync(
        DownloadJob job,
        DownloadRule rule,
        CancellationToken cancellationToken)
    {
        if (job.OriginalPath is null)
        {
            throw new InvalidOperationException("Cannot move a job without a source path.");
        }

        if (job.BrowserState != BrowserTransferState.Complete)
        {
            throw new InvalidOperationException("Cannot move a download before the browser reports completion.");
        }

        stateMachine.EnsureCanTransition(job.RoutingState, RoutingState.Moving);
        job = job with { RoutingState = RoutingState.Moving };
        await repository.UpdateJobAsync(job, "file.move-started", cancellationToken).ConfigureAwait(false);

        var root = pathTokenResolver.Resolve(rule.StorageRoot);
        var result = await fileMoveService.MoveAsync(
            job.OriginalPath,
            root,
            job.SelectedRelativeFolder,
            browserReportedComplete: true,
            cancellationToken).ConfigureAwait(false);
        var next = result.Success
            ? RoutingState.Completed
            : result.CanRetry ? RoutingState.RetryPending : RoutingState.Failed;
        stateMachine.EnsureCanTransition(job.RoutingState, next);
        job = job with
        {
            RoutingState = next,
            FinalPath = result.DestinationPath,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage,
            CompletedAt = result.Success ? DateTimeOffset.UtcNow : null,
        };
        await repository.UpdateJobAsync(job, result.Success ? "file.move-completed" : "file.move-failed", cancellationToken).ConfigureAwait(false);
        return job;
    }

    private static T Deserialize<T>(JsonElement payload)
        => payload.Deserialize<T>(ProtocolJson.Options)
            ?? throw new JsonException($"A {typeof(T).Name} payload is required.");

    private static bool TryParseBrowser(string value, out BrowserKind browser)
        => Enum.TryParse(value, ignoreCase: true, out browser) && browser != BrowserKind.Unknown;

    private static string? NormalizeOptionalSourcePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Download paths must be absolute.", nameof(path));
        }

        return Path.GetFullPath(path);
    }

    private static string NormalizeBrowserError(string? value)
    {
        var known = value?.Trim().ToUpperInvariant();
        return known switch
        {
            "FILE_FAILED" => "file-failed",
            "FILE_ACCESS_DENIED" => "file-access-denied",
            "FILE_NO_SPACE" => "file-no-space",
            "FILE_NAME_TOO_LONG" => "file-name-too-long",
            "FILE_TOO_LARGE" => "file-too-large",
            "FILE_VIRUS_INFECTED" => "file-virus-infected",
            "FILE_TRANSIENT_ERROR" => "file-transient-error",
            "FILE_BLOCKED" => "file-blocked",
            "FILE_SECURITY_CHECK_FAILED" => "file-security-check-failed",
            "FILE_TOO_SHORT" => "file-too-short",
            "FILE_HASH_MISMATCH" => "file-hash-mismatch",
            "NETWORK_FAILED" => "network-failed",
            "NETWORK_TIMEOUT" => "network-timeout",
            "NETWORK_DISCONNECTED" => "network-disconnected",
            "NETWORK_SERVER_DOWN" => "network-server-down",
            "NETWORK_INVALID_REQUEST" => "network-invalid-request",
            "SERVER_FAILED" => "server-failed",
            "SERVER_NO_RANGE" => "server-no-range",
            "SERVER_BAD_CONTENT" => "server-bad-content",
            "SERVER_UNAUTHORIZED" => "server-unauthorized",
            "SERVER_CERT_PROBLEM" => "server-cert-problem",
            "SERVER_FORBIDDEN" => "server-forbidden",
            "SERVER_UNREACHABLE" => "server-unreachable",
            "SERVER_CONTENT_LENGTH_MISMATCH" => "server-content-length-mismatch",
            "SERVER_CROSS_ORIGIN_REDIRECT" => "server-cross-origin-redirect",
            "USER_SHUTDOWN" => "user-shutdown",
            "CRASH" => "crash",
            _ => "unknown",
        };
    }

    private static object JobState(DownloadJob job)
        => new
        {
            tracked = true,
            status = job.Status.ToString(),
            browserState = job.BrowserState.ToString(),
            routingState = job.RoutingState.ToString(),
        };

    private sealed record JobRetryPayload(Guid JobId);
}
