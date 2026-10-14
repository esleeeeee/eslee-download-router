using System.Globalization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Startup;
using DownloadRouter.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace DownloadRouter.Infrastructure.Tests;

/// <summary>
/// Uses an explicit data root instead of the process-wide environment variable, so it can
/// run in parallel with the other repository tests.
/// </summary>
public sealed class SchemaVersion8Tests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "download-router-schema-v8-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths paths;
    private readonly DownloadRouterRepository repository;

    public SchemaVersion8Tests()
    {
        paths = AppPaths.For(AppInstance.Resolve(["--data-root", root], null));
        repository = new DownloadRouterRepository(paths);
    }

    [Fact]
    public async Task Version7DataMigratesWithoutLossAndTheMigrationIsIdempotent()
    {
        await repository.InitializeAsync(CancellationToken.None);
        var rule = Rule("legacy");
        await repository.UpsertRuleAsync(rule, CancellationToken.None);
        var job = Job(rule.Id, BrowserTransferState.InProgress, RoutingState.WaitingForSelection, DateTimeOffset.UtcNow);
        await repository.CreateJobAsync(job, CancellationToken.None);

        // Turn the database back into the v7 shape while keeping its rows.
        await ExecuteAsync("""
            ALTER TABLE Rules DROP COLUMN FileTypeMode;
            ALTER TABLE Rules DROP COLUMN FileExtensions;
            ALTER TABLE DownloadJobs DROP COLUMN IsRuleTentative;
            DELETE FROM MigrationHistory WHERE Version = 8;
            """);

        await repository.InitializeAsync(CancellationToken.None);
        await repository.InitializeAsync(CancellationToken.None);

        var migratedRule = Assert.Single(await repository.GetRulesAsync(CancellationToken.None));
        Assert.Equal(rule.Id, migratedRule.Id);
        Assert.Equal(rule.Name, migratedRule.Name);
        Assert.Equal(FileTypeFilterMode.AllFiles, migratedRule.FileTypeMode);
        Assert.Equal(string.Empty, migratedRule.FileExtensions);
        var migratedJob = await repository.GetJobAsync(job.Id, CancellationToken.None);
        Assert.NotNull(migratedJob);
        Assert.False(migratedJob.IsRuleTentative);
        Assert.Equal(RoutingState.WaitingForSelection, migratedJob.RoutingState);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM MigrationHistory WHERE Version = 8;"));
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM DownloadEvents WHERE JobId = '{job.Id:D}';"));
    }

    [Fact]
    public async Task Version8JobsAndEventsSurviveIdempotentVersion9Migration()
    {
        await repository.InitializeAsync();
        var rule = Rule("v8");
        await repository.UpsertRuleAsync(rule);
        var job = Job(rule.Id, BrowserTransferState.InProgress, RoutingState.WaitingForSelection, DateTimeOffset.UtcNow)
            with { IsRuleTentative = true };
        await repository.CreateJobAsync(job);
        var before = await repository.GetJobAsync(job.Id);
        await ExecuteAsync("ALTER TABLE DownloadJobs DROP COLUMN CandidateRuleIds; DELETE FROM MigrationHistory WHERE Version = 9;");

        await repository.InitializeAsync();
        await repository.InitializeAsync();

        Assert.Equal(before, await repository.GetJobAsync(job.Id));
        Assert.Equal(rule, await repository.GetRuleAsync(rule.Id));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM MigrationHistory WHERE Version = 9;"));
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM DownloadEvents WHERE JobId = '{job.Id:D}';"));
        var candidates = rule.Id.ToString("D");
        var newJob = job with { Id = Guid.NewGuid(), BrowserDownloadId = "v9", CandidateRuleIds = candidates };
        await repository.CreateJobAsync(newJob);
        await repository.InitializeAsync();
        Assert.Equal(candidates, (await repository.GetJobAsync(newJob.Id))!.CandidateRuleIds);
        await repository.UpdateJobAsync(newJob with { IsRuleTentative = false }, "confirmed");
        Assert.Equal(candidates, (await repository.GetJobAsync(newJob.Id))!.CandidateRuleIds);
    }

    [Fact]
    public async Task Version9MigrationRollsBackColumnAndVersionTogetherOnFailure()
    {
        await repository.InitializeAsync();
        var rule = Rule("rollback");
        await repository.UpsertRuleAsync(rule);
        var job = Job(rule.Id, BrowserTransferState.InProgress, RoutingState.WaitingForSelection, DateTimeOffset.UtcNow);
        await repository.CreateJobAsync(job);
        await ExecuteAsync("""
            ALTER TABLE DownloadJobs DROP COLUMN CandidateRuleIds;
            DELETE FROM MigrationHistory WHERE Version = 9;
            CREATE TRIGGER FailVersion9 BEFORE INSERT ON MigrationHistory WHEN NEW.Version = 9
            BEGIN SELECT RAISE(ABORT, 'test migration failure'); END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() => repository.InitializeAsync());
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('DownloadJobs') WHERE name = 'CandidateRuleIds';"));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM MigrationHistory WHERE Version = 9;"));
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM DownloadJobs WHERE Id = '{job.Id:D}';"));
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM DownloadEvents WHERE JobId = '{job.Id:D}';"));
        await ExecuteAsync("DROP TRIGGER FailVersion9;");
        await repository.InitializeAsync();
        Assert.NotNull(await repository.GetJobAsync(job.Id));
    }

    [Fact]
    public async Task ExpiredHistoryRechecksSelectedIdsAndNeverDeletesNewCandidates()
    {
        await repository.InitializeAsync();
        var rule = Rule("history-recheck");
        await repository.UpsertRuleAsync(rule);
        var old = DateTimeOffset.UtcNow.AddDays(-60);
        var selected = Job(rule.Id, BrowserTransferState.Complete, RoutingState.Completed, old);
        await repository.CreateJobAsync(selected);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var ids = await repository.GetExpiredHistoryIdsAsync(cutoff);
        Assert.Equal([selected.Id], ids);
        await repository.UpdateJobAsync(selected with { RoutingState = RoutingState.Moving }, "moving");
        var later = Job(rule.Id, BrowserTransferState.Complete, RoutingState.Completed, old);
        await repository.CreateJobAsync(later);

        Assert.Equal(0, await repository.DeleteExpiredHistoryAsync(cutoff, ids));
        Assert.Equal(RoutingState.Moving, (await repository.GetJobAsync(selected.Id))!.RoutingState);
        Assert.NotNull(await repository.GetJobAsync(later.Id));
        Assert.Equal(2L, await ScalarAsync($"SELECT COUNT(*) FROM DownloadEvents WHERE JobId = '{selected.Id:D}';"));
    }

    [Fact]
    public async Task FileTypeFilterAndTentativeRuleRoundTrip()
    {
        await repository.InitializeAsync(CancellationToken.None);
        var documents = Rule("documents") with { FileTypeMode = FileTypeFilterMode.OnlyListed, FileExtensions = ".pdf;.tar.gz" };
        var general = Rule("general");
        await repository.UpsertRuleAsync(documents, CancellationToken.None);
        await repository.UpsertRuleAsync(general, CancellationToken.None);
        var job = Job(documents.Id, BrowserTransferState.InProgress, RoutingState.NotRequired, DateTimeOffset.UtcNow) with
        {
            IsRuleTentative = true,
        };
        await repository.CreateJobAsync(job, CancellationToken.None);

        var restoredRule = await repository.GetRuleAsync(documents.Id, CancellationToken.None);
        Assert.Equal(FileTypeFilterMode.OnlyListed, restoredRule!.FileTypeMode);
        Assert.Equal(".pdf;.tar.gz", restoredRule.FileExtensions);
        Assert.True((await repository.GetJobAsync(job.Id, CancellationToken.None))!.IsRuleTentative);

        await repository.UpsertRuleAsync(documents with { FileTypeMode = FileTypeFilterMode.ExceptListed, FileExtensions = ".exe" }, CancellationToken.None);
        await repository.UpdateJobAsync(job with { IsRuleTentative = false, RuleId = general.Id }, "rule.reevaluated", CancellationToken.None);

        var edited = await repository.GetRuleAsync(documents.Id, CancellationToken.None);
        Assert.Equal(FileTypeFilterMode.ExceptListed, edited!.FileTypeMode);
        Assert.Equal(".exe", edited.FileExtensions);
        var updated = await repository.GetJobAsync(job.Id, CancellationToken.None);
        Assert.False(updated!.IsRuleTentative);
        Assert.Equal(general.Id, updated.RuleId);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpsertRuleAsync(
            documents with { FileTypeMode = (FileTypeFilterMode)7 }, CancellationToken.None));
    }

    [Fact]
    public async Task SettingsCanBeSetReadAndRemovedConditionally()
    {
        await repository.InitializeAsync(CancellationToken.None);

        Assert.Null(await repository.GetSettingAsync("routing-paused-until", CancellationToken.None));
        await repository.SetSettingAsync("routing-paused-until", "indefinite", CancellationToken.None);
        Assert.Equal("indefinite", await repository.GetSettingAsync("routing-paused-until", CancellationToken.None));
        Assert.False(await repository.DeleteSettingAsync("routing-paused-until", "other", CancellationToken.None));
        Assert.Equal("indefinite", await repository.GetSettingAsync("routing-paused-until", CancellationToken.None));
        Assert.True(await repository.DeleteSettingAsync("routing-paused-until", "indefinite", CancellationToken.None));
        Assert.Null(await repository.GetSettingAsync("routing-paused-until", CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredHistoryCleanupKeepsUnfinishedWorkAndDeletesEvents()
    {
        await repository.InitializeAsync(CancellationToken.None);
        var rule = Rule("history");
        await repository.UpsertRuleAsync(rule, CancellationToken.None);
        var old = DateTimeOffset.UtcNow.AddDays(-100);
        var recent = DateTimeOffset.UtcNow.AddDays(-1);
        var eligible = new[]
        {
            Job(rule.Id, BrowserTransferState.Complete, RoutingState.Completed, old),
            Job(rule.Id, BrowserTransferState.Complete, RoutingState.Skipped, old),
            Job(rule.Id, BrowserTransferState.Cancelled, RoutingState.NotRequired, old),
            Job(rule.Id, BrowserTransferState.Interrupted, RoutingState.Failed, old),
        };
        var kept = new[]
        {
            Job(rule.Id, BrowserTransferState.Complete, RoutingState.Completed, old) with { CompletedAt = recent },
            Job(rule.Id, BrowserTransferState.Complete, RoutingState.Failed, old),
            Job(rule.Id, BrowserTransferState.Complete, RoutingState.RetryPending, old),
            Job(rule.Id, BrowserTransferState.Complete, RoutingState.Moving, old),
            Job(rule.Id, BrowserTransferState.Complete, RoutingState.WaitingForSelection, old),
            Job(rule.Id, BrowserTransferState.InProgress, RoutingState.SelectionReady, old),
            Job(rule.Id, BrowserTransferState.InProgress, RoutingState.NotRequired, old),
            Job(rule.Id, BrowserTransferState.InProgress, RoutingState.Skipped, old),
        };
        foreach (var job in eligible.Concat(kept))
        {
            await repository.CreateJobAsync(job, CancellationToken.None);
        }

        var deleted = await repository.DeleteExpiredHistoryAsync(DateTimeOffset.UtcNow.AddDays(-30), CancellationToken.None);

        Assert.Equal(eligible.Length, deleted);
        foreach (var job in eligible)
        {
            Assert.Null(await repository.GetJobAsync(job.Id, CancellationToken.None));
            Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM DownloadEvents WHERE JobId = '{job.Id:D}';"));
        }

        foreach (var job in kept)
        {
            Assert.NotNull(await repository.GetJobAsync(job.Id, CancellationToken.None));
        }

        Assert.Equal(0, await repository.DeleteExpiredHistoryAsync(DateTimeOffset.UtcNow.AddDays(-30), CancellationToken.None));
    }

    private static DownloadRule Rule(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new DownloadRule(
            Guid.NewGuid(), name, true, RuleMatchType.DomainAndSubdomains, "example.com",
            RuleMatchTarget.InitiatingPage, "{Downloads}", StorageMode.Automatic, 0, 0, now, now);
    }

    private static DownloadJob Job(Guid ruleId, BrowserTransferState browser, RoutingState routing, DateTimeOffset completedAt)
        => new(
            Guid.NewGuid(), BrowserKind.Edge, Guid.NewGuid().ToString("N"), "a.pdf", "a.pdf", null, null, null, null, null,
            ruleId, null, null, null, browser, routing, null, null, completedAt.AddMinutes(-1), completedAt,
            BrowserStartedAt: completedAt.AddMinutes(-1));

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
