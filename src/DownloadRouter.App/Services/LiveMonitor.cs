using System.Text;
using DownloadRouter.Core.Models;
using Microsoft.UI.Dispatching;

namespace DownloadRouter.App.Services;

/// <summary>One consistent read of the agent state.</summary>
public sealed record LiveSnapshot(
    IReadOnlyList<DownloadJob> Jobs,
    IReadOnlyList<DownloadRule> Rules,
    RoutingPauseStatus Pause,
    bool AgentReachable,
    string Signature,
    DateTimeOffset ReadAt);

public sealed class LiveSnapshotEventArgs(LiveSnapshot snapshot, bool changed) : EventArgs
{
    public LiveSnapshot Snapshot { get; } = snapshot;

    /// <summary>False when the agent returned exactly what the previous poll saw.</summary>
    public bool Changed { get; } = changed;
}

/// <summary>
/// Polls the agent on a background thread (pipe I/O and JSON parsing never run on the UI thread)
/// and raises <see cref="Updated"/> on the UI thread after every poll.
/// </summary>
public sealed class LiveMonitor : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(700);
    private const int PauseStatusEveryTicks = 4;

    private readonly AgentClient agent;
    private readonly DispatcherQueue dispatcher;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim pollGate = new(1, 1);
    private RoutingPauseStatus pause = new(false, null);
    private int tick;
    private Task? loop;

    public LiveMonitor(AgentClient agent, DispatcherQueue dispatcher)
    {
        this.agent = agent;
        this.dispatcher = dispatcher;
    }

    public event EventHandler<LiveSnapshotEventArgs>? Updated;

    public LiveSnapshot? Latest { get; private set; }

    public void Start()
        => loop ??= Task.Run(() => RunAsync(lifetime.Token));

    /// <summary>Polls immediately (for example right after a command) and returns the new snapshot.</summary>
    public async Task<LiveSnapshot?> RefreshNowAsync()
    {
        tick = 0;
        return await Task.Run(() => PollAsync(lifetime.Token)).ConfigureAwait(true);
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await PollAsync(cancellationToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<LiveSnapshot?> PollAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Latest;
        }

        await pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LiveSnapshot snapshot;
            try
            {
                var jobs = AgentClient.ReadData<List<DownloadJob>>(
                    await agent.SendAsync("jobs.list", cancellationToken: cancellationToken).ConfigureAwait(false)) ?? [];
                var rules = AgentClient.ReadData<List<DownloadRule>>(
                    await agent.SendAsync("rules.list", cancellationToken: cancellationToken).ConfigureAwait(false)) ?? [];
                if (tick++ % PauseStatusEveryTicks == 0)
                {
                    var status = await agent.SendAsync("routing.status", cancellationToken: cancellationToken).ConfigureAwait(false);
                    pause = status.Success ? AgentClient.ReadData<RoutingPauseStatus>(status) ?? pause : pause;
                }

                snapshot = new LiveSnapshot(jobs, rules, pause, true, Sign(jobs, rules, pause), DateTimeOffset.Now);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                snapshot = new LiveSnapshot(
                    Latest?.Jobs ?? [],
                    Latest?.Rules ?? [],
                    pause,
                    false,
                    "unreachable",
                    DateTimeOffset.Now);
            }

            var changed = !string.Equals(Latest?.Signature, snapshot.Signature, StringComparison.Ordinal);
            Latest = snapshot;
            dispatcher.TryEnqueue(() => Updated?.Invoke(this, new LiveSnapshotEventArgs(snapshot, changed)));
            return snapshot;
        }
        finally
        {
            pollGate.Release();
        }
    }

    private static string Sign(IReadOnlyList<DownloadJob> jobs, IReadOnlyList<DownloadRule> rules, RoutingPauseStatus pause)
    {
        var builder = new StringBuilder();
        builder.Append(pause.IsPaused).Append(pause.PausedUntil?.ToString("O")).Append('|');
        foreach (var rule in rules)
        {
            builder.Append(rule.Id.ToString("N")).Append(rule.UpdatedAt.UtcTicks).Append(rule.IsEnabled).Append(';');
        }

        builder.Append('|');
        foreach (var job in jobs)
        {
            builder.Append(job.Id.ToString("N")).Append(':')
                .Append(job.CurrentFileName).Append(':')
                .Append(job.BrowserState).Append(':')
                .Append(job.RoutingState).Append(':')
                .Append(job.SelectionPromptState).Append(':')
                .Append(job.SelectedRelativeFolder).Append(':')
                .Append(job.SelectedDestinationFolder).Append(':')
                .Append(job.SelectedFileName).Append(':')
                .Append(job.FinalPath).Append(':')
                .Append(job.RuleId.ToString("N")).Append(':')
                .Append(job.IsRuleTentative).Append(':')
                .Append(job.CompletedAt?.UtcTicks).Append(':')
                .Append(job.ErrorCode).Append(';');
        }

        return builder.ToString();
    }
}
