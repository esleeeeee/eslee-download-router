using DownloadRouter.Core.Models;
using DownloadRouter.Core.Jobs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Text;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<DownloadJob> historyJobs = [];
    private IReadOnlyDictionary<Guid, DownloadRule> historyRules = new Dictionary<Guid, DownloadRule>();
    private readonly HashSet<Guid> selectedHistoryJobs = [];
    private HistoryFilter historyFilter = HistoryFilter.All;
    private string? historySnapshot;
    private bool blockingDialogOpen;

    private static readonly (HistoryFilter Filter, string Label)[] HistoryFilters =
    [
        (HistoryFilter.All, "전체"),
        (HistoryFilter.Pending, "처리 대기"),
        (HistoryFilter.InProgress, "다운로드 중"),
        (HistoryFilter.Completed, "이동 완료"),
        (HistoryFilter.Skipped, "이동하지 않음"),
        (HistoryFilter.Cancelled, "취소"),
        (HistoryFilter.Interrupted, "중단"),
        (HistoryFilter.Failed, "실패 또는 재시도"),
    ];

    private async Task ShowHistoryAsync()
    {
        try
        {
            var response = await agent.SendAsync("jobs.list");
            var rulesResponse = await agent.SendAsync("rules.list");
            historyJobs = AgentClient.ReadData<List<DownloadJob>>(response) ?? [];
            historyRules = (AgentClient.ReadData<List<DownloadRule>>(rulesResponse) ?? [])
                .ToDictionary(static rule => rule.Id);
            RenderHistory();
        }
        catch (Exception exception)
        {
            Prepare("다운로드 이력", "사이트 규칙에 매칭된 작업의 전송 상태와 라우팅 상태를 각각 표시합니다.");
            AddError("다운로드 이력을 불러오지 못했습니다.", exception);
        }
    }

    private void RenderHistory()
    {
        Prepare(
            "다운로드 이력",
            "저장 위치 선택이 필요한 파일도 여기에서 함께 처리합니다. 이력 삭제는 실제 파일에 영향을 주지 않습니다.");

        var pendingCount = historyJobs.Count(static job => job.IsSelectionPending);
        var filter = new ComboBox
        {
            Header = "상태 필터",
            ItemsSource = HistoryFilters
                .Select(entry => entry.Filter == HistoryFilter.Pending && pendingCount > 0
                    ? $"{entry.Label} ({pendingCount})"
                    : entry.Label)
                .ToArray(),
            SelectedIndex = Array.FindIndex(HistoryFilters, entry => entry.Filter == historyFilter),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        filter.SelectionChanged += (_, _) =>
        {
            historyFilter = HistoryFilters[Math.Clamp(filter.SelectedIndex, 0, HistoryFilters.Length - 1)].Filter;
            selectedHistoryJobs.Clear();
            RenderHistory();
        };
        ContentPanel.Children.Add(filter);

        if (historyFilter == HistoryFilter.Pending)
        {
            RenderPendingWorkspace();
            historySnapshot = CreateHistorySnapshot(historyJobs);
            return;
        }

        var visible = historyJobs.Where(MatchesHistoryFilter).ToList();
        ContentPanel.Children.Add(CreateSelectionToolbar(visible, allowRouting: false));

        foreach (var job in visible)
        {
            ContentPanel.Children.Add(CreateHistoryCard(job));
        }

        if (visible.Count == 0)
        {
            AddMuted(historyJobs.Count == 0
                ? "아직 규칙에 매칭되어 추적된 다운로드가 없습니다."
                : "선택한 상태에 해당하는 이력이 없습니다.");
        }

        historySnapshot = CreateHistorySnapshot(historyJobs);
    }

    /// <summary>
    /// The former "저장 위치 선택 대기" tab, rebuilt inside the history page.
    /// Jobs are grouped by rule so a bulk folder can never cross storage roots.
    /// </summary>
    private void RenderPendingWorkspace()
    {
        var pending = DownloadJobQueries.ActiveSelections(historyJobs);
        var previousSession = DownloadJobQueries.PreviousSessionSelections(historyJobs);
        if (previousSession.Count > 0)
        {
            ContentPanel.Children.Add(new InfoBar
            {
                IsOpen = true,
                Severity = InfoBarSeverity.Informational,
                Title = "이전에 남겨 둔 대기 작업",
                Message = $"{previousSession.Count}개 작업은 자동 팝업 없이 이 목록에서만 처리합니다. 컴퓨터나 브라우저를 다시 시작해도 팝업이 다시 뜨지 않습니다.",
            });
        }

        ContentPanel.Children.Add(CreateSelectionToolbar(pending, allowRouting: true));

        var groups = pending
            .GroupBy(static job => job.RuleId)
            .Select(group => (Rule: historyRules.TryGetValue(group.Key, out var rule) ? rule : null, Jobs: group.ToList()))
            .Where(entry => entry.Rule is not null)
            .ToList();

        foreach (var (rule, jobs) in groups)
        {
            ContentPanel.Children.Add(CreatePendingRuleGroup(rule!, jobs));
        }

        var orphaned = pending.Count - groups.Sum(entry => entry.Jobs.Count);
        if (orphaned > 0)
        {
            AddMuted($"규칙이 삭제된 대기 작업 {orphaned}개는 저장 위치를 지정할 수 없습니다. 이력에서 삭제할 수 있습니다.");
        }

        if (pending.Count == 0)
        {
            AddMuted("지금 저장 위치를 기다리는 파일이 없습니다. 취소·중단·완료·이동하지 않음 작업은 이 목록에서 제외됩니다.");
        }
    }

    private Border CreatePendingRuleGroup(DownloadRule rule, IReadOnlyList<DownloadJob> jobs)
    {
        var root = pathResolver.Resolve(rule.StorageRoot);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{rule.Name} · {jobs.Count}개 파일",
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(CreateHelpText($"기준 폴더: {root}"));

        foreach (var job in jobs)
        {
            panel.Children.Add(CreatePendingCard(job, root));
        }

        var applyGroup = CreatePrimaryButton("이 규칙의 대기 파일에 같은 폴더 적용");
        applyGroup.Click += async (_, _) =>
        {
            var result = await CreateFolderSelectionWindow().ShowAsync(
                root,
                selectedRelativeFolder: null,
                $"{rule.Name} 규칙의 대기 파일 {jobs.Count}개에 같은 하위 폴더를 적용합니다.",
                allowLater: false,
                allowSkip: false);
            if (result.Action != FolderSelectionAction.Apply)
            {
                return;
            }

            await ApplySelectionAsync(jobs.Select(static job => job.Id).ToArray(), result.RelativeFolder);
        };
        panel.Children.Add(applyGroup);

        var skipGroup = new Button
        {
            Content = "이 규칙의 대기 파일 모두 이동하지 않기",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = themeManager.GetThemeBrush("DangerTextBrush"),
        };
        skipGroup.Click += async (_, _) =>
        {
            var confirmed = await ShowConfirmationAsync(
                "대기 파일 모두 이동하지 않기",
                $"{jobs.Count}개 작업을 처리 완료로 표시합니다. 브라우저가 받은 실제 파일은 이동하거나 삭제하지 않습니다.");
            if (confirmed)
            {
                await SkipSelectionsAsync(jobs.Select(static job => job.Id).ToArray());
            }
        };
        panel.Children.Add(skipGroup);
        return CreateCard(panel, 20);
    }

    private Border CreatePendingCard(DownloadJob job, string root)
    {
        var panel = new StackPanel { Spacing = 8 };
        var checkbox = new CheckBox
        {
            Content = "선택 항목 작업에 포함",
            IsChecked = selectedHistoryJobs.Contains(job.Id),
        };
        checkbox.Checked += (_, _) =>
        {
            selectedHistoryJobs.Add(job.Id);
            RenderHistory();
        };
        checkbox.Unchecked += (_, _) =>
        {
            selectedHistoryJobs.Remove(job.Id);
            RenderHistory();
        };
        panel.Children.Add(checkbox);
        panel.Children.Add(new TextBlock
        {
            Text = DownloadPresentation.DisplayFileName(job),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(CreateStatusBadge(job));
        if (SelectionPromptPolicy.IsPreviousSessionPending(job))
        {
            panel.Children.Add(CreateHelpText(job.IsBrowserRecordStale
                ? "브라우저 기록을 확인할 수 없어 자동 팝업 대상에서 제외된 작업입니다."
                : "이전에 대기 목록에 남겨 둔 작업입니다."));
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"출처 호스트: {GetSourceHost(job)}\n브라우저: {job.Browser}\n다운로드 시작: {job.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n현재 선택 폴더: {DisplayFolder(job.SelectedRelativeFolder)}",
            TextWrapping = TextWrapping.Wrap,
        });

        var apply = CreatePrimaryButton("이 파일의 저장 위치 선택");
        apply.Click += async (_, _) =>
        {
            var result = await CreateFolderSelectionWindow().ShowAsync(
                root,
                job.SelectedRelativeFolder,
                $"파일: {DownloadPresentation.DisplayFileName(job)}\n현재 선택: {DisplayFolder(job.SelectedRelativeFolder)}",
                allowLater: true,
                allowSkip: true);
            if (result.Action == FolderSelectionAction.Apply)
            {
                await ApplySelectionAsync([job.Id], result.RelativeFolder);
            }
            else if (result.Action == FolderSelectionAction.Skip)
            {
                await SkipSelectionAsync(job.Id);
            }
            else
            {
                await DeferSelectionPromptAsync(job.Id);
                await ShowHistoryAsync();
            }
        };
        panel.Children.Add(apply);

        var skip = new Button
        {
            Content = "이 파일은 이동하지 않기",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = themeManager.GetThemeBrush("DangerTextBrush"),
        };
        skip.Click += async (_, _) => await SkipSelectionAsync(job.Id);
        panel.Children.Add(skip);

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = themeManager.GetThemeBrush("CardBorderBrush"),
            Background = themeManager.GetThemeBrush("SectionSurfaceBrush"),
            Child = panel,
        };
    }

    /// <summary>
    /// Shared toolbar for the current filter. Routing actions only appear on the
    /// pending filter, and the destructive history delete stays visually separated.
    /// </summary>
    private Border CreateSelectionToolbar(IReadOnlyList<DownloadJob> visible, bool allowRouting)
    {
        var visibleIds = visible.Select(static job => job.Id).ToHashSet();
        var selectedVisible = visibleIds.Where(selectedHistoryJobs.Contains).ToArray();
        var panel = new StackPanel { Spacing = 8 };

        var selectAll = new CheckBox
        {
            Content = "현재 목록 전체 선택",
            IsThreeState = true,
            IsChecked = visible.Count == 0 || selectedVisible.Length == 0
                ? false
                : selectedVisible.Length == visible.Count ? true : null,
        };
        selectAll.Checked += (_, _) =>
        {
            selectedHistoryJobs.UnionWith(visibleIds);
            RenderHistory();
        };
        selectAll.Unchecked += (_, _) =>
        {
            selectedHistoryJobs.ExceptWith(visibleIds);
            RenderHistory();
        };
        selectAll.Indeterminate += (_, _) =>
        {
            selectedHistoryJobs.ExceptWith(visibleIds);
            RenderHistory();
        };
        panel.Children.Add(selectAll);
        panel.Children.Add(CreateHelpText($"선택됨: {selectedVisible.Length}개 · 필터를 바꾸면 선택이 초기화됩니다."));

        if (allowRouting)
        {
            var selectedRules = selectedVisible
                .Select(id => visible.First(job => job.Id == id).RuleId)
                .Distinct()
                .ToArray();
            var singleRule = selectedRules.Length == 1 && historyRules.ContainsKey(selectedRules[0]);

            var applySelected = CreatePrimaryButton("선택 항목 저장 위치 지정");
            applySelected.IsEnabled = singleRule;
            applySelected.Click += async (_, _) =>
            {
                var rule = historyRules[selectedRules[0]];
                var root = pathResolver.Resolve(rule.StorageRoot);
                var result = await CreateFolderSelectionWindow().ShowAsync(
                    root,
                    selectedRelativeFolder: null,
                    $"{rule.Name} 규칙의 선택한 파일 {selectedVisible.Length}개에 같은 하위 폴더를 적용합니다.",
                    allowLater: false,
                    allowSkip: false);
                if (result.Action == FolderSelectionAction.Apply)
                {
                    await ApplySelectionAsync(selectedVisible, result.RelativeFolder);
                }
            };
            panel.Children.Add(applySelected);
            panel.Children.Add(CreateHelpText(selectedVisible.Length == 0
                ? "먼저 저장 위치를 지정할 파일을 선택하세요."
                : singleRule
                    ? "같은 규칙의 파일이므로 하나의 하위 폴더를 함께 적용할 수 있습니다."
                    : "서로 다른 규칙은 기준 폴더가 다르므로 함께 적용할 수 없습니다. 규칙별로 나누어 처리하세요."));

            var skipSelected = new Button
            {
                Content = "선택 항목 이동하지 않기",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = selectedVisible.Length > 0,
                Foreground = themeManager.GetThemeBrush("DangerTextBrush"),
            };
            skipSelected.Click += async (_, _) =>
            {
                var confirmed = await ShowConfirmationAsync(
                    "선택 항목 이동하지 않기",
                    $"{selectedVisible.Length}개 작업을 처리 완료로 표시합니다. 실제 파일은 이동하거나 삭제하지 않습니다.");
                if (confirmed)
                {
                    await SkipSelectionsAsync(selectedVisible);
                }
            };
            panel.Children.Add(skipSelected);
        }

        panel.Children.Add(CreateSeparator(4));
        var deleteSelected = new Button
        {
            Content = "선택 항목 이력 삭제",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = selectedVisible.Length > 0,
            Foreground = themeManager.GetThemeBrush("DangerTextBrush"),
        };
        deleteSelected.Click += async (_, _) => await DeleteHistoryAsync(selectedVisible);
        panel.Children.Add(deleteSelected);
        panel.Children.Add(CreateHelpText("이력 삭제는 프로그램 기록만 지우며 실제 다운로드 파일은 그대로 둡니다."));

        if (!allowRouting)
        {
            var clearCancelled = new Button
            {
                Content = "취소된 이력 정리",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = historyJobs.Any(static job => job.BrowserState == BrowserTransferState.Cancelled),
                Foreground = themeManager.GetThemeBrush("DangerTextBrush"),
            };
            clearCancelled.Click += async (_, _) => await DeleteHistoryAsync(
                historyJobs.Where(static job => job.BrowserState == BrowserTransferState.Cancelled)
                    .Select(static job => job.Id)
                    .ToArray());
            panel.Children.Add(clearCancelled);
        }

        return CreateCard(panel);
    }

    private async Task ApplySelectionAsync(IReadOnlyList<Guid> jobIds, string displayedFolder)
    {
        var relativeFolder = displayedFolder == "." ? string.Empty : displayedFolder;
        var response = await agent.SendAsync(
            "selection.complete",
            new SelectionCompletedPayload(jobIds, relativeFolder));
        if (!response.Success)
        {
            await ShowMessageAsync(response.Message ?? "선택 적용에 실패했습니다.");
            return;
        }

        selectedHistoryJobs.ExceptWith(jobIds);
        await ShowHistoryAsync();
    }

    private async Task SkipSelectionAsync(Guid jobId)
    {
        var confirmed = await ShowConfirmationAsync(
            "이 파일은 이동하지 않기",
            "Download Router의 이동만 건너뜁니다. 브라우저가 받은 실제 파일은 삭제하지 않습니다.");
        if (!confirmed)
        {
            return;
        }

        var response = await agent.SendAsync("selection.skip", new SelectionSkippedPayload(jobId));
        if (!response.Success)
        {
            await ShowMessageAsync(response.Message ?? "이동 건너뛰기 적용에 실패했습니다.");
            return;
        }

        selectedHistoryJobs.Remove(jobId);
        await ShowHistoryAsync();
    }

    private async Task SkipSelectionsAsync(IReadOnlyList<Guid> jobIds)
    {
        var response = await agent.SendAsync(
            "selection.skip-many",
            new SelectionsSkippedPayload(jobIds));
        if (!response.Success)
        {
            await ShowMessageAsync(response.Message ?? "대기 파일 일괄 건너뛰기 적용에 실패했습니다.");
            return;
        }

        selectedHistoryJobs.ExceptWith(jobIds);
        await ShowHistoryAsync();
    }

    private static string DisplayFolder(string? relativeFolder)
        => string.IsNullOrEmpty(relativeFolder) ? "기준 폴더" : relativeFolder;

    private Border CreateHistoryCard(DownloadJob job)
    {
        var panel = new StackPanel { Spacing = 8 };
        var checkbox = new CheckBox
        {
            Content = "이 항목 선택",
            IsChecked = selectedHistoryJobs.Contains(job.Id),
        };
        checkbox.Checked += (_, _) =>
        {
            selectedHistoryJobs.Add(job.Id);
            RenderHistory();
        };
        checkbox.Unchecked += (_, _) =>
        {
            selectedHistoryJobs.Remove(job.Id);
            RenderHistory();
        };
        panel.Children.Add(checkbox);

        var title = new TextBlock
        {
            Text = DownloadPresentation.DisplayFileName(job),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = job.BrowserState == BrowserTransferState.Cancelled
                ? TextDecorations.Strikethrough
                : TextDecorations.None,
        };
        panel.Children.Add(title);
        panel.Children.Add(CreateStatusBadge(job));
        var auxiliary = new TextBlock
        {
            Text = $"{job.Browser} · {GetSourceHost(job)} · 시작 {job.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = DownloadPresentation.IsCancelled(job)
                ? TextDecorations.Strikethrough
                : TextDecorations.None,
            Opacity = DownloadPresentation.IsCancelled(job) ? 0.55 : 1,
        };
        panel.Children.Add(auxiliary);

        if (DownloadPresentation.IsCancelled(job))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "사용자가 다운로드를 취소했습니다.",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
        }

        if (!string.IsNullOrWhiteSpace(job.FinalPath))
        {
            panel.Children.Add(new TextBlock { Text = $"이동 위치: {job.FinalPath}", TextWrapping = TextWrapping.Wrap });
        }

        if (!string.IsNullOrWhiteSpace(job.ErrorCode))
        {
            panel.Children.Add(new TextBlock { Text = $"오류 코드: {job.ErrorCode}", TextWrapping = TextWrapping.Wrap });
        }

        if (DownloadPresentation.CanChangeRoute(job) && historyRules.TryGetValue(job.RuleId, out var rule))
        {
            var route = new Button
            {
                Content = job.RoutingState == RoutingState.Completed ? "다른 위치로 이동" : "저장 위치 선택/변경",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            route.Click += async (_, _) => await ChangeHistoryRouteAsync(job, rule);
            panel.Children.Add(route);
        }
        else if (job.BrowserState is BrowserTransferState.Cancelled or BrowserTransferState.Interrupted
                 || job.RoutingState is RoutingState.Failed or RoutingState.RetryPending)
        {
            panel.Children.Add(CreateHelpText(job.BrowserState == BrowserTransferState.Cancelled
                ? "취소된 다운로드는 저장 위치를 변경할 수 없습니다."
                : job.BrowserState == BrowserTransferState.Interrupted
                    ? "중단된 다운로드는 저장 위치를 변경할 수 없습니다."
                    : "실패 또는 재시도 상태에서는 먼저 원본 파일 상태를 확인하세요."));
        }

        var delete = new Button
        {
            Content = "이력에서 삭제",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = themeManager.GetThemeBrush("DangerTextBrush"),
        };
        delete.Click += async (_, _) => await DeleteHistoryAsync([job.Id]);
        panel.Children.Add(delete);

        var card = CreateCard(panel);
        card.Opacity = job.BrowserState == BrowserTransferState.Cancelled ? 0.55 : 1;
        return card;
    }

    private async Task ChangeHistoryRouteAsync(DownloadJob job, DownloadRule rule)
    {
        var root = pathResolver.Resolve(rule.StorageRoot);
        var completedMove = job.RoutingState == RoutingState.Completed;
        if (completedMove && !await ShowConfirmationAsync(
                "이미 이동된 파일을 다시 이동",
                "기존 안전 이동 절차를 사용해 이 파일만 다른 위치로 이동합니다. 같은 이름의 파일은 덮어쓰지 않습니다."))
        {
            return;
        }

        var result = await CreateFolderSelectionWindow().ShowAsync(
            root,
            job.SelectedRelativeFolder,
            $"파일: {DownloadPresentation.DisplayFileName(job)}\n현재 상태: {DescribeStatus(job)}\n기준 폴더: {root}",
            allowLater: !completedMove,
            allowSkip: !completedMove);
        if (result.Action == FolderSelectionAction.Skip)
        {
            var skipped = await agent.SendAsync("selection.skip", new SelectionSkippedPayload(job.Id));
            if (!skipped.Success)
            {
                await ShowMessageAsync(skipped.Message ?? "이동 건너뛰기에 실패했습니다.");
            }
        }
        else if (result.Action == FolderSelectionAction.Later || result.Action == FolderSelectionAction.Closed)
        {
            if (job.IsSelectionPending)
            {
                await DeferSelectionPromptAsync(job.Id);
            }
        }
        else if (result.Action == FolderSelectionAction.Apply)
        {
            var response = await agent.SendAsync(
                "route.change",
                new JobRouteChangePayload(job.Id, result.RelativeFolder, completedMove));
            if (!response.Success)
            {
                await ShowMessageAsync(response.Message ?? "저장 위치 변경에 실패했습니다.");
            }
        }

        await ShowHistoryAsync();
    }

    private Border CreateStatusBadge(DownloadJob job)
        => new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(12),
            Background = themeManager.GetThemeBrush(
                job.BrowserState == BrowserTransferState.Interrupted || job.RoutingState == RoutingState.Failed
                    ? "WarningStatusSurfaceBrush"
                    : job.IsSelectionPending
                        ? "AccentStatusSurfaceBrush"
                        : "StatusSurfaceBrush"),
            Child = new TextBlock
            {
                Text = DescribeStatus(job),
                Foreground = job.IsSelectionPending
                    && job.BrowserState != BrowserTransferState.Interrupted
                    && job.RoutingState != RoutingState.Failed
                    ? themeManager.GetThemeBrush("AccentTextBrush")
                    : null,
                TextWrapping = TextWrapping.Wrap,
            },
        };

    private async Task DeleteHistoryAsync(IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0)
        {
            await ShowMessageAsync("삭제할 이력을 선택하세요.");
            return;
        }

        var confirmed = await ShowConfirmationAsync(
            "다운로드 이력 삭제",
            $"{ids.Count}개 항목을 이력에서 삭제하시겠습니까?\n\n이 작업은 프로그램 이력에서만 삭제되며 실제 다운로드 파일은 삭제하지 않습니다.");
        if (!confirmed)
        {
            return;
        }

        var response = await agent.SendAsync("jobs.delete", new JobsDeletePayload(ids));
        if (!response.Success)
        {
            await ShowMessageAsync(response.Message ?? "이력 삭제에 실패했습니다.");
            return;
        }

        selectedHistoryJobs.ExceptWith(ids);
        await ShowHistoryAsync();
    }

    private async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        blockingDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "확인",
                CloseButtonText = "취소",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = ContentPanel.XamlRoot,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            blockingDialogOpen = false;
        }
    }

    private bool MatchesHistoryFilter(DownloadJob job)
        => historyFilter switch
        {
            HistoryFilter.All => true,
            HistoryFilter.Pending => job.IsSelectionPending,
            HistoryFilter.Completed => job.RoutingState == RoutingState.Completed,
            HistoryFilter.Skipped => job.RoutingState == RoutingState.Skipped,
            HistoryFilter.Cancelled => job.BrowserState == BrowserTransferState.Cancelled,
            HistoryFilter.Interrupted => job.BrowserState == BrowserTransferState.Interrupted,
            HistoryFilter.Failed => job.RoutingState is RoutingState.Failed or RoutingState.RetryPending,
            HistoryFilter.InProgress => job.BrowserState == BrowserTransferState.InProgress
                && !job.IsSelectionPending,
            _ => true,
        };

    private static string CreateHistorySnapshot(IEnumerable<DownloadJob> jobs)
        => string.Join('|', jobs.Select(static job =>
            $"{job.Id:N}:{job.CurrentFileName}:{job.BrowserState}:{job.RoutingState}:{job.SelectionPromptState}:{job.SelectedRelativeFolder}:{job.FinalPath}:{job.CompletedAt:O}:{job.ErrorCode}"));

    private static string DescribeStatus(DownloadJob job)
        => job.BrowserState switch
        {
            BrowserTransferState.Cancelled => "사용자가 다운로드를 취소했습니다",
            BrowserTransferState.Interrupted => "브라우저 또는 네트워크 오류로 중단",
            BrowserTransferState.InProgress when job.RoutingState == RoutingState.WaitingForSelection => "다운로드 중 · 위치 선택 대기",
            BrowserTransferState.Complete when job.RoutingState == RoutingState.WaitingForSelection => "다운로드 완료 · 위치 선택 대기",
            BrowserTransferState.InProgress when job.RoutingState == RoutingState.SelectionReady => "위치 선택 완료 · 다운로드 진행 중",
            _ => job.RoutingState switch
            {
                RoutingState.Moving => "파일 이동 중",
                RoutingState.Completed => "이동 완료",
                RoutingState.Skipped => "이 파일 이동 안 함",
                RoutingState.RetryPending => "파일 이동 재시도 대기",
                RoutingState.Failed => "파일 이동 실패",
                RoutingState.NotRequired when job.BrowserState == BrowserTransferState.InProgress => "다운로드 중",
                RoutingState.NotRequired => "다운로드 완료",
                _ => "상태 확인 중",
            },
        };

    private static string GetSourceHost(DownloadJob job)
        => Uri.TryCreate(job.SanitizedSource, UriKind.Absolute, out var source)
            ? source.Host
            : "출처 확인 불가";

    private enum HistoryFilter
    {
        All,
        Pending,
        InProgress,
        Completed,
        Skipped,
        Cancelled,
        Interrupted,
        Failed,
    }
}
