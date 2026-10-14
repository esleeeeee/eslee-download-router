using System.Collections.ObjectModel;
using System.Text.Json;
using DownloadRouter.App.Controls;
using DownloadRouter.App.Services;
using DownloadRouter.App.ViewModels;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App.Views;

public sealed partial class HomeView : UserControl, IAppView
{
    private const int RecentCount = 6;

    private readonly ObservableCollection<JobRowItem> recent = [];
    private readonly ObservableCollection<ActiveTemporaryFolder> temporaryFolders = [];
    private readonly SummaryTile pendingTile;
    private readonly SummaryTile downloadingTile;
    private readonly SummaryTile movedTodayTile;
    private readonly SummaryTile problemTile;
    private string? temporaryFoldersSignature;

    /// <summary>
    /// False until the agent answered once. Right after sign-in the page can open before the agent
    /// listens, so the extension check is repeated on a later poll instead of being lost.
    /// </summary>
    private bool extensionStatusKnown;
    private bool extensionStatusLoading;

    public HomeView()
    {
        InitializeComponent();
        pendingTile = new SummaryTile(AppText.Pick("저장 위치 선택 대기", "Waiting for a folder"), "", DownloadFilter.Pending, attentionWhenPositive: true);
        downloadingTile = new SummaryTile(AppText.Pick("다운로드 중", "Downloading"), "", DownloadFilter.InProgress, attentionWhenPositive: false);
        movedTodayTile = new SummaryTile(AppText.Pick("오늘 옮긴 파일", "Moved today"), "", DownloadFilter.Completed, attentionWhenPositive: false);
        problemTile = new SummaryTile(AppText.Pick("확인이 필요한 파일", "Needs attention"), "", DownloadFilter.Failed, attentionWhenPositive: true);
        TileRepeater.ItemsSource = new[] { pendingTile, downloadingTile, movedTodayTile, problemTile };
        RecentList.ItemsSource = recent;
        TemporaryFoldersList.ItemsSource = temporaryFolders;
    }

    public void OnShown(object? parameter)
    {
        _ = RefreshExtensionStatusAsync();
        _ = RefreshTemporaryFoldersAsync();
    }

    public void OnLiveUpdate(LiveSnapshot snapshot, bool changed)
    {
        RenderStatus(snapshot);
        if (!extensionStatusKnown && snapshot.AgentReachable)
        {
            _ = RefreshExtensionStatusAsync();
        }

        var attention = AppServices.RootHealth.AttentionCount;
        RootAttentionBar.IsOpen = attention > 0;
        RootAttentionBar.Message = AppText.Pick(
            $"{attention}개 규칙의 기준 폴더가 없거나 연결되어 있지 않습니다. 이 규칙에 맞는 파일은 옮기지 못할 수 있습니다.",
            $"The base folder of {attention} rule(s) is missing or offline. Matching files may not be moved.");
        if (!changed)
        {
            return;
        }

        var jobs = snapshot.Jobs;
        var today = DateTimeOffset.Now.Date;
        pendingTile.SetCount(DownloadJobQueries.ActiveSelections(jobs).Count);
        downloadingTile.SetCount(jobs.Count(static job => job.BrowserState == BrowserTransferState.InProgress && !job.IsSelectionPending));
        movedTodayTile.SetCount(jobs.Count(job => job.RoutingState == RoutingState.Completed
            && (job.CompletedAt ?? job.CreatedAt).ToLocalTime().Date == today));
        problemTile.SetCount(jobs.Count(static job => job.RoutingState is RoutingState.Failed or RoutingState.RetryPending
            && job.BrowserState == BrowserTransferState.Complete));

        var rules = snapshot.Rules.ToDictionary(static rule => rule.Id, static rule => rule.Name);
        var latest = jobs.OrderByDescending(static job => job.CreatedAt).Take(RecentCount).ToArray();
        SyncRecent(latest, rules);
        RecentEmpty.Visibility = latest.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = RefreshTemporaryFoldersAsync();
    }

    private void RenderStatus(LiveSnapshot snapshot)
    {
        if (!snapshot.AgentReachable)
        {
            StatusPlate.Kind = BadgeKind.Critical;
            StatusPlate.Glyph = "";
            StatusTitle.Text = AppText.Pick("백그라운드 서비스에 연결할 수 없습니다", "The background service is not responding");
            StatusDescription.Text = AppText.Pick(
                "규칙이 적용되지 않는 동안에도 브라우저 다운로드는 평소처럼 저장됩니다.",
                "While rules cannot be applied, the browser still saves downloads normally.");
            PauseButton.Visibility = Visibility.Collapsed;
            ResumeButton.Visibility = Visibility.Collapsed;
            return;
        }

        var enabledRules = snapshot.Rules.Count(static rule => rule.IsEnabled);
        if (snapshot.Pause.IsPaused)
        {
            StatusPlate.Kind = BadgeKind.Caution;
            StatusPlate.Glyph = "";
            StatusTitle.Text = MainWindow.DescribePause(snapshot.Pause);
            StatusDescription.Text = AppText.Pick(
                "멈춘 동안 새 다운로드는 규칙을 적용하지 않고 브라우저의 기본 위치에 그대로 저장됩니다.",
                "While paused, new downloads stay in the browser's default folder without applying rules.");
            PauseButton.Visibility = Visibility.Collapsed;
            ResumeButton.Visibility = Visibility.Visible;
        }
        else
        {
            StatusPlate.Kind = BadgeKind.Success;
            StatusPlate.Glyph = "";
            StatusTitle.Text = AppText.Pick("자동 분류를 사용하고 있습니다", "Downloads are being sorted");
            StatusDescription.Text = enabledRules == 0
                ? AppText.Pick("사용 중인 규칙이 없습니다. 사이트 규칙을 만들면 그때부터 분류합니다.", "No rule is turned on. Sorting starts once you create a site rule.")
                : AppText.Pick($"사용 중인 규칙 {enabledRules}개를 새 다운로드에 적용합니다.", $"{enabledRules} active rule(s) apply to new downloads.");
            PauseButton.Visibility = Visibility.Visible;
            ResumeButton.Visibility = Visibility.Collapsed;
        }
    }

    private void SyncRecent(IReadOnlyList<DownloadJob> jobs, IReadOnlyDictionary<Guid, string> rules)
    {
        for (var index = 0; index < jobs.Count; index++)
        {
            var job = jobs[index];
            var ruleName = rules.TryGetValue(job.RuleId, out var name) ? name : string.Empty;
            if (index < recent.Count && recent[index].Id == job.Id)
            {
                recent[index].Update(job, ruleName);
            }
            else if (index < recent.Count)
            {
                recent[index] = new JobRowItem(job, ruleName);
            }
            else
            {
                recent.Add(new JobRowItem(job, ruleName));
            }
        }

        while (recent.Count > jobs.Count)
        {
            recent.RemoveAt(recent.Count - 1);
        }
    }

    private async Task RefreshExtensionStatusAsync()
    {
        if (extensionStatusLoading)
        {
            return;
        }

        extensionStatusLoading = true;
        try
        {
            var response = await AppServices.Agent.SendAsync("diagnostics.status");
            var refresh = response.Data is JsonElement data
                && data.TryGetProperty("extensionRefreshRequired", out var value)
                && value.ValueKind == JsonValueKind.True;
            ExtensionRefreshBar.Message = DownloadRegistrationPolicy.ExtensionRefreshGuidance;
            ExtensionRefreshBar.IsOpen = refresh;
            extensionStatusKnown = response.Success;
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"extension status read failed: {exception.GetType().Name}");
            ExtensionRefreshBar.IsOpen = false;
            extensionStatusKnown = false;
        }
        finally
        {
            extensionStatusLoading = false;
        }
    }

    private async Task RefreshTemporaryFoldersAsync()
    {
        try
        {
            var response = await AppServices.Agent.SendAsync("temporary-folder.list");
            if (!response.Success)
            {
                return;
            }

            var active = AgentClient.ReadData<List<ActiveTemporaryFolder>>(response) ?? [];
            var signature = string.Join('|', active.Select(static entry => $"{entry.RuleId}:{entry.DestinationFolder}:{entry.ExpiresAt:O}"));
            if (string.Equals(signature, temporaryFoldersSignature, StringComparison.Ordinal))
            {
                return;
            }

            temporaryFoldersSignature = signature;
            temporaryFolders.Clear();
            foreach (var entry in active)
            {
                temporaryFolders.Add(entry);
            }

            TemporaryFoldersSection.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"temporary folders read failed: {exception.GetType().Name}");
        }
    }

    private async void CancelTemporaryFolder_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: ActiveTemporaryFolder choice } button)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            var response = await AppServices.Agent.SendAsync("temporary-folder.cancel", new TemporaryFolderCancelPayload(choice.RuleId));
            if (!response.Success)
            {
                button.IsEnabled = true;
                await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Describe(response, AppText.Pick("10분 자동 저장을 취소하지 못했습니다.", "Could not cancel the 10-minute folder.")), DialogTone.Error);
                return;
            }

            temporaryFoldersSignature = null;
            await RefreshTemporaryFoldersAsync();
        }
        catch (Exception exception)
        {
            button.IsEnabled = true;
            await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Unreachable(exception), DialogTone.Error);
        }
    }

    private void Pause30_Click(object sender, RoutedEventArgs args) => _ = AppServices.Main?.PauseRoutingAsync(30);

    private void Pause60_Click(object sender, RoutedEventArgs args) => _ = AppServices.Main?.PauseRoutingAsync(60);

    private void Pause180_Click(object sender, RoutedEventArgs args) => _ = AppServices.Main?.PauseRoutingAsync(180);

    private void PauseIndefinitely_Click(object sender, RoutedEventArgs args) => _ = AppServices.Main?.PauseRoutingAsync(null);

    private void Resume_Click(object sender, RoutedEventArgs args) => _ = AppServices.Main?.ResumeRoutingAsync();

    private void Tile_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: SummaryTile tile })
        {
            AppServices.Main?.NavigateTo("history", new DownloadsNavigation(tile.Filter));
        }
    }

    private void RecentList_ItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is JobRowItem item)
        {
            AppServices.Main?.NavigateTo("history", new DownloadsNavigation(DownloadFilter.All, item.Id));
        }
    }

    private void ShowAllDownloads_Click(object sender, RoutedEventArgs args)
        => AppServices.Main?.NavigateTo("history", new DownloadsNavigation(DownloadFilter.All));

    private void ShowAttentionRules_Click(object sender, RoutedEventArgs args)
        => AppServices.Main?.NavigateTo("rules", new RulesNavigation(AttentionOnly: true));
}
