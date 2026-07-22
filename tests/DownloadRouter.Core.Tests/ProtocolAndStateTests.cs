using System.Buffers.Binary;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;

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

    private static DownloadJob CreateJob(BrowserTransferState browserState, RoutingState routingState)
        => new(
            Guid.NewGuid(), BrowserKind.Whale, Guid.NewGuid().ToString("N"), "sample.bin", "sample.bin",
            null, null, null, null, "https://example.com", Guid.NewGuid(), null, null, null,
            browserState, routingState, null, null, DateTimeOffset.UtcNow, null);
}
