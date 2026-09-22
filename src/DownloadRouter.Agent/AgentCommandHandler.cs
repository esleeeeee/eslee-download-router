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
    ILogger<AgentCommandHandler> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    /// <summary>
    /// Records the most recent rejection caused by an unrecognised extension build so the
    /// app can tell the user to refresh the extension. Counts only, no browser data.
    /// </summary>
    private static int unsupportedExtensionRejections;
    private static long lastUnsupportedExtensionTicks;
    private static int lastExtensionBuildSupported = -1;
    private static long lastExtensionHelloTicks;
    private static string? lastExtensionBuild;

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
                "download.metadata" => await HandleDownloadMetadataAsync(request, cancellationToken).ConfigureAwait(false),
                "download.changed" => await HandleDownloadChangedAsync(request, cancellationToken).ConfigureAwait(false),
                "download.cancelled" => await HandleDownloadChangedAsync(request, cancellationToken, "cancelled").ConfigureAwait(false),
                "download.interrupted" => await HandleDownloadChangedAsync(request, cancellationToken, "interrupted").ConfigureAwait(false),
                "rules.list" => AgentResponse.Ok(request.RequestId, await repository.GetRulesAsync(cancellationToken).ConfigureAwait(false)),
                "rules.upsert" => await HandleRuleUpsertAsync(request, cancellationToken).ConfigureAwait(false),
                "rules.delete" => await HandleRuleDeleteAsync(request, cancellationToken).ConfigureAwait(false),
                "jobs.list" => AgentResponse.Ok(request.RequestId, await repository.GetRecentJobsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)),
                "jobs.delete" => await HandleJobsDeleteAsync(request, cancellationToken).ConfigureAwait(false),
                "downloads.active" => await HandleActiveDownloadsAsync(request, cancellationToken).ConfigureAwait(false),
                "extension.hello" => HandleExtensionHello(request),
                "selection.complete" => await HandleSelectionCompletedAsync(request, cancellationToken).ConfigureAwait(false),
                "selection.skip" => await HandleSelectionSkippedAsync(request, cancellationToken).ConfigureAwait(false),
                "selection.skip-many" => await HandleSelectionsSkippedAsync(request, cancellationToken).ConfigureAwait(false),
                "selection.prompt-state" => await HandleSelectionPromptStateAsync(request, cancellationToken).ConfigureAwait(false),
                "route.change" => await HandleRouteChangeAsync(request, cancellationToken).ConfigureAwait(false),
                "job.retry" => await HandleRetryAsync(request, cancellationToken).ConfigureAwait(false),
                "diagnostics.status" => AgentResponse.Ok(request.RequestId, new
                {
                    dataDirectory = paths.RootDirectory,
                    databaseExists = File.Exists(paths.DatabasePath),
                    protocolVersion = ProtocolConstants.CurrentVersion,
                    processId = Environment.ProcessId,
                    supportedExtensionBuilds = DownloadRegistrationPolicy.SupportedExtensionBuilds.ToArray(),
                    unsupportedExtensionRejections = Volatile.Read(ref unsupportedExtensionRejections),
                    lastUnsupportedExtensionAt = Volatile.Read(ref lastUnsupportedExtensionTicks) == 0
                        ? null
                        : new DateTimeOffset(Volatile.Read(ref lastUnsupportedExtensionTicks), TimeSpan.Zero).ToString("O"),
                    lastExtensionBuild = Volatile.Read(ref lastExtensionBuild),
                    extensionBuildSupported = Volatile.Read(ref lastExtensionBuildSupported) switch
                    {
                        1 => (bool?)true,
                        0 => false,
                        _ => null,
                    },
                    lastExtensionHelloAt = Volatile.Read(ref lastExtensionHelloTicks) == 0
                        ? null
                        : new DateTimeOffset(Volatile.Read(ref lastExtensionHelloTicks), TimeSpan.Zero).ToString("O"),
                    extensionRefreshRequired = Volatile.Read(ref lastExtensionBuildSupported) == 0
                        || Volatile.Read(ref unsupportedExtensionRejections) > 0,
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
            || string.IsNullOrWhiteSpace(payload.DownloadId))
        {
            return AgentResponse.Error(request.RequestId, "download.invalid-metadata", "Browser and download ID are required.");
        }

        var existing = await repository.GetJobAsync(browser, payload.DownloadId, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            existing = await UpdateFileNameAsync(
                existing,
                payload.FileName,
                payload.FilePath,
                refreshBrowserActivity: false,
                cancellationToken).ConfigureAwait(false);
            return AgentResponse.Ok(request.RequestId, new { tracked = true, jobId = existing.Id, existing = true });
        }

        // Nothing exists for this download yet, so this request would create a job.
        // Creation is fail-closed: replayed browser history and unrecognised extension
        // builds are refused. The browser download itself is never blocked.
        var registration = DownloadRegistrationPolicy.Classify(payload, DateTimeOffset.UtcNow);
        if (registration != DownloadRegistrationDecision.Track)
        {
            var reason = DownloadRegistrationPolicy.DescribeRejection(registration);
            var unsupportedExtension = registration == DownloadRegistrationDecision.RejectUnsupportedExtension;
            if (unsupportedExtension)
            {
                Interlocked.Increment(ref unsupportedExtensionRejections);
                Interlocked.Exchange(ref lastUnsupportedExtensionTicks, DateTimeOffset.UtcNow.UtcTicks);
                logger.LogWarning(
                    "Refused to register download {DownloadId} from an unrecognised extension build; "
                        + "the browser is likely still running a cached older extension",
                    SanitizeDownloadId(payload.DownloadId));
            }
            else
            {
                logger.LogInformation(
                    "Ignored a replayed browser download {DownloadId}; reason={Reason}",
                    SanitizeDownloadId(payload.DownloadId),
                    reason);
            }

            return AgentResponse.Ok(request.RequestId, new
            {
                tracked = false,
                failOpen = true,
                ignored = reason,
                extensionRefreshRequired = unsupportedExtension,
                message = unsupportedExtension ? DownloadRegistrationPolicy.ExtensionRefreshGuidance : null,
            });
        }

        var trustedFileName = DownloadPresentation.TrustedFileName(payload.FileName)
            ?? DownloadPresentation.TrustedFileName(payload.FilePath)
            ?? string.Empty;

        var metadata = new DownloadMetadata(
            browser,
            payload.DownloadId,
            trustedFileName,
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
            logger.LogInformation(
                "No enabled rule matched download {BrowserDownloadId}; browser behavior remains unchanged",
                SanitizeDownloadId(payload.DownloadId));
            return AgentResponse.Ok(request.RequestId, new { tracked = false, failOpen = true });
        }

        string? temporaryDestination = null;
        if (matched.Rule.StorageMode == StorageMode.SelectSubfolder)
        {
            var choice = await repository.GetTemporaryFolderAsync(matched.Rule.Id, cancellationToken).ConfigureAwait(false);
            if (choice?.IsActive(matched.Rule, clock.GetUtcNow()) == true)
            {
                try { temporaryDestination = SelectionDestination.ValidateFolder(choice.DestinationFolder); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // An unavailable remembered folder must return to normal selection, never lose a download.
                    logger.LogInformation("Temporary destination unavailable for rule {RuleId}", matched.Rule.Id);
                }
            }
        }
        var requiresSelection = matched.Rule.StorageMode == StorageMode.SelectSubfolder && temporaryDestination is null;
        var routingState = requiresSelection
            ? RoutingState.WaitingForSelection
            : temporaryDestination is not null ? RoutingState.SelectionReady : RoutingState.NotRequired;
        // Automatic rules never open the selection window, so they start already resolved.
        var promptState = requiresSelection
            ? SelectionPromptState.NeverShown
            : SelectionPromptState.Resolved;
        var job = new DownloadJob(
            Guid.NewGuid(),
            browser,
            payload.DownloadId,
            trustedFileName,
            trustedFileName,
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
            null,
            DateTimeOffset.UtcNow,
            false,
            promptState,
            SelectedDestinationFolder: temporaryDestination);

        await repository.CreateJobAsync(job, cancellationToken).ConfigureAwait(false);
        var selectionUiRequested = requiresSelection && selectionUiLauncher.RequestSelectionUi();
        logger.LogInformation(
            "Rule {RuleId} matched download {DownloadId} using {SourceField}",
            matched.Rule.Id,
            SanitizeDownloadId(payload.DownloadId),
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

    private async Task<AgentResponse> HandleDownloadMetadataAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<DownloadMetadataChangedPayload>(request.Payload);
        if (!TryParseBrowser(payload.Browser, out var browser))
        {
            return AgentResponse.Error(request.RequestId, "download.unknown-browser", "The browser is not supported.");
        }

        var job = await repository.GetJobAsync(browser, payload.DownloadId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return AgentResponse.Ok(request.RequestId, new { tracked = false, failOpen = true });
        }

        var updated = await UpdateFileNameAsync(
            job,
            payload.FileName,
            payload.FilePath,
            refreshBrowserActivity: true,
            cancellationToken).ConfigureAwait(false);
        return AgentResponse.Ok(request.RequestId, new
        {
            tracked = true,
            jobId = updated.Id,
            fileName = DownloadPresentation.DisplayFileName(updated),
            changed = !string.Equals(job.CurrentFileName, updated.CurrentFileName, StringComparison.Ordinal),
        });
    }

    private async Task<AgentResponse> HandleDownloadChangedAsync(
        AgentCommand request,
        CancellationToken cancellationToken,
        string? forcedState = null)
    {
        var payload = Deserialize<DownloadChangedPayload>(request.Payload);
        if (!TryParseBrowser(payload.Browser, out var browser))
        {
            return AgentResponse.Error(request.RequestId, "download.unknown-browser", "The browser is not supported.");
        }

        var sanitizedDownloadId = SanitizeDownloadId(payload.DownloadId);
        var state = (forcedState ?? payload.State).Trim().ToLowerInvariant();
        logger.LogInformation(
            "Browser command received: command={Command}, browser={Browser}, downloadId={DownloadId}, state={State}, error={Error}",
            request.Command,
            browser,
            sanitizedDownloadId,
            SanitizeState(state),
            NormalizeBrowserError(payload.Error));

        var job = await repository.GetJobAsync(browser, payload.DownloadId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return AgentResponse.Ok(request.RequestId, new { tracked = false, failOpen = true });
        }

        var beforeBrowser = job.BrowserState;
        var beforeRouting = job.RoutingState;
        if (state == "stale")
        {
            if (job.BrowserState == BrowserTransferState.InProgress && !job.IsBrowserRecordStale)
            {
                job = job with { IsBrowserRecordStale = true };
                await repository.UpdateJobAsync(job, "download.browser-record-stale", cancellationToken).ConfigureAwait(false);
            }

            LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, job);
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        if (state == "in_progress")
        {
            if (job.BrowserState == BrowserTransferState.InProgress)
            {
                job = job with
                {
                    LastBrowserEventAt = payload.IsReconciliation
                        ? job.LastBrowserEventAt
                        : DateTimeOffset.UtcNow,
                    IsBrowserRecordStale = false,
                };
                await repository.UpdateJobAsync(job, "download.reconciled-in-progress", cancellationToken).ConfigureAwait(false);
            }

            LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, job);
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        if (state is "interrupted" or "cancelled")
        {
            var cancelled = string.Equals(payload.Error, "USER_CANCELED", StringComparison.OrdinalIgnoreCase)
                || state == "cancelled";
            var next = cancelled ? BrowserTransferState.Cancelled : BrowserTransferState.Interrupted;
            if (job.BrowserState != BrowserTransferState.InProgress)
            {
                LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, job);
                return AgentResponse.Ok(request.RequestId, JobState(job));
            }

            stateMachine.EnsureCanTransition(job.BrowserState, next);
            var routing = cancelled
                ? job.RoutingState is RoutingState.WaitingForSelection or RoutingState.SelectionReady
                    ? RoutingState.NotRequired
                    : job.RoutingState
                : job.RoutingState is RoutingState.Completed or RoutingState.Skipped
                    ? job.RoutingState
                    : RoutingState.Failed;
            if (!cancelled)
            {
                stateMachine.EnsureCanTransition(job.RoutingState, routing);
            }
            var reportedFileName = string.IsNullOrWhiteSpace(payload.FilePath)
                ? job.CurrentFileName
                : DownloadPresentation.TrustedFileName(payload.FileName)
                    ?? DownloadPresentation.TrustedFileName(NormalizeOptionalSourcePath(payload.FilePath))
                    ?? job.CurrentFileName;
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
                LastBrowserEventAt = payload.IsReconciliation
                    ? job.LastBrowserEventAt
                    : DateTimeOffset.UtcNow,
                IsBrowserRecordStale = false,
                // A cancelled or interrupted transfer never needs a folder decision again.
                SelectionPromptState = SelectionPromptState.Resolved,
            };
            await repository.UpdateJobAsync(job, cancelled ? "download.cancelled" : "download.interrupted", cancellationToken).ConfigureAwait(false);
            LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, job);
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        if (state != "complete")
        {
            LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, job);
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        if (job.BrowserState != BrowserTransferState.InProgress)
        {
            LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, job);
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        stateMachine.EnsureCanTransition(job.BrowserState, BrowserTransferState.Complete);
        var sourcePath = NormalizeOptionalSourcePath(payload.FilePath)
            ?? throw new ArgumentException("A completed download must include its final local path.");
        job = job with
        {
            OriginalPath = sourcePath,
            CurrentFileName = DownloadPresentation.TrustedFileName(payload.FileName)
                ?? DownloadPresentation.TrustedFileName(sourcePath)
                ?? job.CurrentFileName,
            BrowserState = BrowserTransferState.Complete,
            ErrorCode = null,
            ErrorMessage = null,
            LastBrowserEventAt = payload.IsReconciliation
                ? job.LastBrowserEventAt
                : DateTimeOffset.UtcNow,
            IsBrowserRecordStale = false,
        };

        var rule = await repository.GetRuleAsync(job.RuleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The matched rule no longer exists.");
        if (job.RoutingState == RoutingState.WaitingForSelection)
        {
            await repository.UpdateJobAsync(job, "download.completed-selection-pending", cancellationToken).ConfigureAwait(false);
            LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, job);
            return AgentResponse.Ok(request.RequestId, new
            {
                tracked = true,
                status = job.Status.ToString(),
                browserState = job.BrowserState.ToString(),
                routingState = job.RoutingState.ToString(),
                requiresSelection = true,
            });
        }

        if (job.RoutingState == RoutingState.Skipped)
        {
            job = job with { CompletedAt = DateTimeOffset.UtcNow };
            await repository.UpdateJobAsync(job, "download.completed-routing-skipped", cancellationToken).ConfigureAwait(false);
            LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, job);
            return AgentResponse.Ok(request.RequestId, new
            {
                tracked = true,
                status = job.Status.ToString(),
                browserState = job.BrowserState.ToString(),
                routingState = job.RoutingState.ToString(),
                destinationPath = (string?)null,
            });
        }

        await repository.UpdateJobAsync(job, "download.completed", cancellationToken).ConfigureAwait(false);
        var moved = await MoveJobAsync(job, rule, cancellationToken).ConfigureAwait(false);
        LogTransition(sanitizedDownloadId, request.Command, beforeBrowser, beforeRouting, moved);
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
        var existing = await repository.GetRuleAsync(rule.Id, cancellationToken).ConfigureAwait(false);
        var persisted = rule with
        {
            CreatedAt = existing?.CreatedAt ?? rule.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await repository.UpsertRuleAsync(persisted, cancellationToken).ConfigureAwait(false);
        return AgentResponse.Ok(request.RequestId, new { ruleId = rule.Id });
    }

    private async Task<AgentResponse> HandleRuleDeleteAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<RuleDeletePayload>(request.Payload);
        var deleted = await repository.DeleteRuleAsync(payload.RuleId, cancellationToken).ConfigureAwait(false);
        return deleted
            ? AgentResponse.Ok(request.RequestId, new { ruleId = payload.RuleId, jobsDeleted = 0, filesDeleted = 0 })
            : AgentResponse.Error(request.RequestId, "rule.not-found", "The rule was not found or was already deleted.");
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
        if (payload.UseForTenMinutes && payload.JobIds.Count != 1)
            return AgentResponse.Error(request.RequestId, "selection.temporary-single", "10분 저장은 개별 파일 선택창에서 설정하세요.");
        var destinationFolder = payload.DestinationFolder is null
            ? null : SelectionDestination.ValidateFolder(payload.DestinationFolder);
        var selectedFileName = payload.FileName is null
            ? null : SelectionDestination.ValidateFileName(payload.FileName);
        if (selectedFileName is not null && payload.JobIds.Count != 1)
            return AgentResponse.Error(request.RequestId, "selection.rename-single", "파일 이름은 한 파일씩 변경하세요.");
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
            if (destinationFolder is null)
                _ = boundaryValidator.ValidateRelativeFolder(root, payload.RelativeFolder);

            TemporaryFolderChoice? temporaryFolder = null;
            if (payload.UseForTenMinutes)
            {
                if (!rule.IsEnabled || rule.StorageMode != StorageMode.SelectSubfolder)
                    return AgentResponse.Error(request.RequestId, "selection.temporary-rule", "활성 폴더 선택 규칙에서만 사용할 수 있습니다.");
                var folder = SelectionDestination.ValidateFolder(destinationFolder
                    ?? boundaryValidator.ValidateRelativeFolder(root, payload.RelativeFolder));
                var now = clock.GetUtcNow();
                temporaryFolder = new TemporaryFolderChoice(folder, now, now + TemporaryFolderChoice.Duration, rule.UpdatedAt);
            }

            stateMachine.EnsureCanTransition(job.RoutingState, RoutingState.SelectionReady);
            job = job with
            {
                SelectedRelativeFolder = payload.RelativeFolder,
                SelectedDestinationFolder = destinationFolder,
                SelectedFileName = selectedFileName,
                RoutingState = RoutingState.SelectionReady,
                SelectionPromptState = SelectionPromptState.Resolved,
                ErrorCode = null,
                ErrorMessage = null,
            };
            await repository.UpdateJobAsync(job, "selection.completed", cancellationToken, temporaryFolder).ConfigureAwait(false);

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

        if (job.RoutingState == RoutingState.Skipped)
        {
            return AgentResponse.Ok(request.RequestId, JobState(job));
        }

        if (job.BrowserState is not (BrowserTransferState.InProgress or BrowserTransferState.Complete)
            || job.RoutingState is not (RoutingState.WaitingForSelection or RoutingState.SelectionReady))
        {
            return AgentResponse.Error(request.RequestId, "selection.not-pending", "This download can no longer skip folder routing.");
        }

        stateMachine.EnsureCanTransition(job.RoutingState, RoutingState.Skipped);
        var skippedIds = await repository.SkipSelectionsAsync(
            [job.Id],
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (skippedIds.Count != 1)
        {
            return AgentResponse.Error(request.RequestId, "selection.state-changed", "The selection state changed before it could be skipped.");
        }

        job = await repository.GetJobAsync(job.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The skipped download job could not be reloaded.");
        return AgentResponse.Ok(request.RequestId, JobState(job));
    }

    private async Task<AgentResponse> HandleSelectionsSkippedAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<SelectionsSkippedPayload>(request.Payload);
        var jobIds = payload.JobIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (jobIds.Length is < 1 or > 1000)
        {
            return AgentResponse.Error(request.RequestId, "selection.invalid-count", "Skip between 1 and 1000 queued jobs.");
        }

        foreach (var jobId in jobIds)
        {
            var job = await repository.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                return AgentResponse.Error(request.RequestId, "job.not-found", "A queued download job was not found.");
            }

            if (!job.IsTerminal
                && (job.BrowserState is not (BrowserTransferState.InProgress or BrowserTransferState.Complete)
                    || job.RoutingState is not (RoutingState.WaitingForSelection or RoutingState.SelectionReady)))
            {
                return AgentResponse.Error(request.RequestId, "selection.not-pending", "A queued download can no longer skip folder routing.");
            }
        }

        var skippedIds = await repository.SkipSelectionsAsync(
            jobIds,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        var states = new List<object>(jobIds.Length);
        foreach (var jobId in jobIds)
        {
            var job = await repository.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("A skipped download job could not be reloaded.");
            if (!job.IsTerminal)
            {
                return AgentResponse.Error(request.RequestId, "selection.state-changed", "A queued selection did not reach a terminal state.");
            }

            states.Add(new
            {
                jobId,
                status = job.Status.ToString(),
                browserState = job.BrowserState.ToString(),
                routingState = job.RoutingState.ToString(),
            });
        }

        return AgentResponse.Ok(request.RequestId, new
        {
            requested = jobIds.Length,
            skipped = skippedIds.Count,
            jobs = states,
        });
    }

    private async Task<AgentResponse> HandleSelectionPromptStateAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<SelectionPromptStatePayload>(request.Payload);
        if (payload.State is not (SelectionPromptState.Shown or SelectionPromptState.Deferred))
        {
            return AgentResponse.Error(
                request.RequestId,
                "selection.invalid-prompt-state",
                "Only Shown and Deferred can be recorded from the selection window.");
        }

        var jobIds = payload.JobIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (jobIds.Length is < 1 or > 1000)
        {
            return AgentResponse.Error(
                request.RequestId,
                "selection.invalid-count",
                "Record between 1 and 1000 selection prompts.");
        }

        var advanced = await repository.AdvanceSelectionPromptStateAsync(
            jobIds,
            payload.State,
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Selection prompt state recorded: state={State}, requested={Requested}, advanced={Advanced}",
            payload.State,
            jobIds.Length,
            advanced.Count);
        return AgentResponse.Ok(request.RequestId, new
        {
            requested = jobIds.Length,
            advanced = advanced.Count,
            state = payload.State.ToString(),
        });
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

    private async Task<AgentResponse> HandleRouteChangeAsync(
        AgentCommand request,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<JobRouteChangePayload>(request.Payload);
        var job = await repository.GetJobAsync(payload.JobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return AgentResponse.Error(request.RequestId, "job.not-found", "The download job was not found.");
        }

        if (!DownloadPresentation.CanChangeRoute(job))
        {
            return AgentResponse.Error(
                request.RequestId,
                "route.change-not-allowed",
                job.BrowserState is BrowserTransferState.Cancelled or BrowserTransferState.Interrupted
                    ? "Cancelled or interrupted downloads cannot change destination."
                    : "This download state cannot change destination.");
        }

        var rule = await repository.GetRuleAsync(job.RuleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The matched rule no longer exists.");
        var root = pathTokenResolver.Resolve(rule.StorageRoot);
        _ = boundaryValidator.ValidateRelativeFolder(root, payload.RelativeFolder);

        if (job.RoutingState == RoutingState.Completed)
        {
            if (!payload.ConfirmCompletedMove)
            {
                return AgentResponse.Error(request.RequestId, "route.confirmation-required", "Moving an already routed file requires explicit confirmation.");
            }

            if (string.IsNullOrWhiteSpace(job.FinalPath) || !File.Exists(job.FinalPath))
            {
                return AgentResponse.Error(request.RequestId, "file.source-missing", "The routed file no longer exists at its recorded location.");
            }

            job = job with
            {
                OriginalPath = job.FinalPath,
                SelectedRelativeFolder = payload.RelativeFolder,
                SelectedDestinationFolder = null,
                SelectedFileName = null,
                ErrorCode = null,
                ErrorMessage = null,
            };
            var movedAgain = await MoveJobAsync(job, rule, cancellationToken).ConfigureAwait(false);
            return AgentResponse.Ok(request.RequestId, JobRouteState(movedAgain));
        }

        if (job.RoutingState is RoutingState.WaitingForSelection or RoutingState.Skipped)
        {
            stateMachine.EnsureCanTransition(job.RoutingState, RoutingState.SelectionReady);
            job = job with { RoutingState = RoutingState.SelectionReady };
        }

        job = job with
        {
            SelectedRelativeFolder = payload.RelativeFolder,
            SelectedDestinationFolder = null,
            SelectedFileName = null,
            SelectionPromptState = SelectionPromptState.Resolved,
            ErrorCode = null,
            ErrorMessage = null,
            CompletedAt = null,
        };
        await repository.UpdateJobAsync(job, "route.changed", cancellationToken).ConfigureAwait(false);

        if (job.BrowserState == BrowserTransferState.Complete && job.OriginalPath is not null)
        {
            job = await MoveJobAsync(job, rule, cancellationToken).ConfigureAwait(false);
        }

        return AgentResponse.Ok(request.RequestId, JobRouteState(job));
    }

    /// <summary>
    /// Startup handshake. Lets the app warn about a browser that is still running a cached
    /// older extension before the user tries to download anything.
    /// </summary>
    private AgentResponse HandleExtensionHello(AgentCommand request)
    {
        var payload = Deserialize<ExtensionHelloPayload>(request.Payload);
        var build = payload.ExtensionBuild?.Trim();
        var supported = !string.IsNullOrEmpty(build)
            && DownloadRegistrationPolicy.SupportedExtensionBuilds.Contains(build);

        Interlocked.Exchange(ref lastExtensionBuildSupported, supported ? 1 : 0);
        Interlocked.Exchange(ref lastExtensionHelloTicks, DateTimeOffset.UtcNow.UtcTicks);
        lastExtensionBuild = supported ? build : "unsupported";

        if (supported)
        {
            logger.LogInformation("Extension handshake accepted for build {Build}", build);
        }
        else
        {
            Interlocked.Increment(ref unsupportedExtensionRejections);
            Interlocked.Exchange(ref lastUnsupportedExtensionTicks, DateTimeOffset.UtcNow.UtcTicks);
            logger.LogWarning(
                "Extension handshake reported an unrecognised build; the browser is likely still "
                    + "running a cached older extension");
        }

        return AgentResponse.Ok(request.RequestId, new
        {
            supported,
            supportedExtensionBuilds = DownloadRegistrationPolicy.SupportedExtensionBuilds.ToArray(),
            message = supported ? null : DownloadRegistrationPolicy.ExtensionRefreshGuidance,
        });
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

        var root = job.SelectedDestinationFolder is null
            ? pathTokenResolver.Resolve(rule.StorageRoot)
            : job.SelectedDestinationFolder;
        var result = await fileMoveService.MoveAsync(
            job.OriginalPath,
            root,
            job.SelectedDestinationFolder is null ? job.SelectedRelativeFolder : string.Empty,
            browserReportedComplete: true,
            cancellationToken,
            selectedFileName: job.SelectedFileName,
            validateSelectedRoot: job.SelectedDestinationFolder is not null).ConfigureAwait(false);
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

    private async Task<DownloadJob> UpdateFileNameAsync(
        DownloadJob job,
        string? reportedFileName,
        string? reportedFilePath,
        bool refreshBrowserActivity,
        CancellationToken cancellationToken)
    {
        var trusted = DownloadPresentation.TrustedFileName(reportedFileName)
            ?? DownloadPresentation.TrustedFileName(reportedFilePath);
        var currentFileName = trusted ?? job.CurrentFileName;
        var originalFileName = trusted is not null && string.IsNullOrWhiteSpace(job.OriginalFileName)
            ? trusted
            : job.OriginalFileName;
        var fileNameChanged = !string.Equals(currentFileName, job.CurrentFileName, StringComparison.Ordinal)
            || !string.Equals(originalFileName, job.OriginalFileName, StringComparison.Ordinal);
        if (!fileNameChanged)
        {
            return job;
        }

        var updated = job with
        {
            CurrentFileName = currentFileName,
            OriginalFileName = originalFileName,
            LastBrowserEventAt = refreshBrowserActivity ? DateTimeOffset.UtcNow : job.LastBrowserEventAt,
            IsBrowserRecordStale = refreshBrowserActivity ? false : job.IsBrowserRecordStale,
        };
        await repository.UpdateJobAsync(
            updated,
            "download.filename-updated",
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private void LogTransition(
        string downloadId,
        string command,
        BrowserTransferState beforeBrowser,
        RoutingState beforeRouting,
        DownloadJob after)
        => logger.LogInformation(
            "Browser command applied: command={Command}, downloadId={DownloadId}, browserState={BeforeBrowser}->{AfterBrowser}, routingState={BeforeRouting}->{AfterRouting}",
            command,
            downloadId,
            beforeBrowser,
            after.BrowserState,
            beforeRouting,
            after.RoutingState);

    private static string SanitizeDownloadId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "invalid";
        }

        var sanitized = new string(value.Where(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.').Take(64).ToArray());
        return sanitized.Length == 0 ? "invalid" : sanitized;
    }

    private static string SanitizeReportedState(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "in_progress" or "complete" or "interrupted" or "cancelled"
            ? normalized
            : "unknown";
    }

    private static string SanitizeState(string? value)
        => value is "complete" or "interrupted" or "cancelled" or "in_progress" or "stale"
            ? value
            : "unknown";

    private static object JobRouteState(DownloadJob job)
        => new
        {
            jobId = job.Id,
            status = job.Status.ToString(),
            browserState = job.BrowserState.ToString(),
            routingState = job.RoutingState.ToString(),
            destinationPath = job.FinalPath,
        };

    private sealed record JobRetryPayload(Guid JobId);
}
