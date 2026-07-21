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
        Assert.Empty(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
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
