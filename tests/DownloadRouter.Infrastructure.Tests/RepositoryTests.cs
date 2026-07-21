using DownloadRouter.Core.Models;
using DownloadRouter.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace DownloadRouter.Infrastructure.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "download-router-repository-tests", Guid.NewGuid().ToString("N"));
    private readonly string? previousOverride;

    public RepositoryTests()
    {
        previousOverride = Environment.GetEnvironmentVariable("DOWNLOAD_ROUTER_DATA_DIR");
        Environment.SetEnvironmentVariable("DOWNLOAD_ROUTER_DATA_DIR", root);
    }

    [Fact]
    public async Task MigrationCreatesDatabaseAndRuleRoundTrips()
    {
        var paths = AppPaths.CreateDefault();
        var repository = new DownloadRouterRepository(paths);
        await repository.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(), "네이버", true, RuleMatchType.DomainAndSubdomains, "naver.com",
            RuleMatchTarget.InitiatingPage, "{Downloads}\\naver", StorageMode.Automatic,
            10, 1, now, now);

        await repository.UpsertRuleAsync(rule, CancellationToken.None);
        var restored = Assert.Single(await repository.GetRulesAsync(CancellationToken.None));

        Assert.Equal(rule.Id, restored.Id);
        Assert.Equal(rule.MatchValue, restored.MatchValue);
        Assert.True(File.Exists(paths.DatabasePath));
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
