using System.Globalization;
using System.Text.Json;
using DownloadRouter.Core.Models;
using Microsoft.Data.Sqlite;

namespace DownloadRouter.Infrastructure.Storage;

public sealed class DownloadRouterRepository(AppPaths paths)
{
    private const int CurrentSchemaVersion = 6;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS MigrationHistory (
                Version INTEGER PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Rules (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                MatchType TEXT NOT NULL,
                MatchValue TEXT NOT NULL,
                MatchTarget TEXT NOT NULL,
                StorageRoot TEXT NOT NULL,
                StorageMode TEXT NOT NULL,
                Priority INTEGER NOT NULL,
                ListOrder INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_Rules_EnabledOrder
                ON Rules(IsEnabled, Priority DESC, ListOrder ASC);

            CREATE TABLE IF NOT EXISTS DownloadJobs (
                Id TEXT PRIMARY KEY,
                Browser TEXT NOT NULL,
                BrowserDownloadId TEXT NOT NULL,
                OriginalFileName TEXT NOT NULL,
                CurrentFileName TEXT NOT NULL,
                InitiatingPageUrl TEXT NULL,
                InitialUrl TEXT NULL,
                FinalUrl TEXT NULL,
                ReferrerUrl TEXT NULL,
                SanitizedSource TEXT NULL,
                RuleId TEXT NOT NULL,
                OriginalPath TEXT NULL,
                FinalPath TEXT NULL,
                SelectedRelativeFolder TEXT NULL,
                Status TEXT NOT NULL,
                BrowserState TEXT NOT NULL DEFAULT 'InProgress',
                RoutingState TEXT NOT NULL DEFAULT 'NotRequired',
                ErrorCode TEXT NULL,
                ErrorMessage TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                LastBrowserEventAt TEXT NULL,
                IsBrowserRecordStale INTEGER NOT NULL DEFAULT 0,
                SelectionPromptState TEXT NOT NULL DEFAULT 'NeverShown',
                FOREIGN KEY(RuleId) REFERENCES Rules(Id),
                UNIQUE(Browser, BrowserDownloadId)
            );

            CREATE INDEX IF NOT EXISTS IX_DownloadJobs_StatusCreated
                ON DownloadJobs(Status, CreatedAt DESC);

            CREATE TABLE IF NOT EXISTS DownloadEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Detail TEXT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY(JobId) REFERENCES DownloadJobs(Id)
            );

            CREATE TABLE IF NOT EXISTS BrowserConnections (
                Browser TEXT PRIMARY KEY,
                IsInstalled INTEGER NOT NULL DEFAULT 0,
                IsExtensionConnected INTEGER NOT NULL DEFAULT 0,
                LastSeenAt TEXT NULL,
                LastError TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PendingSelections (
                RuleId TEXT PRIMARY KEY,
                RequestedAt TEXT NOT NULL,
                FOREIGN KEY(RuleId) REFERENCES Rules(Id)
            );

            INSERT OR IGNORE INTO MigrationHistory(Version, AppliedAt)
                VALUES (1, $appliedAt);
            """;
        command.Parameters.AddWithValue("$appliedAt", Format(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await MigrateToCurrentVersionAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DownloadRule>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, IsEnabled, MatchType, MatchValue, MatchTarget,
                   StorageRoot, StorageMode, Priority, ListOrder, CreatedAt, UpdatedAt
            FROM Rules
            WHERE IsDeleted = 0
            ORDER BY ListOrder, CreatedAt, Id;
            """;

        var result = new List<DownloadRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadRule(reader));
        }

        return result;
    }

    public async Task<DownloadRule?> GetRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, IsEnabled, MatchType, MatchValue, MatchTarget,
                   StorageRoot, StorageMode, Priority, ListOrder, CreatedAt, UpdatedAt
            FROM Rules WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRule(reader) : null;
    }

    public async Task UpsertRuleAsync(DownloadRule rule, CancellationToken cancellationToken = default)
    {
        ValidateRule(rule);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Rules(
                Id, Name, IsEnabled, MatchType, MatchValue, MatchTarget, StorageRoot,
                StorageMode, Priority, ListOrder, CreatedAt, UpdatedAt, IsDeleted)
            VALUES(
                $id, $name, $enabled, $matchType, $matchValue, $matchTarget, $storageRoot,
                $storageMode, $priority, $listOrder, $createdAt, $updatedAt, 0)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                IsEnabled = excluded.IsEnabled,
                MatchType = excluded.MatchType,
                MatchValue = excluded.MatchValue,
                MatchTarget = excluded.MatchTarget,
                StorageRoot = excluded.StorageRoot,
                StorageMode = excluded.StorageMode,
                Priority = excluded.Priority,
                ListOrder = excluded.ListOrder,
                UpdatedAt = excluded.UpdatedAt,
                IsDeleted = 0;
            """;
        AddRuleParameters(command, rule);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        if (ruleId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty rule ID is required.", nameof(ruleId));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Rules
            SET IsDeleted = 1,
                IsEnabled = 0,
                UpdatedAt = $updatedAt
            WHERE Id = $id AND IsDeleted = 0;
            """;
        command.Parameters.AddWithValue("$id", ruleId.ToString("D"));
        command.Parameters.AddWithValue("$updatedAt", Format(DateTimeOffset.UtcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task CreateJobAsync(DownloadJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DownloadJobs(
                Id, Browser, BrowserDownloadId, OriginalFileName, CurrentFileName,
                InitiatingPageUrl, InitialUrl, FinalUrl, ReferrerUrl, SanitizedSource,
                RuleId, OriginalPath, FinalPath, SelectedRelativeFolder, Status,
                BrowserState, RoutingState, ErrorCode, ErrorMessage, CreatedAt, CompletedAt,
                LastBrowserEventAt, IsBrowserRecordStale, SelectionPromptState, SelectedDestinationFolder, SelectedFileName)
            VALUES(
                $id, $browser, $browserDownloadId, $originalFileName, $currentFileName,
                $initiatingPageUrl, $initialUrl, $finalUrl, $referrerUrl, $sanitizedSource,
                $ruleId, $originalPath, $finalPath, $selectedRelativeFolder, $status,
                $browserState, $routingState, $errorCode, $errorMessage, $createdAt, $completedAt,
                $lastBrowserEventAt, $isBrowserRecordStale, $selectionPromptState, $selectedDestinationFolder, $selectedFileName);
            """;
        AddJobParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(connection, job.Id, "job.created", job.Status.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<DownloadJob?> GetJobAsync(
        BrowserKind browser,
        string browserDownloadId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectJobColumns + " WHERE Browser = $browser AND BrowserDownloadId = $downloadId;";
        command.Parameters.AddWithValue("$browser", browser.ToString());
        command.Parameters.AddWithValue("$downloadId", browserDownloadId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadJob(reader) : null;
    }

    public async Task<DownloadJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectJobColumns + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadJob(reader) : null;
    }

    public async Task<IReadOnlyList<DownloadJob>> GetRecentJobsAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectJobColumns + " ORDER BY CreatedAt DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        var jobs = new List<DownloadJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    public async Task<TemporaryFolderChoice?> GetTemporaryFolderAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", $"temporary-folder:{ruleId:N}");
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        try { return value is null ? null : JsonSerializer.Deserialize<TemporaryFolderChoice>(value, ProtocolJson.Options); }
        catch (JsonException) { return null; }
    }

    public async Task<IReadOnlyDictionary<Guid, TemporaryFolderChoice>> GetTemporaryFoldersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM AppSettings WHERE Key LIKE 'temporary-folder:%';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var choices = new Dictionary<Guid, TemporaryFolderChoice>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Guid.TryParse(reader.GetString(0)["temporary-folder:".Length..], out var ruleId))
                continue;
            try
            {
                var choice = JsonSerializer.Deserialize<TemporaryFolderChoice>(reader.GetString(1), ProtocolJson.Options);
                if (choice is not null) choices[ruleId] = choice;
            }
            catch (JsonException) { }
        }
        return choices;
    }

    public async Task<bool> CancelTemporaryFolderAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", $"temporary-folder:{ruleId:N}");
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task UpdateJobAsync(DownloadJob job, string eventType, CancellationToken cancellationToken = default,
        TemporaryFolderChoice? temporaryFolder = null)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE DownloadJobs SET
                CurrentFileName = $currentFileName,
                InitialUrl = $initialUrl,
                FinalUrl = $finalUrl,
                ReferrerUrl = $referrerUrl,
                SanitizedSource = $sanitizedSource,
                OriginalPath = $originalPath,
                FinalPath = $finalPath,
                SelectedRelativeFolder = $selectedRelativeFolder,
                Status = $status,
                BrowserState = $browserState,
                RoutingState = $routingState,
                ErrorCode = $errorCode,
                ErrorMessage = $errorMessage,
                CompletedAt = $completedAt,
                LastBrowserEventAt = $lastBrowserEventAt,
                IsBrowserRecordStale = $isBrowserRecordStale,
                SelectionPromptState = $selectionPromptState,
                SelectedDestinationFolder = $selectedDestinationFolder,
                SelectedFileName = $selectedFileName
            WHERE Id = $id;
            """;
        AddJobParameters(command, job);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException($"Download job {job.Id} was not found.");
        }

        await AppendEventAsync(connection, job.Id, eventType, job.Status.ToString(), cancellationToken, (SqliteTransaction)transaction).ConfigureAwait(false);
        if (temporaryFolder is not null)
        {
            await using var setting = connection.CreateCommand();
            setting.Transaction = (SqliteTransaction)transaction;
            setting.CommandText = """
                INSERT INTO AppSettings(Key, Value, UpdatedAt) VALUES($key, $value, $now)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedAt = excluded.UpdatedAt;
                """;
            setting.Parameters.AddWithValue("$key", $"temporary-folder:{job.RuleId:N}");
            setting.Parameters.AddWithValue("$value", JsonSerializer.Serialize(temporaryFolder, ProtocolJson.Options));
            setting.Parameters.AddWithValue("$now", temporaryFolder.CreatedAt.ToString("O"));
            await setting.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> SkipSelectionsAsync(
        IReadOnlyCollection<Guid> jobIds,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        var ids = jobIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(jobIds), "Skip between 1 and 1000 selection jobs.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var placeholders = ids.Select((_, index) => $"$id{index}").ToArray();
        var inClause = string.Join(", ", placeholders);
        var skipped = new List<Guid>();

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"""
                UPDATE DownloadJobs
                SET Status = 'Skipped',
                    RoutingState = 'Skipped',
                    SelectionPromptState = 'Resolved',
                    ErrorCode = NULL,
                    ErrorMessage = NULL,
                    CompletedAt = COALESCE(CompletedAt, $completedAt)
                WHERE Id IN ({inClause})
                  AND BrowserState IN ('InProgress', 'Complete')
                  AND RoutingState IN ('WaitingForSelection', 'SelectionReady')
                RETURNING Id;
                """;
            AddIdParameters(update, ids);
            update.Parameters.AddWithValue("$completedAt", Format(completedAt));
            await using var reader = await update.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                skipped.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        await using (var verify = connection.CreateCommand())
        {
            verify.Transaction = transaction;
            verify.CommandText = $"""
                SELECT COUNT(*)
                FROM DownloadJobs
                WHERE Id IN ({inClause})
                  AND (
                    BrowserState IN ('Cancelled', 'Interrupted')
                    OR RoutingState IN ('Completed', 'Skipped', 'Failed')
                  );
                """;
            AddIdParameters(verify, ids);
            var terminalCount = Convert.ToInt32(
                await verify.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (terminalCount != ids.Length)
            {
                throw new InvalidOperationException(
                    "A queued selection changed state before the batch skip could be committed.");
            }
        }

        foreach (var jobId in skipped)
        {
            await AppendEventAsync(
                connection,
                jobId,
                "selection.skipped",
                DownloadJobStatus.Skipped.ToString(),
                cancellationToken,
                transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return skipped;
    }

    public async Task<int> DeleteJobsAsync(
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        var ids = jobIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(jobIds), "Delete between 1 and 1000 history items.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var placeholders = ids.Select((_, index) => $"$id{index}").ToArray();
        var inClause = string.Join(", ", placeholders);

        await using (var deleteEvents = connection.CreateCommand())
        {
            deleteEvents.Transaction = (SqliteTransaction)transaction;
            deleteEvents.CommandText = $"DELETE FROM DownloadEvents WHERE JobId IN ({inClause});";
            AddIdParameters(deleteEvents, ids);
            await deleteEvents.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        int affected;
        await using (var deleteJobs = connection.CreateCommand())
        {
            deleteJobs.Transaction = (SqliteTransaction)transaction;
            deleteJobs.CommandText = $"DELETE FROM DownloadJobs WHERE Id IN ({inClause});";
            AddIdParameters(deleteJobs, ids);
            affected = await deleteJobs.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return affected;
    }

    public async Task<IReadOnlyList<ActiveBrowserDownload>> GetActiveBrowserDownloadsAsync(
        BrowserKind browser,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BrowserDownloadId
            FROM DownloadJobs
            WHERE Browser = $browser
              AND BrowserState = 'InProgress';
            """;
        command.Parameters.AddWithValue("$browser", browser.ToString());
        var result = new List<ActiveBrowserDownload>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new ActiveBrowserDownload(Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        }

        return result;
    }

    public async Task RecoverInProgressJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DownloadJobs
            SET Status = 'RetryPending',
                RoutingState = 'RetryPending',
                ErrorCode = 'agent.restarted',
                ErrorMessage = 'The agent restarted while this file was being moved.'
            WHERE RoutingState = 'Moving';
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SelectJobColumns = """
        SELECT Id, Browser, BrowserDownloadId, OriginalFileName, CurrentFileName,
               InitiatingPageUrl, InitialUrl, FinalUrl, ReferrerUrl, SanitizedSource,
               RuleId, OriginalPath, FinalPath, SelectedRelativeFolder, Status,
               BrowserState, RoutingState, ErrorCode, ErrorMessage, CreatedAt, CompletedAt,
               LastBrowserEventAt, IsBrowserRecordStale, SelectionPromptState, SelectedDestinationFolder, SelectedFileName
        FROM DownloadJobs
        """;

    private static async Task MigrateToCurrentVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var columnCommand = connection.CreateCommand())
        {
            columnCommand.Transaction = transaction;
            columnCommand.CommandText = "PRAGMA table_info(DownloadJobs);";
            await using var reader = await columnCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("BrowserState"))
        {
            await ExecuteAsync(
                connection,
                "ALTER TABLE DownloadJobs ADD COLUMN BrowserState TEXT NOT NULL DEFAULT 'InProgress';",
                cancellationToken,
                transaction).ConfigureAwait(false);
        }

        if (!columns.Contains("RoutingState"))
        {
            await ExecuteAsync(
                connection,
                "ALTER TABLE DownloadJobs ADD COLUMN RoutingState TEXT NOT NULL DEFAULT 'NotRequired';",
                cancellationToken,
                transaction).ConfigureAwait(false);
        }

        await ExecuteAsync(
            connection,
            """
            UPDATE DownloadJobs
            SET BrowserState = CASE
                    WHEN Status = 'Cancelled' THEN 'Cancelled'
                    WHEN Status = 'Interrupted' THEN 'Interrupted'
                    WHEN OriginalPath IS NOT NULL OR Status IN ('ReadyToMove', 'Moving', 'RetryPending', 'Completed', 'Failed') THEN 'Complete'
                    ELSE 'InProgress'
                END,
                RoutingState = CASE
                    WHEN Status = 'WaitingForSelection' THEN 'WaitingForSelection'
                    WHEN Status = 'WaitingForDownload' AND SelectedRelativeFolder IS NOT NULL THEN 'SelectionReady'
                    WHEN Status = 'ReadyToMove' AND SelectedRelativeFolder IS NOT NULL THEN 'SelectionReady'
                    WHEN Status = 'Moving' THEN 'Moving'
                    WHEN Status = 'RetryPending' THEN 'RetryPending'
                    WHEN Status = 'Completed' THEN 'Completed'
                    WHEN Status = 'Failed' OR Status = 'Interrupted' THEN 'Failed'
                    WHEN Status = 'Cancelled' THEN 'NotRequired'
                    ELSE 'NotRequired'
                END
            WHERE NOT EXISTS (SELECT 1 FROM MigrationHistory WHERE Version = 2);

            INSERT OR IGNORE INTO MigrationHistory(Version, AppliedAt)
            VALUES (2, CURRENT_TIMESTAMP);
            """,
            cancellationToken,
            transaction).ConfigureAwait(false);

        var ruleColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var ruleColumnCommand = connection.CreateCommand())
        {
            ruleColumnCommand.Transaction = transaction;
            ruleColumnCommand.CommandText = "PRAGMA table_info(Rules);";
            await using var reader = await ruleColumnCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ruleColumns.Add(reader.GetString(1));
            }
        }

        if (!ruleColumns.Contains("IsDeleted"))
        {
            await ExecuteAsync(
                connection,
                "ALTER TABLE Rules ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;",
                cancellationToken,
                transaction).ConfigureAwait(false);
        }

        await ExecuteAsync(
            connection,
            """
            INSERT OR IGNORE INTO MigrationHistory(Version, AppliedAt)
            VALUES (3, CURRENT_TIMESTAMP);
            """,
            cancellationToken,
            transaction).ConfigureAwait(false);

        if (!columns.Contains("LastBrowserEventAt"))
        {
            await ExecuteAsync(
                connection,
                "ALTER TABLE DownloadJobs ADD COLUMN LastBrowserEventAt TEXT NULL;",
                cancellationToken,
                transaction).ConfigureAwait(false);
        }

        if (!columns.Contains("IsBrowserRecordStale"))
        {
            await ExecuteAsync(
                connection,
                "ALTER TABLE DownloadJobs ADD COLUMN IsBrowserRecordStale INTEGER NOT NULL DEFAULT 0;",
                cancellationToken,
                transaction).ConfigureAwait(false);
        }

        await ExecuteAsync(
            connection,
            """
            UPDATE DownloadJobs
            SET LastBrowserEventAt = CreatedAt
            WHERE LastBrowserEventAt IS NULL;

            INSERT OR IGNORE INTO MigrationHistory(Version, AppliedAt)
            VALUES (4, CURRENT_TIMESTAMP);
            """,
            cancellationToken,
            transaction).ConfigureAwait(false);

        if (!columns.Contains("SelectionPromptState"))
        {
            await ExecuteAsync(
                connection,
                "ALTER TABLE DownloadJobs ADD COLUMN SelectionPromptState TEXT NOT NULL DEFAULT 'NeverShown';",
                cancellationToken,
                transaction).ConfigureAwait(false);
        }

        // v5 classifies every pre-existing job exactly once. Terminal work is already
        // resolved; anything still waiting for a folder becomes Deferred so it stays in the
        // download history "처리 대기" filter without replaying an automatic prompt.
        // File locations, routing states, and user files are never changed here.
        await ExecuteAsync(
            connection,
            """
            UPDATE DownloadJobs
            SET SelectionPromptState = CASE
                    WHEN BrowserState IN ('Cancelled', 'Interrupted') THEN 'Resolved'
                    WHEN RoutingState IN ('Completed', 'Skipped', 'Failed') THEN 'Resolved'
                    WHEN RoutingState = 'WaitingForSelection' THEN 'Deferred'
                    ELSE 'Resolved'
                END
            WHERE NOT EXISTS (SELECT 1 FROM MigrationHistory WHERE Version = 5);

            INSERT OR IGNORE INTO MigrationHistory(Version, AppliedAt)
            VALUES (5, CURRENT_TIMESTAMP);
            """,
            cancellationToken,
            transaction).ConfigureAwait(false);

        foreach (var column in new[] { "SelectedDestinationFolder", "SelectedFileName" })
        {
            if (!columns.Contains(column))
                await ExecuteAsync(connection, $"ALTER TABLE DownloadJobs ADD COLUMN {column} TEXT NULL;",
                    cancellationToken, transaction).ConfigureAwait(false);
        }
        await ExecuteAsync(connection,
            "INSERT OR IGNORE INTO MigrationHistory(Version, AppliedAt) VALUES (6, CURRENT_TIMESTAMP);",
            cancellationToken, transaction).ConfigureAwait(false);

        if (CurrentSchemaVersion != 6)
        {
            throw new InvalidOperationException("Repository migration version is inconsistent.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection CreateConnection()
        => new(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true,
        }.ToString());

    private static void ValidateRule(DownloadRule rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.MatchValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.StorageRoot);
        if (rule.Name.Length > 200 || rule.MatchValue.Length > 2048 || rule.StorageRoot.Length > 32767)
        {
            throw new ArgumentException("A rule field exceeds its maximum allowed length.", nameof(rule));
        }
    }

    private static DownloadRule ReadRule(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetBoolean(2),
            Enum.Parse<RuleMatchType>(reader.GetString(3)),
            reader.GetString(4),
            Enum.Parse<RuleMatchTarget>(reader.GetString(5)),
            reader.GetString(6),
            Enum.Parse<StorageMode>(reader.GetString(7)),
            reader.GetInt32(8),
            reader.GetInt32(9),
            Parse(reader.GetString(10)),
            Parse(reader.GetString(11)));

    private static DownloadJob ReadJob(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            Enum.Parse<BrowserKind>(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            GetNullableString(reader, 5),
            GetNullableString(reader, 6),
            GetNullableString(reader, 7),
            GetNullableString(reader, 8),
            GetNullableString(reader, 9),
            Guid.Parse(reader.GetString(10)),
            GetNullableString(reader, 11),
            GetNullableString(reader, 12),
            GetNullableString(reader, 13),
            Enum.Parse<BrowserTransferState>(reader.GetString(15)),
            Enum.Parse<RoutingState>(reader.GetString(16)),
            GetNullableString(reader, 17),
            GetNullableString(reader, 18),
            Parse(reader.GetString(19)),
            reader.IsDBNull(20) ? null : Parse(reader.GetString(20)),
            reader.IsDBNull(21) ? null : Parse(reader.GetString(21)),
            reader.GetBoolean(22),
            Enum.TryParse<SelectionPromptState>(reader.GetString(23), out var promptState)
                ? promptState
                : SelectionPromptState.NeverShown,
            GetNullableString(reader, 24),
            GetNullableString(reader, 25));

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void AddRuleParameters(SqliteCommand command, DownloadRule rule)
    {
        command.Parameters.AddWithValue("$id", rule.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", rule.Name);
        command.Parameters.AddWithValue("$enabled", rule.IsEnabled);
        command.Parameters.AddWithValue("$matchType", rule.MatchType.ToString());
        command.Parameters.AddWithValue("$matchValue", rule.MatchValue);
        command.Parameters.AddWithValue("$matchTarget", rule.MatchTarget.ToString());
        command.Parameters.AddWithValue("$storageRoot", rule.StorageRoot);
        command.Parameters.AddWithValue("$storageMode", rule.StorageMode.ToString());
        command.Parameters.AddWithValue("$priority", rule.Priority);
        command.Parameters.AddWithValue("$listOrder", rule.ListOrder);
        command.Parameters.AddWithValue("$createdAt", Format(rule.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", Format(rule.UpdatedAt));
    }

    private static void AddJobParameters(SqliteCommand command, DownloadJob job)
    {
        command.Parameters.AddWithValue("$id", job.Id.ToString("D"));
        command.Parameters.AddWithValue("$browser", job.Browser.ToString());
        command.Parameters.AddWithValue("$browserDownloadId", job.BrowserDownloadId);
        command.Parameters.AddWithValue("$originalFileName", job.OriginalFileName);
        command.Parameters.AddWithValue("$currentFileName", job.CurrentFileName);
        command.Parameters.AddWithValue("$initiatingPageUrl", Db(job.InitiatingPageUrl));
        command.Parameters.AddWithValue("$initialUrl", Db(job.InitialUrl));
        command.Parameters.AddWithValue("$finalUrl", Db(job.FinalUrl));
        command.Parameters.AddWithValue("$referrerUrl", Db(job.ReferrerUrl));
        command.Parameters.AddWithValue("$sanitizedSource", Db(job.SanitizedSource));
        command.Parameters.AddWithValue("$ruleId", job.RuleId.ToString("D"));
        command.Parameters.AddWithValue("$originalPath", Db(job.OriginalPath));
        command.Parameters.AddWithValue("$finalPath", Db(job.FinalPath));
        command.Parameters.AddWithValue("$selectedRelativeFolder", Db(job.SelectedRelativeFolder));
        command.Parameters.AddWithValue("$selectedDestinationFolder", Db(job.SelectedDestinationFolder));
        command.Parameters.AddWithValue("$selectedFileName", Db(job.SelectedFileName));
        command.Parameters.AddWithValue("$status", job.Status.ToString());
        command.Parameters.AddWithValue("$browserState", job.BrowserState.ToString());
        command.Parameters.AddWithValue("$routingState", job.RoutingState.ToString());
        command.Parameters.AddWithValue("$errorCode", Db(job.ErrorCode));
        command.Parameters.AddWithValue("$errorMessage", Db(job.ErrorMessage));
        command.Parameters.AddWithValue("$createdAt", Format(job.CreatedAt));
        command.Parameters.AddWithValue("$completedAt", job.CompletedAt is null ? DBNull.Value : Format(job.CompletedAt.Value));
        command.Parameters.AddWithValue(
            "$lastBrowserEventAt",
            job.LastBrowserEventAt is null ? DBNull.Value : Format(job.LastBrowserEventAt.Value));
        command.Parameters.AddWithValue("$isBrowserRecordStale", job.IsBrowserRecordStale);
        command.Parameters.AddWithValue("$selectionPromptState", job.SelectionPromptState.ToString());
    }

    /// <summary>
    /// Persists the automatic prompt decision for the given jobs in a single transaction.
    /// The state only moves forward, so a late browser event cannot replay a prompt
    /// the user already answered or postponed.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> AdvanceSelectionPromptStateAsync(
        IReadOnlyCollection<Guid> jobIds,
        SelectionPromptState state,
        CancellationToken cancellationToken = default)
    {
        var ids = jobIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(jobIds), "Advance between 1 and 1000 selection prompts.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var advanced = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            var inClause = string.Join(", ", ids.Select((_, index) => $"$id{index}"));
            var allowed = SelectionPromptRanksBelow(state);
            var allowedClause = string.Join(", ", allowed.Select(static value => $"'{value}'"));
            command.CommandText = $"""
                UPDATE DownloadJobs
                SET SelectionPromptState = $state
                WHERE Id IN ({inClause})
                  AND SelectionPromptState IN ({allowedClause})
                RETURNING Id;
                """;
            AddIdParameters(command, ids);
            command.Parameters.AddWithValue("$state", state.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                advanced.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        foreach (var jobId in advanced)
        {
            await AppendEventAsync(
                connection,
                jobId,
                $"selection.prompt-{state.ToString().ToLowerInvariant()}",
                state.ToString(),
                cancellationToken,
                transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return advanced;
    }

    private static IReadOnlyList<string> SelectionPromptRanksBelow(SelectionPromptState state)
        => state switch
        {
            SelectionPromptState.Shown => ["NeverShown"],
            SelectionPromptState.Deferred => ["NeverShown", "Shown"],
            SelectionPromptState.Resolved => ["NeverShown", "Shown", "Deferred"],
            _ => [],
        };

    private static async Task AppendEventAsync(
        SqliteConnection connection,
        Guid jobId,
        string eventType,
        string? detail,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO DownloadEvents(JobId, EventType, Detail, CreatedAt)
            VALUES($jobId, $eventType, $detail, $createdAt);
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$detail", Db(detail));
        command.Parameters.AddWithValue("$createdAt", Format(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static object Db(string? value) => value is null ? DBNull.Value : value;

    private static void AddIdParameters(SqliteCommand command, IReadOnlyList<Guid> ids)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            command.Parameters.AddWithValue($"$id{index}", ids[index].ToString("D"));
        }
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
