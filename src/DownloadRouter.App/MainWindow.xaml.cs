using System.Text.Json;
using DownloadRouter.App.Services;
using DownloadRouter.App.Views;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App;

/// <summary>
/// Main window shell: navigation pane, page host, and the app-wide reactions to each agent
/// poll (badges, pause status, selection prompts, move notifications).
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, FrameworkElement> views = new(StringComparer.Ordinal);
    private object? pendingNavigationParameter;
    private string currentTag = "dashboard";
    private bool hostInitialized;
    private bool launchedInBackground;
    private bool browserSetupCheckInProgress;
    private bool browserSetupChecked;

    public MainWindow()
    {
        InitializeComponent();
        Title = "eslee Download Router";
        AppServices.Main = this;
        AppServices.Theme = new ThemeService(AppServices.Preferences.ThemePreference, DispatcherQueue);
        AppServices.Theme.Register(this);
        AppServices.Notifications = new NotificationService(DispatcherQueue);
        AppServices.Notifications.JobNotificationInvoked += (_, jobId) =>
        {
            ShowFromTray();
            NavigateTo("history", new DownloadsNavigation(JobId: jobId));
        };
        if (AppServices.Preferences.NotifyOnMoveCompleted)
        {
            AppServices.Notifications.EnsureRegistered();
        }

        AppServices.Live = new LiveMonitor(AppServices.Agent, DispatcherQueue);
        AppServices.Live.Updated += Live_Updated;
        AppServices.RootHealth.Changed += (_, _) => UpdateRulesBadge();

        var version = ProductVersionInfo.Read(typeof(App).Assembly, AppContext.BaseDirectory);
        VersionText.Text = AppServices.Instance.IsDefault
            ? $"v{version.SemanticVersion}"
            : $"v{version.SemanticVersion} · " + AppText.Pick("테스트 데이터 폴더", "Test data folder");
        RoutingStatusText.Text = AppText.Pick("연결하는 중…", "Connecting…");
        Navigation.SelectedItem = HomeNavigationItem;
        Closed += (_, _) => AppServices.Live.Dispose();
    }

    /// <summary>Selects a page in the navigation pane; the parameter is delivered to the page.</summary>
    public void NavigateTo(string tag, object? parameter = null)
    {
        var item = Navigation.MenuItems
            .Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (item is null)
        {
            return;
        }

        if (ReferenceEquals(Navigation.SelectedItem, item))
        {
            ShowView(tag, parameter);
            return;
        }

        pendingNavigationParameter = parameter;
        Navigation.SelectedItem = item;
    }

    /// <summary>Opens the downloads page filtered to files waiting for a folder.</summary>
    public void OpenPendingHistory()
        => NavigateTo("history", new DownloadsNavigation(DownloadFilter.Pending));

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        var parameter = pendingNavigationParameter;
        pendingNavigationParameter = null;
        ShowView(tag, parameter);
    }

    private void ShowView(string tag, object? parameter)
    {
        if (!views.TryGetValue(tag, out var view))
        {
            view = tag switch
            {
                "rules" => new RulesView(),
                "history" => new DownloadsView(),
                "browsers" => new BrowsersView(),
                "settings" => new SettingsView(),
                _ => new HomeView(),
            };
            views[tag] = view;
        }

        currentTag = tag;
        PageHost.Content = view;
        if (view is IAppView appView)
        {
            appView.OnShown(parameter);
            if (AppServices.Live.Latest is { } latest)
            {
                appView.OnLiveUpdate(latest, changed: true);
            }
        }
    }

    private void Live_Updated(object? sender, LiveSnapshotEventArgs args)
    {
        var snapshot = args.Snapshot;
        AgentUnavailableBar.IsOpen = !snapshot.AgentReachable;
        if (!snapshot.AgentReachable)
        {
            _ = EnsureAgentForHostAsync();
        }
        UpdateRoutingStatus(snapshot);
        var active = Core.Jobs.DownloadJobQueries.ActiveSelections(snapshot.Jobs);
        var automatic = Core.Jobs.DownloadJobQueries.AutomaticSelections(snapshot.Jobs);
        UpdatePendingBadge(active.Count, automatic.Count);
        if (snapshot.AgentReachable)
        {
            _ = ShowBrowserSetupOnFirstLaunchAsync();
            AppServices.RootHealth.Observe(snapshot.Rules);
            DetectCompletedMoves(snapshot);
            SynchronizeSelectionPrompt(active, automatic);
            if (!selectionPromptInProgress && !AppDialog.IsOpen)
            {
                // Detached on purpose: while the selection window is open polling keeps running so
                // the window can be refreshed or invalidated by later events.
                _ = ProcessNextSelectionPromptAsync(automatic, snapshot.Rules);
            }
        }

        if (PageHost.Content is IAppView view)
        {
            view.OnLiveUpdate(snapshot, args.Changed);
        }
    }

    private async Task ShowBrowserSetupOnFirstLaunchAsync()
    {
        if (!hostInitialized || launchedInBackground || browserSetupChecked || browserSetupCheckInProgress
            || AppServices.Preferences.BrowserSetupShown)
        {
            return;
        }

        browserSetupCheckInProgress = true;
        try
        {
            var response = await AppServices.Agent.SendAsync("diagnostics.status");
            if (!response.Success || response.Data is not JsonElement status
                || !status.TryGetProperty("extensionEverConnected", out var connected)
                || connected.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return; // Retry on the next reachable poll, not while the service is unavailable.
            }

            if (connected.GetBoolean())
            {
                browserSetupChecked = true;
                return;
            }

            if (!AppServices.Preferences.BrowserSetupShown)
            {
                NavigateTo("browsers");
                browserSetupChecked = true;
                await AppServices.UpdatePreferencesAsync(preferences => preferences with { BrowserSetupShown = true });
            }
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"first browser setup failed: {exception.GetType().Name}");
        }
        finally
        {
            browserSetupCheckInProgress = false;
        }
    }

    private void UpdateRoutingStatus(LiveSnapshot snapshot)
    {
        ActiveDot.Visibility = Visibility.Collapsed;
        PausedDot.Visibility = Visibility.Collapsed;
        OfflineDot.Visibility = Visibility.Collapsed;
        if (!snapshot.AgentReachable)
        {
            OfflineDot.Visibility = Visibility.Visible;
            RoutingStatusText.Text = AppText.Pick("서비스 연결 끊김", "Service unavailable");
        }
        else if (snapshot.Pause.IsPaused)
        {
            PausedDot.Visibility = Visibility.Visible;
            RoutingStatusText.Text = DescribePause(snapshot.Pause);
        }
        else
        {
            ActiveDot.Visibility = Visibility.Visible;
            RoutingStatusText.Text = AppText.Pick("자동 분류 사용 중", "Sorting downloads");
        }

        trayIcon?.SetRoutingPaused(snapshot.Pause.IsPaused);
    }

    public static string DescribePause(RoutingPauseStatus pause)
        => pause.PausedUntil is { } until
            ? AppText.Pick($"{until.ToLocalTime():HH:mm}까지 일시 중지", $"Paused until {until.ToLocalTime():HH:mm}")
            : AppText.Pick("다시 켤 때까지 일시 중지", "Paused until resumed");

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

    private void UpdateRulesBadge()
    {
        RulesAttentionBadge.Visibility = AppServices.RootHealth.AttentionCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PageHost.Content is IAppView view && AppServices.Live.Latest is { } latest)
        {
            view.OnLiveUpdate(latest, changed: true);
        }
    }

    /// <summary>Pauses rule matching for new downloads. Null minutes means until resumed.</summary>
    public async Task<bool> PauseRoutingAsync(int? minutes)
    {
        try
        {
            var response = await AppServices.Agent.SendAsync("routing.pause", new RoutingPausePayload(minutes));
            if (!response.Success)
            {
                await ShowErrorAsync(AgentMessages.Describe(response, AppText.Pick("자동 분류를 멈추지 못했습니다.", "Could not pause sorting.")));
                return false;
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(AgentMessages.Unreachable(exception));
            return false;
        }

        await AppServices.Live.RefreshNowAsync();
        return true;
    }

    public async Task<bool> ResumeRoutingAsync()
    {
        try
        {
            var response = await AppServices.Agent.SendAsync("routing.resume");
            if (!response.Success)
            {
                await ShowErrorAsync(AgentMessages.Describe(response, AppText.Pick("자동 분류를 다시 켜지 못했습니다.", "Could not resume sorting.")));
                return false;
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(AgentMessages.Unreachable(exception));
            return false;
        }

        await AppServices.Live.RefreshNowAsync();
        return true;
    }

    public Task ShowErrorAsync(string message)
        => AppDialog.ShowMessageAsync(RootGrid.XamlRoot, message, DialogTone.Error);

    public XamlRoot? DialogRoot => RootGrid.XamlRoot;
}
