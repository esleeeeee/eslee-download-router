using DownloadRouter.App.Services;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App;

/// <summary>
/// Automatic "choose a folder" prompts for SelectSubfolder rules. Eligibility comes from the
/// persisted prompt state, one window is open at a time (FIFO), and every decision, including
/// closing the window, is written to the agent before the queue moves on.
/// </summary>
public sealed partial class MainWindow
{
    private readonly SelectionPromptQueue selectionPromptQueue = new();
    private CancellationTokenSource? currentSelectionCancellation;
    private Guid? currentSelectionJobId;
    private DownloadRule? currentSelectionRule;
    private FolderSelectionWindow? currentSelectionWindow;
    private string? currentSelectionSnapshot;
    private bool currentSelectionInvalidated;
    private bool selectionPromptInProgress;
    private int previousAutomaticCount;
    private int shownAutoPromptCount;
    private int sessionAutoPromptTotal;

    private void SynchronizeSelectionPrompt(
        IReadOnlyList<DownloadJob> active,
        IReadOnlyList<DownloadJob> automatic)
    {
        var activeIds = active.Select(static job => job.Id).ToHashSet();
        if (currentSelectionJobId is Guid current && !activeIds.Contains(current))
        {
            WriteWindowActivationDiagnostic(
                $"selection-window invalidated current-job={current:N} active-count={active.Count}");
            currentSelectionInvalidated = true;
            currentSelectionCancellation?.Cancel();
        }
        else if (currentSelectionJobId is Guid activeCurrent)
        {
            var currentJob = active.First(candidate => candidate.Id == activeCurrent);
            var updatedSnapshot = CreateSelectionPromptSnapshot(currentJob, active.Count);
            if (!string.Equals(currentSelectionSnapshot, updatedSnapshot, StringComparison.Ordinal))
            {
                currentSelectionSnapshot = updatedSnapshot;
                if (currentSelectionRule is not null)
                {
                    currentSelectionWindow?.UpdateFileName(DownloadPresentation.DisplayFileName(currentJob));
                    currentSelectionWindow?.UpdateDetails(CreateSelectionDetails(
                        currentJob,
                        currentSelectionRule,
                        Math.Max(0, active.Count - 1),
                        Math.Max(1, shownAutoPromptCount),
                        Math.Max(shownAutoPromptCount, sessionAutoPromptTotal)));
                }
            }
        }

        // Eligibility already comes from the persisted prompt state, so the queue only
        // needs to guard against inserting the same job twice within this session.
        foreach (var job in automatic)
        {
            if (job.Id != currentSelectionJobId && selectionPromptQueue.Enqueue(job.Id))
            {
                sessionAutoPromptTotal++;
            }
        }
    }

    private async Task ProcessNextSelectionPromptAsync(
        IReadOnlyList<DownloadJob> automatic,
        IReadOnlyList<DownloadRule> rules)
    {
        // Claim the single-prompt slot before the first await so a later poll
        // cannot start a second window while the prompt state is being persisted.
        if (selectionPromptInProgress)
        {
            return;
        }

        selectionPromptInProgress = true;
        var promptOpened = false;
        try
        {
            while (selectionPromptQueue.TryDequeue(out var jobId))
            {
                var job = automatic.FirstOrDefault(candidate => candidate.Id == jobId);
                var rule = job is null ? null : rules.FirstOrDefault(candidate => candidate.Id == job.RuleId);
                if (job is null || rule is null)
                {
                    continue;
                }

                // Persist "Shown" before the window opens. A crash or a forced exit therefore
                // cannot replay this prompt on the next launch.
                if (!await RecordSelectionPromptStateAsync([job.Id], SelectionPromptState.Shown))
                {
                    WriteWindowActivationDiagnostic(
                        $"selection-window prompt-state-not-persisted job={job.Id:N} skipping-auto-prompt");
                    continue;
                }

                shownAutoPromptCount++;
                promptOpened = true;
                await ShowSelectionPromptAsync(
                    job,
                    rule,
                    Math.Max(0, automatic.Count - 1),
                    shownAutoPromptCount,
                    Math.Max(shownAutoPromptCount, sessionAutoPromptTotal),
                    automatic.Select(static candidate => candidate.Id).ToArray());
                return;
            }
        }
        finally
        {
            if (!promptOpened)
            {
                selectionPromptInProgress = false;
            }
        }
    }

    /// <summary>
    /// Records the automatic prompt decision in SQLite. Returns false when the agent
    /// could not persist it, in which case the prompt is not opened.
    /// </summary>
    internal async Task<bool> RecordSelectionPromptStateAsync(
        IReadOnlyList<Guid> jobIds,
        SelectionPromptState state)
    {
        if (jobIds.Count == 0)
        {
            return true;
        }

        try
        {
            var response = await AppServices.Agent.SendAsync(
                "selection.prompt-state",
                new SelectionPromptStatePayload(jobIds, state));
            WriteWindowActivationDiagnostic(
                $"selection-prompt-state state={state} count={jobIds.Count} success={response.Success}");
            return response.Success;
        }
        catch (Exception exception)
        {
            WriteWindowActivationDiagnostic(
                $"selection-prompt-state state={state} count={jobIds.Count} failed={exception.GetType().Name}");
            return false;
        }
    }

    private async Task ShowSelectionPromptAsync(
        DownloadJob job,
        DownloadRule rule,
        int otherPendingCount,
        int queuePosition,
        int queueTotal,
        IReadOnlyList<Guid> currentAutoQueueJobIds)
    {
        // The slot was already claimed by ProcessNextSelectionPromptAsync.
        selectionPromptInProgress = true;
        currentSelectionJobId = job.Id;
        currentSelectionRule = rule;
        currentSelectionSnapshot = CreateSelectionPromptSnapshot(job, otherPendingCount + 1);
        currentSelectionInvalidated = false;
        try
        {
            var root = AppServices.PathResolver.Resolve(rule.StorageRoot);
            currentSelectionCancellation = new CancellationTokenSource();
            currentSelectionWindow = CreateFolderSelectionWindow();
            var result = await currentSelectionWindow.ShowAsync(new FolderSelectionRequest(
                root,
                job.SelectedRelativeFolder,
                CreateSelectionDetails(job, rule, otherPendingCount, queuePosition, queueTotal),
                AllowLater: true,
                AllowSkip: true,
                AllowSkipAll: true,
                AllowParentNavigation: true,
                FileName: DownloadPresentation.DisplayFileName(job),
                AllowTenMinuteFolder: true,
                RuleId: rule.Id,
                RecentFolders: RecentFoldersFor(rule.Id)),
                currentSelectionCancellation.Token);

            if (currentSelectionInvalidated)
            {
                return;
            }

            if (result.Action == FolderSelectionAction.Apply)
            {
                var response = await AppServices.Agent.SendAsync(
                    "selection.complete",
                    new SelectionCompletedPayload([job.Id], result.RelativeFolder, result.DestinationFolder, result.FileName, result.UseForTenMinutes));
                if (!response.Success)
                {
                    await ShowErrorAsync(AgentMessages.Describe(response, AppText.Pick("저장 위치를 적용하지 못했습니다.", "Could not apply the folder.")));
                }
            }
            else if (result.Action == FolderSelectionAction.Skip)
            {
                var response = await AppServices.Agent.SendAsync("selection.skip", new SelectionSkippedPayload(job.Id));
                WriteWindowActivationDiagnostic(
                    $"selection-window decision-persisted action=Skip job={job.Id:N} success={response.Success}");
                if (!response.Success)
                {
                    await ShowErrorAsync(AgentMessages.Describe(response, AppText.Pick("이동하지 않기를 적용하지 못했습니다.", "Could not mark the file as not moved.")));
                }
            }
            else if (result.Action == FolderSelectionAction.SkipAll)
            {
                var queuedJobIds = currentAutoQueueJobIds
                    .Append(job.Id)
                    .Distinct()
                    .ToArray();
                var response = await AppServices.Agent.SendAsync(
                    "selection.skip-many",
                    new SelectionsSkippedPayload(queuedJobIds));
                WriteWindowActivationDiagnostic(
                    $"selection-window decision-persisted action=SkipAll count={queuedJobIds.Length} success={response.Success}");
                if (!response.Success)
                {
                    await ShowErrorAsync(AgentMessages.Describe(response, AppText.Pick("대기 파일을 한꺼번에 건너뛰지 못했습니다.", "Could not skip the waiting files.")));
                }
                else
                {
                    selectionPromptQueue.Drain();
                    shownAutoPromptCount = 0;
                    sessionAutoPromptTotal = 0;
                }
            }
            else
            {
                // "대기 목록에 남기기" and closing the window both mean the same thing:
                // keep the file waiting, but never auto-open this prompt again.
                await DeferSelectionPromptAsync(job.Id);
            }
        }
        catch (Exception exception)
        {
            await DeferSelectionPromptAsync(job.Id);
            WriteWindowActivationDiagnostic($"selection-prompt failed-open {exception.GetType().Name}");
        }
        finally
        {
            currentSelectionJobId = null;
            currentSelectionRule = null;
            currentSelectionWindow = null;
            currentSelectionSnapshot = null;
            currentSelectionInvalidated = false;
            currentSelectionCancellation?.Dispose();
            currentSelectionCancellation = null;
            selectionPromptInProgress = false;
            _ = AppServices.Live.RefreshNowAsync();
        }
    }

    internal async Task DeferSelectionPromptAsync(Guid jobId)
        => _ = await RecordSelectionPromptStateAsync([jobId], SelectionPromptState.Deferred);

    /// <summary>Most recent distinct destinations used by a rule, newest first (for the picker's shortcuts).</summary>
    internal static IReadOnlyList<string> RecentFoldersFor(Guid ruleId)
    {
        var jobs = AppServices.Live.Latest?.Jobs ?? [];
        return jobs
            .Where(job => job.RuleId == ruleId && job.RoutingState == RoutingState.Completed && !string.IsNullOrEmpty(job.FinalPath))
            .OrderByDescending(static job => job.CompletedAt ?? job.CreatedAt)
            .Select(static job => Path.GetDirectoryName(job.FinalPath!))
            .Where(static folder => !string.IsNullOrEmpty(folder))
            .Select(static folder => folder!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static string CreateSelectionPromptSnapshot(DownloadJob job, int activeCount)
        => $"{job.Id:N}:{job.CurrentFileName}:{job.BrowserState}:{job.RoutingState}:{activeCount}";

    internal static FolderSelectionDetails CreateSelectionDetails(
        DownloadJob job,
        DownloadRule rule,
        int otherPendingCount,
        int queuePosition = 1,
        int queueTotal = 1)
        => new(
            JobText.SourceHost(job),
            rule.Name,
            JobText.DescribeStatus(job),
            AppServices.ResolveForDisplay(rule.StorageRoot),
            otherPendingCount,
            queuePosition,
            queueTotal);
}
