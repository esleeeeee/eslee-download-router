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
            paths,
            NullLogger<AgentCommandHandler>.Instance);
        return (handler, repository);
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
