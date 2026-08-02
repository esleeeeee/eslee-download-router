using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer liveUpdateTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly SelectionPromptQueue selectionPromptQueue = new();
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
    private int sessionAutoPromptTotal;
    private bool extensionRefreshRequired;

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
            extensionRefreshRequired = await ReadExtensionRefreshRequiredAsync();
            RenderDashboard(jobs, rules);
        }
        catch (Exception exception)
        {
            AddError("Agent에 연결할 수 없습니다. scripts/run-dev.ps1을 실행한 뒤 다시 시도하세요.", exception);
        }
    }

    /// <summary>
    /// The browser can keep serving a cached older extension after an upgrade. Until it is
    /// refreshed the app cannot track new downloads, so this must be impossible to miss.
    /// </summary>
    private async Task<bool> ReadExtensionRefreshRequiredAsync()
    {
        try
        {
            var response = await agent.SendAsync("diagnostics.status");
            return response.Data is System.Text.Json.JsonElement data
                && data.TryGetProperty("extensionRefreshRequired", out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Extension status read failed: {exception.GetType().Name}");
            return false;
        }
    }

    private void RenderDashboard(IReadOnlyList<DownloadJob> jobs, IReadOnlyList<DownloadRule> rules)
    {
        Prepare("대시보드", "브라우저 전송 상태와 파일 라우팅 상태를 구분해 표시합니다.");
        if (extensionRefreshRequired)
        {
            ContentPanel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = "브라우저 확장을 새로 고쳐야 합니다",
                Message = DownloadRegistrationPolicy.ExtensionRefreshGuidance,
            });
        }

        var counts = DownloadJobQueries.CountDashboard(jobs);
        AddCard("활성 규칙", rules.Count(static rule => rule.IsEnabled).ToString());
        AddCard("다운로드 중", counts.DownloadsInProgress.ToString());
        AddPendingDashboardCard(counts.WaitingForSelection);
        AddCard("최근 완료", counts.RecentlyCompleted.ToString());
        AddCard("취소/중단", counts.CancelledOrInterrupted.ToString());
        AddCard("재시도/실패", counts.RetryOrFailed.ToString());
    }

    /// <summary>
    /// The pending card is the dashboard entry point into the download history
    /// "처리 대기" filter now that the separate navigation tab is gone.
    /// </summary>
    private void AddPendingDashboardCard(int pendingCount)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "저장 위치 선택 대기",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = pendingCount.ToString(),
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
        });
        panel.Children.Add(CreateHelpText(pendingCount == 0
            ? "지금 처리할 대기 파일이 없습니다."
            : "다운로드 이력의 처리 대기 목록에서 저장 위치를 지정할 수 있습니다."));

        var open = new Button
        {
            Content = "처리 대기 목록 열기",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = pendingCount > 0,
        };
        open.Click += async (_, _) => await OpenPendingHistoryAsync();
        panel.Children.Add(open);
        ContentPanel.Children.Add(CreateCard(panel));
    }

    private async Task OpenPendingHistoryAsync()
    {
        historyFilter = HistoryFilter.Pending;
        selectedHistoryJobs.Clear();
        Navigation.SelectedItem = HistoryNavigationItem;
        await ShowHistoryAsync();
        ContentScrollViewer.ChangeView(null, 0, null);
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
            var automatic = DownloadJobQueries.AutomaticSelections(jobs);
            UpdatePendingBadge(active.Count, automatic.Count);
            SynchronizeSelectionPrompt(active, automatic);

            if (!selectionPromptInProgress && !blockingDialogOpen)
            {
                // Detached on purpose: while the modal selection window is open the timer must
                // keep running so the window can be refreshed or invalidated by later events.
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
        // Claim the single-prompt slot before the first await so a later timer tick
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
    private async Task<bool> RecordSelectionPromptStateAsync(
        IReadOnlyList<Guid> jobIds,
        SelectionPromptState state)
    {
        if (jobIds.Count == 0)
        {
            return true;
        }

        try
        {
            var response = await agent.SendAsync(
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
                allowSkipAll: true);

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
                WriteWindowActivationDiagnostic(
                    $"selection-window decision-persisted action=Skip job={job.Id:N} success={response.Success}");
                if (!response.Success)
                {
                    await ShowMessageAsync(response.Message ?? "이동 건너뛰기 적용에 실패했습니다.");
                }
            }
            else if (result.Action == FolderSelectionAction.SkipAll)
            {
                var queuedJobIds = currentAutoQueueJobIds
                    .Append(job.Id)
                    .Distinct()
                    .ToArray();
                var response = await agent.SendAsync(
                    "selection.skip-many",
                    new SelectionsSkippedPayload(queuedJobIds));
                WriteWindowActivationDiagnostic(
                    $"selection-window decision-persisted action=SkipAll count={queuedJobIds.Length} success={response.Success}");
                if (!response.Success)
                {
                    await ShowMessageAsync(response.Message ?? "대기 파일 일괄 건너뛰기 적용에 실패했습니다.");
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

    private async Task DeferSelectionPromptAsync(Guid jobId)
        => _ = await RecordSelectionPromptStateAsync([jobId], SelectionPromptState.Deferred);

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
        }

        await Task.CompletedTask;
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
