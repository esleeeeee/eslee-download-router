using DownloadRouter.App.Services;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App;

/// <summary>
/// "File moved" notifications. Only moves this app instance watched happen are announced: jobs
/// that were already moved when the app started are remembered silently on the first poll.
/// </summary>
public sealed partial class MainWindow
{
    private readonly HashSet<Guid> announcedMoves = [];
    private bool moveBaselineTaken;

    private void DetectCompletedMoves(LiveSnapshot snapshot)
    {
        var completed = snapshot.Jobs
            .Where(static job => job.RoutingState == RoutingState.Completed && !string.IsNullOrEmpty(job.FinalPath))
            .ToArray();
        if (!moveBaselineTaken)
        {
            moveBaselineTaken = true;
            announcedMoves.UnionWith(completed.Select(static job => job.Id));
            return;
        }

        foreach (var job in completed)
        {
            if (!announcedMoves.Add(job.Id) || !AppServices.Preferences.NotifyOnMoveCompleted)
            {
                continue;
            }

            var folder = Path.GetDirectoryName(job.FinalPath!) ?? job.FinalPath!;
            var fileName = Path.GetFileName(job.FinalPath!);
            if (!AppServices.Notifications.ShowMoveCompleted(job.Id, fileName, folder))
            {
                trayIcon?.ShowMoveNotification(fileName, folder);
            }
        }
    }
}
