using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

public sealed class DownloadJobStateMachine
{
    private static readonly IReadOnlyDictionary<DownloadJobStatus, ISet<DownloadJobStatus>> AllowedTransitions
        = new Dictionary<DownloadJobStatus, ISet<DownloadJobStatus>>
        {
            [DownloadJobStatus.Detected] = Set(DownloadJobStatus.WaitingForDownload, DownloadJobStatus.WaitingForSelection, DownloadJobStatus.Cancelled, DownloadJobStatus.Failed),
            [DownloadJobStatus.WaitingForDownload] = Set(DownloadJobStatus.ReadyToMove, DownloadJobStatus.WaitingForSelection, DownloadJobStatus.Cancelled, DownloadJobStatus.Interrupted, DownloadJobStatus.Failed),
            [DownloadJobStatus.WaitingForSelection] = Set(DownloadJobStatus.WaitingForDownload, DownloadJobStatus.ReadyToMove, DownloadJobStatus.Cancelled, DownloadJobStatus.Interrupted, DownloadJobStatus.Failed),
            [DownloadJobStatus.ReadyToMove] = Set(DownloadJobStatus.Moving, DownloadJobStatus.Cancelled, DownloadJobStatus.Failed),
            [DownloadJobStatus.Moving] = Set(DownloadJobStatus.Completed, DownloadJobStatus.RetryPending, DownloadJobStatus.Failed),
            [DownloadJobStatus.RetryPending] = Set(DownloadJobStatus.ReadyToMove, DownloadJobStatus.Moving, DownloadJobStatus.Failed),
            [DownloadJobStatus.Interrupted] = Set(DownloadJobStatus.WaitingForDownload, DownloadJobStatus.WaitingForSelection, DownloadJobStatus.ReadyToMove, DownloadJobStatus.Failed),
            [DownloadJobStatus.Failed] = Set(DownloadJobStatus.RetryPending),
            [DownloadJobStatus.Completed] = Set(),
            [DownloadJobStatus.Cancelled] = Set(),
        };

    public bool CanTransition(DownloadJobStatus current, DownloadJobStatus next)
        => current == next || (AllowedTransitions.TryGetValue(current, out var allowed) && allowed.Contains(next));

    public void EnsureCanTransition(DownloadJobStatus current, DownloadJobStatus next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Invalid download job transition: {current} -> {next}.");
        }
    }

    private static ISet<DownloadJobStatus> Set(params DownloadJobStatus[] values)
        => new HashSet<DownloadJobStatus>(values);
}
