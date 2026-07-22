using System.Buffers.Binary;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Settings;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using System.Xml.Linq;

namespace DownloadRouter.Core.Tests;

public sealed class ProtocolAndStateTests
{
    [Fact]
    public async Task LengthPrefixedProtocolRoundTripsJson()
    {
        await using var stream = new MemoryStream();
        var value = new { command = "ping", number = 42 };

        await LengthPrefixedJsonProtocol.WriteAsync(stream, value, CancellationToken.None);
        stream.Position = 0;
        var result = await LengthPrefixedJsonProtocol.ReadAsync<Dictionary<string, object>>(stream, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ping", result["command"].ToString());
    }

    [Fact]
    public async Task LengthPrefixedProtocolRejectsOversizedInputBeforeAllocation()
    {
        await using var stream = new MemoryStream();
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, ProtocolConstants.MaximumMessageBytes + 1);
        await stream.WriteAsync(prefix, CancellationToken.None);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            LengthPrefixedJsonProtocol.ReadAsync<object>(stream, CancellationToken.None));
    }

    [Fact]
    public void StateMachineRejectsMovingBeforeDownloadCompletion()
    {
        var stateMachine = new DownloadJobStateMachine();
        Assert.False(stateMachine.CanTransition(RoutingState.WaitingForSelection, RoutingState.Moving));
        Assert.Throws<InvalidOperationException>(() =>
            stateMachine.EnsureCanTransition(RoutingState.WaitingForSelection, RoutingState.Moving));
    }

    [Fact]
    public void CompletedAndCancelledJobsAreTerminal()
    {
        var stateMachine = new DownloadJobStateMachine();
        Assert.False(stateMachine.CanTransition(RoutingState.Completed, RoutingState.RetryPending));
        Assert.False(stateMachine.CanTransition(BrowserTransferState.Cancelled, BrowserTransferState.InProgress));
    }

    [Fact]
    public void CancelledJobsAreExcludedFromPendingAndDashboardCounts()
    {
        var cancelled = CreateJob(BrowserTransferState.Cancelled, RoutingState.NotRequired);
        var pending = CreateJob(BrowserTransferState.InProgress, RoutingState.WaitingForSelection);

        var active = DownloadJobQueries.ActiveSelections([cancelled, pending]);
        var counts = DownloadJobQueries.CountDashboard([cancelled, pending]);

        Assert.Equal(pending.Id, Assert.Single(active).Id);
        Assert.Equal(1, counts.WaitingForSelection);
        Assert.Equal(1, counts.CancelledOrInterrupted);
    }

    [Fact]
    public void SelectionPromptQueueIsFifoAndRejectsDuplicateJobs()
    {
        var queue = new SelectionPromptQueue();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.True(queue.Enqueue(first));
        Assert.False(queue.Enqueue(first));
        Assert.True(queue.Enqueue(second));
        Assert.True(queue.TryDequeue(out var dequeuedFirst));
        Assert.True(queue.TryDequeue(out var dequeuedSecond));
        Assert.Equal(first, dequeuedFirst);
        Assert.Equal(second, dequeuedSecond);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void SelectionPromptQueueCanRefreshTheCurrentJobWithoutBreakingFifo()
    {
        var queue = new SelectionPromptQueue();
        var current = Guid.NewGuid();
        var next = Guid.NewGuid();
        queue.Enqueue(next);
        queue.EnqueueFirst(current);

        Assert.True(queue.TryDequeue(out var refreshed));
        Assert.True(queue.TryDequeue(out var queuedNext));
        Assert.Equal(current, refreshed);
        Assert.Equal(next, queuedNext);
    }

    [Fact]
    public void SelectionPromptQueueCanBeHiddenAsOneSessionBatch()
    {
        var queue = new SelectionPromptQueue();
        var ids = Enumerable.Range(0, 23).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            queue.Enqueue(id);
        }

        Assert.Equal(ids, queue.Drain());
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void AutomaticSelectionPolicyKeepsOldAndStaleJobsInPendingWithoutPopup()
    {
        var now = DateTimeOffset.UtcNow;
        var recent = CreateJob(BrowserTransferState.InProgress, RoutingState.WaitingForSelection) with
        {
            CreatedAt = now.AddMinutes(-5),
            LastBrowserEventAt = now.AddMinutes(-1),
        };
        var old = recent with
        {
            Id = Guid.NewGuid(),
            CreatedAt = now.AddHours(-1),
            LastBrowserEventAt = now.AddMinutes(-31),
        };
        var stale = recent with { Id = Guid.NewGuid(), IsBrowserRecordStale = true };
        var cancelled = recent with { Id = Guid.NewGuid(), BrowserState = BrowserTransferState.Cancelled };

        var pending = DownloadJobQueries.ActiveSelections([recent, old, stale, cancelled]);
        var automatic = DownloadJobQueries.AutomaticSelections([old, stale, recent, cancelled], now);

        Assert.Equal(3, pending.Count);
        Assert.Equal(recent.Id, Assert.Single(automatic).Id);
        Assert.True(SelectionPromptPolicy.IsPreviousSessionPending(old, now));
        Assert.True(SelectionPromptPolicy.IsPreviousSessionPending(stale, now));
    }

    [Theory]
    [InlineData(null, AppThemePreference.System, "Default")]
    [InlineData("invalid-old-value", AppThemePreference.System, "Default")]
    [InlineData("Light", AppThemePreference.Light, "Light")]
    [InlineData("dark", AppThemePreference.Dark, "Dark")]
    public void ThemeSettingsMapSafely(string? value, AppThemePreference expected, string elementTheme)
    {
        var preference = AppThemePolicy.Parse(value);
        Assert.Equal(expected, preference);
        Assert.Equal(elementTheme, AppThemePolicy.ToElementThemeName(preference));
    }

    [Fact]
    public void PreferencesPersistAndInvalidThemeFallsBackWithoutLosingOtherSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "download-router-preferences-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.local.json");
        try
        {
            var store = new AppPreferencesStore(path);
            store.Save(new AppPreferences(WindowCloseBehavior.ExitApplication, "Dark"));
            var restored = store.Load();
            Assert.Equal(WindowCloseBehavior.ExitApplication, restored.CloseBehavior);
            Assert.Equal(AppThemePreference.Dark, restored.ThemePreference);

            File.WriteAllText(path, "{\"closeBehavior\":1,\"theme\":\"retired-theme\"}");
            restored = store.Load();
            Assert.Equal(WindowCloseBehavior.ExitApplication, restored.CloseBehavior);
            Assert.Equal(AppThemePreference.System, restored.ThemePreference);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProductVersionUsesAssemblyInformationalVersionAndSurvivesMissingCommit()
    {
        var current = ProductVersionInfo.Read(typeof(ProductVersionInfo).Assembly, AppContext.BaseDirectory);
        var assemblyName = new AssemblyName("NoInformationalVersion") { Version = new Version(7, 2, 1, 0) };
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var fallback = ProductVersionInfo.Read(dynamicAssembly, AppContext.BaseDirectory);

        Assert.False(string.IsNullOrWhiteSpace(current.InformationalVersion));
        Assert.StartsWith(current.SemanticVersion, current.InformationalVersion, StringComparison.Ordinal);
        Assert.Equal("7.2.1", fallback.SemanticVersion);
        Assert.Null(fallback.Commit);
    }

    [Fact]
    public void AppAndInstallerDeriveTheirVersionFromDirectoryBuildProps()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var version = document.Descendants("VersionPrefix").Single().Value;
        var appExecutable = Directory
            .EnumerateFiles(
                Path.Combine(repositoryRoot, "src", "DownloadRouter.App", "bin"),
                "DownloadRouter.App.exe",
                SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();
        var appProductVersion = FileVersionInfo.GetVersionInfo(appExecutable).ProductVersion;
        var installerSource = File.ReadAllText(Path.Combine(repositoryRoot, "installer", "DownloadRouter.iss"));
        var installerBuild = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "build-installer.ps1"));

        Assert.Matches("^\\d+\\.\\d+\\.\\d+$", version);
        Assert.True(
            string.Equals(appProductVersion, version, StringComparison.Ordinal)
            || appProductVersion?.StartsWith(version + "+", StringComparison.Ordinal) == true);
        Assert.Contains("#ifndef AppVersion", installerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("#define AppVersion \"", installerSource, StringComparison.Ordinal);
        Assert.Contains("Directory.Build.props", installerBuild, StringComparison.Ordinal);
        Assert.Contains("/DAppVersion=", installerBuild, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found from the test output path.");
    }

    [Fact]
    public void TemporaryDownloadNamesUseThePendingLabelUntilTrustedMetadataArrives()
    {
        Assert.Null(DownloadPresentation.TrustedFileName("download"));
        Assert.Null(DownloadPresentation.TrustedFileName("미확인 197533.crdownload"));
        Assert.Null(DownloadPresentation.TrustedFileName("C:\\Downloads\\sample.partial"));
        Assert.Equal("최종 이름.zip", DownloadPresentation.TrustedFileName("C:\\Downloads\\최종 이름.zip"));

        var pending = CreateJob(BrowserTransferState.InProgress, RoutingState.WaitingForSelection) with
        {
            CurrentFileName = "download",
        };
        Assert.Equal(DownloadPresentation.PendingFileName, DownloadPresentation.DisplayFileName(pending));
    }

    [Theory]
    [InlineData(RoutingState.WaitingForSelection)]
    [InlineData(RoutingState.SelectionReady)]
    [InlineData(RoutingState.Skipped)]
    [InlineData(RoutingState.NotRequired)]
    [InlineData(RoutingState.Failed)]
    public void CancellationPresentationDependsOnlyOnBrowserState(RoutingState routingState)
    {
        var job = CreateJob(BrowserTransferState.Cancelled, routingState);

        Assert.True(DownloadPresentation.IsCancelled(job));
        Assert.False(DownloadPresentation.CanChangeRoute(job));
        Assert.Empty(DownloadJobQueries.ActiveSelections([job]));
    }

    [Fact]
    public async Task FolderTreeLoadsOnlyTheExpandedLevelAndRejectsRootEscape()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "download-router-tree-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(root, "kr")).FullName;
            Directory.CreateDirectory(Path.Combine(first, "모야지"));
            Directory.CreateDirectory(Path.Combine(root, new string('긴', 110)));
            var provider = new SafeFolderTreeProvider(new DownloadRouter.Core.Paths.PathBoundaryValidator());

            var rootChildren = await provider.GetChildrenAsync(root, root, CancellationToken.None);
            Assert.Contains(rootChildren.Entries, entry => entry.Name == "kr");
            Assert.DoesNotContain(rootChildren.Entries, entry => entry.Name == "모야지");
            Assert.Contains(rootChildren.Entries, entry => entry.Name.Length >= 100);

            var nested = await provider.GetChildrenAsync(root, first, CancellationToken.None);
            Assert.Equal("모야지", Assert.Single(nested.Entries).Name);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                provider.GetChildrenAsync(root, Path.GetDirectoryName(root)!, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DownloadJob CreateJob(BrowserTransferState browserState, RoutingState routingState)
        => new(
            Guid.NewGuid(), BrowserKind.Whale, Guid.NewGuid().ToString("N"), "sample.bin", "sample.bin",
            null, null, null, null, "https://example.com", Guid.NewGuid(), null, null, null,
            browserState, routingState, null, null, DateTimeOffset.UtcNow, null);
}
