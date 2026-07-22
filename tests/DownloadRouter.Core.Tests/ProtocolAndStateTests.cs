using System.Buffers.Binary;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;

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
