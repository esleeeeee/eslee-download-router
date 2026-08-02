using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Tests;

public sealed class DownloadRegistrationPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-02T03:08:27Z");

    private static readonly string SupportedBuild =
        DownloadRegistrationPolicy.SupportedExtensionBuilds.First();

    /// <summary>Builds a payload from a supported extension build unless one is given.</summary>
    private static DownloadStartedPayload Payload(string? state, DateTimeOffset? startedAt)
        => Build(state, startedAt, SupportedBuild);

    private static DownloadStartedPayload Build(
        string? state,
        DateTimeOffset? startedAt,
        string? extensionBuild)
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
            startedAt,
            extensionBuild);

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
    public void AnOlderExtensionBuildCanNeverRegisterANewDownload()
    {
        // The browser can keep serving a cached older service worker after an upgrade.
        // Such a build sends no state, so creation must be refused rather than assumed live.
        Assert.Equal(
            DownloadRegistrationDecision.RejectUnsupportedExtension,
            DownloadRegistrationPolicy.Classify(Build(null, null, null), Now));
        Assert.Equal(
            DownloadRegistrationDecision.RejectUnsupportedExtension,
            DownloadRegistrationPolicy.Classify(Build("in_progress", Now, null), Now));
        Assert.Equal(
            DownloadRegistrationDecision.RejectUnsupportedExtension,
            DownloadRegistrationPolicy.Classify(Build("in_progress", Now, "   "), Now));
        Assert.Equal(
            DownloadRegistrationDecision.RejectUnsupportedExtension,
            DownloadRegistrationPolicy.Classify(Build("in_progress", Now, "1999.01.01"), Now));
    }

    [Fact]
    public void AKnownBuildWithoutATransferStateIsAlsoRefused()
        => Assert.Equal(
            DownloadRegistrationDecision.RejectUnsupportedExtension,
            DownloadRegistrationPolicy.Classify(Payload(null, Now), Now));

    [Fact]
    public void TheAgentAndExtensionAgreeOnTheBuildIdentifier()
    {
        var extensionSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DownloadRouter.Extension", "src", "download-origin.ts"));

        Assert.Single(DownloadRegistrationPolicy.SupportedExtensionBuilds);
        Assert.Contains(
            $"export const extensionBuild = \"{SupportedBuild}\"",
            extensionSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshGuidanceTellsTheUserWhatToDo()
    {
        Assert.Contains("새로 고", DownloadRegistrationPolicy.ExtensionRefreshGuidance, StringComparison.Ordinal);
        Assert.Contains("다시 시작", DownloadRegistrationPolicy.ExtensionRefreshGuidance, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DownloadRouter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
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
