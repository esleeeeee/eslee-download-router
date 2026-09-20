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

    [Fact]
    public async Task VersionOneJobsMigrateToSeparateBrowserAndRoutingStates()
    {
        var paths = AppPaths.CreateDefault();
        paths.EnsureCreated();
        var ruleId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE MigrationHistory (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);
                INSERT INTO MigrationHistory(Version, AppliedAt) VALUES (1, '2026-01-01T00:00:00.0000000+00:00');
                CREATE TABLE Rules (
                    Id TEXT PRIMARY KEY, Name TEXT NOT NULL, IsEnabled INTEGER NOT NULL,
                    MatchType TEXT NOT NULL, MatchValue TEXT NOT NULL, MatchTarget TEXT NOT NULL,
                    StorageRoot TEXT NOT NULL, StorageMode TEXT NOT NULL, Priority INTEGER NOT NULL,
                    ListOrder INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                CREATE TABLE DownloadJobs (
                    Id TEXT PRIMARY KEY, Browser TEXT NOT NULL, BrowserDownloadId TEXT NOT NULL,
                    OriginalFileName TEXT NOT NULL, CurrentFileName TEXT NOT NULL,
                    InitiatingPageUrl TEXT NULL, InitialUrl TEXT NULL, FinalUrl TEXT NULL,
                    ReferrerUrl TEXT NULL, SanitizedSource TEXT NULL, RuleId TEXT NOT NULL,
                    OriginalPath TEXT NULL, FinalPath TEXT NULL, SelectedRelativeFolder TEXT NULL,
                    Status TEXT NOT NULL, ErrorCode TEXT NULL, ErrorMessage TEXT NULL,
                    CreatedAt TEXT NOT NULL, CompletedAt TEXT NULL,
                    FOREIGN KEY(RuleId) REFERENCES Rules(Id), UNIQUE(Browser, BrowserDownloadId));
                INSERT INTO Rules VALUES(
                    $ruleId, 'legacy rule', 1, 'DomainAndSubdomains', 'example.com', 'InitiatingPage',
                    $root, 'SelectSubfolder', 0, 0, $created, $created);
                INSERT INTO DownloadJobs VALUES(
                    $jobId, 'Whale', 'legacy-1', 'legacy.txt', 'legacy.txt',
                    NULL, NULL, NULL, NULL, 'https://example.com', $ruleId,
                    NULL, NULL, NULL, 'WaitingForSelection', NULL, NULL, $created, NULL);
                """;
            command.Parameters.AddWithValue("$ruleId", ruleId.ToString("D"));
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$root", root);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var repository = new DownloadRouterRepository(paths);
        await repository.InitializeAsync(CancellationToken.None);

        var migrated = await repository.GetJobAsync(jobId, CancellationToken.None);
        Assert.NotNull(migrated);
        Assert.Equal(BrowserTransferState.InProgress, migrated.BrowserState);
        Assert.Equal(RoutingState.WaitingForSelection, migrated.RoutingState);
        Assert.Null(migrated.SelectedDestinationFolder);
        Assert.Null(migrated.SelectedFileName);

        await using var verification = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await verification.OpenAsync(CancellationToken.None);
        await using var versionCommand = verification.CreateCommand();
        versionCommand.CommandText = "SELECT COUNT(*) FROM MigrationHistory WHERE Version = 2;";
        Assert.Equal(1L, (long)(await versionCommand.ExecuteScalarAsync(CancellationToken.None))!);
        versionCommand.CommandText = "SELECT COUNT(*) FROM MigrationHistory WHERE Version = 3;";
        Assert.Equal(1L, (long)(await versionCommand.ExecuteScalarAsync(CancellationToken.None))!);
        versionCommand.CommandText = "SELECT COUNT(*) FROM MigrationHistory WHERE Version = 4;";
        Assert.Equal(1L, (long)(await versionCommand.ExecuteScalarAsync(CancellationToken.None))!);
        versionCommand.CommandText = "SELECT COUNT(*) FROM MigrationHistory WHERE Version = 5;";
        Assert.Equal(1L, (long)(await versionCommand.ExecuteScalarAsync(CancellationToken.None))!);
        versionCommand.CommandText = "SELECT COUNT(*) FROM MigrationHistory WHERE Version = 6;";
        Assert.Equal(1L, (long)(await versionCommand.ExecuteScalarAsync(CancellationToken.None))!);
        Assert.NotNull(migrated.LastBrowserEventAt);
        Assert.False(migrated.IsBrowserRecordStale);
        // An existing waiting job must never replay its automatic prompt after upgrading.
        Assert.Equal(SelectionPromptState.Deferred, migrated.SelectionPromptState);
    }

    [Fact]
    public async Task SelectionPromptMigrationClassifiesExistingJobsAndIsIdempotent()
    {
        var paths = AppPaths.CreateDefault();
        var repository = new DownloadRouterRepository(paths);
        await repository.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(), "prompt migration", true, RuleMatchType.ExactHost, "example.com",
            RuleMatchTarget.FileUrl, root, StorageMode.SelectSubfolder, 0, 0, now, now);
        await repository.UpsertRuleAsync(rule, CancellationToken.None);

        var waiting = new DownloadJob(
            Guid.NewGuid(), BrowserKind.Whale, "migrate-waiting", "waiting.bin", "waiting.bin",
            null, null, null, null, "https://example.com", rule.Id, null, null, null,
            BrowserTransferState.Complete, RoutingState.WaitingForSelection,
            null, null, now, null);
        var completed = waiting with
        {
            Id = Guid.NewGuid(),
            BrowserDownloadId = "migrate-completed",
            RoutingState = RoutingState.Completed,
        };
        var cancelled = waiting with
        {
            Id = Guid.NewGuid(),
            BrowserDownloadId = "migrate-cancelled",
            BrowserState = BrowserTransferState.Cancelled,
            RoutingState = RoutingState.NotRequired,
        };
        await repository.CreateJobAsync(waiting, CancellationToken.None);
        await repository.CreateJobAsync(completed, CancellationToken.None);
        await repository.CreateJobAsync(cancelled, CancellationToken.None);

        // Simulate a database written before v5 by clearing the marker and the column value.
        await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var reset = connection.CreateCommand();
            reset.CommandText = """
                DELETE FROM MigrationHistory WHERE Version = 5;
                UPDATE DownloadJobs SET SelectionPromptState = 'NeverShown';
                """;
            await reset.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await repository.InitializeAsync(CancellationToken.None);

        Assert.Equal(
            SelectionPromptState.Deferred,
            (await repository.GetJobAsync(waiting.Id, CancellationToken.None))!.SelectionPromptState);
        Assert.Equal(
            SelectionPromptState.Resolved,
            (await repository.GetJobAsync(completed.Id, CancellationToken.None))!.SelectionPromptState);
        Assert.Equal(
            SelectionPromptState.Resolved,
            (await repository.GetJobAsync(cancelled.Id, CancellationToken.None))!.SelectionPromptState);

        // Re-running the migration must not reclassify a decision made after the upgrade.
        await repository.AdvanceSelectionPromptStateAsync(
            [waiting.Id],
            SelectionPromptState.Resolved,
            CancellationToken.None);
        await repository.InitializeAsync(CancellationToken.None);

        Assert.Equal(
            SelectionPromptState.Resolved,
            (await repository.GetJobAsync(waiting.Id, CancellationToken.None))!.SelectionPromptState);
        Assert.NotNull(await repository.GetJobAsync(completed.Id, CancellationToken.None));
        Assert.Equal(3, (await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None)).Count);
    }

    [Fact]
    public async Task AdvanceSelectionPromptStateOnlyMovesForward()
    {
        var paths = AppPaths.CreateDefault();
        var repository = new DownloadRouterRepository(paths);
        await repository.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(), "prompt order", true, RuleMatchType.ExactHost, "example.com",
            RuleMatchTarget.FileUrl, root, StorageMode.SelectSubfolder, 0, 0, now, now);
        await repository.UpsertRuleAsync(rule, CancellationToken.None);
        var job = new DownloadJob(
            Guid.NewGuid(), BrowserKind.Whale, "prompt-order", "order.bin", "order.bin",
            null, null, null, null, "https://example.com", rule.Id, null, null, null,
            BrowserTransferState.Complete, RoutingState.WaitingForSelection,
            null, null, now, null);
        await repository.CreateJobAsync(job, CancellationToken.None);

        Assert.Single(await repository.AdvanceSelectionPromptStateAsync(
            [job.Id], SelectionPromptState.Shown, CancellationToken.None));
        Assert.Single(await repository.AdvanceSelectionPromptStateAsync(
            [job.Id], SelectionPromptState.Deferred, CancellationToken.None));
        Assert.Empty(await repository.AdvanceSelectionPromptStateAsync(
            [job.Id], SelectionPromptState.Shown, CancellationToken.None));

        Assert.Equal(
            SelectionPromptState.Deferred,
            (await repository.GetJobAsync(job.Id, CancellationToken.None))!.SelectionPromptState);
    }

    [Fact]
    public async Task SoftDeletingRulePreservesItsJobsAndAllowsNoFurtherMatching()
    {
        var paths = AppPaths.CreateDefault();
        var repository = new DownloadRouterRepository(paths);
        await repository.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(), "preserved history rule", true, RuleMatchType.ExactHost, "example.com",
            RuleMatchTarget.FileUrl, root, StorageMode.SelectSubfolder, 0, 0, now, now);
        await repository.UpsertRuleAsync(rule, CancellationToken.None);
        var job = new DownloadJob(
            Guid.NewGuid(), BrowserKind.Whale, "soft-delete-1", "sample.bin", "sample.bin",
            null, null, null, null, "https://example.com", rule.Id, null, null, null,
            BrowserTransferState.InProgress, RoutingState.WaitingForSelection,
            null, null, now, null);
        await repository.CreateJobAsync(job, CancellationToken.None);

        Assert.True(await repository.DeleteRuleAsync(rule.Id, CancellationToken.None));

        Assert.Empty(await repository.GetRulesAsync(CancellationToken.None));
        Assert.NotNull(await repository.GetRuleAsync(rule.Id, CancellationToken.None));
        Assert.NotNull(await repository.GetJobAsync(job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SkipSelectionsRollsBackTheWholeBatchWhenOneJobIsNotTerminalOrPending()
    {
        var paths = AppPaths.CreateDefault();
        var repository = new DownloadRouterRepository(paths);
        await repository.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(), "batch skip", true, RuleMatchType.ExactHost, "example.com",
            RuleMatchTarget.FileUrl, root, StorageMode.SelectSubfolder, 0, 0, now, now);
        await repository.UpsertRuleAsync(rule, CancellationToken.None);
        var pending = new DownloadJob(
            Guid.NewGuid(), BrowserKind.Whale, "batch-pending", "pending.bin", "pending.bin",
            null, null, null, null, "https://example.com", rule.Id, null, null, null,
            BrowserTransferState.Complete, RoutingState.WaitingForSelection,
            null, null, now, null);
        var moving = pending with
        {
            Id = Guid.NewGuid(),
            BrowserDownloadId = "batch-moving",
            OriginalFileName = "moving.bin",
            CurrentFileName = "moving.bin",
            RoutingState = RoutingState.Moving,
        };
        await repository.CreateJobAsync(pending, CancellationToken.None);
        await repository.CreateJobAsync(moving, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SkipSelectionsAsync(
            [pending.Id, moving.Id],
            DateTimeOffset.UtcNow,
            CancellationToken.None));

        Assert.Equal(
            RoutingState.WaitingForSelection,
            (await repository.GetJobAsync(pending.Id, CancellationToken.None))!.RoutingState);
        Assert.Equal(
            RoutingState.Moving,
            (await repository.GetJobAsync(moving.Id, CancellationToken.None))!.RoutingState);
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
