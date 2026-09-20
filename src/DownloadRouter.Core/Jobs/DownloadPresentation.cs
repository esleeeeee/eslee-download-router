using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

public static class DownloadPresentation
{
    public const string PendingFileName = "파일 이름 확인 중…";

    public static string DisplayFileName(DownloadJob job)
        => (job.RoutingState == RoutingState.Completed ? Path.GetFileName(job.FinalPath) : null)
            ?? job.SelectedFileName ?? TrustedFileName(job.CurrentFileName) ?? PendingFileName;

    public static string? TrustedFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var fileName = Path.GetFileName(value.Trim());
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Equals("download", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fileName;
    }

    public static bool IsCancelled(DownloadJob job)
        => job.BrowserState == BrowserTransferState.Cancelled;

    public static bool CanChangeRoute(DownloadJob job)
        => job.BrowserState is BrowserTransferState.InProgress or BrowserTransferState.Complete
            && job.RoutingState is RoutingState.WaitingForSelection
                or RoutingState.SelectionReady
                or RoutingState.Skipped
                or RoutingState.Completed;
}
