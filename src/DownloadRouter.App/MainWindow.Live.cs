using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer liveUpdateTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly SelectionPromptQueue selectionPromptQueue = new();
    private readonly HashSet<Guid> deferredSelectionPrompts = [];
    private readonly HashSet<Guid> sessionAutoPromptJobs = [];
    private CancellationTokenSource? currentSelectionCancellation;
    private Guid? currentSelectionJobId;
    private DownloadRule? currentSelectionRule;
    private FolderSelectionWindow? currentSelectionWindow;
    private string? currentSelectionSnapshot;
    private bool currentSelectionInvalidated;
    private bool liveUpdateInProgress;
    private bool selectionPromptInProgress;
    private string? livePageSnapshot;
    private int previousAutomaticCount;
    private int shownAutoPromptCount;

    private void InitializeLiveUpdates()
    {
        liveUpdateTimer.Tick += LiveUpdateTimer_Tick;
        liveUpdateTimer.Start();
    }

    private void StopLiveUpdates()
    {
        liveUpdateTimer.Stop();
        currentSelectionCancellation?.Cancel();
    }

    private async Task ShowDashboardAsync()
    {
        Prepare("대시보드", "브라우저 전송 상태와 파일 라우팅 상태를 구분해 표시합니다.");
        try
        {
            var jobsResponse = await agent.SendAsync("jobs.list");
            var rulesResponse = await agent.SendAsync("rules.list");
            var jobs = AgentClient.ReadData<List<DownloadJob>>(jobsResponse) ?? [];
            var rules = AgentClient.ReadData<List<DownloadRule>>(rulesResponse) ?? [];
            RenderDashboard(jobs, rules);
        }
        catch (Exception exception)
        {
            AddError("Agent에 연결할 수 없습니다. scripts/run-dev.ps1을 실행한 뒤 다시 시도하세요.", exception);
        }
    }

    private void RenderDashboard(IReadOnlyList<DownloadJob> jobs, IReadOnlyList<DownloadRule> rules)
    {
        Prepare("대시보드", "브라우저 전송 상태와 파일 라우팅 상태를 구분해 표시합니다.");
        var counts = DownloadJobQueries.CountDashboard(jobs);
        AddCard("활성 규칙", rules.Count(static rule => rule.IsEnabled).ToString());
        AddCard("다운로드 중", counts.DownloadsInProgress.ToString());
        AddCard("저장 위치 선택 대기", counts.WaitingForSelection.ToString());
        AddCard("최근 완료", counts.RecentlyCompleted.ToString());
        AddCard("취소/중단", counts.CancelledOrInterrupted.ToString());
        AddCard("재시도/실패", counts.RetryOrFailed.ToString());
    }

    private async void LiveUpdateTimer_Tick(object? sender, object args)
    {
        if (liveUpdateInProgress)
        {
            return;
        }

        liveUpdateInProgress = true;
        try
        {
            var jobs = AgentClient.ReadData<List<DownloadJob>>(await agent.SendAsync("jobs.list")) ?? [];
            var rules = AgentClient.ReadData<List<DownloadRule>>(await agent.SendAsync("rules.list")) ?? [];
            var active = DownloadJobQueries.ActiveSelections(jobs);
            var automatic = DownloadJobQueries.AutomaticSelections(jobs, DateTimeOffset.UtcNow);
            UpdatePendingBadge(active.Count, automatic.Count);
            SynchronizeSelectionPrompt(active, automatic);

            if (!selectionPromptInProgress && !blockingDialogOpen)
            {
                _ = ProcessNextSelectionPromptAsync(automatic, rules);
            }

            await RefreshVisibleLivePageAsync(jobs, rules);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Live UI refresh failed: {exception.GetType().Name}");
        }
        finally
        {
            liveUpdateInProgress = false;
        }
    }

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
                    currentSelectionWindow?.UpdateDescription(
                        CreateSelectionDescription(
                            currentJob,
                            currentSelectionRule,
                            Math.Max(0, active.Count - 1),
                            Math.Max(1, shownAutoPromptCount),
                            Math.Max(shownAutoPromptCount, sessionAutoPromptJobs.Count)));
                }
            }
        }

        foreach (var job in automatic)
        {
            if (job.Id != currentSelectionJobId && !deferredSelectionPrompts.Contains(job.Id))
            {
                if (selectionPromptQueue.Enqueue(job.Id))
                {
                    sessionAutoPromptJobs.Add(job.Id);
                }
            }
        }
    }

    private async Task ProcessNextSelectionPromptAsync(
        IReadOnlyList<DownloadJob> active,
        IReadOnlyList<DownloadRule> rules)
    {
        while (selectionPromptQueue.TryDequeue(out var jobId))
        {
            var job = active.FirstOrDefault(candidate => candidate.Id == jobId);
            var rule = job is null ? null : rules.FirstOrDefault(candidate => candidate.Id == job.RuleId);
            if (job is null || rule is null || deferredSelectionPrompts.Contains(jobId))
            {
                continue;
            }

            shownAutoPromptCount++;
            await ShowSelectionPromptAsync(
                job,
                rule,
                Math.Max(0, active.Count - 1),
                shownAutoPromptCount,
                Math.Max(shownAutoPromptCount, sessionAutoPromptJobs.Count),
                active.Select(static candidate => candidate.Id).ToArray());
            return;
        }
    }

    private async Task ShowSelectionPromptAsync(
        DownloadJob job,
        DownloadRule rule,
        int otherPendingCount,
        int queuePosition,
        int queueTotal,
        IReadOnlyList<Guid> currentQueueJobIds)
    {
        selectionPromptInProgress = true;
        currentSelectionJobId = job.Id;
        currentSelectionRule = rule;
        currentSelectionSnapshot = CreateSelectionPromptSnapshot(job, otherPendingCount + 1);
        currentSelectionInvalidated = false;
        try
        {
            var root = pathResolver.Resolve(rule.StorageRoot);
            currentSelectionCancellation = new CancellationTokenSource();
            currentSelectionWindow = CreateFolderSelectionWindow();
            var result = await currentSelectionWindow.ShowAsync(
                root,
                job.SelectedRelativeFolder,
                CreateSelectionDescription(job, rule, otherPendingCount, queuePosition, queueTotal),
                allowLater: true,
                allowSkip: true,
                cancellationToken: currentSelectionCancellation.Token,
                allowLaterAll: true);

            if (currentSelectionInvalidated)
            {
                return;
            }

            if (result.Action == FolderSelectionAction.Apply)
            {
                var response = await agent.SendAsync(
                    "selection.complete",
                    new SelectionCompletedPayload([job.Id], result.RelativeFolder));
                if (!response.Success)
                {
                    await ShowMessageAsync(response.Message ?? "선택 적용에 실패했습니다.");
                }
            }
            else if (result.Action == FolderSelectionAction.Skip)
            {
                var response = await agent.SendAsync("selection.skip", new SelectionSkippedPayload(job.Id));
                if (!response.Success)
                {
                    await ShowMessageAsync(response.Message ?? "이동 건너뛰기 적용에 실패했습니다.");
                }
            }
            else if (result.Action == FolderSelectionAction.LaterAll)
            {
                foreach (var queuedJobId in currentQueueJobIds)
                {
                    deferredSelectionPrompts.Add(queuedJobId);
                }

                selectionPromptQueue.Drain();
                sessionAutoPromptJobs.Clear();
                shownAutoPromptCount = 0;
            }
            else
            {
                deferredSelectionPrompts.Add(job.Id);
            }
        }
        catch (Exception exception)
        {
            deferredSelectionPrompts.Add(job.Id);
            System.Diagnostics.Debug.WriteLine($"Selection prompt failed open: {exception.GetType().Name}");
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
        }
    }

    private async Task RefreshVisibleLivePageAsync(
        IReadOnlyList<DownloadJob> jobs,
        IReadOnlyList<DownloadRule> rules)
    {
        if (blockingDialogOpen || selectionPromptInProgress)
        {
            return;
        }

        var snapshot = CreateHistorySnapshot(jobs);
        if (string.Equals(livePageSnapshot, snapshot, StringComparison.Ordinal))
        {
            return;
        }

        livePageSnapshot = snapshot;
        var tag = (Navigation.SelectedItem as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "dashboard":
                RenderDashboard(jobs, rules);
                break;
            case "history":
                historyJobs = jobs;
                historyRules = rules.ToDictionary(static rule => rule.Id);
                selectedHistoryJobs.IntersectWith(jobs.Select(static job => job.Id));
                RenderHistory();
                break;
            case "pending":
                await ShowPendingAsync();
                break;
        }
    }

    private static string CreateSelectionPromptSnapshot(DownloadJob job, int activeCount)
        => $"{job.Id:N}:{job.CurrentFileName}:{job.BrowserState}:{job.RoutingState}:{activeCount}";

    private string CreateSelectionDescription(
        DownloadJob job,
        DownloadRule rule,
        int otherPendingCount,
        int queuePosition = 1,
        int queueTotal = 1)
        => $"{queuePosition} / {queueTotal}\n파일: {DownloadPresentation.DisplayFileName(job)}\n출처 사이트: {GetSourceHost(job)}\n상태: {DescribeStatus(job)}\n규칙: {rule.Name}\n저장 루트: {pathResolver.Resolve(rule.StorageRoot)}\n동시에 대기 중인 다른 파일 {otherPendingCount}개";

    private void UpdatePendingBadge(int count, int automaticCount)
    {
        PendingInfoBadge.Value = count;
        PendingInfoBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (automaticCount > previousAutomaticCount)
        {
            trayIcon?.ShowSelectionNotification(count);
        }
        previousAutomaticCount = automaticCount;
    }

}
