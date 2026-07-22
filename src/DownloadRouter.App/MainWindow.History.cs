using DownloadRouter.Core.Models;
using DownloadRouter.Core.Jobs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
            Prepare("다운로드 작업 및 이력", "사이트 규칙에 매칭된 작업의 전송 상태와 라우팅 상태를 각각 표시합니다.");
            AddError("다운로드 이력을 불러오지 못했습니다.", exception);
        }
    }

    private void RenderHistory()
    {
        Prepare("다운로드 작업 및 이력", "취소된 작업도 자동 삭제하지 않으며, 이력 삭제는 실제 파일에 영향을 주지 않습니다.");

        var filter = new ComboBox
        {
            Header = "상태 필터",
            ItemsSource = new[] { "전체", "완료", "취소", "중단", "실패", "진행 중" },
            SelectedIndex = (int)historyFilter,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        filter.SelectionChanged += (_, _) =>
        {
            historyFilter = (HistoryFilter)Math.Max(filter.SelectedIndex, 0);
            selectedHistoryJobs.Clear();
            RenderHistory();
        };
        ContentPanel.Children.Add(filter);

        var visible = historyJobs.Where(MatchesHistoryFilter).ToList();
        var visibleIds = visible.Select(static job => job.Id).ToHashSet();
        var selectedVisibleCount = visibleIds.Count(selectedHistoryJobs.Contains);
        var selectAll = new CheckBox
        {
            Content = "현재 필터의 항목 전체 선택",
            IsThreeState = true,
            IsChecked = visible.Count == 0 || selectedVisibleCount == 0
                ? false
                : selectedVisibleCount == visible.Count ? true : null,
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
        ContentPanel.Children.Add(selectAll);
        ContentPanel.Children.Add(new TextBlock
        {
            Text = $"선택됨: {selectedHistoryJobs.Count}개 · 필터 변경 시 선택은 초기화됩니다.",
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });

        var deleteSelected = new Button
        {
            Content = "선택 항목 삭제",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = selectedHistoryJobs.Count > 0,
        };
        deleteSelected.Click += async (_, _) => await DeleteHistoryAsync(selectedHistoryJobs.ToArray());
        ContentPanel.Children.Add(deleteSelected);

        var clearCancelled = new Button
        {
            Content = "취소된 이력 정리",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = historyJobs.Any(static job => job.BrowserState == BrowserTransferState.Cancelled),
        };
        clearCancelled.Click += async (_, _) => await DeleteHistoryAsync(
            historyJobs.Where(static job => job.BrowserState == BrowserTransferState.Cancelled)
                .Select(static job => job.Id)
                .ToArray());
        ContentPanel.Children.Add(clearCancelled);

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
            panel.Children.Add(new TextBlock
            {
                Text = job.BrowserState == BrowserTransferState.Cancelled
                    ? "취소된 다운로드는 저장 위치를 변경할 수 없습니다."
                    : job.BrowserState == BrowserTransferState.Interrupted
                        ? "중단된 다운로드는 저장 위치를 변경할 수 없습니다."
                        : "실패 또는 재시도 상태에서는 먼저 원본 파일 상태를 확인하세요.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
        }

        var delete = new Button { Content = "이력에서 삭제", HorizontalAlignment = HorizontalAlignment.Stretch };
        delete.Click += async (_, _) => await DeleteHistoryAsync([job.Id]);
        panel.Children.Add(delete);

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            Opacity = job.BrowserState == BrowserTransferState.Cancelled ? 0.55 : 1,
            Child = panel,
        };
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

        var result = await new FolderSelectionWindow().ShowAsync(
            root,
            job.SelectedRelativeFolder,
            $"파일: {DownloadPresentation.DisplayFileName(job)}\n현재 상태: {DescribeStatus(job)}\n저장 루트: {root}",
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
        else if (result.Action == FolderSelectionAction.Later)
        {
            deferredSelectionPrompts.Add(job.Id);
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

    private static Border CreateStatusBadge(DownloadJob job)
        => new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(12),
            Background = Application.Current.Resources[
                job.BrowserState == BrowserTransferState.Interrupted || job.RoutingState == RoutingState.Failed
                    ? "SystemFillColorCautionBackgroundBrush"
                    : "SubtleFillColorSecondaryBrush"] as Brush,
            Child = new TextBlock
            {
                Text = DescribeStatus(job),
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
            HistoryFilter.Completed => job.RoutingState == RoutingState.Completed,
            HistoryFilter.Cancelled => job.BrowserState == BrowserTransferState.Cancelled,
            HistoryFilter.Interrupted => job.BrowserState == BrowserTransferState.Interrupted,
            HistoryFilter.Failed => job.RoutingState is RoutingState.Failed or RoutingState.RetryPending,
            HistoryFilter.InProgress => job.BrowserState == BrowserTransferState.InProgress,
            _ => true,
        };

    private static string CreateHistorySnapshot(IEnumerable<DownloadJob> jobs)
        => string.Join('|', jobs.Select(static job =>
            $"{job.Id:N}:{job.CurrentFileName}:{job.BrowserState}:{job.RoutingState}:{job.SelectedRelativeFolder}:{job.FinalPath}:{job.CompletedAt:O}:{job.ErrorCode}"));

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
                RoutingState.Skipped => "이번 파일 이동 안 함",
                RoutingState.RetryPending => "파일 이동 재시도 대기",
                RoutingState.Failed => "파일 이동 실패",
                RoutingState.NotRequired when job.BrowserState == BrowserTransferState.InProgress => "다운로드 중",
                RoutingState.NotRequired => "다운로드 완료",
                _ => $"{job.BrowserState} · {job.RoutingState}",
            },
        };

    private static string GetSourceHost(DownloadJob job)
        => Uri.TryCreate(job.SanitizedSource, UriKind.Absolute, out var source)
            ? source.Host
            : "출처 확인 불가";

    private enum HistoryFilter
    {
        All,
        Completed,
        Cancelled,
        Interrupted,
        Failed,
        InProgress,
    }
}
