using System.Collections.ObjectModel;
using DownloadRouter.App.Controls;
using DownloadRouter.App.Services;
using DownloadRouter.App.ViewModels;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DownloadRouter.App.Views;

/// <summary>
/// Download history and the pending workspace: list on the left, actions for the selected
/// download(s) on the right. Every change goes through the agent; history deletion never
/// touches the files themselves.
/// </summary>
public sealed partial class DownloadsView : UserControl, IAppView
{
    private readonly ObservableCollection<JobRowItem> items = [];
    private readonly ObservableCollection<PendingRuleGroup> pendingGroups = [];
    private IReadOnlyList<DownloadJob> jobs = [];
    private IReadOnlyDictionary<Guid, DownloadRule> rules = new Dictionary<Guid, DownloadRule>();
    private DownloadFilter historyFilter = DownloadFilter.All;
    private string search = string.Empty;
    private bool suppressSelection;
    private string? detailSignature;

    public DownloadsView()
    {
        InitializeComponent();
        JobList.ItemsSource = items;
        PendingGroups.ItemsSource = pendingGroups;
    }

    public void OnShown(object? parameter)
    {
        if (parameter is DownloadsNavigation navigation)
        {
            if (navigation.Search is string text)
            {
                SearchBox.Text = text;
            }

            if (navigation.Filter is DownloadFilter filter)
            {
                ChipFor(filter).IsChecked = true;
                historyFilter = filter;
            }

            if (AppServices.Live.Latest is { } latest)
            {
                Apply(latest);
            }

            if (navigation.JobId is Guid jobId)
            {
                SelectJob(jobId);
            }
        }
    }

    public void OnLiveUpdate(LiveSnapshot snapshot, bool changed)
    {
        if (changed)
        {
            Apply(snapshot);
        }
    }

    // ---------------------------------------------------------------- list

    private void Apply(LiveSnapshot snapshot)
    {
        jobs = snapshot.Jobs;
        rules = snapshot.Rules.ToDictionary(static rule => rule.Id);
        RebuildList();
        UpdateChips();
        RenderDetail(force: false);
    }

    private RadioButton ChipFor(DownloadFilter filter)
        => filter switch
        {
            DownloadFilter.Pending => PendingChip,
            DownloadFilter.InProgress => InProgressChip,
            DownloadFilter.Completed => CompletedChip,
            DownloadFilter.Failed => FailedChip,
            DownloadFilter.Skipped => SkippedChip,
            DownloadFilter.CancelledOrInterrupted => CancelledChip,
            _ => AllChip,
        };

    private bool MatchesFilter(DownloadJob job)
        => historyFilter switch
        {
            DownloadFilter.Pending => job.IsSelectionPending,
            DownloadFilter.InProgress => job.BrowserState == BrowserTransferState.InProgress && !job.IsSelectionPending,
            DownloadFilter.Completed => job.RoutingState == RoutingState.Completed,
            DownloadFilter.Failed => job.BrowserState == BrowserTransferState.Complete
                && job.RoutingState is RoutingState.Failed or RoutingState.RetryPending,
            DownloadFilter.Skipped => job.RoutingState == RoutingState.Skipped,
            DownloadFilter.CancelledOrInterrupted => job.BrowserState is BrowserTransferState.Cancelled or BrowserTransferState.Interrupted,
            _ => true,
        };

    private bool MatchesSearch(DownloadJob job)
    {
        if (search.Length == 0)
        {
            return true;
        }

        return JobText.DisplayFileName(job).Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || JobText.SourceHost(job).Contains(search, StringComparison.OrdinalIgnoreCase)
            || (rules.TryGetValue(job.RuleId, out var rule) && rule.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }

    private void RebuildList()
    {
        var visible = jobs
            .Where(MatchesFilter)
            .Where(MatchesSearch)
            .OrderByDescending(static job => job.CreatedAt)
            .ToArray();
        var selectedIds = SelectedJobIds().ToHashSet();

        suppressSelection = true;
        try
        {
            for (var index = 0; index < visible.Length; index++)
            {
                var job = visible[index];
                var ruleName = rules.TryGetValue(job.RuleId, out var rule) ? rule.Name : string.Empty;
                var existingIndex = -1;
                for (var probe = index; probe < items.Count; probe++)
                {
                    if (items[probe].Id == job.Id)
                    {
                        existingIndex = probe;
                        break;
                    }
                }

                if (existingIndex < 0)
                {
                    items.Insert(index, new JobRowItem(job, ruleName));
                }
                else
                {
                    if (existingIndex != index)
                    {
                        items.Move(existingIndex, index);
                    }

                    items[index].Update(job, ruleName);
                }
            }

            while (items.Count > visible.Length)
            {
                items.RemoveAt(items.Count - 1);
            }

            JobList.SelectedItems.Clear();
            foreach (var item in items.Where(item => selectedIds.Contains(item.Id)))
            {
                JobList.SelectedItems.Add(item);
            }
        }
        finally
        {
            suppressSelection = false;
        }

        ListEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListEmptyText.Text = jobs.Count == 0
            ? AppText.Pick("아직 규칙에 맞는 다운로드가 없습니다.", "No downloads have matched a rule yet.")
            : historyFilter == DownloadFilter.Pending
                ? AppText.Pick("저장 위치를 기다리는 파일이 없습니다.", "No files are waiting for a folder.")
                : AppText.Pick("조건에 맞는 기록이 없습니다.", "Nothing matches this filter.");
    }

    private void UpdateChips()
    {
        int Count(Func<DownloadJob, bool> predicate) => jobs.Count(predicate);
        var pending = Count(static job => job.IsSelectionPending);
        var failed = Count(static job => job.BrowserState == BrowserTransferState.Complete && job.RoutingState is RoutingState.Failed or RoutingState.RetryPending);
        AllChip.Content = AppText.Pick($"전체 {jobs.Count}", $"All {jobs.Count}");
        PendingChip.Content = AppText.Pick($"처리 대기 {pending}", $"Waiting {pending}");
        InProgressChip.Content = AppText.Pick("다운로드 중", "Downloading");
        CompletedChip.Content = AppText.Pick("옮김", "Moved");
        FailedChip.Content = AppText.Pick($"확인 필요 {failed}", $"Needs attention {failed}");
        SkippedChip.Content = AppText.Pick("옮기지 않음", "Not moved");
        CancelledChip.Content = AppText.Pick("취소·중단", "Cancelled");
    }

    private IEnumerable<Guid> SelectedJobIds()
        => JobList.SelectedItems.OfType<JobRowItem>().Select(static item => item.Id);

    private IReadOnlyList<DownloadJob> SelectedJobs()
    {
        var ids = SelectedJobIds().ToHashSet();
        return jobs.Where(job => ids.Contains(job.Id)).ToArray();
    }

    private void SelectJob(Guid jobId)
    {
        var item = items.FirstOrDefault(candidate => candidate.Id == jobId);
        if (item is null && jobs.Any(job => job.Id == jobId))
        {
            AllChip.IsChecked = true;
            SearchBox.Text = string.Empty;
            item = items.FirstOrDefault(candidate => candidate.Id == jobId);
        }

        if (item is not null)
        {
            JobList.SelectedItems.Clear();
            JobList.SelectedItem = item;
            JobList.ScrollIntoView(item);
        }
    }

    private void Filter_Checked(object sender, RoutedEventArgs args)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<DownloadFilter>(tag, out var filter))
        {
            historyFilter = filter;
            JobList.SelectedItems.Clear();
            RebuildList();
            RenderDetail(force: true);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        search = SearchBox.Text.Trim();
        RebuildList();
        RenderDetail(force: true);
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!suppressSelection)
        {
            RenderDetail(force: true);
        }
    }

    private void JobList_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Delete && JobList.SelectedItems.Count > 0)
        {
            args.Handled = true;
            _ = DeleteHistoryAsync(SelectedJobIds().ToArray());
        }
    }

    private void JobRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: JobRowItem item })
        {
            return;
        }

        args.Handled = true;
        if (!JobList.SelectedItems.Contains(item))
        {
            JobList.SelectedItems.Clear();
            JobList.SelectedItem = item;
        }

        var job = item.Job;
        var menu = new MenuFlyout();
        if (JobList.SelectedItems.Count > 1)
        {
            menu.Items.Add(MenuItem(AppText.Pick("선택 항목 기록 삭제", "Delete selected from history"), "", () => _ = DeleteHistoryAsync(SelectedJobIds().ToArray())));
        }
        else
        {
            if (job.IsSelectionPending)
            {
                menu.Items.Add(MenuItem(AppText.Pick("저장 위치 선택", "Choose folder"), "", () => _ = ChooseFolderAsync(job)));
            }

            if (JobText.CurrentFilePath(job) is not null)
            {
                menu.Items.Add(MenuItem(AppText.Pick("파일 열기", "Open file"), "", () => _ = OpenFileAsync(job)));
                menu.Items.Add(MenuItem(AppText.Pick("폴더에서 보기", "Show in folder"), "", () => _ = ShowInFolderAsync(job)));
            }

            if (JobText.CanRetry(job))
            {
                menu.Items.Add(MenuItem(AppText.Pick("다시 시도", "Retry"), "", () => _ = RetryAsync(job)));
            }

            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            menu.Items.Add(MenuItem(AppText.Pick("기록에서 삭제", "Delete from history"), "", () => _ = DeleteHistoryAsync([job.Id])));
        }

        if (args.TryGetPosition(sender, out var point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt((FrameworkElement)sender);
        }
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    // ---------------------------------------------------------------- detail pane

    private void RenderDetail(bool force)
    {
        var selected = SelectedJobs();
        var signature = string.Join('|', selected.Select(static job =>
            $"{job.Id:N}:{job.BrowserState}:{job.RoutingState}:{job.CurrentFileName}:{job.FinalPath}:{job.ErrorCode}:{job.SelectionPromptState}"))
            + $"#{historyFilter}#{jobs.Count(static job => job.IsSelectionPending)}";
        if (!force && string.Equals(signature, detailSignature, StringComparison.Ordinal))
        {
            return;
        }

        detailSignature = signature;
        NoSelectionPanel.Visibility = selected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = selected.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        MultiPanel.Visibility = selected.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (selected.Count == 0)
        {
            RenderPendingWorkspace();
        }
        else if (selected.Count == 1)
        {
            RenderSingle(selected[0]);
        }
        else
        {
            RenderMultiple(selected);
        }
    }

    /// <summary>
    /// The former "저장 위치 선택 대기" tab, kept inside the downloads page.
    /// Jobs are grouped by rule so a bulk folder can never cross storage roots.
    /// </summary>
    private void RenderPendingWorkspace()
    {
        var showPending = historyFilter == DownloadFilter.Pending;
        PendingWorkspace.Visibility = showPending ? Visibility.Visible : Visibility.Collapsed;
        NoSelectionHint.Visibility = showPending ? Visibility.Collapsed : Visibility.Visible;
        if (!showPending)
        {
            return;
        }

        var pending = DownloadJobQueries.ActiveSelections(jobs);
        var previousSession = DownloadJobQueries.PreviousSessionSelections(jobs);
        PreviousSessionBar.IsOpen = previousSession.Count > 0;
        PreviousSessionBar.Message = AppText.Pick(
            $"{previousSession.Count}개 파일은 창이 자동으로 다시 뜨지 않고 이 목록에서만 처리합니다. 컴퓨터나 브라우저를 다시 시작해도 다시 뜨지 않습니다.",
            $"{previousSession.Count} file(s) are handled only from this list; their window will not pop up again, even after a restart.");

        pendingGroups.Clear();
        foreach (var group in pending.GroupBy(static job => job.RuleId))
        {
            if (rules.TryGetValue(group.Key, out var rule))
            {
                pendingGroups.Add(new PendingRuleGroup(rule, group.ToArray(), AppServices.ResolveForDisplay(rule.StorageRoot)));
            }
        }

        var orphaned = pending.Count - pendingGroups.Sum(static group => group.Jobs.Count);
        OrphanedText.Visibility = orphaned > 0 ? Visibility.Visible : Visibility.Collapsed;
        OrphanedText.Text = AppText.Pick(
            $"규칙이 삭제된 대기 파일 {orphaned}개는 저장 위치를 정할 수 없습니다. 기록에서 삭제할 수 있습니다.",
            $"{orphaned} waiting file(s) belong to a deleted rule and cannot get a folder. You can delete them from history.");
    }

    private void RenderSingle(DownloadJob job)
    {
        var rule = rules.TryGetValue(job.RuleId, out var found) ? found : null;
        DetailPlate.Glyph = JobText.Glyph(job);
        DetailPlate.Kind = JobText.Kind(job);
        DetailTitle.Text = JobText.DisplayFileName(job);
        DetailBadge.Kind = JobText.Kind(job);
        DetailBadge.Text = JobText.DescribeStatus(job);

        ActionButtons.Children.Clear();
        var filePath = JobText.CurrentFilePath(job);
        if (job.IsSelectionPending)
        {
            ActionTitle.Text = AppText.Pick("저장 위치를 골라 주세요", "Choose where to save this file");
            ActionDescription.Text = rule is null
                ? AppText.Pick("이 파일의 규칙이 삭제되어 저장 위치를 정할 수 없습니다.", "The rule for this file was deleted, so no folder can be chosen.")
                : AppText.Pick(
                    $"'{rule.Name}' 규칙의 기준 폴더 안에서 이 파일을 저장할 폴더를 고릅니다.",
                    $"Pick a folder inside the base folder of the rule '{rule.Name}'.");
            if (SelectionPromptPolicy.IsPreviousSessionPending(job))
            {
                ActionDescription.Text += "\n" + (job.IsBrowserRecordStale
                    ? AppText.Pick("브라우저 기록을 확인할 수 없어 창이 자동으로 뜨지 않는 파일입니다.", "The browser record could not be checked, so this file does not pop up automatically.")
                    : AppText.Pick("이전에 대기 목록에 남겨 둔 파일입니다.", "You left this file waiting earlier."));
            }

            if (rule is not null)
            {
                AddAction(AppText.Pick("저장 위치 선택", "Choose folder"), "", () => _ = ChooseFolderAsync(job), primary: true);
            }

            AddAction(AppText.Pick("이 파일은 옮기지 않기", "Do not move this file"), "", () => _ = SkipAsync(job));
        }
        else if (JobText.CanRetry(job))
        {
            ActionTitle.Text = AppText.Pick("파일을 옮기지 못했습니다", "The file could not be moved");
            ActionDescription.Text = (string.IsNullOrWhiteSpace(job.ErrorMessage) ? string.Empty : job.ErrorMessage + "\n")
                + AppText.Pick(
                    "원래 파일은 브라우저가 저장한 위치에 그대로 있습니다. 폴더 문제를 해결한 뒤 다시 시도하세요.",
                    "The original file is still where the browser saved it. Fix the folder problem, then retry.");
            AddAction(AppText.Pick("다시 시도", "Retry"), "", () => _ = RetryAsync(job), primary: true);
        }
        else if (job.RoutingState == RoutingState.Completed)
        {
            ActionTitle.Text = AppText.Pick("옮김 완료", "Moved");
            ActionDescription.Text = job.FinalPath ?? string.Empty;
        }
        else if (job.BrowserState == BrowserTransferState.InProgress)
        {
            ActionTitle.Text = AppText.Pick("다운로드 중", "Downloading");
            ActionDescription.Text = job.RoutingState == RoutingState.SelectionReady
                ? AppText.Pick("저장 위치가 정해졌습니다. 다운로드가 끝나면 옮깁니다.", "A folder is set. The file moves when the download finishes.")
                : AppText.Pick("다운로드가 끝나면 규칙에 따라 옮깁니다.", "The file moves according to the rule when the download finishes.");
        }
        else
        {
            ActionTitle.Text = JobText.DescribeStatus(job);
            ActionDescription.Text = job.BrowserState switch
            {
                BrowserTransferState.Cancelled => AppText.Pick("브라우저에서 다운로드를 취소했습니다.", "The download was cancelled in the browser."),
                BrowserTransferState.Interrupted => AppText.Pick("다운로드가 끝나지 않아 옮길 파일이 없습니다.", "The download did not finish, so there is no file to move."),
                _ when job.RoutingState == RoutingState.Skipped => AppText.Pick("이 파일은 옮기지 않기로 했습니다. 브라우저가 저장한 위치에 그대로 있습니다.", "You chose not to move this file. It stays where the browser saved it."),
                _ => string.Empty,
            };
        }

        if (DownloadPresentation.CanChangeRoute(job) && rule is not null && !job.IsSelectionPending)
        {
            AddAction(
                job.RoutingState == RoutingState.Completed ? AppText.Pick("다른 폴더로 옮기기", "Move to another folder") : AppText.Pick("저장 위치 바꾸기", "Change folder"),
                "",
                () => _ = ChangeRouteAsync(job, rule));
        }

        if (filePath is not null)
        {
            AddAction(AppText.Pick("파일 열기", "Open file"), "", () => _ = OpenFileAsync(job));
            AddAction(AppText.Pick("폴더에서 보기", "Show in folder"), "", () => _ = ShowInFolderAsync(job));
        }

        AddAction(AppText.Pick("기록에서 삭제", "Delete from history"), "", () => _ = DeleteHistoryAsync([job.Id]), danger: true);

        DetailRows.Children.Clear();
        AddRow(AppText.Pick("상태", "Status"), JobText.DescribeStatus(job));
        AddRow(AppText.Pick("규칙", "Rule"), rule?.Name ?? AppText.Pick("삭제된 규칙", "Deleted rule"));
        AddRow(AppText.Pick("출처 사이트", "Source site"), JobText.SourceHost(job));
        AddRow(AppText.Pick("브라우저", "Browser"), job.Browser.ToString());
        AddRow(AppText.Pick("다운로드 시작", "Started"), job.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        if (job.CompletedAt is { } completedAt)
        {
            AddRow(AppText.Pick("처리 시각", "Finished"), completedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (!string.IsNullOrWhiteSpace(job.FinalPath))
        {
            AddRow(AppText.Pick("옮긴 위치", "Moved to"), job.FinalPath);
        }
        else if (job.SelectedDestinationFolder is not null || job.SelectedRelativeFolder is not null)
        {
            AddRow(AppText.Pick("정한 폴더", "Chosen folder"), job.SelectedDestinationFolder
                ?? (string.IsNullOrEmpty(job.SelectedRelativeFolder) ? AppText.Pick("기준 폴더", "Base folder") : job.SelectedRelativeFolder));
        }

        if (!string.IsNullOrWhiteSpace(job.OriginalPath))
        {
            AddRow(AppText.Pick("브라우저 저장 위치", "Browser location"), job.OriginalPath);
        }

        if (!string.IsNullOrWhiteSpace(job.ErrorCode))
        {
            AddRow(AppText.Pick("오류 코드", "Error code"), job.ErrorCode);
        }
    }

    private void RenderMultiple(IReadOnlyList<DownloadJob> selected)
    {
        MultiTitle.Text = AppText.Pick($"{selected.Count}개 선택됨", $"{selected.Count} selected");
        var pending = selected.Where(static job => job.IsSelectionPending).ToArray();
        var selectedRules = pending.Select(static job => job.RuleId).Distinct().ToArray();
        var singleRule = pending.Length == selected.Count && selectedRules.Length == 1 && rules.ContainsKey(selectedRules[0]);
        ApplySelectedButton.IsEnabled = singleRule;
        SkipSelectedButton.IsEnabled = pending.Length > 0;
        MultiDescription.Text = pending.Length == 0
            ? AppText.Pick("선택한 기록을 한꺼번에 지울 수 있습니다. 실제 파일은 지워지지 않습니다.", "You can delete the selected entries together. The files themselves stay.")
            : singleRule
                ? AppText.Pick("같은 규칙의 대기 파일이므로 하나의 폴더를 함께 정할 수 있습니다.", "These waiting files share a rule, so one folder can be applied to all of them.")
                : AppText.Pick("서로 다른 규칙은 기준 폴더가 다르므로 함께 적용할 수 없습니다. 규칙별로 나누어 처리하세요.", "Different rules have different base folders, so a folder cannot be applied to all of them. Handle them per rule.");
    }

    private void AddAction(string text, string glyph, Action action, bool primary = false, bool danger = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = text });
        var button = new Button
        {
            Content = content,
            Margin = new Thickness(0, 0, 8, 8),
            Style = primary
                ? ThemeStyles.Get<Style>("AccentButtonStyle")
                : danger ? ThemeStyles.Get<Style>("DangerTextButtonStyle") : null,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        ActionButtons.Children.Add(button);
    }

    private void AddRow(string label, string value)
    {
        var grid = new Grid
        {
            Padding = new Thickness(20, 12, 20, 12),
            ColumnSpacing = 16,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, Style = ThemeStyles.Get<Style>("SecondaryTextStyle") });
        var valueText = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Style = ThemeStyles.Get<Style>("BodyTextBlockStyle"),
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        DetailRows.Children.Add(new Border
        {
            Style = ThemeStyles.Get<Style>("DividerRowBorderStyle"),
            BorderThickness = new Thickness(0, DetailRows.Children.Count == 0 ? 0 : 1, 0, 0),
            Child = grid,
        });
    }

    // ---------------------------------------------------------------- actions

    private XamlRoot? DialogRoot => XamlRoot;

    private FolderSelectionWindow CreateFolderSelectionWindow()
        => new(
            AppServices.Main is { } main ? WinRT.Interop.WindowNative.GetWindowHandle(main) : 0,
            message => AppServices.WriteDiagnostic("window-activation.log", message));

    private async Task ChooseFolderAsync(DownloadJob job)
    {
        if (!rules.TryGetValue(job.RuleId, out var rule))
        {
            return;
        }

        var root = AppServices.ResolveForDisplay(rule.StorageRoot);
        var result = await CreateFolderSelectionWindow().ShowAsync(new FolderSelectionRequest(
            root,
            job.SelectedRelativeFolder,
            MainWindow.CreateSelectionDetails(job, rule, 0),
            AllowLater: true,
            AllowSkip: true,
            AllowParentNavigation: true,
            FileName: JobText.DisplayFileName(job),
            AllowTenMinuteFolder: true,
            RuleId: rule.Id,
            RecentFolders: MainWindow.RecentFoldersFor(rule.Id)));
        if (result.Action == FolderSelectionAction.Apply)
        {
            await ApplySelectionAsync([job.Id], result.RelativeFolder, result.DestinationFolder, result.FileName, result.UseForTenMinutes);
        }
        else if (result.Action == FolderSelectionAction.Skip)
        {
            await SendAsync("selection.skip", new SelectionSkippedPayload(job.Id), AppText.Pick("이동하지 않기를 적용하지 못했습니다.", "Could not mark the file as not moved."));
        }
        else if (AppServices.Main is { } main)
        {
            await main.DeferSelectionPromptAsync(job.Id);
            await AppServices.Live.RefreshNowAsync();
        }
    }

    private async Task ApplySelectionAsync(IReadOnlyList<Guid> jobIds, string displayedFolder,
        string? destinationFolder = null, string? fileName = null, bool useForTenMinutes = false)
    {
        var relativeFolder = displayedFolder == "." ? string.Empty : displayedFolder;
        await SendAsync(
            "selection.complete",
            new SelectionCompletedPayload(jobIds, relativeFolder, destinationFolder, fileName, useForTenMinutes),
            AppText.Pick("저장 위치를 적용하지 못했습니다.", "Could not apply the folder."));
    }

    private async Task SkipAsync(DownloadJob job)
    {
        var confirmed = await AppDialog.ConfirmAsync(
            DialogRoot,
            AppText.Pick("이 파일은 옮기지 않을까요?", "Leave this file where it is?"),
            AppText.Pick("eslee Download Router가 옮기지만 않습니다. 브라우저가 받은 파일은 지우지 않습니다.", "eslee Download Router just won't move it. The downloaded file is not deleted."),
            AppText.Pick("옮기지 않기", "Do not move"));
        if (confirmed)
        {
            await SendAsync("selection.skip", new SelectionSkippedPayload(job.Id), AppText.Pick("이동하지 않기를 적용하지 못했습니다.", "Could not mark the file as not moved."));
        }
    }

    private async Task ChangeRouteAsync(DownloadJob job, DownloadRule rule)
    {
        var root = AppServices.ResolveForDisplay(rule.StorageRoot);
        var completedMove = job.RoutingState == RoutingState.Completed;
        if (completedMove && !await AppDialog.ConfirmAsync(
                DialogRoot,
                AppText.Pick("이미 옮긴 파일을 다시 옮길까요?", "Move this file again?"),
                AppText.Pick("안전한 이동 절차로 이 파일만 다른 폴더로 옮깁니다. 같은 이름의 파일은 덮어쓰지 않습니다.", "Only this file moves, using the safe move steps. Files with the same name are never overwritten."),
                AppText.Pick("폴더 고르기", "Choose folder")))
        {
            return;
        }

        var result = await CreateFolderSelectionWindow().ShowAsync(new FolderSelectionRequest(
            root,
            job.SelectedRelativeFolder,
            MainWindow.CreateSelectionDetails(job, rule, 0),
            AllowLater: !completedMove,
            AllowSkip: !completedMove,
            RuleId: rule.Id,
            RecentFolders: MainWindow.RecentFoldersFor(rule.Id)));
        if (result.Action == FolderSelectionAction.Skip)
        {
            await SendAsync("selection.skip", new SelectionSkippedPayload(job.Id), AppText.Pick("이동하지 않기를 적용하지 못했습니다.", "Could not mark the file as not moved."));
        }
        else if (result.Action is FolderSelectionAction.Later or FolderSelectionAction.Closed)
        {
            if (job.IsSelectionPending && AppServices.Main is { } main)
            {
                await main.DeferSelectionPromptAsync(job.Id);
            }
        }
        else if (result.Action == FolderSelectionAction.Apply)
        {
            await SendAsync(
                "route.change",
                new JobRouteChangePayload(job.Id, result.RelativeFolder, completedMove),
                AppText.Pick("저장 위치를 바꾸지 못했습니다.", "Could not change the folder."));
        }
    }

    private Task RetryAsync(DownloadJob job)
        => SendAsync("job.retry", new RetryRequest(job.Id), AppText.Pick("다시 시도하지 못했습니다.", "Could not retry."), showSuccess: true);

    private async Task OpenFileAsync(DownloadJob job)
    {
        var path = JobText.CurrentFilePath(job);
        if (!await ShellActions.FileExistsAsync(path))
        {
            await AppDialog.ShowMessageAsync(DialogRoot, AppText.Pick($"파일을 찾을 수 없습니다. 옮겨졌거나 삭제되었을 수 있습니다.\n{path}", $"The file was not found. It may have been moved or deleted.\n{path}"), DialogTone.Warning);
            return;
        }

        if (ShellActions.IsExecutable(path!) && !await AppDialog.ConfirmAsync(
                DialogRoot,
                AppText.Pick("프로그램 파일을 실행할까요?", "Run this program file?"),
                AppText.Pick(
                    $"'{Path.GetFileName(path)}'은(는) 실행되는 파일입니다. 믿을 수 있는 사이트에서 받은 경우에만 여세요.",
                    $"'{Path.GetFileName(path)}' runs as a program. Open it only if it came from a site you trust."),
                AppText.Pick("실행", "Run"),
                destructive: true))
        {
            return;
        }

        try
        {
            ShellActions.OpenFile(path!);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await AppDialog.ShowMessageAsync(DialogRoot, AppText.Pick("파일을 열지 못했습니다.", "Could not open the file.") + "\n\n" + exception.Message, DialogTone.Error);
        }
    }

    private async Task ShowInFolderAsync(DownloadJob job)
    {
        var path = JobText.CurrentFilePath(job);
        if (!await ShellActions.FileExistsAsync(path))
        {
            var folder = path is null ? null : Path.GetDirectoryName(path);
            if (await ShellActions.DirectoryExistsAsync(folder))
            {
                ShellActions.OpenFolder(folder!);
                return;
            }

            await AppDialog.ShowMessageAsync(DialogRoot, AppText.Pick($"파일을 찾을 수 없습니다.\n{path}", $"The file was not found.\n{path}"), DialogTone.Warning);
            return;
        }

        ShellActions.ShowInFolder(path!);
    }

    private async Task DeleteHistoryAsync(IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var confirmed = await AppDialog.ConfirmAsync(
            DialogRoot,
            AppText.Pick($"기록 {ids.Count}개를 삭제할까요?", $"Delete {ids.Count} entry(ies) from history?"),
            AppText.Pick("프로그램 기록에서만 지웁니다. 실제 다운로드 파일은 지우지 않습니다.", "Only the app's history is cleared. The downloaded files are not deleted."),
            AppText.Pick("삭제", "Delete"),
            destructive: true);
        if (confirmed)
        {
            await SendAsync("jobs.delete", new JobsDeletePayload(ids), AppText.Pick("기록을 삭제하지 못했습니다.", "Could not delete the history."));
        }
    }

    private async void ApplyGroup_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: PendingRuleGroup group })
        {
            return;
        }

        var result = await CreateFolderSelectionWindow().ShowAsync(new FolderSelectionRequest(
            group.Root,
            null,
            null,
            AllowLater: false,
            AllowSkip: false,
            AllowParentNavigation: true,
            RuleId: group.Rule.Id,
            RecentFolders: MainWindow.RecentFoldersFor(group.Rule.Id),
            Heading: AppText.Pick(
                $"{group.Rule.Name} 규칙의 대기 파일 {group.Jobs.Count}개에 같은 폴더를 적용합니다.",
                $"Apply one folder to {group.Jobs.Count} waiting file(s) of {group.Rule.Name}.")));
        if (result.Action == FolderSelectionAction.Apply)
        {
            await ApplySelectionAsync(group.Jobs.Select(static job => job.Id).ToArray(), result.RelativeFolder, result.DestinationFolder);
        }
    }

    private async void SkipGroup_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: PendingRuleGroup group })
        {
            return;
        }

        var confirmed = await AppDialog.ConfirmAsync(
            DialogRoot,
            AppText.Pick("대기 파일을 모두 옮기지 않을까요?", "Leave all waiting files where they are?"),
            AppText.Pick(
                $"{group.Jobs.Count}개 파일을 처리 완료로 표시합니다. 브라우저가 받은 파일은 옮기거나 지우지 않습니다.",
                $"{group.Jobs.Count} file(s) are marked as handled. The downloaded files are neither moved nor deleted."),
            AppText.Pick("모두 옮기지 않기", "Do not move any"),
            destructive: true);
        if (confirmed)
        {
            await SendAsync("selection.skip-many", new SelectionsSkippedPayload(group.Jobs.Select(static job => job.Id).ToArray()),
                AppText.Pick("대기 파일을 한꺼번에 건너뛰지 못했습니다.", "Could not skip the waiting files."));
        }
    }

    private async void ApplySelected_Click(object sender, RoutedEventArgs args)
    {
        var selected = SelectedJobs().Where(static job => job.IsSelectionPending).ToArray();
        var selectedRules = selected.Select(static job => job.RuleId).Distinct().ToArray();
        var singleRule = selectedRules.Length == 1 && rules.ContainsKey(selectedRules[0]);
        if (!singleRule)
        {
            return;
        }

        var rule = rules[selectedRules[0]];
        var result = await CreateFolderSelectionWindow().ShowAsync(new FolderSelectionRequest(
            AppServices.ResolveForDisplay(rule.StorageRoot),
            null,
            null,
            AllowLater: false,
            AllowSkip: false,
            AllowParentNavigation: true,
            RuleId: rule.Id,
            RecentFolders: MainWindow.RecentFoldersFor(rule.Id),
            Heading: AppText.Pick(
                $"{rule.Name} 규칙의 선택한 파일 {selected.Length}개에 같은 폴더를 적용합니다.",
                $"Apply one folder to the {selected.Length} selected file(s) of {rule.Name}.")));
        if (result.Action == FolderSelectionAction.Apply)
        {
            await ApplySelectionAsync(selected.Select(static job => job.Id).ToArray(), result.RelativeFolder, result.DestinationFolder);
        }
    }

    private async void SkipSelected_Click(object sender, RoutedEventArgs args)
    {
        var pending = SelectedJobs().Where(static job => job.IsSelectionPending).Select(static job => job.Id).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        var confirmed = await AppDialog.ConfirmAsync(
            DialogRoot,
            AppText.Pick("선택 항목을 옮기지 않을까요?", "Leave the selected files where they are?"),
            AppText.Pick($"{pending.Length}개 작업을 처리 완료로 표시합니다. 실제 파일은 옮기거나 지우지 않습니다.", $"{pending.Length} file(s) are marked as handled. Files are neither moved nor deleted."),
            AppText.Pick("옮기지 않기", "Do not move"));
        if (confirmed)
        {
            await SendAsync("selection.skip-many", new SelectionsSkippedPayload(pending), AppText.Pick("대기 파일을 한꺼번에 건너뛰지 못했습니다.", "Could not skip the waiting files."));
        }
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs args)
        => _ = DeleteHistoryAsync(SelectedJobIds().ToArray());

    private void ClearSelection_Click(object sender, RoutedEventArgs args)
        => JobList.SelectedItems.Clear();

    private void ClearCancelled_Click(object sender, RoutedEventArgs args)
    {
        var cancelled = jobs.Where(static job => job.BrowserState == BrowserTransferState.Cancelled).Select(static job => job.Id).ToArray();
        if (cancelled.Length == 0)
        {
            _ = AppDialog.ShowMessageAsync(DialogRoot, AppText.Pick("정리할 취소 기록이 없습니다.", "There are no cancelled entries to clear."));
            return;
        }

        _ = DeleteHistoryAsync(cancelled);
    }

    private void OpenRetentionSettings_Click(object sender, RoutedEventArgs args)
        => AppServices.Main?.NavigateTo("settings");

    private async Task SendAsync(string command, object payload, string failure, bool showSuccess = false)
    {
        try
        {
            var response = await AppServices.Agent.SendAsync(command, payload);
            if (!response.Success)
            {
                await AppDialog.ShowMessageAsync(DialogRoot, AgentMessages.Describe(response, failure), DialogTone.Error);
            }
            else if (showSuccess)
            {
                await AppServices.Live.RefreshNowAsync();
                var job = AppServices.Live.Latest?.Jobs.FirstOrDefault(candidate => payload is RetryRequest retry && candidate.Id == retry.JobId);
                if (job is not null && job.RoutingState != RoutingState.Completed)
                {
                    await AppDialog.ShowMessageAsync(
                        DialogRoot,
                        AppText.Pick("이번에도 옮기지 못했습니다.", "The move failed again.") + (string.IsNullOrWhiteSpace(job.ErrorMessage) ? string.Empty : "\n\n" + job.ErrorMessage),
                        DialogTone.Warning);
                }
            }
        }
        catch (Exception exception)
        {
            await AppDialog.ShowMessageAsync(DialogRoot, AgentMessages.Unreachable(exception), DialogTone.Error);
        }

        if (await AppServices.Live.RefreshNowAsync() is { } latest)
        {
            Apply(latest);
        }
    }

    /// <summary>Wire shape of the agent's job.retry payload.</summary>
    private sealed record RetryRequest(Guid JobId);
}
