using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Tests;

public sealed class DownloadRegistrationPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-02T03:08:27Z");

    private static DownloadStartedPayload Payload(string? state, DateTimeOffset? startedAt)
        => new(
            "Whale",
            "500",
            "file.bin",
            null,
            "https://example.com/downloads",
            "https://example.com/file.bin",
            null,
            null,
            state,
            startedAt);

    [Theory]
    [InlineData("complete")]
    [InlineData("Complete")]
    [InlineData("interrupted")]
    [InlineData("cancelled")]
    public void AFinishedTransferCanNeverStartANewJob(string state)
        => Assert.Equal(
            DownloadRegistrationDecision.RejectAlreadyFinished,
            DownloadRegistrationPolicy.Classify(Payload(state, Now.AddDays(-1)), Now));

    [Fact]
    public void AnUnfinishedRecordFromAnEarlierSessionIsRejected()
        => Assert.Equal(
            DownloadRegistrationDecision.RejectStartedBeforeSession,
            DownloadRegistrationPolicy.Classify(Payload("in_progress", Now.AddDays(-1)), Now));

    [Fact]
    public void ALiveTransferIsTracked()
        => Assert.Equal(
            DownloadRegistrationDecision.Track,
            DownloadRegistrationPolicy.Classify(Payload("in_progress", Now.AddSeconds(-2)), Now));

    [Fact]
    public void TheLiveWindowBoundaryIsInclusive()
    {
        var edge = Now - DownloadRegistrationPolicy.LiveDownloadWindow;
        Assert.Equal(
            DownloadRegistrationDecision.Track,
            DownloadRegistrationPolicy.Classify(Payload("in_progress", edge), Now));
        Assert.Equal(
            DownloadRegistrationDecision.RejectStartedBeforeSession,
            DownloadRegistrationPolicy.Classify(Payload("in_progress", edge.AddSeconds(-1)), Now));
    }

    [Fact]
    public void AMissingStateKeepsTheExistingFailOpenBehaviourForOlderExtensions()
    {
        Assert.Equal(
            DownloadRegistrationDecision.Track,
            DownloadRegistrationPolicy.Classify(Payload(null, null), Now));
        Assert.Equal(
            DownloadRegistrationDecision.Track,
            DownloadRegistrationPolicy.Classify(Payload(null, Now), Now));
    }

    [Fact]
    public void AStartTimeInTheFutureIsNotTreatedAsHistory()
        => Assert.Equal(
            DownloadRegistrationDecision.Track,
            DownloadRegistrationPolicy.Classify(Payload("in_progress", Now.AddMinutes(5)), Now));

    [Fact]
    public void RejectionReasonsAreStableDiagnosticStrings()
    {
        Assert.Equal("already-finished", DownloadRegistrationPolicy.DescribeRejection(
            DownloadRegistrationDecision.RejectAlreadyFinished));
        Assert.Equal("started-before-session", DownloadRegistrationPolicy.DescribeRejection(
            DownloadRegistrationDecision.RejectStartedBeforeSession));
        Assert.Equal("tracked", DownloadRegistrationPolicy.DescribeRejection(
            DownloadRegistrationDecision.Track));
    }

    [Fact]
    public void TheLiveWindowStaysBoundedSoHistoryCannotSlipThrough()
        => Assert.True(DownloadRegistrationPolicy.LiveDownloadWindow <= TimeSpan.FromMinutes(15));
}
