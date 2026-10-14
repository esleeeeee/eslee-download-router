using System.Text.Json;
using DownloadRouter.Agent;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Privacy;
using DownloadRouter.Core.Rules;
using DownloadRouter.Core.Startup;
using DownloadRouter.Infrastructure.Files;
using DownloadRouter.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownloadRouter.IntegrationTests;

/// <summary>
/// v2.0.0 agent behaviour: file-type rules confirmed once the name is known, pausing,
/// history retention and per-job serialization. Uses an explicit data root rather than the
/// process-wide environment variable, so it can run beside the other integration tests.
/// </summary>
public sealed class RoutingFeatureTests : IDisposable
{
    private static readonly string SupportedBuild = DownloadRegistrationPolicy.SupportedExtensionBuilds.First();

    private readonly string root = Path.Combine(Path.GetTempPath(), "download-router-feature-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths paths;
    private readonly DownloadRouterRepository repository;
    private readonly CountingSelectionUiLauncher launcher = new();
    private readonly MutableClock clock = new();
    private readonly AgentCommandHandler handler;
    private readonly Dictionary<string, DateTimeOffset> startedAt = new(StringComparer.Ordinal);

    public RoutingFeatureTests()
    {
        paths = AppPaths.For(AppInstance.Resolve(["--data-root", Path.Combine(root, "data-root")], null));
        repository = new DownloadRouterRepository(paths);
        var boundary = new PathBoundaryValidator();
        handler = new AgentCommandHandler(
            repository,
            new RuleMatcher(),
            new UrlSanitizer(),
            new PathTokenResolver(new WindowsKnownPathProvider()),
            boundary,
            new DownloadJobStateMachine(),
            new FileMoveService(boundary, NullLogger<FileMoveService>.Instance),
            launcher,
            paths,
            NullLogger<AgentCommandHandler>.Instance,
            clock);
        repository.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    // ------------------------------------------------------------------
    // File-type rules and tentative matching
    // ------------------------------------------------------------------

    [Fact]
    public async Task RuleUpsertStoresNormalizedFileTypesAndRejectsAnEmptyFilter()
    {
        var rule = NewRule("docs", "example.com", Folder("docs"), StorageMode.Automatic) with
        {
            FileTypeMode = FileTypeFilterMode.OnlyListed,
            FileExtensions = "PDF, .Zip tar.gz",
        };

        var saved = await SendAsync("rules.upsert", rule);
        var rejected = await SendAsync("rules.upsert", rule with { Id = Guid.NewGuid(), FileExtensions = " " });
        var invalid = await SendAsync("rules.upsert", rule with { Id = Guid.NewGuid(), FileExtensions = "exe!" });

        Assert.True(saved.Success, saved.Message);
        var stored = await repository.GetRuleAsync(rule.Id, CancellationToken.None);
        Assert.Equal(FileTypeFilterMode.OnlyListed, stored!.FileTypeMode);
        Assert.Equal(".pdf;.zip;.tar.gz", stored.FileExtensions);
        Assert.Equal("validation.invalid-argument", rejected.ErrorCode);
        Assert.Equal("validation.invalid-argument", invalid.ErrorCode);
        Assert.Single(await repository.GetRulesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AKnownNameIsMatchedAgainstFileTypesAtCreation()
    {
        var documents = await AddRuleAsync("documents", "files.example.com", Folder("documents"), StorageMode.Automatic,
            RuleMatchType.ExactHost, FileTypeFilterMode.OnlyListed, ".pdf");
        var general = await AddRuleAsync("general", "example.com", Folder("general"), StorageMode.Automatic);

        var pdf = await StartAsync("known-1", "report.pdf", page: "https://files.example.com/page");
        var exe = await StartAsync("known-2", "setup.exe", page: "https://files.example.com/page");

        Assert.Equal(documents.Id, (await JobAsync(pdf)).RuleId);
        Assert.Equal(general.Id, (await JobAsync(exe)).RuleId);
        Assert.False((await JobAsync(pdf)).IsRuleTentative);
    }

    [Fact]
    public async Task AnUnknownNameCreatesATentativeJobThatNeitherPromptsNorUsesTheTenMinuteFolder()
    {
        var rule = await AddRuleAsync("select", "example.com", Folder("select"), StorageMode.SelectSubfolder,
            fileTypeMode: FileTypeFilterMode.OnlyListed, extensions: ".pdf");
        var remembered = Folder("remembered");
        await RememberTenMinuteFolderAsync(remembered);
        var promptsBefore = launcher.Requests;

        var response = await SendStartedAsync("tentative-1", string.Empty);
        var jobId = response.Data!.Value.GetProperty("jobId").GetGuid();
        var job = await JobAsync(jobId);

        Assert.True(response.Data.Value.GetProperty("ruleTentative").GetBoolean());
        Assert.False(response.Data.Value.GetProperty("selectionUiRequested").GetBoolean());
        Assert.True(job.IsRuleTentative);
        Assert.Equal(rule.Id, job.RuleId);
        Assert.Equal(RoutingState.WaitingForSelection, job.RoutingState);
        Assert.Null(job.SelectedDestinationFolder);
        Assert.Equal(promptsBefore, launcher.Requests);
        Assert.False(SelectionPromptPolicy.IsAutoPromptEligible(job));
        Assert.DoesNotContain(DownloadJobQueries.AutomaticSelections([job]), candidate => candidate.Id == jobId);
        Assert.Contains(DownloadJobQueries.ActiveSelections([job]), candidate => candidate.Id == jobId);

        // The same rule is confirmed: the deferred 10-minute folder now applies.
        var metadata = await SendAsync("download.metadata", new DownloadMetadataChangedPayload(
            "Whale", "tentative-1", null, "manual.pdf", startedAt["tentative-1"]));

        Assert.True(metadata.Data!.Value.GetProperty("tracked").GetBoolean());
        job = await JobAsync(jobId);
        Assert.False(job.IsRuleTentative);
        Assert.Equal(rule.Id, job.RuleId);
        Assert.Equal(RoutingState.SelectionReady, job.RoutingState);
        Assert.Equal(remembered, job.SelectedDestinationFolder);
        Assert.Equal(SelectionPromptState.Resolved, job.SelectionPromptState);
        Assert.Equal(promptsBefore, launcher.Requests);
    }

    [Fact]
    public async Task ConfirmingTheSameRuleRequestsTheDeferredSelectionWindow()
    {
        await AddRuleAsync("select", "example.com", Folder("select"), StorageMode.SelectSubfolder,
            fileTypeMode: FileTypeFilterMode.ExceptListed, extensions: ".exe");
        var jobId = await StartAsync("same-1", string.Empty);
        Assert.Equal(0, launcher.Requests);

        await SendAsync("download.metadata", new DownloadMetadataChangedPayload(
            "Whale", "same-1", null, "photo.jpg", startedAt["same-1"]));

        var job = await JobAsync(jobId);
        Assert.False(job.IsRuleTentative);
        Assert.Equal(RoutingState.WaitingForSelection, job.RoutingState);
        Assert.Equal(SelectionPromptState.NeverShown, job.SelectionPromptState);
        Assert.True(SelectionPromptPolicy.IsAutoPromptEligible(job));
        Assert.Equal(1, launcher.Requests);
    }

    [Fact]
    public async Task ADifferentRuleWinsOnceTheNameIsKnownAndRoutesThere()
    {
        var documents = await AddRuleAsync("documents", "files.example.com", Folder("documents"), StorageMode.Automatic,
            RuleMatchType.ExactHost, FileTypeFilterMode.OnlyListed, ".pdf");
        var general = await AddRuleAsync("general", "example.com", Folder("general"), StorageMode.SelectSubfolder);
        var jobId = await StartAsync("switch-1", string.Empty, page: "https://files.example.com/list");
        Assert.Equal(documents.Id, (await JobAsync(jobId)).RuleId);

        await SendAsync("download.metadata", new DownloadMetadataChangedPayload(
            "Whale", "switch-1", null, "setup.exe", startedAt["switch-1"]));

        var job = await JobAsync(jobId);
        Assert.False(job.IsRuleTentative);
        Assert.Equal(general.Id, job.RuleId);
        Assert.Equal(RoutingState.WaitingForSelection, job.RoutingState);
        Assert.Equal(SelectionPromptState.NeverShown, job.SelectionPromptState);
        Assert.Equal("https://files.example.com/list", job.SanitizedSource);
        Assert.Equal(1, launcher.Requests);
    }

    [Fact]
    public async Task NoRuleOnceTheNameIsKnownRemovesTheJobAndLeavesTheFileAlone()
    {
        await AddRuleAsync("documents", "example.com", Folder("documents"), StorageMode.Automatic,
            fileTypeMode: FileTypeFilterMode.OnlyListed, extensions: ".pdf");
        var jobId = await StartAsync("drop-1", string.Empty);

        var metadata = await SendAsync("download.metadata", new DownloadMetadataChangedPayload(
            "Whale", "drop-1", null, "setup.exe", startedAt["drop-1"]));

        Assert.True(metadata.Success, metadata.Message);
        Assert.False(metadata.Data!.Value.GetProperty("tracked").GetBoolean());
        Assert.True(metadata.Data.Value.GetProperty("failOpen").GetBoolean());
        Assert.Null(await repository.GetJobAsync(jobId, CancellationToken.None));
        Assert.Equal(0L, await EventCountAsync(jobId));

        var source = SourceFile("setup.exe");
        var completed = await SendAsync("download.changed", new DownloadChangedPayload(
            "Whale", "drop-1", "complete", source, null, StartedAt: startedAt["drop-1"]));
        Assert.False(completed.Data!.Value.GetProperty("tracked").GetBoolean());
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task AFolderTheUserAlreadyChoseIsKeptAndOnlyTheTentativeMarkIsCleared()
    {
        var rule = await AddRuleAsync("select", "example.com", Folder("select"), StorageMode.SelectSubfolder,
            fileTypeMode: FileTypeFilterMode.OnlyListed, extensions: ".pdf");
        Directory.CreateDirectory(Path.Combine(rule.StorageRoot, "chosen"));
        var jobId = await StartAsync("decided-1", string.Empty);
        var chosen = await SendAsync("selection.complete", new SelectionCompletedPayload([jobId], "chosen"));
        Assert.True(chosen.Success, chosen.Message);

        await SendAsync("download.metadata", new DownloadMetadataChangedPayload(
            "Whale", "decided-1", null, "setup.exe", startedAt["decided-1"]));

        var job = await JobAsync(jobId);
        Assert.False(job.IsRuleTentative);
        Assert.Equal(rule.Id, job.RuleId);
        Assert.Equal(RoutingState.SelectionReady, job.RoutingState);
        Assert.Equal("chosen", job.SelectedRelativeFolder);

        var source = SourceFile("setup.exe");
        await SendAsync("download.changed", new DownloadChangedPayload(
            "Whale", "decided-1", "complete", source, null, StartedAt: startedAt["decided-1"]));
        Assert.True(File.Exists(Path.Combine(rule.StorageRoot, "chosen", "setup.exe")));
    }

    [Fact]
    public async Task CompletionConfirmsATentativeRuleBeforeMovingTheFile()
    {
        var documents = await AddRuleAsync("documents", "files.example.com", Folder("documents"), StorageMode.Automatic,
            RuleMatchType.ExactHost, FileTypeFilterMode.OnlyListed, ".pdf");
        var general = await AddRuleAsync("general", "example.com", Folder("general"), StorageMode.Automatic);
        var pdfJob = await StartAsync("complete-1", string.Empty, page: "https://files.example.com/a");
        var exeJob = await StartAsync("complete-2", string.Empty, page: "https://files.example.com/b");

        var pdf = SourceFile("report.pdf");
        var exe = SourceFile("tool.exe");
        var pdfResult = await SendAsync("download.changed", new DownloadChangedPayload(
            "Whale", "complete-1", "complete", pdf, null, StartedAt: startedAt["complete-1"]));
        var exeResult = await SendAsync("download.changed", new DownloadChangedPayload(
            "Whale", "complete-2", "complete", exe, null, StartedAt: startedAt["complete-2"]));

        Assert.Equal("Completed", pdfResult.Data!.Value.GetProperty("status").GetString());
        Assert.Equal("Completed", exeResult.Data!.Value.GetProperty("status").GetString());
        Assert.True(File.Exists(Path.Combine(documents.StorageRoot, "report.pdf")));
        Assert.True(File.Exists(Path.Combine(general.StorageRoot, "tool.exe")));
        Assert.Equal(general.Id, (await JobAsync(exeJob)).RuleId);
        Assert.False((await JobAsync(pdfJob)).IsRuleTentative);
    }

    [Fact]
    public async Task CompletionWithNoMatchingRuleRemovesTheJobInsteadOfMoving()
    {
        await AddRuleAsync("documents", "example.com", Folder("documents"), StorageMode.Automatic,
            fileTypeMode: FileTypeFilterMode.OnlyListed, extensions: ".pdf");
        var jobId = await StartAsync("complete-drop", string.Empty);
        var source = SourceFile("installer.msi");

        var result = await SendAsync("download.changed", new DownloadChangedPayload(
            "Whale", "complete-drop", "complete", source, null, StartedAt: startedAt["complete-drop"]));

        Assert.False(result.Data!.Value.GetProperty("tracked").GetBoolean());
        Assert.Null(await repository.GetJobAsync(jobId, CancellationToken.None));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ARepeatedStartForTheSameInstanceAlsoConfirmsTheRule()
    {
        var rule = await AddRuleAsync("documents", "example.com", Folder("documents"), StorageMode.Automatic,
            fileTypeMode: FileTypeFilterMode.OnlyListed, extensions: ".pdf");
        var jobId = await StartAsync("restart-1", string.Empty);

        var again = await SendStartedAsync("restart-1", "manual.pdf", reuseStartedAt: true);

        Assert.True(again.Data!.Value.GetProperty("existing").GetBoolean());
        Assert.True(again.Data.Value.GetProperty("tracked").GetBoolean());
        var job = await JobAsync(jobId);
        Assert.False(job.IsRuleTentative);
        Assert.Equal(rule.Id, job.RuleId);
        Assert.Single(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));
    }

    // ------------------------------------------------------------------
    // Pause routing
    // ------------------------------------------------------------------

    [Fact]
    public async Task PausingLeavesNewDownloadsAloneButFinishesExistingJobs()
    {
        var rule = await AddRuleAsync("auto", "example.com", Folder("auto"), StorageMode.Automatic);
        var status = await SendAsync("routing.status", new { });
        Assert.False(status.Data!.Value.GetProperty("isPaused").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.Data.Value.GetProperty("pausedUntil").ValueKind);

        var existingJob = await StartAsync("before-pause", "before.txt");
        var paused = await SendAsync("routing.pause", new RoutingPausePayload(30));

        Assert.True(paused.Success, paused.Message);
        Assert.True(paused.Data!.Value.GetProperty("isPaused").GetBoolean());
        Assert.Equal(clock.Now.AddMinutes(30), paused.Data.Value.GetProperty("pausedUntil").GetDateTimeOffset());
        Assert.Equal(
            RoutingPausePolicy.ToStoredValue(30, clock.Now),
            await repository.GetSettingAsync(RoutingPausePolicy.SettingKey, CancellationToken.None));

        var ignored = await SendStartedAsync("during-pause", "during.txt");
        Assert.True(ignored.Success);
        Assert.False(ignored.Data!.Value.GetProperty("tracked").GetBoolean());
        Assert.True(ignored.Data.Value.GetProperty("failOpen").GetBoolean());
        Assert.Equal("routing-paused", ignored.Data.Value.GetProperty("ignored").GetString());
        Assert.Single(await repository.GetRecentJobsAsync(cancellationToken: CancellationToken.None));

        var source = SourceFile("before.txt");
        var completed = await SendAsync("download.changed", new DownloadChangedPayload(
            "Whale", "before-pause", "complete", source, null, StartedAt: startedAt["before-pause"]));
        Assert.Equal("Completed", completed.Data!.Value.GetProperty("status").GetString());
        Assert.True(File.Exists(Path.Combine(rule.StorageRoot, "before.txt")));
        Assert.Equal(RoutingState.Completed, (await JobAsync(existingJob)).RoutingState);

        var resumed = await SendAsync("routing.resume", new { });
        Assert.False(resumed.Data!.Value.GetProperty("isPaused").GetBoolean());
        Assert.Null(await repository.GetSettingAsync(RoutingPausePolicy.SettingKey, CancellationToken.None));
        var tracked = await SendStartedAsync("after-pause", "after.txt");
        Assert.True(tracked.Data!.Value.GetProperty("tracked").GetBoolean());
    }

    [Fact]
    public async Task AnIndefinitePauseHasNoEndAndATimedPauseExpiresAndIsCleanedUp()
    {
        await AddRuleAsync("auto", "example.com", Folder("auto"), StorageMode.Automatic);

        var indefinite = await SendAsync("routing.pause", new RoutingPausePayload(null));
        Assert.True(indefinite.Data!.Value.GetProperty("isPaused").GetBoolean());
        Assert.Equal(JsonValueKind.Null, indefinite.Data.Value.GetProperty("pausedUntil").ValueKind);
        clock.Now = clock.Now.AddDays(30);
        Assert.True((await SendAsync("routing.status", new { })).Data!.Value.GetProperty("isPaused").GetBoolean());

        await SendAsync("routing.pause", new RoutingPausePayload(1));
        clock.Now = clock.Now.AddMinutes(1);
        var expired = await SendAsync("routing.status", new { });

        Assert.False(expired.Data!.Value.GetProperty("isPaused").GetBoolean());
        Assert.Null(await repository.GetSettingAsync(RoutingPausePolicy.SettingKey, CancellationToken.None));
        var tracked = await SendStartedAsync("after-expiry", "after.txt");
        Assert.True(tracked.Data!.Value.GetProperty("tracked").GetBoolean());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task PauseDurationsOutsideTheRangeAreRejected(int minutes)
    {
        var response = await SendAsync("routing.pause", new RoutingPausePayload(minutes));

        Assert.False(response.Success);
        Assert.Equal("validation.invalid-argument", response.ErrorCode);
        Assert.Null(await repository.GetSettingAsync(RoutingPausePolicy.SettingKey, CancellationToken.None));
    }

    // ------------------------------------------------------------------
    // History retention
    // ------------------------------------------------------------------

    [Fact]
    public async Task RetentionDefaultsToKeepingEverythingAndRejectsUnofferedValues()
    {
        var current = await SendAsync("history.retention.get", new { });
        var rejected = await SendAsync("history.retention.set", new HistoryRetentionPayload(14));

        Assert.Equal(0, current.Data!.Value.GetProperty("days").GetInt32());
        Assert.Equal("validation.invalid-argument", rejected.ErrorCode);
        Assert.Equal(0, await handler.RunHistoryCleanupAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SettingRetentionCleansUpFinishedHistoryImmediatelyAndNeverTouchesFiles()
    {
        var rule = await AddRuleAsync("auto", "example.com", Folder("auto"), StorageMode.Automatic);
        var routedFile = Path.Combine(rule.StorageRoot, "kept-on-disk.txt");
        await File.WriteAllTextAsync(routedFile, "user data", CancellationToken.None);
        var old = clock.Now.AddDays(-45);
        var finished = Job(rule.Id, BrowserTransferState.Complete, RoutingState.Completed, old) with { FinalPath = routedFile };
        var cancelled = Job(rule.Id, BrowserTransferState.Cancelled, RoutingState.NotRequired, old);
        var failed = Job(rule.Id, BrowserTransferState.Complete, RoutingState.Failed, old);
        var pending = Job(rule.Id, BrowserTransferState.Complete, RoutingState.WaitingForSelection, old);
        var recent = Job(rule.Id, BrowserTransferState.Complete, RoutingState.Completed, clock.Now.AddDays(-2));
        foreach (var job in new[] { finished, cancelled, failed, pending, recent })
        {
            await repository.CreateJobAsync(job, CancellationToken.None);
        }

        var response = await SendAsync("history.retention.set", new HistoryRetentionPayload(30));

        Assert.True(response.Success, response.Message);
        Assert.Equal(30, response.Data!.Value.GetProperty("days").GetInt32());
        Assert.Equal(2, response.Data.Value.GetProperty("deleted").GetInt32());
        Assert.Null(await repository.GetJobAsync(finished.Id, CancellationToken.None));
        Assert.Null(await repository.GetJobAsync(cancelled.Id, CancellationToken.None));
        Assert.Equal(0L, await EventCountAsync(finished.Id));
        Assert.NotNull(await repository.GetJobAsync(failed.Id, CancellationToken.None));
        Assert.NotNull(await repository.GetJobAsync(pending.Id, CancellationToken.None));
        Assert.NotNull(await repository.GetJobAsync(recent.Id, CancellationToken.None));
        Assert.True(File.Exists(routedFile));
        Assert.Equal(30, (await SendAsync("history.retention.get", new { })).Data!.Value.GetProperty("days").GetInt32());

        // The periodic run uses the same setting and the injected clock.
        clock.Now = clock.Now.AddDays(29);
        Assert.Equal(1, await handler.RunHistoryCleanupAsync(CancellationToken.None));
        Assert.Null(await repository.GetJobAsync(recent.Id, CancellationToken.None));
    }

    [Fact]
    public async Task TheRetentionServiceRunsAfterOneMinuteAndThenEverySixHours()
    {
        var time = new ManualTimeProvider();
        var cleanup = new CountingCleanup();
        using var service = new HistoryRetentionService(cleanup, time, NullLogger<HistoryRetentionService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => time.PendingTimers == 1);
        time.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(50);
        Assert.Equal(0, cleanup.Runs);

        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => cleanup.Runs == 1 && time.PendingTimers == 1);
        time.Advance(HistoryRetentionService.Interval - TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        Assert.Equal(1, cleanup.Runs);

        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => cleanup.Runs == 2 && time.PendingTimers == 1);
        cleanup.FailNext = true;
        time.Advance(HistoryRetentionService.Interval);
        await WaitUntilAsync(() => cleanup.Runs == 3 && time.PendingTimers == 1);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(3, cleanup.Runs);
    }

    // ------------------------------------------------------------------
    // Per-job serialization
    // ------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentUpdatesToOneJobBothSurvive()
    {
        var rule = await AddRuleAsync("select", "example.com", Folder("select"), StorageMode.SelectSubfolder);
        Directory.CreateDirectory(Path.Combine(rule.StorageRoot, "picked"));
        var jobs = new List<(Guid Id, string DownloadId)>();
        for (var index = 0; index < 20; index++)
        {
            var downloadId = $"race-{index}";
            jobs.Add((await StartAsync(downloadId, string.Empty), downloadId));
        }

        // A browser name update and a folder choice for the same job read and write the same
        // row; without the per-job lock one of them would overwrite the other.
        await Task.WhenAll(jobs.SelectMany(job => new[]
        {
            Task.Run(() => SendAsync("download.metadata", new DownloadMetadataChangedPayload(
                "Whale", job.DownloadId, null, $"{job.DownloadId}.bin", startedAt[job.DownloadId]))),
            Task.Run(() => SendAsync("selection.complete", new SelectionCompletedPayload([job.Id], "picked"))),
        }));

        foreach (var (id, downloadId) in jobs)
        {
            var job = await JobAsync(id);
            Assert.Equal($"{downloadId}.bin", job.CurrentFileName);
            Assert.Equal(RoutingState.SelectionReady, job.RoutingState);
            Assert.Equal("picked", job.SelectedRelativeFolder);
        }
    }

    [Fact]
    public async Task CompletionRacingAFolderChoiceMovesTheFileExactlyOnce()
    {
        var rule = await AddRuleAsync("select", "example.com", Folder("select"), StorageMode.SelectSubfolder);
        Directory.CreateDirectory(Path.Combine(rule.StorageRoot, "picked"));
        var jobId = await StartAsync("race-complete", "race.txt");
        var source = SourceFile("race.txt");

        var results = await Task.WhenAll(
            Task.Run(() => SendAsync("download.changed", new DownloadChangedPayload(
                "Whale", "race-complete", "complete", source, null, StartedAt: startedAt["race-complete"]))),
            Task.Run(() => SendAsync("selection.complete", new SelectionCompletedPayload([jobId], "picked"))));

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        var job = await JobAsync(jobId);
        Assert.Equal(RoutingState.Completed, job.RoutingState);
        Assert.Equal(BrowserTransferState.Complete, job.BrowserState);
        Assert.Equal(Path.Combine(rule.StorageRoot, "picked", "race.txt"), job.FinalPath);
        Assert.True(File.Exists(job.FinalPath));
        Assert.False(File.Exists(source));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(root, "routed", name)).FullName;

    private string SourceFile(string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "incoming", Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "public test fixture");
        return path;
    }

    private DownloadRule NewRule(
        string name,
        string matchValue,
        string storageRoot,
        StorageMode storageMode,
        RuleMatchType matchType = RuleMatchType.DomainAndSubdomains)
        => new(Guid.NewGuid(), name, true, matchType, matchValue, RuleMatchTarget.InitiatingPage,
            storageRoot, storageMode, 0, 0, clock.Now, clock.Now);

    private async Task<DownloadRule> AddRuleAsync(
        string name,
        string matchValue,
        string storageRoot,
        StorageMode storageMode,
        RuleMatchType matchType = RuleMatchType.DomainAndSubdomains,
        FileTypeFilterMode fileTypeMode = FileTypeFilterMode.AllFiles,
        string extensions = "")
    {
        var rule = NewRule(name, matchValue, storageRoot, storageMode, matchType) with
        {
            FileTypeMode = fileTypeMode,
            FileExtensions = extensions,
        };
        var response = await SendAsync("rules.upsert", rule);
        Assert.True(response.Success, response.Message);
        return (await repository.GetRuleAsync(rule.Id, CancellationToken.None))!;
    }

    private async Task RememberTenMinuteFolderAsync(string folder)
    {
        var seedId = await StartAsync("seed-ten-minute", "seed.pdf");
        var response = await SendAsync("selection.complete", new SelectionCompletedPayload(
            [seedId], string.Empty, folder, UseForTenMinutes: true));
        Assert.True(response.Success, response.Message);
        launcher.Reset();
    }

    private async Task<Guid> StartAsync(string downloadId, string fileName, string page = "https://example.com/downloads")
    {
        var response = await SendStartedAsync(downloadId, fileName, page);
        Assert.True(response.Data!.Value.GetProperty("tracked").GetBoolean(), response.Data.Value.ToString());
        return response.Data.Value.GetProperty("jobId").GetGuid();
    }

    private async Task<AgentResponse> SendStartedAsync(
        string downloadId,
        string fileName,
        string page = "https://example.com/downloads",
        bool reuseStartedAt = false)
    {
        if (!reuseStartedAt)
        {
            startedAt[downloadId] = DateTimeOffset.UtcNow;
        }

        var response = await SendAsync("download.started", new DownloadStartedPayload(
            "Whale", downloadId, fileName, null, page, $"https://cdn.example.net/{downloadId}", null, null,
            State: "in_progress",
            StartedAt: startedAt[downloadId],
            ExtensionBuild: SupportedBuild));
        Assert.True(response.Success, response.Message);
        return response;
    }

    private async Task<DownloadJob> JobAsync(Guid id)
        => await repository.GetJobAsync(id, CancellationToken.None) ?? throw new InvalidOperationException("Job missing.");

    private Task<AgentResponse> SendAsync(string command, object payload)
        => handler.HandleAsync(
            new AgentCommand(
                ProtocolConstants.CurrentVersion,
                Guid.NewGuid(),
                command,
                JsonSerializer.SerializeToElement(payload, ProtocolJson.Options)),
            CancellationToken.None);

    private async Task<long> EventCountAsync(Guid jobId)
    {
        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DownloadEvents WHERE JobId = $id;";
        command.Parameters.AddWithValue("$id", jobId.ToString("D"));
        return (long)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }

    private static DownloadJob Job(Guid ruleId, BrowserTransferState browser, RoutingState routing, DateTimeOffset completedAt)
        => new(
            Guid.NewGuid(), BrowserKind.Whale, Guid.NewGuid().ToString("N"), "a.txt", "a.txt", null, null, null, null, null,
            ruleId, null, null, null, browser, routing, null, null, completedAt.AddMinutes(-1), completedAt,
            BrowserStartedAt: completedAt.AddMinutes(-1));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The expected state was not reached.");
            }

            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CountingSelectionUiLauncher : ISelectionUiLauncher
    {
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        public bool RequestSelectionUi()
        {
            Interlocked.Increment(ref requests);
            return true;
        }

        public void Reset() => Interlocked.Exchange(ref requests, 0);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingCleanup : IHistoryCleanup
    {
        private int runs;

        public int Runs => Volatile.Read(ref runs);

        public bool FailNext { get; set; }

        public Task<int> RunHistoryCleanupAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref runs);
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Simulated cleanup failure.");
            }

            return Task.FromResult(0);
        }
    }

    /// <summary>A clock whose timers fire only when the test advances time.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset now = DateTimeOffset.Parse("2026-10-14T00:00:00Z");

        public int PendingTimers
        {
            get
            {
                lock (gate)
                {
                    return timers.Count(static timer => timer.DueAt is not null);
                }
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (gate)
            {
                timers.Add(timer);
            }

            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            List<ManualTimer> due;
            lock (gate)
            {
                now += by;
                due = timers.Where(timer => timer.DueAt <= now).ToList();
                foreach (var timer in due)
                {
                    timer.DueAt = null;
                }
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private void Schedule(ManualTimer timer, TimeSpan dueTime)
        {
            lock (gate)
            {
                timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (gate)
            {
                timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? DueAt { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                owner.Schedule(this, dueTime);
                return true;
            }

            public void Fire() => callback(state);

            public void Dispose() => owner.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
