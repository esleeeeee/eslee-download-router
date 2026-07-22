using System.Runtime.InteropServices;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer liveUpdateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SelectionPromptQueue selectionPromptQueue = new();
    private readonly HashSet<Guid> deferredSelectionPrompts = [];
    private ContentDialog? currentSelectionDialog;
    private Guid? currentSelectionJobId;
    private bool currentSelectionInvalidated;
    private bool liveUpdateInProgress;
    private bool selectionPromptInProgress;
    private string? livePageSnapshot;

    private void InitializeLiveUpdates()
    {
        liveUpdateTimer.Tick += LiveUpdateTimer_Tick;
        liveUpdateTimer.Start();
    }

    private void StopLiveUpdates()
    {
        liveUpdateTimer.Stop();
        currentSelectionDialog?.Hide();
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
            SynchronizeSelectionPrompt(active);

            if (!selectionPromptInProgress && !blockingDialogOpen)
            {
                _ = ProcessNextSelectionPromptAsync(active, rules);
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

    private void SynchronizeSelectionPrompt(IReadOnlyList<DownloadJob> active)
    {
        var activeIds = active.Select(static job => job.Id).ToHashSet();
        if (currentSelectionJobId is Guid current && !activeIds.Contains(current))
        {
            currentSelectionInvalidated = true;
            currentSelectionDialog?.Hide();
        }

        foreach (var job in active)
        {
            if (job.Id != currentSelectionJobId && !deferredSelectionPrompts.Contains(job.Id))
            {
                selectionPromptQueue.Enqueue(job.Id);
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

            await ShowSelectionPromptAsync(job, rule, Math.Max(0, active.Count - 1));
            return;
        }
    }

    private async Task ShowSelectionPromptAsync(DownloadJob job, DownloadRule rule, int otherPendingCount)
    {
        selectionPromptInProgress = true;
        currentSelectionJobId = job.Id;
        currentSelectionInvalidated = false;
        string? action = null;
        try
        {
            var root = pathResolver.Resolve(rule.StorageRoot);
            var folders = EnumerateSafeFolders(root);
            var picker = CreateFolderPicker("선택 가능한 하위 폴더", folders, job.SelectedRelativeFolder);
            var content = new StackPanel { Spacing = 10, MinWidth = 360 };
            content.Children.Add(new TextBlock
            {
                Text = $"파일: {job.CurrentFileName}\n출처 사이트: {GetSourceHost(job)}\n상태: {DescribeStatus(job)}\n규칙: {rule.Name}\n저장 루트: {root}\n동시에 대기 중인 다른 파일: {otherPendingCount}개",
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(picker);

            var send = new Button { Content = "이 위치로 보내기", HorizontalAlignment = HorizontalAlignment.Stretch };
            send.Click += (_, _) =>
            {
                if (picker.SelectedItem is not string)
                {
                    return;
                }

                action = "apply";
                currentSelectionDialog?.Hide();
            };
            content.Children.Add(send);

            var later = new Button { Content = "나중에 선택", HorizontalAlignment = HorizontalAlignment.Stretch };
            later.Click += (_, _) =>
            {
                action = "later";
                currentSelectionDialog?.Hide();
            };
            content.Children.Add(later);

            var skip = new Button { Content = "이번 파일은 이동하지 않기", HorizontalAlignment = HorizontalAlignment.Stretch };
            skip.Click += (_, _) =>
            {
                action = "skip";
                currentSelectionDialog?.Hide();
            };
            content.Children.Add(skip);

            currentSelectionDialog = new ContentDialog
            {
                Title = "다운로드 저장 위치 선택",
                Content = content,
                XamlRoot = ContentPanel.XamlRoot,
            };
            BringSelectionWindowToFront();
            await currentSelectionDialog.ShowAsync();

            if (currentSelectionInvalidated)
            {
                return;
            }

            if (action == "apply" && picker.SelectedItem is string selectedFolder)
            {
                var relativeFolder = selectedFolder == "." ? string.Empty : selectedFolder;
                var response = await agent.SendAsync(
                    "selection.complete",
                    new SelectionCompletedPayload([job.Id], relativeFolder));
                if (!response.Success)
                {
                    await ShowMessageAsync(response.Message ?? "선택 적용에 실패했습니다.");
                }
            }
            else if (action == "skip")
            {
                var response = await agent.SendAsync("selection.skip", new SelectionSkippedPayload(job.Id));
                if (!response.Success)
                {
                    await ShowMessageAsync(response.Message ?? "이동 건너뛰기 적용에 실패했습니다.");
                }
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
            currentSelectionDialog = null;
            currentSelectionJobId = null;
            currentSelectionInvalidated = false;
            selectionPromptInProgress = false;
        }
    }

    private async Task RefreshVisibleLivePageAsync(
        IReadOnlyList<DownloadJob> jobs,
        IReadOnlyList<DownloadRule> rules)
    {
        if (blockingDialogOpen || currentSelectionDialog is not null)
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
                selectedHistoryJobs.IntersectWith(jobs.Select(static job => job.Id));
                RenderHistory();
                break;
            case "pending":
                await ShowPendingAsync();
                break;
        }
    }

    private void BringSelectionWindowToFront()
    {
        Activate();
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = ShowWindow(windowHandle, 9);
        _ = SetForegroundWindow(windowHandle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);
}
