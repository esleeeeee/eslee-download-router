using System.Buffers.Binary;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Settings;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using System.Text.Json;
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
    public void SkippedByUserIsTerminalAndCannotReturnToPending()
    {
        var stateMachine = new DownloadJobStateMachine();
        var skipped = CreateJob(BrowserTransferState.Complete, RoutingState.Skipped);

        Assert.True(skipped.IsTerminal);
        Assert.False(skipped.IsSelectionPending);
        Assert.False(stateMachine.CanTransition(RoutingState.Skipped, RoutingState.WaitingForSelection));
        Assert.Empty(DownloadJobQueries.ActiveSelections([skipped]));
        Assert.Empty(DownloadJobQueries.AutomaticSelections([skipped], DateTimeOffset.UtcNow));
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
    public void CancelledSelectionIsRemovedWithoutBreakingFifoOrCreatingDuplicates()
    {
        var queue = new SelectionPromptQueue();
        var cancelled = Guid.NewGuid();
        var next = Guid.NewGuid();
        Assert.True(queue.Enqueue(cancelled));
        Assert.True(queue.Enqueue(next));
        Assert.False(queue.Enqueue(next));

        Assert.True(queue.Remove(cancelled));
        Assert.False(queue.Remove(cancelled));
        Assert.True(queue.TryDequeue(out var remaining));
        Assert.Equal(next, remaining);
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
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{\"dataDirectory\":\"preserve-me\",\"fileStability\":{\"maxWaitSeconds\":30}}");
            var store = new AppPreferencesStore(path);
            store.Save(new AppPreferences(WindowCloseBehavior.ExitApplication, "Dark"));
            var restored = store.Load();
            Assert.Equal(WindowCloseBehavior.ExitApplication, restored.CloseBehavior);
            Assert.Equal(AppThemePreference.Dark, restored.ThemePreference);
            var savedJson = File.ReadAllText(path);
            Assert.Contains("preserve-me", savedJson, StringComparison.Ordinal);
            Assert.Contains("maxWaitSeconds", savedJson, StringComparison.Ordinal);

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

    [Fact]
    public void BrandingAssetsAreValidAndWiredToEveryUserVisibleSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var iconPath = Path.Combine(repositoryRoot, "assets", "branding", "eslee-download-router.ico");
        var appProject = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "DownloadRouter.App", "DownloadRouter.App.csproj"));
        var installerSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "installer", "DownloadRouter.iss"));
        var mainWindowLifetime = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "DownloadRouter.App", "MainWindow.Lifetime.cs"));
        var selectionWindow = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "DownloadRouter.App", "FolderSelectionWindow.cs"));
        var productBranding = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "DownloadRouter.App", "ProductBranding.cs"));
        var trayIconHost = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "DownloadRouter.App", "TrayIconHost.cs"));
        var brandingGenerator = File.ReadAllText(
            Path.Combine(repositoryRoot, "scripts", "generate-branding-assets.ps1"));
        var extensionRoot = Path.Combine(repositoryRoot, "src", "DownloadRouter.Extension");
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(extensionRoot, "manifest.json")));

        Assert.Contains(
            @"<ApplicationIcon>..\..\assets\branding\eslee-download-router.ico</ApplicationIcon>",
            appProject,
            StringComparison.Ordinal);
        Assert.Contains(
            @"SetupIconFile=..\assets\branding\eslee-download-router.ico",
            installerSource,
            StringComparison.Ordinal);
        Assert.Contains("ProductBranding.ApplyWindowIcon(appWindow)", mainWindowLifetime, StringComparison.Ordinal);
        Assert.Contains("ProductBranding.ApplyWindowIcon(appWindow)", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("ApplicationIconResourceId = 32512", productBranding, StringComparison.Ordinal);
        Assert.Contains("$artworkScale = 1.18", brandingGenerator, StringComparison.Ordinal);
        Assert.Contains("16 = 1.15", brandingGenerator, StringComparison.Ordinal);
        Assert.Contains("20 = 1.15", brandingGenerator, StringComparison.Ordinal);
        Assert.Contains("ExtractIconEx(executablePath", trayIconHost, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadImage", trayIconHost, StringComparison.Ordinal);

        var expectedExtensionSizes = new[] { 16, 32, 48, 128 };
        var icons = manifest.RootElement.GetProperty("icons");
        foreach (var size in expectedExtensionSizes)
        {
            var relativePath = icons.GetProperty(size.ToString()).GetString();
            Assert.Equal($"icons/icon-{size}.png", relativePath);
            Assert.True(File.Exists(Path.Combine(extensionRoot, relativePath!)));
        }

        using var stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        Assert.Equal(9, count);

        var actualSizes = new List<int>();
        for (var index = 0; index < count; index++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            Assert.True(reader.ReadUInt32() > 0);
            Assert.True(reader.ReadUInt32() > 0);
            actualSizes.Add(width == 0 ? 256 : width);
            Assert.Equal(width, height);
        }

        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }, actualSizes);
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
    public void HiddenMainWindowShowsAndActivatesOnlyTheIndependentSelectionWindow()
    {
        var operations = new FakeSelectionWindowActivationOperations(
            mainVisible: false,
            mainIconic: false,
            selectionVisible: false,
            selectionIconic: false)
        {
            DirectForegroundSucceeds = true,
        };

        var result = new SelectionWindowActivationCoordinator(operations)
            .Activate(operations.MainWindowHandle, operations.SelectionWindowHandle);

        Assert.True(result.SelectionAfter.IsVisible);
        Assert.False(result.SelectionAfter.IsIconic);
        Assert.True(result.SelectionAfter.IsForeground);
        Assert.False(result.MainAfter.IsVisible);
        Assert.False(result.MainAfter.IsIconic);
        Assert.All(operations.MutatedWindowHandles, handle =>
            Assert.Equal(operations.SelectionWindowHandle, handle));
    }

    [Fact]
    public void MinimizedMainWindowRemainsMinimizedWhenAttachedInputActivatesSelection()
    {
        var operations = new FakeSelectionWindowActivationOperations(
            mainVisible: true,
            mainIconic: true,
            selectionVisible: false,
            selectionIconic: false)
        {
            AttachedForegroundSucceeds = true,
        };

        var result = new SelectionWindowActivationCoordinator(operations)
            .Activate(operations.MainWindowHandle, operations.SelectionWindowHandle);

        Assert.False(result.DirectForegroundSucceeded);
        Assert.True(result.AttachedForegroundSucceeded);
        Assert.True(result.SelectionAfter.IsForeground);
        Assert.True(result.MainAfter.IsVisible);
        Assert.True(result.MainAfter.IsIconic);
        Assert.All(operations.MutatedWindowHandles, handle =>
            Assert.Equal(operations.SelectionWindowHandle, handle));
    }

    [Fact]
    public void IconicSelectionWindowIsShownNormalBeforeForegroundActivation()
    {
        var operations = new FakeSelectionWindowActivationOperations(
            mainVisible: true,
            mainIconic: true,
            selectionVisible: true,
            selectionIconic: true)
        {
            DirectForegroundSucceeds = true,
        };

        var result = new SelectionWindowActivationCoordinator(operations)
            .Activate(operations.MainWindowHandle, operations.SelectionWindowHandle);

        Assert.True(result.SelectionBefore.IsIconic);
        Assert.True(result.ShowNormalSucceeded);
        Assert.True(result.SelectionAfterShow.IsVisible);
        Assert.False(result.SelectionAfterShow.IsIconic);
    }

    [Fact]
    public void TaskbarFlashIsUsedOnlyAfterAllForegroundAttemptsFail()
    {
        var operations = new FakeSelectionWindowActivationOperations(
            mainVisible: true,
            mainIconic: true,
            selectionVisible: false,
            selectionIconic: false);

        var result = new SelectionWindowActivationCoordinator(operations)
            .Activate(operations.MainWindowHandle, operations.SelectionWindowHandle);

        Assert.False(result.DirectForegroundSucceeded);
        Assert.False(result.AttachedForegroundSucceeded);
        Assert.False(result.RaisedForegroundSucceeded);
        Assert.True(result.FlashFallbackUsed);
        Assert.True(operations.FlashUsed);
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

    private sealed class FakeSelectionWindowActivationOperations :
        ISelectionWindowActivationOperations
    {
        public nint MainWindowHandle { get; } = new(101);
        public nint SelectionWindowHandle { get; } = new(202);
        public bool DirectForegroundSucceeds { get; init; }
        public bool AttachedForegroundSucceeds { get; init; }
        public bool RaisedForegroundSucceeds { get; init; }
        public bool FlashUsed { get; private set; }
        public List<nint> MutatedWindowHandles { get; } = [];

        private bool mainVisible;
        private bool mainIconic;
        private bool selectionVisible;
        private bool selectionIconic;
        private nint foregroundHandle = new(303);

        public FakeSelectionWindowActivationOperations(
            bool mainVisible,
            bool mainIconic,
            bool selectionVisible,
            bool selectionIconic)
        {
            this.mainVisible = mainVisible;
            this.mainIconic = mainIconic;
            this.selectionVisible = selectionVisible;
            this.selectionIconic = selectionIconic;
        }

        public NativeWindowState Capture(nint windowHandle)
            => windowHandle == MainWindowHandle
                ? new NativeWindowState(
                    MainWindowHandle,
                    mainVisible,
                    mainIconic,
                    0,
                    foregroundHandle)
                : new NativeWindowState(
                    SelectionWindowHandle,
                    selectionVisible,
                    selectionIconic,
                    0,
                    foregroundHandle);

        public bool ShowSelectionNormal(nint selectionWindowHandle)
        {
            TrackSelectionMutation(selectionWindowHandle);
            selectionVisible = true;
            selectionIconic = false;
            return true;
        }

        public void ActivateSelection()
        {
            MutatedWindowHandles.Add(SelectionWindowHandle);
        }

        public bool BringSelectionToTop(nint selectionWindowHandle)
        {
            TrackSelectionMutation(selectionWindowHandle);
            return true;
        }

        public bool TrySetSelectionForeground(nint selectionWindowHandle)
        {
            TrackSelectionMutation(selectionWindowHandle);
            if (DirectForegroundSucceeds)
            {
                foregroundHandle = selectionWindowHandle;
            }

            return DirectForegroundSucceeds;
        }

        public bool TryAttachInputAndActivate(nint selectionWindowHandle)
        {
            TrackSelectionMutation(selectionWindowHandle);
            if (AttachedForegroundSucceeds)
            {
                foregroundHandle = selectionWindowHandle;
            }

            return AttachedForegroundSucceeds;
        }

        public bool RaiseSelectionAboveOtherWindows(nint selectionWindowHandle)
        {
            TrackSelectionMutation(selectionWindowHandle);
            if (RaisedForegroundSucceeds)
            {
                foregroundHandle = selectionWindowHandle;
            }

            return RaisedForegroundSucceeds;
        }

        public void FlashSelection(nint selectionWindowHandle)
        {
            TrackSelectionMutation(selectionWindowHandle);
            FlashUsed = true;
        }

        private void TrackSelectionMutation(nint windowHandle)
        {
            Assert.Equal(SelectionWindowHandle, windowHandle);
            MutatedWindowHandles.Add(windowHandle);
        }
    }
}
