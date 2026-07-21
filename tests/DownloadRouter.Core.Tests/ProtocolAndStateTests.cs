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
        Assert.False(stateMachine.CanTransition(DownloadJobStatus.WaitingForDownload, DownloadJobStatus.Moving));
        Assert.Throws<InvalidOperationException>(() =>
            stateMachine.EnsureCanTransition(DownloadJobStatus.WaitingForDownload, DownloadJobStatus.Moving));
    }

    [Fact]
    public void CompletedAndCancelledJobsAreTerminal()
    {
        var stateMachine = new DownloadJobStateMachine();
        Assert.False(stateMachine.CanTransition(DownloadJobStatus.Completed, DownloadJobStatus.RetryPending));
        Assert.False(stateMachine.CanTransition(DownloadJobStatus.Cancelled, DownloadJobStatus.WaitingForDownload));
    }
}
