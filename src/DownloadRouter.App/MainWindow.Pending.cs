using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private async Task ShowPendingAsync()
    {
        Prepare("저장 위치 선택 대기", "파일마다 서로 다른 하위 폴더를 선택할 수 있습니다. 완료 전 선택은 저장만 하고, 실제 이동은 다운로드 완료 후 시작합니다.");
        try
        {
            var jobs = AgentClient.ReadData<List<DownloadJob>>(await agent.SendAsync("jobs.list")) ?? [];
            var rules = (AgentClient.ReadData<List<DownloadRule>>(await agent.SendAsync("rules.list")) ?? [])
                .ToDictionary(static rule => rule.Id);
            var active = DownloadJobQueries.ActiveSelections(jobs);
            var groups = active.GroupBy(static job => job.RuleId).ToList();

            foreach (var group in groups)
            {
                if (!rules.TryGetValue(group.Key, out var rule))
                {
                    continue;
                }

                AddPendingRuleGroup(rule, group.ToList());
            }

            if (groups.Count == 0)
            {
                AddMuted("현재 선택을 기다리는 활성 파일이 없습니다. 취소·중단·완료·건너뜀 작업은 이 목록에서 제외됩니다.");
            }
        }
        catch (Exception exception)
        {
            AddError("선택 대기 목록을 불러오지 못했습니다.", exception);
        }
    }

    private void AddPendingRuleGroup(DownloadRule rule, IReadOnlyList<DownloadJob> jobs)
    {
        var root = pathResolver.Resolve(rule.StorageRoot);
        var selectedJobs = new HashSet<Guid>();

        ContentPanel.Children.Add(new TextBlock
        {
            Text = $"{rule.Name} · {jobs.Count}개 파일",
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
            TextWrapping = TextWrapping.Wrap,
        });
        ContentPanel.Children.Add(new TextBlock
        {
            Text = $"저장 루트: {root}",
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var job in jobs)
        {
            ContentPanel.Children.Add(CreatePendingCard(job, rule, root, selectedJobs));
        }

        var applySelected = new Button
        {
            Content = "체크한 파일의 저장 위치 선택",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        applySelected.Click += async (_, _) =>
        {
            if (selectedJobs.Count == 0)
            {
                await ShowMessageAsync("먼저 적용할 파일의 체크박스를 선택하세요.");
                return;
            }

            var result = await new FolderSelectionWindow().ShowAsync(
                root,
                selectedRelativeFolder: null,
                $"{selectedJobs.Count}개 파일에 같은 하위 폴더를 적용합니다. 체크하지 않은 파일은 변경하지 않습니다.",
                allowLater: false,
                allowSkip: false);
            if (result.Action != FolderSelectionAction.Apply)
            {
                return;
            }

            await ApplySelectionAsync(selectedJobs.ToArray(), result.RelativeFolder);
        };
        ContentPanel.Children.Add(applySelected);
        ContentPanel.Children.Add(new Border { Height = 1, Opacity = 0.35, Margin = new Thickness(0, 8, 0, 8) });
    }

    private Border CreatePendingCard(
        DownloadJob job,
        DownloadRule rule,
        string root,
        ISet<Guid> selectedJobs)
    {
        var panel = new StackPanel { Spacing = 8 };
        var selected = new CheckBox { Content = "일괄 적용 대상으로 선택" };
        selected.Checked += (_, _) => selectedJobs.Add(job.Id);
        selected.Unchecked += (_, _) => selectedJobs.Remove(job.Id);
        panel.Children.Add(selected);
        panel.Children.Add(new TextBlock
        {
            Text = DownloadPresentation.DisplayFileName(job),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(CreateStatusBadge(job));
        panel.Children.Add(new TextBlock
        {
            Text = $"출처 호스트: {GetSourceHost(job)}\n브라우저: {job.Browser}\n다운로드 시작: {job.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n규칙: {rule.Name}\n저장 루트: {root}\n현재 선택: {DisplayFolder(job.SelectedRelativeFolder)}",
            TextWrapping = TextWrapping.Wrap,
        });

        var apply = new Button { Content = "이 파일의 저장 위치 선택/변경", HorizontalAlignment = HorizontalAlignment.Stretch };
        apply.Click += async (_, _) =>
        {
            var result = await new FolderSelectionWindow().ShowAsync(
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
            else if (result.Action == FolderSelectionAction.Later)
            {
                deferredSelectionPrompts.Add(job.Id);
            }
        };
        panel.Children.Add(apply);

        var later = new Button { Content = "나중에 선택", HorizontalAlignment = HorizontalAlignment.Stretch };
        later.Click += async (_, _) =>
        {
            deferredSelectionPrompts.Add(job.Id);
            await ShowMessageAsync("선택 대기 상태를 유지합니다. 이 파일은 이동되지 않았습니다.");
        };
        panel.Children.Add(later);

        var skip = new Button { Content = "이번 파일은 이동하지 않기", HorizontalAlignment = HorizontalAlignment.Stretch };
        skip.Click += async (_, _) => await SkipSelectionAsync(job.Id);
        panel.Children.Add(skip);

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            Child = panel,
        };
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

        foreach (var jobId in jobIds)
        {
            deferredSelectionPrompts.Remove(jobId);
        }

        await ShowPendingAsync();
    }

    private async Task SkipSelectionAsync(Guid jobId)
    {
        var confirmed = await ShowConfirmationAsync(
            "이번 파일은 이동하지 않기",
            "이 작업은 Download Router의 이동만 건너뜁니다. 브라우저가 받은 실제 파일은 삭제하지 않습니다.");
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

        deferredSelectionPrompts.Remove(jobId);
        await ShowPendingAsync();
    }

    private static string DisplayFolder(string? relativeFolder)
        => string.IsNullOrEmpty(relativeFolder) ? "저장 루트" : relativeFolder;
}
