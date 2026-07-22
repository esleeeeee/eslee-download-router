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
                    null),
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
            "download.changed",
            new DownloadChangedPayload("Whale", "cancel-1", "cancelled", source, "USER_CANCELED"));

        Assert.True(response.Success, response.Message);
        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.Equal(BrowserTransferState.Cancelled, job!.BrowserState);
        Assert.Equal(RoutingState.NotRequired, job.RoutingState);
        Assert.Equal("download.cancelled", job.ErrorCode);
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

    private static async Task CreateRuleAsync(
        DownloadRouterRepository repository,
        string destination,
        StorageMode storageMode)
    {
        var now = DateTimeOffset.UtcNow;
        await repository.UpsertRuleAsync(
            new DownloadRule(
                Guid.NewGuid(), "selection rule", true, RuleMatchType.DomainAndSubdomains, "example.com",
                RuleMatchTarget.InitiatingPage, destination, storageMode, 0, 0, now, now),
            CancellationToken.None);
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
                $"https://example.com/{fileName}", null, null));
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
