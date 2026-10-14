using System.Text.Json;
using DownloadRouter.App.Services;
using DownloadRouter.App.ViewModels;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Settings;
using DownloadRouter.Core.Startup;
using DownloadRouter.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App.Views;

/// <summary>
/// Windows 11 style settings. Every control saves on change; there is no Save button.
/// Switches save from <see cref="ToggleSwitch.Toggled"/>, which fires however the value is
/// changed (mouse, keyboard, or a screen reader's toggle pattern).
/// </summary>
public sealed partial class SettingsView : UserControl, IAppView
{
    private static readonly int[] RetentionDays = [0, 7, 30, 90, 180, 365];

    private readonly AutoStartRegistration autoStartRegistration = new();
    private readonly IReadOnlyList<Option<AppThemePreference>> themes =
    [
        new(AppThemePreference.System, AppText.Pick("Windows 설정 따르기", "Use Windows setting"), string.Empty, string.Empty),
        new(AppThemePreference.Light, AppText.Pick("라이트", "Light"), string.Empty, string.Empty),
        new(AppThemePreference.Dark, AppText.Pick("다크", "Dark"), string.Empty, string.Empty),
    ];
    private readonly IReadOnlyList<Option<AppLanguage>> languages =
    [
        new(AppLanguage.System, AppText.Pick("Windows 설정 따르기", "Use Windows setting"), string.Empty, string.Empty),
        new(AppLanguage.Korean, "한국어", string.Empty, string.Empty),
        new(AppLanguage.English, "English", string.Empty, string.Empty),
    ];
    private readonly IReadOnlyList<Option<WindowCloseBehavior>> closeBehaviors =
    [
        new(WindowCloseBehavior.MinimizeToTray, AppText.Pick("시스템 트레이로 최소화 (앱은 계속 실행)", "Minimize to the tray (keeps running)"), string.Empty, string.Empty),
        new(WindowCloseBehavior.ExitApplication, AppText.Pick("앱 화면 완전히 종료", "Close the app window completely"), string.Empty, string.Empty),
    ];
    private readonly IReadOnlyList<Option<int>> retentionOptions = RetentionDays
        .Select(static days => new Option<int>(
            days,
            days switch
            {
                0 => AppText.Pick("사용 안 함", "Never"),
                365 => AppText.Pick("1년 지난 기록", "Older than 1 year"),
                _ => AppText.Pick($"{days}일 지난 기록", $"Older than {days} days"),
            },
            string.Empty,
            string.Empty))
        .ToArray();

    private bool loading;
    private int? loadedRetentionDays;

    public SettingsView()
    {
        InitializeComponent();
        ThemeBox.ItemsSource = themes;
        LanguageBox.ItemsSource = languages;
        CloseBehaviorBox.ItemsSource = closeBehaviors;
        RetentionBox.ItemsSource = retentionOptions;

        var version = MainWindow.CurrentVersion();
        VersionCard.Description = AppText.Pick(
            $"버전 {version.DisplayVersion} · {version.DistributionDescription}",
            $"Version {version.DisplayVersion} · {version.DistributionDescription}");
        SupportCard.Description = BrowserSupportCatalog.SupportSummary;
        DataFolderCard.Description = AppServices.Instance.DataRoot;
        TestInstanceBar.IsOpen = !AppServices.Instance.IsDefault;
        TestInstanceBar.Message = AppText.Pick(
            $"데이터 폴더: {AppServices.Instance.DataRoot}\n이 실행은 Windows 자동 실행, Tray Folder 연결을 바꾸지 않습니다.",
            $"Data folder: {AppServices.Instance.DataRoot}\nThis copy does not change Windows startup or the Tray Folder connection.");
        RestartBar.IsOpen = false;
    }

    public void OnShown(object? parameter)
    {
        loading = true;
        try
        {
            var preferences = AppServices.Preferences;
            ThemeBox.SelectedItem = themes.First(option => option.Value == preferences.ThemePreference);
            LanguageBox.SelectedItem = languages.First(option => option.Value == preferences.LanguagePreference);
            CloseBehaviorBox.SelectedItem = closeBehaviors.First(option => option.Value == preferences.CloseBehavior);
            NotifySwitch.IsOn = preferences.NotifyOnMoveCompleted;

            // A copy started on another data folder must never take over the installed app's
            // Windows startup entry, so the switch only shows its state there.
            var executable = Environment.ProcessPath;
            AutoStartSwitch.IsOn = executable is not null && autoStartRegistration.IsEnabled(executable);
            AutoStartSwitch.IsEnabled = AppServices.Instance.IsDefault && executable is not null;
            if (!AppServices.Instance.IsDefault)
            {
                AutoStartCard.Description = AppText.Pick(
                    "테스트 데이터 폴더로 실행 중이라 바꿀 수 없습니다.",
                    "Unavailable while running on a test data folder.");
            }

            var scale = XamlRoot?.RasterizationScale;
            ScaleCard.Description = scale is null
                ? AppText.Pick("확인할 수 없습니다.", "Unavailable.")
                : $"{scale.Value * 100:0}%";
            RenderUpdateState(null);
        }
        finally
        {
            loading = false;
        }

        _ = LoadRetentionAsync();
    }

    public void OnLiveUpdate(LiveSnapshot snapshot, bool changed)
    {
    }

    private async void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (loading || ThemeBox.SelectedItem is not Option<AppThemePreference> option)
        {
            return;
        }

        AppServices.Theme.SetPreference(option.Value);
        await SaveAsync(preferences => preferences with { Theme = AppThemePolicy.ToStorageValue(option.Value) });
    }

    private async void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (loading || LanguageBox.SelectedItem is not Option<AppLanguage> option)
        {
            return;
        }

        await SaveAsync(preferences => preferences with { Language = option.Value.ToString() });
        RestartBar.IsOpen = AppText.ResolveCode(option.Value) != AppText.LanguageCode;
    }

    private void Restart_Click(object sender, RoutedEventArgs args)
        => AppRestart.Request();

    private async void CloseBehaviorBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (loading || CloseBehaviorBox.SelectedItem is not Option<WindowCloseBehavior> option)
        {
            return;
        }

        await SaveAsync(preferences => preferences with { CloseBehavior = option.Value });
    }

    private async void AutoStartSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (loading || Environment.ProcessPath is not string executable || !AppServices.Instance.IsDefault)
        {
            return;
        }

        var requested = AutoStartSwitch.IsOn;
        try
        {
            await Task.Run(() => autoStartRegistration.SetEnabled(requested, executable));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            loading = true;
            AutoStartSwitch.IsOn = autoStartRegistration.IsEnabled(executable);
            loading = false;
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("자동 실행 설정을 바꾸지 못했습니다.", "Could not change the startup setting.") + "\n\n" + exception.Message, DialogTone.Error);
        }
    }

    private async void NotifySwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (loading)
        {
            return;
        }

        var enabled = NotifySwitch.IsOn;
        await SaveAsync(preferences => preferences with { NotifyOnMoveCompleted = enabled });
        if (enabled)
        {
            if (!AppServices.Notifications.EnsureRegistered())
            {
                await AppDialog.ShowMessageAsync(
                    XamlRoot,
                    AppText.Pick(
                        "Windows 알림을 사용할 수 없어 트레이 아이콘 알림으로 대신 표시합니다.",
                        "Windows notifications are unavailable, so the tray icon shows the message instead."),
                    DialogTone.Warning);
            }
        }
        else
        {
            AppServices.Notifications.Disable();
        }
    }

    private async Task LoadRetentionAsync()
    {
        RetentionBox.IsEnabled = false;
        try
        {
            var response = await AppServices.Agent.SendAsync("history.retention.get");
            if (response.Success && AgentClient.ReadData<HistoryRetentionStatus>(response) is { } status)
            {
                loading = true;
                loadedRetentionDays = status.Days;
                RetentionBox.SelectedItem = retentionOptions.FirstOrDefault(option => option.Value == status.Days) ?? retentionOptions[0];
                loading = false;
                RetentionBox.IsEnabled = true;
            }
        }
        catch (Exception exception)
        {
            RetentionResult.Text = AgentMessages.Unreachable(exception);
            RetentionResult.Visibility = Visibility.Visible;
        }
    }

    private async void RetentionBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (loading || RetentionBox.SelectedItem is not Option<int> option || option.Value == loadedRetentionDays)
        {
            return;
        }

        RetentionBox.IsEnabled = false;
        try
        {
            var response = await AppServices.Agent.SendAsync("history.retention.set", new HistoryRetentionPayload(option.Value));
            if (!response.Success)
            {
                await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Describe(response, AppText.Pick("설정을 저장하지 못했습니다.", "Could not save the setting.")), DialogTone.Error);
                return;
            }

            loadedRetentionDays = option.Value;
            var deleted = response.Data is JsonElement data && data.TryGetProperty("deleted", out var value) && value.TryGetInt32(out var count) ? count : 0;
            RetentionResult.Text = option.Value == 0
                ? AppText.Pick("기록을 자동으로 지우지 않습니다.", "History is kept.")
                : AppText.Pick($"지금 {deleted}개를 정리했습니다. 이후 6시간마다 다시 확인합니다.", $"Cleaned up {deleted} entry(ies) now. Checked again every 6 hours.");
            RetentionResult.Visibility = Visibility.Visible;
            _ = AppServices.Live.RefreshNowAsync();
        }
        catch (Exception exception)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Unreachable(exception), DialogTone.Error);
        }
        finally
        {
            RetentionBox.IsEnabled = true;
        }
    }

    private async void Ping_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var response = await AppServices.Agent.SendAsync("ping");
            await AppDialog.ShowMessageAsync(
                XamlRoot,
                response.Success
                    ? AppText.Pick("Agent가 정상적으로 응답했습니다.", "The agent responded normally.")
                    : AgentMessages.Describe(response, AppText.Pick("Agent가 오류를 알렸습니다.", "The agent reported an error.")),
                response.Success ? DialogTone.Success : DialogTone.Error);
        }
        catch (Exception exception)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Unreachable(exception), DialogTone.Error);
        }
    }

    private async void DiagnosticsExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        DiagnosticsText.Text = AppText.Pick("불러오는 중…", "Loading…");
        try
        {
            var response = await AppServices.Agent.SendAsync("diagnostics.status");
            DiagnosticsText.Text = response.Data is JsonElement data
                ? JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })
                : AppText.Pick("응답 없음", "No response");
        }
        catch (Exception exception)
        {
            DiagnosticsText.Text = AgentMessages.Unreachable(exception);
        }
    }

    private async void OpenDataFolder_Click(object sender, RoutedEventArgs args)
    {
        var folder = AppServices.Instance.DataRoot;
        if (!await ShellActions.DirectoryExistsAsync(folder))
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("데이터 폴더가 아직 없습니다.", "The data folder does not exist yet."), DialogTone.Warning);
            return;
        }

        ShellActions.OpenFolder(folder);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs args)
    {
        if (AppServices.Main is not { } main)
        {
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        UpdateCard.Description = AppText.Pick("확인하는 중…", "Checking…");
        try
        {
            var outcome = await main.RunUpdateCheckAsync();
            RenderUpdateState(outcome.Succeeded
                ? null
                : AppText.Pick("최신 버전을 확인하지 못했습니다. 네트워크 연결을 확인한 뒤 다시 시도해 주세요.", "Could not check for the latest version. Check the network and try again."));
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    /// <summary>Always renders from the persisted state so the card and the buttons cannot disagree.</summary>
    private void RenderUpdateState(string? failureNote)
    {
        var version = MainWindow.CurrentVersion();
        var (status, latest) = MainWindow.ReadCachedUpdateState(version.SemanticVersion);
        var text = latest is null && AppServices.Preferences.LastUpdateCheckAt is null
            ? AppText.Pick("아직 최신 버전을 확인하지 않았습니다.", "Not checked yet.")
            : UpdateCheckPolicy.DescribeForUser(status, version.SemanticVersion, latest);
        if (DateTimeOffset.TryParse(AppServices.Preferences.LastUpdateCheckAt, out var lastChecked))
        {
            text += "\n" + AppText.Pick($"마지막 확인: {lastChecked.ToLocalTime():yyyy-MM-dd HH:mm}", $"Last checked: {lastChecked.ToLocalTime():yyyy-MM-dd HH:mm}");
        }

        if (failureNote is not null)
        {
            text += "\n" + failureNote;
        }

        UpdateCard.Description = text;
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs args)
        => ShellActions.OpenUrl(MainWindow.ReleasePageUrl());

    private void OpenGitHub_Click(object sender, RoutedEventArgs args)
        => ShellActions.OpenUrl("https://github.com/esleeeeee/eslee-download-router");

    private async Task SaveAsync(Func<Core.Settings.AppPreferences, Core.Settings.AppPreferences> change)
    {
        try
        {
            await AppServices.UpdatePreferencesAsync(change);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("설정을 저장하지 못했습니다.", "Could not save the setting.") + "\n\n" + exception.Message, DialogTone.Error);
        }
    }
}
