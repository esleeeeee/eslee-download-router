using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

public static class DownloadJobQueries
{
    public static IReadOnlyList<DownloadJob> ActiveSelections(IEnumerable<DownloadJob> jobs)
        => jobs.Where(static job => job.IsSelectionPending).ToList();

    public static IReadOnlyList<DownloadJob> AutomaticSelections(
        IEnumerable<DownloadJob> jobs,
        DateTimeOffset now)
        => jobs
            .Where(job => SelectionPromptPolicy.IsAutoPromptEligible(job, now))
            .OrderBy(static job => job.LastBrowserEventAt ?? job.CreatedAt)
            .ThenBy(static job => job.CreatedAt)
            .ToList();

    public static DashboardCounts CountDashboard(IEnumerable<DownloadJob> jobs)
    {
        var snapshot = jobs.ToList();
        return new DashboardCounts(
            snapshot.Count(static job => job.BrowserState == BrowserTransferState.InProgress),
            snapshot.Count(static job => job.IsSelectionPending),
            snapshot.Count(static job => job.RoutingState == RoutingState.Completed),
            snapshot.Count(static job => job.BrowserState is BrowserTransferState.Cancelled or BrowserTransferState.Interrupted),
            snapshot.Count(static job => job.RoutingState is RoutingState.RetryPending or RoutingState.Failed));
    }
}
