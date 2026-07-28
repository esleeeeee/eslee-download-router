using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

public static class SelectionPromptPolicy
{
    public static readonly TimeSpan AutoPromptWindow = TimeSpan.FromMinutes(30);

    public static bool IsAutoPromptEligible(DownloadJob job, DateTimeOffset now)
    {
        if (!job.IsSelectionPending || job.IsBrowserRecordStale)
        {
            return false;
        }

        var lastActivity = job.LastBrowserEventAt ?? job.CreatedAt;
        return lastActivity <= now && now - lastActivity <= AutoPromptWindow;
    }

    public static bool IsPreviousSessionPending(DownloadJob job, DateTimeOffset now)
        => job.IsSelectionPending && !IsAutoPromptEligible(job, now);
}
