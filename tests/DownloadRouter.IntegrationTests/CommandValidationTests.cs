using System.Text.Json;
using DownloadRouter.Agent;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Privacy;
using DownloadRouter.Core.Rules;
using DownloadRouter.Infrastructure.Files;
using DownloadRouter.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownloadRouter.IntegrationTests;

public sealed class CommandValidationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "download-router-integration-tests", Guid.NewGuid().ToString("N"));
    private readonly string? previousOverride;

    private static readonly string SupportedBuild =
        DownloadRegistrationPolicy.SupportedExtensionBuilds.First();

    public CommandValidationTests()
    {
        previousOverride = Environment.GetEnvironmentVariable("DOWNLOAD_ROUTER_DATA_DIR");
        Environment.SetEnvironmentVariable("DOWNLOAD_ROUTER_DATA_DIR", root);
    }

    [Fact]
    public async Task UnknownCommandIsRejectedWithoutSideEffects()
    {
        var (handler, _) = await CreateHandlerAsync();
        var request = new AgentCommand(
            ProtocolConstants.CurrentVersion,
            Guid.NewGuid(),
            "shell.execute",
            JsonSerializer.SerializeToElement(new { command = "whoami" }));

        var response = await handler.HandleAsync(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("protocol.command-not-allowed", response.ErrorCode);
    }

    [Fact]
    public async Task UnmatchedDownloadFailsOpenAndCreatesNoJob()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var payload = new DownloadStartedPayload(
            "Edge", "7", "file.pdf", null, "https://unmatched.example/page",
            "https://cdn.example/file", null, null);
        var request = new AgentCommand(
            ProtocolConstants.CurrentVersion,
            Guid.NewGuid(),
            "download.started",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options));

        var response = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.False(response.Data!.Value.GetProperty("tracked").GetBoolean());
        Assert.True(response.Data.Value.GetProperty("failOpen").GetBoolean());
        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task MatchedDownloadCreatesHistoryAndRoutesCompletedFile()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        var source = Path.Combine(incoming, "example.txt");
        await File.WriteAllTextAsync(source, "public test fixture", CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(),
            "example.com smoke rule",
            true,
            RuleMatchType.DomainAndSubdomains,
            "example.com",
            RuleMatchTarget.InitiatingPage,
            destination,
            StorageMode.Automatic,
            0,
            0,
            now,
            now);
        await repository.UpsertRuleAsync(rule, CancellationToken.None);

        var started = new AgentCommand(
            ProtocolConstants.CurrentVersion,
            Guid.NewGuid(),
            "download.started",
            JsonSerializer.SerializeToElement(
                new DownloadStartedPayload(
                    "Whale",
                    "example-download-1",
                    "example.txt",
                    null,
                    "https://example.com/downloads",
                    "https://example.com/example.txt",
                    null,
                    null,
                    State: "in_progress",
                    StartedAt: DateTimeOffset.UtcNow,
                    ExtensionBuild: SupportedBuild),
                ProtocolJson.Options));
        var startedResponse = await handler.HandleAsync(started, CancellationToken.None);

        Assert.True(startedResponse.Success, startedResponse.Message);
        Assert.True(startedResponse.Data!.Value.GetProperty("tracked").GetBoolean());

        var changed = new AgentCommand(
            ProtocolConstants.CurrentVersion,
            Guid.NewGuid(),
            "download.changed",
            JsonSerializer.SerializeToElement(
                new DownloadChangedPayload("Whale", "example-download-1", "complete", source, null),
                ProtocolJson.Options));
        var changedResponse = await handler.HandleAsync(changed, CancellationToken.None);

        Assert.True(changedResponse.Success, changedResponse.Message);
        Assert.Equal("Completed", changedResponse.Data!.Value.GetProperty("status").GetString());
        var job = Assert.Single(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
        Assert.Equal(DownloadJobStatus.Completed, job.Status);
        Assert.False(File.Exists(source));
        Assert.NotNull(job.FinalPath);
        Assert.True(File.Exists(job.FinalPath));
        Assert.Equal("public test fixture", await File.ReadAllTextAsync(job.FinalPath!, CancellationToken.None));
    }

    [Fact]
    public async Task SameRuleDownloadsCanSelectDifferentFoldersBeforeAndAfterCompletion()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        Directory.CreateDirectory(Path.Combine(destination, "A"));
        Directory.CreateDirectory(Path.Combine(destination, "B"));
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);

        var firstId = await StartAsync(handler, "select-1", "first.txt");
        var secondId = await StartAsync(handler, "select-2", "second.txt");
        var firstSource = Path.Combine(incoming, "first.txt");
        var secondSource = Path.Combine(incoming, "second.txt");
        await File.WriteAllTextAsync(firstSource, "first", CancellationToken.None);
        await File.WriteAllTextAsync(secondSource, "second", CancellationToken.None);

        var selectFirst = await SendAsync(
            handler,
            "selection.complete",
            new SelectionCompletedPayload([firstId], "A"));
        Assert.True(selectFirst.Success, selectFirst.Message);
        var firstBeforeCompletion = await repository.GetJobAsync(firstId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.InProgress, firstBeforeCompletion!.BrowserState);
        Assert.Equal(RoutingState.SelectionReady, firstBeforeCompletion.RoutingState);

        var completeFirst = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "select-1", "complete", firstSource, null));
        Assert.True(completeFirst.Success, completeFirst.Message);
        Assert.True(File.Exists(Path.Combine(destination, "A", "first.txt")));

        var completeSecond = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "select-2", "complete", secondSource, null));
        Assert.True(completeSecond.Success, completeSecond.Message);
        Assert.True(File.Exists(secondSource));
        var secondBeforeSelection = await repository.GetJobAsync(secondId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.Complete, secondBeforeSelection!.BrowserState);
        Assert.Equal(RoutingState.WaitingForSelection, secondBeforeSelection.RoutingState);

        var selectSecond = await SendAsync(
            handler,
            "selection.complete",
            new SelectionCompletedPayload([secondId], "B"));
        Assert.True(selectSecond.Success, selectSecond.Message);
        Assert.True(File.Exists(Path.Combine(destination, "B", "second.txt")));
        Assert.False(File.Exists(secondSource));
    }

    [Fact]
    public async Task BatchSelectionChangesOnlyExplicitlySelectedJobs()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var firstId = await StartAsync(handler, "batch-1", "first.txt");
        var secondId = await StartAsync(handler, "batch-2", "second.txt");
        var thirdId = await StartAsync(handler, "batch-3", "third.txt");

        var response = await SendAsync(
            handler,
            "selection.complete",
            new SelectionCompletedPayload([firstId, thirdId], string.Empty));

        Assert.True(response.Success, response.Message);
        Assert.Equal(RoutingState.SelectionReady, (await repository.GetJobAsync(firstId, CancellationToken.None))!.RoutingState);
        Assert.Equal(RoutingState.WaitingForSelection, (await repository.GetJobAsync(secondId, CancellationToken.None))!.RoutingState);
        Assert.Equal(RoutingState.SelectionReady, (await repository.GetJobAsync(thirdId, CancellationToken.None))!.RoutingState);
    }

    [Fact]
    public async Task UserCancellationIsTerminalPendingIsExcludedAndNoFileIsMoved()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "cancel-1", "cancelled.txt");
        var source = Path.Combine(incoming, "cancelled.txt");
        await File.WriteAllTextAsync(source, "partial content", CancellationToken.None);

        var response = await SendAsync(
            handler,
            "download.cancelled",
            new DownloadChangedPayload("Whale", "cancel-1", "cancelled", source, "USER_CANCELED"));

        Assert.True(response.Success, response.Message);
        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.Cancelled, job!.BrowserState);
        Assert.Equal(RoutingState.NotRequired, job.RoutingState);
        Assert.Equal("download.cancelled", job.ErrorCode);
        Assert.Equal("cancelled.txt", job.CurrentFileName);
        Assert.Empty(DownloadJobQueries.ActiveSelections(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None)));
        Assert.Equal(0, DownloadJobQueries.CountDashboard([job]).WaitingForSelection);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(destination));
    }

    [Fact]
    public async Task BrowserInterruptionStoresOnlyARecognizedErrorCode()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "interrupt-1", "interrupted.txt");

        var response = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload(
                "Whale",
                "interrupt-1",
                "interrupted",
                null,
                "https://example.com/file?token=secret"));

        Assert.True(response.Success, response.Message);
        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.Interrupted, job!.BrowserState);
        Assert.Equal(RoutingState.Failed, job.RoutingState);
        Assert.Equal("download.interrupted.unknown", job.ErrorCode);
        Assert.DoesNotContain("secret", job.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("no-selection")]
    [InlineData("selection-ready")]
    [InlineData("later")]
    [InlineData("skipped")]
    public async Task ExplicitUserCancellationIsIdempotentForEverySelectionFlow(string scenario)
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        Directory.CreateDirectory(Path.Combine(destination, "selected"));
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var downloadId = "cancel-" + scenario;
        var jobId = await StartAsync(handler, downloadId, scenario + ".bin");
        if (scenario == "selection-ready")
        {
            Assert.True((await SendAsync(
                handler,
                "selection.complete",
                new SelectionCompletedPayload([jobId], "selected"))).Success);
        }
        else if (scenario == "skipped")
        {
            Assert.True((await SendAsync(
                handler,
                "selection.skip",
                new SelectionSkippedPayload(jobId))).Success);
        }

        var source = Path.Combine(incoming, scenario + ".bin");
        await File.WriteAllTextAsync(source, "partial", CancellationToken.None);
        var payload = new DownloadChangedPayload("Whale", downloadId, "cancelled", source, "USER_CANCELED");
        var first = await SendAsync(handler, "download.cancelled", payload);
        var duplicate = await SendAsync(handler, "download.cancelled", payload);

        Assert.True(first.Success, first.Message);
        Assert.True(duplicate.Success, duplicate.Message);
        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.Cancelled, job!.BrowserState);
        Assert.Equal(
            scenario == "skipped" ? RoutingState.Skipped : RoutingState.NotRequired,
            job.RoutingState);
        Assert.Equal("download.cancelled", job.ErrorCode);
        Assert.Empty(DownloadJobQueries.ActiveSelections([job]));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MissingBrowserRecordBecomesStaleWithoutBeingGuessedCancelled()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "stale-1", "stale.bin");

        Assert.True((await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "stale-1", "stale", null, null))).Success);
        var stale = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.InProgress, stale!.BrowserState);
        Assert.Equal(RoutingState.WaitingForSelection, stale.RoutingState);
        Assert.True(stale.IsBrowserRecordStale);
        Assert.Single(DownloadJobQueries.ActiveSelections([stale]));
        Assert.Empty(DownloadJobQueries.AutomaticSelections([stale]));

        Assert.True((await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "stale-1", "in_progress", null, null))).Success);
        var restored = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.False(restored!.IsBrowserRecordStale);
        Assert.Single(DownloadJobQueries.AutomaticSelections([restored]));
    }

    [Fact]
    public async Task StartupReconciliationDoesNotMakeADeferredPendingJobAutoPromptEligibleAgain()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "old-reconciled", "old.bin");
        var oldTime = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(1));
        var original = await repository.GetJobAsync(jobId, CancellationToken.None);
        await repository.UpdateJobAsync(
            original! with { CreatedAt = oldTime, LastBrowserEventAt = oldTime },
            "test-aged-pending",
            CancellationToken.None);

        // The user left this file in the pending list yesterday.
        Assert.True((await SendAsync(
            handler,
            "selection.prompt-state",
            new SelectionPromptStatePayload([jobId], SelectionPromptState.Deferred))).Success);

        var reconciled = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload(
                "Whale", "old-reconciled", "in_progress", null, null, IsReconciliation: true));

        Assert.True(reconciled.Success, reconciled.Message);
        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(oldTime, job!.LastBrowserEventAt);
        Assert.Equal(SelectionPromptState.Deferred, job.SelectionPromptState);
        Assert.Single(DownloadJobQueries.ActiveSelections([job]));
        Assert.Empty(DownloadJobQueries.AutomaticSelections([job]));
    }

    [Fact]
    public async Task ShownPromptIsNotReplayedAfterAnAgentRestart()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "prompt-shown-once", "once.bin");

        var initial = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(SelectionPromptState.NeverShown, initial!.SelectionPromptState);
        Assert.Single(DownloadJobQueries.AutomaticSelections([initial]));

        Assert.True((await SendAsync(
            handler,
            "selection.prompt-state",
            new SelectionPromptStatePayload([jobId], SelectionPromptState.Shown))).Success);

        var (_, restartedRepository) = await CreateHandlerAsync();
        var restored = await restartedRepository.GetJobAsync(jobId, CancellationToken.None);

        Assert.Equal(SelectionPromptState.Shown, restored!.SelectionPromptState);
        Assert.Single(DownloadJobQueries.ActiveSelections([restored]));
        Assert.Empty(DownloadJobQueries.AutomaticSelections([restored]));
    }

    [Fact]
    public async Task PromptStateNeverMovesBackwardsAndRejectsTerminalValues()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "prompt-monotonic", "monotonic.bin");

        Assert.True((await SendAsync(
            handler,
            "selection.prompt-state",
            new SelectionPromptStatePayload([jobId], SelectionPromptState.Deferred))).Success);

        var backwards = await SendAsync(
            handler,
            "selection.prompt-state",
            new SelectionPromptStatePayload([jobId], SelectionPromptState.Shown));
        Assert.True(backwards.Success, backwards.Message);
        Assert.Equal(0, backwards.Data!.Value.GetProperty("advanced").GetInt32());

        var invalid = await SendAsync(
            handler,
            "selection.prompt-state",
            new SelectionPromptStatePayload([jobId], SelectionPromptState.NeverShown));
        Assert.False(invalid.Success);
        Assert.Equal("selection.invalid-prompt-state", invalid.ErrorCode);

        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(SelectionPromptState.Deferred, job!.SelectionPromptState);
    }

    [Fact]
    public async Task ChoosingAFolderResolvesThePromptSoItNeverReturns()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "prompt-resolved", "resolved.bin");

        Assert.True((await SendAsync(
            handler,
            "selection.complete",
            new SelectionCompletedPayload([jobId], "docs"))).Success);

        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(SelectionPromptState.Resolved, job!.SelectionPromptState);
        Assert.Empty(DownloadJobQueries.AutomaticSelections([job]));
    }

    [Fact]
    public async Task SkippingASelectionResolvesThePromptState()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "prompt-skip-resolved", "skip.bin");

        Assert.True((await SendAsync(handler, "selection.skip", new SelectionSkippedPayload(jobId))).Success);

        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(SelectionPromptState.Resolved, job!.SelectionPromptState);
        Assert.Empty(DownloadJobQueries.AutomaticSelections([job]));
    }

    [Fact]
    public async Task AutomaticRulesNeverEnterTheSelectionPromptQueue()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.Automatic);
        var jobId = await StartAsync(handler, "automatic-rule", "auto.bin");

        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(SelectionPromptState.Resolved, job!.SelectionPromptState);
        Assert.Empty(DownloadJobQueries.AutomaticSelections([job]));
        Assert.Empty(DownloadJobQueries.ActiveSelections([job]));
    }

    [Fact]
    public async Task SkippedSelectionSurvivesAgentRestartWithoutBeingReenqueued()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "skip-restart", "keep-original.bin");

        var skipped = await SendAsync(handler, "selection.skip", new SelectionSkippedPayload(jobId));
        Assert.True(skipped.Success, skipped.Message);

        var (_, restartedRepository) = await CreateHandlerAsync();
        await restartedRepository.RecoverInProgressJobsAsync(CancellationToken.None);
        var restored = await restartedRepository.GetJobAsync(jobId, CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(RoutingState.Skipped, restored.RoutingState);
        Assert.True(restored.IsTerminal);
        Assert.Empty(DownloadJobQueries.ActiveSelections([restored]));
        Assert.Empty(DownloadJobQueries.AutomaticSelections([restored]));
    }

    [Fact]
    public async Task NativeMessagingReconciliationCannotReopenSkippedSelection()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "skip-native-reconnect", "keep-original.bin");
        Assert.True((await SendAsync(
            handler,
            "selection.skip",
            new SelectionSkippedPayload(jobId))).Success);

        var active = await SendAsync(handler, "downloads.active", new ActiveDownloadsPayload("Whale"));
        Assert.True(active.Success, active.Message);
        Assert.Equal("skip-native-reconnect", Assert.Single(
            JsonSerializer.Deserialize<List<ActiveBrowserDownload>>(
                active.Data!.Value,
                ProtocolJson.Options)!).DownloadId);

        var reconciled = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload(
                "Whale",
                "skip-native-reconnect",
                "in_progress",
                null,
                null,
                IsReconciliation: true));

        Assert.True(reconciled.Success, reconciled.Message);
        var restored = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(RoutingState.Skipped, restored!.RoutingState);
        Assert.Empty(DownloadJobQueries.ActiveSelections([restored]));
        Assert.Empty(DownloadJobQueries.AutomaticSelections([restored]));
    }

    [Fact]
    public async Task WhaleReconnectDuplicateEventsDoNotCreateOrReenqueueSkippedJob()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "skip-whale-reconnect", "keep-original.bin");
        Assert.True((await SendAsync(
            handler,
            "selection.skip",
            new SelectionSkippedPayload(jobId))).Success);

        var duplicateStart = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale",
                "skip-whale-reconnect",
                "keep-original.bin",
                null,
                "https://example.com/downloads",
                "https://example.com/keep-original.bin",
                null,
                null));
        var duplicateMetadata = await SendAsync(
            handler,
            "download.metadata",
            new DownloadMetadataChangedPayload(
                "Whale",
                "skip-whale-reconnect",
                "C:\\Downloads\\keep-original.bin",
                "keep-original.bin"));
        var source = Path.Combine(incoming, "keep-original.bin");
        await File.WriteAllTextAsync(source, "keep", CancellationToken.None);
        var completed = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload(
                "Whale",
                "skip-whale-reconnect",
                "complete",
                source,
                null,
                "keep-original.bin",
                IsReconciliation: true));

        Assert.True(duplicateStart.Success, duplicateStart.Message);
        Assert.True(duplicateMetadata.Success, duplicateMetadata.Message);
        Assert.True(completed.Success, completed.Message);
        var restored = Assert.Single(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
        Assert.Equal(jobId, restored.Id);
        Assert.Equal(BrowserTransferState.Complete, restored.BrowserState);
        Assert.Equal(RoutingState.Skipped, restored.RoutingState);
        Assert.Empty(DownloadJobQueries.ActiveSelections([restored]));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(destination));
    }

    [Fact]
    public async Task SkipManyMakesCurrentQueueTerminalButLeavesFuturePendingUntouchedAfterRestart()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var queued = new[]
        {
            await StartAsync(handler, "skip-many-1", "one.bin"),
            await StartAsync(handler, "skip-many-2", "two.bin"),
            await StartAsync(handler, "skip-many-3", "three.bin"),
        };

        var response = await SendAsync(
            handler,
            "selection.skip-many",
            new SelectionsSkippedPayload(queued));
        Assert.True(response.Success, response.Message);
        Assert.Equal(queued.Length, response.Data!.Value.GetProperty("skipped").GetInt32());
        var future = await StartAsync(handler, "skip-many-future", "future.bin");

        var (_, restartedRepository) = await CreateHandlerAsync();
        var restored = await restartedRepository.GetRecentJobsAsync(cancellationToken: CancellationToken.None);
        var skippedJobs = restored.Where(job => queued.Contains(job.Id)).ToArray();
        var futureJob = Assert.Single(restored, job => job.Id == future);

        Assert.Equal(queued.Length, skippedJobs.Length);
        Assert.All(skippedJobs, job =>
        {
            Assert.Equal(RoutingState.Skipped, job.RoutingState);
            Assert.True(job.IsTerminal);
        });
        Assert.Equal(RoutingState.WaitingForSelection, futureJob.RoutingState);
        Assert.Equal(future, Assert.Single(DownloadJobQueries.ActiveSelections(restored)).Id);
        Assert.Equal(future, Assert.Single(
            DownloadJobQueries.AutomaticSelections(restored)).Id);
    }

    [Fact]
    public async Task ReplayedStartAndUnchangedMetadataDoNotRefreshOldPendingPromptAge()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "old-replayed-start", "old.bin");
        var oldTime = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(31));
        var original = await repository.GetJobAsync(jobId, CancellationToken.None);
        await repository.UpdateJobAsync(
            original! with { CreatedAt = oldTime, LastBrowserEventAt = oldTime },
            "test-aged-pending",
            CancellationToken.None);
        Assert.True((await SendAsync(
            handler,
            "selection.prompt-state",
            new SelectionPromptStatePayload([jobId], SelectionPromptState.Deferred))).Success);

        Assert.True((await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale",
                "old-replayed-start",
                "old.bin",
                null,
                "https://example.com/downloads",
                "https://example.com/old.bin",
                null,
                null))).Success);
        Assert.True((await SendAsync(
            handler,
            "download.metadata",
            new DownloadMetadataChangedPayload(
                "Whale",
                "old-replayed-start",
                "C:\\Downloads\\old.bin",
                "old.bin"))).Success);

        var restored = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(oldTime, restored!.LastBrowserEventAt);
        Assert.Single(DownloadJobQueries.ActiveSelections([restored]));
        Assert.Empty(DownloadJobQueries.AutomaticSelections([restored]));
    }

    [Fact]
    public async Task DeletingHistoryDoesNotDeleteTheDownloadedFile()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "delete-1", "keep.txt");
        var source = Path.Combine(incoming, "keep.txt");
        await File.WriteAllTextAsync(source, "must remain", CancellationToken.None);

        var response = await SendAsync(handler, "jobs.delete", new JobsDeletePayload([jobId]));

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data!.Value.GetProperty("deleted").GetInt32());
        Assert.Equal(0, response.Data.Value.GetProperty("filesDeleted").GetInt32());
        Assert.Null(await repository.GetJobAsync(jobId, CancellationToken.None));
        Assert.True(File.Exists(source));
        Assert.Equal("must remain", await File.ReadAllTextAsync(source, CancellationToken.None));
    }

    [Fact]
    public async Task TemporaryNameUpdatesTheSameJobWhenDownloadsApiReportsFinalMetadata()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "metadata-1", "download");
        var initial = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(DownloadPresentation.PendingFileName, DownloadPresentation.DisplayFileName(initial!));

        var temporary = await SendAsync(
            handler,
            "download.metadata",
            new DownloadMetadataChangedPayload("Whale", "metadata-1", "C:\\Downloads\\미확인 197533.crdownload", null));
        Assert.True(temporary.Success, temporary.Message);
        Assert.Equal(DownloadPresentation.PendingFileName, DownloadPresentation.DisplayFileName((await repository.GetJobAsync(jobId, CancellationToken.None))!));

        var finalMetadata = await SendAsync(
            handler,
            "download.metadata",
            new DownloadMetadataChangedPayload("Whale", "metadata-1", "C:\\Downloads\\실제 이름.zip", "실제 이름.zip"));
        Assert.True(finalMetadata.Success, finalMetadata.Message);
        Assert.Equal("실제 이름.zip", (await repository.GetJobAsync(jobId, CancellationToken.None))!.CurrentFileName);

        var duplicateStart = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale", "metadata-1", "실제 이름.zip", null, "https://example.com/downloads",
                "https://example.com/file", null, null));
        Assert.True(duplicateStart.Success, duplicateStart.Message);
        Assert.True(duplicateStart.Data!.Value.GetProperty("existing").GetBoolean());
        Assert.Single(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task RouteCanChangeBeforeCompletionAndMovesImmediatelyAfterCompletion()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        Directory.CreateDirectory(Path.Combine(destination, "kr", "모야지"));
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "route-before-complete", "tree.bin");

        var changed = await SendAsync(
            handler,
            "route.change",
            new JobRouteChangePayload(jobId, Path.Combine("kr", "모야지")));
        Assert.True(changed.Success, changed.Message);
        var selected = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.InProgress, selected!.BrowserState);
        Assert.Equal(RoutingState.SelectionReady, selected.RoutingState);

        var source = Path.Combine(incoming, "tree.bin");
        await File.WriteAllTextAsync(source, "tree route", CancellationToken.None);
        var completed = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "route-before-complete", "complete", source, null, "tree.bin"));
        Assert.True(completed.Success, completed.Message);
        Assert.True(File.Exists(Path.Combine(destination, "kr", "모야지", "tree.bin")));
    }

    [Fact]
    public async Task CompletedFileRequiresConfirmationAndCanMoveAgainWithoutOverwrite()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        Directory.CreateDirectory(Path.Combine(destination, "other"));
        await CreateRuleAsync(repository, destination, StorageMode.Automatic);
        var jobId = await StartAsync(handler, "reroute-complete", "completed.txt");
        var source = Path.Combine(incoming, "completed.txt");
        await File.WriteAllTextAsync(source, "completed route", CancellationToken.None);
        Assert.True((await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "reroute-complete", "complete", source, null))).Success);

        var confirmationRequired = await SendAsync(
            handler,
            "route.change",
            new JobRouteChangePayload(jobId, "other"));
        Assert.False(confirmationRequired.Success);
        Assert.Equal("route.confirmation-required", confirmationRequired.ErrorCode);

        var movedAgain = await SendAsync(
            handler,
            "route.change",
            new JobRouteChangePayload(jobId, "other", ConfirmCompletedMove: true));
        Assert.True(movedAgain.Success, movedAgain.Message);
        Assert.True(File.Exists(Path.Combine(destination, "other", "completed.txt")));
        Assert.Equal("completed route", await File.ReadAllTextAsync(Path.Combine(destination, "other", "completed.txt"), CancellationToken.None));
    }

    [Fact]
    public async Task RuleEditAndSoftDeletePreserveIdentityCreationTimeAndHistory()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        var rule = await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "rule-history", "history.bin");
        var attemptedCreatedAt = rule.CreatedAt.AddDays(10);

        var edited = await SendAsync(
            handler,
            "rules.upsert",
            rule with { Name = "edited rule", IsEnabled = false, CreatedAt = attemptedCreatedAt });
        Assert.True(edited.Success, edited.Message);
        var restored = await repository.GetRuleAsync(rule.Id, CancellationToken.None);
        Assert.Equal(rule.Id, restored!.Id);
        Assert.Equal(rule.CreatedAt, restored.CreatedAt);
        Assert.Equal("edited rule", restored.Name);
        Assert.False(restored.IsEnabled);

        var deleted = await SendAsync(handler, "rules.delete", new RuleDeletePayload(rule.Id));
        Assert.True(deleted.Success, deleted.Message);
        Assert.Empty(await repository.GetRulesAsync(CancellationToken.None));
        Assert.NotNull(await repository.GetJobAsync(jobId, CancellationToken.None));
    }

    [Fact]
    public async Task CancelledJobCannotChangeRouteRegardlessOfPreviousSelection()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        Directory.CreateDirectory(Path.Combine(destination, "A"));
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "cancel-route", "cancel.bin");
        Assert.True((await SendAsync(
            handler,
            "selection.complete",
            new SelectionCompletedPayload([jobId], "A"))).Success);
        Assert.True((await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "cancel-route", "cancelled", null, "USER_CANCELED"))).Success);

        var response = await SendAsync(
            handler,
            "route.change",
            new JobRouteChangePayload(jobId, string.Empty));

        Assert.False(response.Success);
        Assert.Equal("route.change-not-allowed", response.ErrorCode);
        var cancelled = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.Cancelled, cancelled!.BrowserState);
        Assert.Equal(RoutingState.NotRequired, cancelled.RoutingState);
    }

    [Fact]
    public async Task SkippingSelectionKeepsTheCompletedFileInItsBrowserLocation()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "skip-route", "keep-here.txt");

        var skipped = await SendAsync(handler, "selection.skip", new SelectionSkippedPayload(jobId));
        Assert.True(skipped.Success, skipped.Message);

        var source = Path.Combine(incoming, "keep-here.txt");
        await File.WriteAllTextAsync(source, "do not move", CancellationToken.None);
        var completed = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "skip-route", "complete", source, null, "keep-here.txt"));

        Assert.True(completed.Success, completed.Message);
        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.Complete, job!.BrowserState);
        Assert.Equal(RoutingState.Skipped, job.RoutingState);
        Assert.Null(job.FinalPath);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(destination));
    }

    [Fact]
    public async Task BrowserStartupHistoryReplayCreatesNoJobsEvenWhenTheDatabaseIsEmpty()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);

        // Chromium replays onCreated for the whole download history at browser startup.
        for (var downloadId = 488; downloadId <= 516; downloadId++)
        {
            var response = await SendAsync(
                handler,
                "download.started",
                new DownloadStartedPayload(
                    "Whale",
                    downloadId.ToString(),
                    "history.bin",
                    null,
                    "https://example.com/downloads",
                    "https://example.com/history.bin",
                    null,
                    null,
                    State: "complete",
                    StartedAt: yesterday,
                    ExtensionBuild: SupportedBuild));

            Assert.True(response.Success, response.Message);
            Assert.False(response.Data!.Value.GetProperty("tracked").GetBoolean());
            Assert.Equal("already-finished", response.Data.Value.GetProperty("ignored").GetString());
        }

        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task AnUnfinishedHistoryRecordRestoredAtStartupIsNotRegistered()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);

        var response = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale", "900", "resumable.bin", null, "https://example.com/downloads",
                "https://example.com/resumable.bin", null, null,
                State: "in_progress",
                StartedAt: DateTimeOffset.UtcNow.AddDays(-1), ExtensionBuild: SupportedBuild));

        Assert.True(response.Success, response.Message);
        Assert.False(response.Data!.Value.GetProperty("tracked").GetBoolean());
        Assert.Equal("started-before-session", response.Data.Value.GetProperty("ignored").GetString());
        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task ALiveDownloadIsStillRegisteredAndPromptsExactlyOnce()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);

        var response = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale", "1001", "live.bin", null, "https://example.com/downloads",
                "https://example.com/live.bin", null, null,
                State: "in_progress",
                StartedAt: DateTimeOffset.UtcNow, ExtensionBuild: SupportedBuild));

        Assert.True(response.Success, response.Message);
        Assert.True(response.Data!.Value.GetProperty("tracked").GetBoolean());
        var jobId = response.Data.Value.GetProperty("jobId").GetGuid();

        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(SelectionPromptState.NeverShown, job!.SelectionPromptState);
        Assert.Single(DownloadJobQueries.AutomaticSelections([job]));

        Assert.True((await SendAsync(
            handler,
            "selection.prompt-state",
            new SelectionPromptStatePayload([jobId], SelectionPromptState.Shown))).Success);

        var afterPrompt = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Empty(DownloadJobQueries.AutomaticSelections([afterPrompt!]));
    }

    [Fact]
    public async Task RepeatedBrowserRestartsNeverGrowTheJobTable()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);

        // One genuine live download exists before the restarts.
        var liveJobId = await StartLiveAsync(handler, "2001", "live.bin");
        Assert.True((await SendAsync(
            handler,
            "selection.prompt-state",
            new SelectionPromptStatePayload([liveJobId], SelectionPromptState.Deferred))).Success);

        for (var restart = 0; restart < 3; restart++)
        {
            // Every restart replays the history, including the live download's own record.
            for (var downloadId = 2001; downloadId <= 2010; downloadId++)
            {
                await SendAsync(
                    handler,
                    "download.started",
                    new DownloadStartedPayload(
                        "Whale", downloadId.ToString(), "history.bin", null,
                        "https://example.com/downloads", "https://example.com/history.bin", null, null,
                        State: "complete",
                        StartedAt: DateTimeOffset.UtcNow.AddDays(-1), ExtensionBuild: SupportedBuild));
            }

            // Reconciliation of the tracked download must only refresh state.
            await SendAsync(
                handler,
                "download.changed",
                new DownloadChangedPayload(
                    "Whale", "2001", "in_progress", null, null, IsReconciliation: true));
        }

        var jobs = await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None);
        Assert.Single(jobs);
        Assert.Equal(liveJobId, jobs[0].Id);
        Assert.Equal(SelectionPromptState.Deferred, jobs[0].SelectionPromptState);
        Assert.Empty(DownloadJobQueries.AutomaticSelections(jobs));
        Assert.Single(DownloadJobQueries.ActiveSelections(jobs));
    }

    [Fact]
    public async Task ReplayingTheSameLiveDownloadUpdatesInsteadOfInsertingADuplicate()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartLiveAsync(handler, "3001", "live.bin");

        for (var repeat = 0; repeat < 5; repeat++)
        {
            var response = await SendAsync(
                handler,
                "download.started",
                new DownloadStartedPayload(
                    "Whale", "3001", "live.bin", null, "https://example.com/downloads",
                    "https://example.com/live.bin", null, null,
                    State: "in_progress",
                    StartedAt: DateTimeOffset.UtcNow, ExtensionBuild: SupportedBuild));
            Assert.True(response.Success, response.Message);
            Assert.True(response.Data!.Value.GetProperty("existing").GetBoolean());
            Assert.Equal(jobId, response.Data.Value.GetProperty("jobId").GetGuid());
        }

        Assert.Single(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task DeletingHistoryDoesNotLetTheBrowserReimportThoseDownloads()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartLiveAsync(handler, "4001", "live.bin");

        Assert.Equal(1, await repository.DeleteJobsAsync([jobId], CancellationToken.None));
        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));

        // The browser still has the record and replays it on the next startup.
        var replay = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale", "4001", "live.bin", null, "https://example.com/downloads",
                "https://example.com/live.bin", null, null,
                State: "complete",
                StartedAt: DateTimeOffset.UtcNow.AddHours(-2), ExtensionBuild: SupportedBuild));

        Assert.True(replay.Success, replay.Message);
        Assert.False(replay.Data!.Value.GetProperty("tracked").GetBoolean());
        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task ACachedOlderExtensionCannotRegisterAnythingAndAsksForARefresh()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);

        // The browser kept serving a cached older service worker after the upgrade, so the
        // payload carries no build identifier and no transfer state. This is exactly the
        // shape that re-registered the whole download history before.
        foreach (var downloadId in new[] { "5001", "5002", "5003" })
        {
            var response = await SendAsync(
                handler,
                "download.started",
                new DownloadStartedPayload(
                    "Whale", downloadId, "legacy.bin", null, "https://example.com/downloads",
                    "https://example.com/legacy.bin", null, null));

            Assert.True(response.Success, response.Message);
            Assert.False(response.Data!.Value.GetProperty("tracked").GetBoolean());
            // The browser download itself must keep working.
            Assert.True(response.Data.Value.GetProperty("failOpen").GetBoolean());
            Assert.Equal("unsupported-extension-build", response.Data.Value.GetProperty("ignored").GetString());
            Assert.True(response.Data.Value.GetProperty("extensionRefreshRequired").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(response.Data.Value.GetProperty("message").GetString()));
        }

        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));

        // The counter is process-wide, so only its growth is meaningful here.
        var status = await SendAsync(handler, "diagnostics.status", new { });
        Assert.True(status.Data!.Value.GetProperty("unsupportedExtensionRejections").GetInt32() >= 3);
        Assert.Contains(
            SupportedBuild,
            status.Data.Value.GetProperty("supportedExtensionBuilds").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task TheStartupHandshakeAcceptsASupportedBuild()
    {
        var (handler, _) = await CreateHandlerAsync();

        var hello = await SendAsync(
            handler,
            "extension.hello",
            new ExtensionHelloPayload("Whale", SupportedBuild));

        Assert.True(hello.Success, hello.Message);
        Assert.True(hello.Data!.Value.GetProperty("supported").GetBoolean());
        Assert.Equal(JsonValueKind.Null, hello.Data.Value.GetProperty("message").ValueKind);

        var status = await SendAsync(handler, "diagnostics.status", new { });
        Assert.True(status.Data!.Value.GetProperty("extensionBuildSupported").GetBoolean());
        Assert.Equal(SupportedBuild, status.Data.Value.GetProperty("lastExtensionBuild").GetString());
    }

    [Fact]
    public async Task TheStartupHandshakeFlagsAStaleBuildBeforeAnyDownloadHappens()
    {
        var (handler, repository) = await CreateHandlerAsync();

        var hello = await SendAsync(
            handler,
            "extension.hello",
            new ExtensionHelloPayload("Whale", "1999.01.01"));

        Assert.True(hello.Success, hello.Message);
        Assert.False(hello.Data!.Value.GetProperty("supported").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(hello.Data.Value.GetProperty("message").GetString()));

        var status = await SendAsync(handler, "diagnostics.status", new { });
        Assert.False(status.Data!.Value.GetProperty("extensionBuildSupported").GetBoolean());
        Assert.True(status.Data.Value.GetProperty("extensionRefreshRequired").GetBoolean());
        // The handshake alone must never create or change any job.
        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task AMissingBuildInTheHandshakeIsTreatedAsStale()
    {
        var (handler, _) = await CreateHandlerAsync();

        var hello = await SendAsync(
            handler,
            "extension.hello",
            new ExtensionHelloPayload("Whale", null));

        Assert.True(hello.Success, hello.Message);
        Assert.False(hello.Data!.Value.GetProperty("supported").GetBoolean());
    }

    [Fact]
    public async Task AnUnknownExtensionBuildCannotRegisterEvenWhenItClaimsALiveTransfer()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);

        var response = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale", "6001", "future.bin", null, "https://example.com/downloads",
                "https://example.com/future.bin", null, null,
                State: "in_progress",
                StartedAt: DateTimeOffset.UtcNow,
                ExtensionBuild: "1999.01.01"));

        Assert.True(response.Success, response.Message);
        Assert.False(response.Data!.Value.GetProperty("tracked").GetBoolean());
        Assert.Equal("unsupported-extension-build", response.Data.Value.GetProperty("ignored").GetString());
        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task ACachedOlderExtensionCanStillUpdateAnExistingJob()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var destination = Directory.CreateDirectory(Path.Combine(root, "routed")).FullName;
        await CreateRuleAsync(repository, destination, StorageMode.SelectSubfolder);
        var jobId = await StartAsync(handler, "7001", "live.bin");

        // Same download, replayed by a stale extension: it must update, never duplicate.
        var replay = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale", "7001", "live.bin", null, "https://example.com/downloads",
                "https://example.com/live.bin", null, null));

        Assert.True(replay.Success, replay.Message);
        Assert.True(replay.Data!.Value.GetProperty("existing").GetBoolean());
        Assert.Equal(jobId, replay.Data.Value.GetProperty("jobId").GetGuid());

        // Terminal browser events from the same stale extension still reach the job.
        var completed = await SendAsync(
            handler,
            "download.changed",
            new DownloadChangedPayload("Whale", "7001", "complete", Path.Combine(root, "live.bin"), null));
        Assert.True(completed.Success, completed.Message);
        Assert.Single(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task ParentDestinationAndRenameSurviveRestartAndBrowserCompletion()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var configured = Directory.CreateDirectory(Path.Combine(root, "parent", "configured")).FullName;
        var parent = Directory.GetParent(configured)!.FullName;
        await CreateRuleAsync(repository, configured, StorageMode.SelectSubfolder);
        var id = await StartAsync(handler, "rename-parent", "browser.txt");
        var selected = await SendAsync(handler, "selection.complete",
            new SelectionCompletedPayload([id], string.Empty, parent, "사용자 이름.txt"));
        Assert.True(selected.Success, selected.Message);
        var (restarted, reloaded) = await CreateHandlerAsync();
        var saved = (await reloaded.GetJobAsync(id, CancellationToken.None))!;
        Assert.Equal(parent, saved.SelectedDestinationFolder);
        Assert.Equal("사용자 이름.txt", saved.SelectedFileName);
        Assert.Equal(RoutingState.SelectionReady, saved.RoutingState);
        var source = Path.Combine(root, "browser-final.txt");
        await File.WriteAllTextAsync(source, "renamed content", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(parent, "사용자 이름.txt"), "existing", CancellationToken.None);
        var complete = await SendAsync(restarted, "download.changed",
            new DownloadChangedPayload("Whale", "rename-parent", "complete", source, null));
        Assert.True(complete.Success, complete.Message);
        saved = (await reloaded.GetJobAsync(id, CancellationToken.None))!;
        Assert.Equal(RoutingState.Completed, saved.RoutingState);
        Assert.Equal(Path.Combine(parent, "사용자 이름 (1).txt"), saved.FinalPath);
        Assert.Equal("사용자 이름 (1).txt", DownloadPresentation.DisplayFileName(saved));
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(parent, "사용자 이름.txt"), CancellationToken.None));
        Assert.Equal("renamed content", await File.ReadAllTextAsync(saved.FinalPath!, CancellationToken.None));
        Assert.False(File.Exists(source));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("CON.txt")]
    [InlineData("bad:name.txt")]
    [InlineData("name.")]
    [InlineData(" ")]
    public async Task InvalidRenameDoesNotChangePendingJob(string name)
    {
        var (handler, repository) = await CreateHandlerAsync();
        var folder = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
        await CreateRuleAsync(repository, folder, StorageMode.SelectSubfolder);
        var id = await StartAsync(handler, "invalid-name", "original.txt");
        var response = await SendAsync(handler, "selection.complete",
            new SelectionCompletedPayload([id], string.Empty, folder, name));
        Assert.False(response.Success);
        var job = (await repository.GetJobAsync(id, CancellationToken.None))!;
        Assert.Equal(RoutingState.WaitingForSelection, job.RoutingState);
        Assert.Null(job.SelectedFileName);
        Assert.Null(job.SelectedDestinationFolder);
    }

    [Fact]
    public async Task RemovedSelectedFolderFailsSafelyWithoutLosingOriginal()
    {
        var (handler, repository) = await CreateHandlerAsync();
        var configured = Directory.CreateDirectory(Path.Combine(root, "configured")).FullName;
        var selected = Directory.CreateDirectory(Path.Combine(root, "selected")).FullName;
        await CreateRuleAsync(repository, configured, StorageMode.SelectSubfolder);
        var id = await StartAsync(handler, "removed-folder", "original.txt");
        Assert.True((await SendAsync(handler, "selection.complete",
            new SelectionCompletedPayload([id], string.Empty, selected, "renamed.txt"))).Success);
        Directory.Delete(selected);
        var source = Path.Combine(root, "original.txt");
        await File.WriteAllTextAsync(source, "preserve", CancellationToken.None);
        await SendAsync(handler, "download.changed",
            new DownloadChangedPayload("Whale", "removed-folder", "complete", source, null));
        var job = (await repository.GetJobAsync(id, CancellationToken.None))!;
        Assert.NotEqual(RoutingState.Moving, job.RoutingState);
        Assert.NotEqual(RoutingState.Completed, job.RoutingState);
        Assert.True(File.Exists(source));
    }

    private static async Task<Guid> StartLiveAsync(
        AgentCommandHandler handler,
        string downloadId,
        string fileName)
    {
        var response = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale", downloadId, fileName, null, "https://example.com/downloads",
                $"https://example.com/{fileName}", null, null,
                State: "in_progress",
                StartedAt: DateTimeOffset.UtcNow, ExtensionBuild: SupportedBuild));
        Assert.True(response.Success, response.Message);
        return response.Data!.Value.GetProperty("jobId").GetGuid();
    }

    private static async Task<DownloadRule> CreateRuleAsync(
        DownloadRouterRepository repository,
        string destination,
        StorageMode storageMode)
    {
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(), "selection rule", true, RuleMatchType.DomainAndSubdomains, "example.com",
            RuleMatchTarget.InitiatingPage, destination, storageMode, 0, 0, now, now);
        await repository.UpsertRuleAsync(rule, CancellationToken.None);
        return rule;
    }

    private static async Task<Guid> StartAsync(
        AgentCommandHandler handler,
        string downloadId,
        string fileName)
    {
        var response = await SendAsync(
            handler,
            "download.started",
            new DownloadStartedPayload(
                "Whale", downloadId, fileName, null, "https://example.com/downloads",
                $"https://example.com/{fileName}", null, null,
                State: "in_progress",
                StartedAt: DateTimeOffset.UtcNow,
                ExtensionBuild: SupportedBuild));
        Assert.True(response.Success, response.Message);
        return response.Data!.Value.GetProperty("jobId").GetGuid();
    }

    private static Task<AgentResponse> SendAsync(
        AgentCommandHandler handler,
        string command,
        object payload)
        => handler.HandleAsync(
            new AgentCommand(
                ProtocolConstants.CurrentVersion,
                Guid.NewGuid(),
                command,
                JsonSerializer.SerializeToElement(payload, ProtocolJson.Options)),
            CancellationToken.None);

    private async Task<(AgentCommandHandler Handler, DownloadRouterRepository Repository)> CreateHandlerAsync()
    {
        var paths = AppPaths.CreateDefault();
        var repository = new DownloadRouterRepository(paths);
        await repository.InitializeAsync(CancellationToken.None);
        var boundary = new PathBoundaryValidator();
        var handler = new AgentCommandHandler(
            repository,
            new RuleMatcher(),
            new UrlSanitizer(),
            new PathTokenResolver(new WindowsKnownPathProvider()),
            boundary,
            new DownloadJobStateMachine(),
            new FileMoveService(boundary, NullLogger<FileMoveService>.Instance),
            new NoOpSelectionUiLauncher(),
            paths,
            NullLogger<AgentCommandHandler>.Instance);
        return (handler, repository);
    }

    private sealed class NoOpSelectionUiLauncher : ISelectionUiLauncher
    {
        public bool RequestSelectionUi() => true;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DOWNLOAD_ROUTER_DATA_DIR", previousOverride);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
