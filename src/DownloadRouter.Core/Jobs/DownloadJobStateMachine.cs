using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

public sealed class DownloadJobStateMachine
{
    private static readonly IReadOnlyDictionary<BrowserTransferState, ISet<BrowserTransferState>> AllowedBrowserTransitions
        = new Dictionary<BrowserTransferState, ISet<BrowserTransferState>>
        {
            [BrowserTransferState.InProgress] = BrowserSet(BrowserTransferState.Complete, BrowserTransferState.Cancelled, BrowserTransferState.Interrupted),
            [BrowserTransferState.Complete] = BrowserSet(),
            [BrowserTransferState.Cancelled] = BrowserSet(),
            [BrowserTransferState.Interrupted] = BrowserSet(),
        };

    private static readonly IReadOnlyDictionary<RoutingState, ISet<RoutingState>> AllowedRoutingTransitions
        = new Dictionary<RoutingState, ISet<RoutingState>>
        {
            [RoutingState.WaitingForSelection] = RoutingSet(RoutingState.SelectionReady, RoutingState.Skipped, RoutingState.Failed, RoutingState.NotRequired),
            [RoutingState.SelectionReady] = RoutingSet(RoutingState.Moving, RoutingState.Skipped, RoutingState.Failed, RoutingState.NotRequired),
            [RoutingState.NotRequired] = RoutingSet(RoutingState.Moving, RoutingState.Failed, RoutingState.Skipped),
            [RoutingState.Moving] = RoutingSet(RoutingState.Completed, RoutingState.RetryPending, RoutingState.Failed),
            [RoutingState.RetryPending] = RoutingSet(RoutingState.Moving, RoutingState.Failed, RoutingState.Skipped),
            [RoutingState.Failed] = RoutingSet(RoutingState.RetryPending),
            [RoutingState.Completed] = RoutingSet(RoutingState.Moving),
            [RoutingState.Skipped] = RoutingSet(RoutingState.SelectionReady, RoutingState.Moving),
        };

    public bool CanTransition(BrowserTransferState current, BrowserTransferState next)
        => current == next || (AllowedBrowserTransitions.TryGetValue(current, out var allowed) && allowed.Contains(next));

    public bool CanTransition(RoutingState current, RoutingState next)
        => current == next || (AllowedRoutingTransitions.TryGetValue(current, out var allowed) && allowed.Contains(next));

    public void EnsureCanTransition(BrowserTransferState current, BrowserTransferState next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Invalid browser transfer transition: {current} -> {next}.");
        }
    }

    public void EnsureCanTransition(RoutingState current, RoutingState next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Invalid routing transition: {current} -> {next}.");
        }
    }

    private static ISet<BrowserTransferState> BrowserSet(params BrowserTransferState[] values)
        => new HashSet<BrowserTransferState>(values);

    private static ISet<RoutingState> RoutingSet(params RoutingState[] values)
        => new HashSet<RoutingState>(values);
}
