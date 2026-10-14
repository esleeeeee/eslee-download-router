using System.Text.RegularExpressions;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Tests;

public sealed class RoutingControlPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-14T00:00:00Z");

    [Fact]
    public void PauseValuesRoundTripAndExpire()
    {
        var timed = RoutingPausePolicy.ToStoredValue(30, Now);
        Assert.Equal(new RoutingPauseStatus(true, Now.AddMinutes(30)), RoutingPausePolicy.Evaluate(timed, Now));
        Assert.Equal(new RoutingPauseStatus(false, null), RoutingPausePolicy.Evaluate(timed, Now.AddMinutes(30)));
        Assert.True(RoutingPausePolicy.IsStale(timed, Now.AddMinutes(31)));
        Assert.False(RoutingPausePolicy.IsStale(timed, Now));

        Assert.Equal("indefinite", RoutingPausePolicy.ToStoredValue(null, Now));
        Assert.Equal(new RoutingPauseStatus(true, null), RoutingPausePolicy.Evaluate("indefinite", Now.AddYears(5)));
        Assert.Equal(new RoutingPauseStatus(false, null), RoutingPausePolicy.Evaluate(null, Now));
        Assert.Equal(new RoutingPauseStatus(false, null), RoutingPausePolicy.Evaluate("garbage", Now));
        Assert.True(RoutingPausePolicy.IsStale("garbage", Now));
        Assert.EndsWith("+00:00", RoutingPausePolicy.ToStoredValue(1440, Now.ToOffset(TimeSpan.FromHours(9))), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public void PauseDurationsOutsideOneDayAreRejected(int minutes)
    {
        Assert.Throws<ArgumentException>(() => RoutingPausePolicy.ToStoredValue(minutes, Now));
    }

    [Fact]
    public void RetentionAcceptsOnlyTheOfferedChoicesAndDefaultsToKeepingEverything()
    {
        Assert.Equal([0, 7, 30, 90, 180, 365], HistoryRetentionPolicy.AllowedDays);
        foreach (var days in HistoryRetentionPolicy.AllowedDays)
        {
            HistoryRetentionPolicy.Validate(days);
            Assert.Equal(days, HistoryRetentionPolicy.Parse(days.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        Assert.Throws<ArgumentException>(() => HistoryRetentionPolicy.Validate(1));
        Assert.Throws<ArgumentException>(() => HistoryRetentionPolicy.Validate(-7));
        Assert.Equal(0, HistoryRetentionPolicy.Parse(null));
        Assert.Equal(0, HistoryRetentionPolicy.Parse("14"));
        Assert.Equal(0, HistoryRetentionPolicy.Parse("seven"));
    }

    [Fact]
    public void OnlyFinishedHistoryIsEligibleForCleanup()
    {
        var cutoff = HistoryRetentionPolicy.Cutoff(30, Now);
        var old = Now.AddDays(-40);
        var job = Job(BrowserTransferState.Complete, RoutingState.Completed, old, old);

        Assert.True(HistoryRetentionPolicy.IsEligible(job, cutoff));
        Assert.True(HistoryRetentionPolicy.IsEligible(job with { RoutingState = RoutingState.Skipped }, cutoff));
        Assert.True(HistoryRetentionPolicy.IsEligible(job with { BrowserState = BrowserTransferState.Cancelled, RoutingState = RoutingState.NotRequired }, cutoff));
        Assert.True(HistoryRetentionPolicy.IsEligible(job with { BrowserState = BrowserTransferState.Interrupted, RoutingState = RoutingState.Failed }, cutoff));
        Assert.True(HistoryRetentionPolicy.IsEligible(job with { CompletedAt = null }, cutoff));

        Assert.False(HistoryRetentionPolicy.IsEligible(job with { CompletedAt = Now.AddDays(-1) }, cutoff));
        Assert.False(HistoryRetentionPolicy.IsEligible(job with { RoutingState = RoutingState.Failed }, cutoff));
        Assert.False(HistoryRetentionPolicy.IsEligible(job with { RoutingState = RoutingState.Moving }, cutoff));
        Assert.False(HistoryRetentionPolicy.IsEligible(job with { RoutingState = RoutingState.RetryPending }, cutoff));
        Assert.False(HistoryRetentionPolicy.IsEligible(job with { RoutingState = RoutingState.WaitingForSelection }, cutoff));
        Assert.False(HistoryRetentionPolicy.IsEligible(job with { RoutingState = RoutingState.SelectionReady }, cutoff));
        Assert.False(HistoryRetentionPolicy.IsEligible(job with { RoutingState = RoutingState.NotRequired }, cutoff));
        Assert.False(HistoryRetentionPolicy.IsEligible(job with { BrowserState = BrowserTransferState.InProgress, RoutingState = RoutingState.Skipped }, cutoff));
    }

    [Fact]
    public void NewCommandsAreAllowedForTheAppButNeverForwardedFromTheBrowser()
    {
        string[] appOnly = ["routing.status", "routing.pause", "routing.resume", "history.retention.get", "history.retention.set"];
        foreach (var command in appOnly)
        {
            Assert.Contains(command, ProtocolConstants.AllowedCommands);
            Assert.Contains(command, ProtocolConstants.AppOnlyCommands);
            Assert.False(ProtocolConstants.IsBrowserForwardable(command));
        }

        Assert.All(ProtocolConstants.AppOnlyCommands, command => Assert.Contains(command, ProtocolConstants.AllowedCommands));
        Assert.False(ProtocolConstants.IsBrowserForwardable("shell.execute"));
        Assert.False(ProtocolConstants.IsBrowserForwardable(null));
    }

    [Fact]
    public void EveryCommandTheExtensionSendsIsStillForwarded()
    {
        var protocol = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DownloadRouter.Extension", "src", "protocol.ts"));
        var union = protocol[protocol.IndexOf("export type AgentCommandName", StringComparison.Ordinal)..];
        union = union[..union.IndexOf(';', StringComparison.Ordinal)];
        var commands = Regex.Matches(union, "\"([a-z.-]+)\"").Select(static match => match.Groups[1].Value).ToArray();

        Assert.NotEmpty(commands);
        Assert.Contains("download.started", commands);
        Assert.All(commands, command => Assert.True(ProtocolConstants.IsBrowserForwardable(command), command));
        Assert.Empty(commands.Intersect(ProtocolConstants.AppOnlyCommands));
    }

    [Fact]
    public void TheNativeHostChecksTheBrowserAllowList()
    {
        var nativeHost = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DownloadRouter.NativeHost", "Program.cs"));

        Assert.Contains("ProtocolConstants.IsBrowserForwardable(command.Command)", nativeHost, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowedCommands.Contains", nativeHost, StringComparison.Ordinal);
    }

    private static DownloadJob Job(BrowserTransferState browser, RoutingState routing, DateTimeOffset createdAt, DateTimeOffset? completedAt)
        => new(
            Guid.NewGuid(), BrowserKind.Edge, "1", "a.pdf", "a.pdf", null, null, null, null, null,
            Guid.NewGuid(), null, null, null, browser, routing, null, null, createdAt, completedAt);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DownloadRouter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
