using DownloadRouter.Core.Models;
using DownloadRouter.Core.Startup;
using DownloadRouter.Core.Settings;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private readonly AutoStartRegistration autoStartRegistration = new();

    private Task ShowSettingsManagementAsync()
    {
        Prepare("일반 설정", "로그인 자동 시작과 창 닫기 동작은 현재 사용자 설정으로 즉시 저장됩니다.");
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("현재 App 실행 파일 경로를 확인할 수 없습니다.");
        var autoStart = new ToggleSwitch
        {
            Header = "Windows 로그인 시 백그라운드로 실행",
            OffContent = "사용 안 함",
            OnContent = "사용 중",
            IsOn = autoStartRegistration.IsEnabled(executablePath),
        };
        autoStart.Toggled += async (_, _) =>
        {
            try
            {
                autoStartRegistration.SetEnabled(autoStart.IsOn, executablePath);
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("자동 시작 설정 변경 실패: " + exception.Message);
                autoStart.IsOn = autoStartRegistration.IsEnabled(executablePath);
            }
        };
        ContentPanel.Children.Add(autoStart);
        AddMuted("로그인 시 DownloadRouter.App --background가 실행되어 Agent와 트레이를 준비합니다. Native Host의 Agent 자동 시작 fallback도 유지됩니다.");

        var closeBehavior = new ComboBox
        {
            Header = "창 닫기 버튼 동작",
            ItemsSource = new[] { "트레이로 최소화", "프로그램 UI 완전 종료" },
            SelectedIndex = preferences.CloseBehavior == WindowCloseBehavior.MinimizeToTray ? 0 : 1,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
        };
        closeBehavior.SelectionChanged += (_, _) =>
        {
            preferences = preferences with
            {
                CloseBehavior = closeBehavior.SelectedIndex == 1
                    ? WindowCloseBehavior.ExitApplication
                    : WindowCloseBehavior.MinimizeToTray,
            };
            preferencesStore.Save(preferences);
        };
        ContentPanel.Children.Add(closeBehavior);
        AddMuted("기본값은 트레이로 최소화입니다. 트레이 메뉴의 ‘종료’는 설정과 관계없이 UI와 트레이를 종료합니다.");

        var theme = new ComboBox
        {
            Header = "테마",
            ItemsSource = new[] { "시스템 설정", "라이트", "다크" },
            SelectedIndex = preferences.ThemePreference switch
            {
                AppThemePreference.Light => 1,
                AppThemePreference.Dark => 2,
                _ => 0,
            },
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
        };
        theme.SelectionChanged += (_, _) =>
        {
            var selected = theme.SelectedIndex switch
            {
                1 => AppThemePreference.Light,
                2 => AppThemePreference.Dark,
                _ => AppThemePreference.System,
            };
            preferences = preferences with { Theme = AppThemePolicy.ToStorageValue(selected) };
            preferencesStore.Save(preferences);
            themeManager.SetPreference(selected);
        };
        ContentPanel.Children.Add(theme);
        AddMuted("시스템 설정은 ElementTheme.Default를 사용하므로 Windows 앱 테마 변경을 따릅니다. 라이트/다크 선택은 열린 창과 이후 생성되는 저장 위치 선택 창에 즉시 적용됩니다.");
        return Task.CompletedTask;
    }
}
