using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App;

public sealed partial class MainWindow : Window
{
    private readonly AgentClient agent = new();
    private readonly PathTokenResolver pathResolver = new(new WindowsKnownPathProvider());
    private readonly PathBoundaryValidator boundaryValidator = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "eslee Download Router";
        Navigation.SelectedItem = Navigation.MenuItems[0];
        InitializeLiveUpdates();
        Closed += (_, _) => StopLiveUpdates();
        _ = ShowDashboardAsync();
    }

    private async void Navigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        await ShowPageAsync(tag);
    }

    private Task ShowPageAsync(string tag)
        => tag switch
        {
            "dashboard" => ShowDashboardAsync(),
            "rules" => ShowRulesManagementAsync(),
            "history" => ShowHistoryAsync(),
            "pending" => ShowPendingAsync(),
            "browsers" => ShowBrowsersAsync(),
            "settings" => ShowSettingsManagementAsync(),
            "diagnostics" => ShowDiagnosticsAsync(),
            "about" => ShowAboutAsync(),
            _ => Task.CompletedTask,
        };

    private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var available = Math.Max(
            0,
            args.NewSize.Width - ContentViewport.Padding.Left - ContentViewport.Padding.Right);
        ContentPanel.Width = Math.Min(1100, available);
    }

    private Task ShowBrowsersAsync()
    {
        Prepare("브라우저 연결", "설치 상태와 개발자 모드 확장 설치 경로를 안내합니다.");
        foreach (var browser in BrowserCatalog.Detect())
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = browser.DisplayName,
                Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
            });
            panel.Children.Add(new TextBlock
            {
                Text = browser.IsInstalled
                    ? $"설치됨 · {(browser.IsOfficial ? "공식 지원" : "호환 지원")} · 확장 연결은 브라우저에서 확인 필요"
                    : "설치되지 않음",
                TextWrapping = TextWrapping.Wrap,
            });
            var open = new Button
            {
                Content = "확장 관리 페이지 열기",
                IsEnabled = browser.IsInstalled,
            };
            open.Click += async (_, _) =>
            {
                try
                {
                    BrowserCatalog.OpenManagementPage(browser);
                }
                catch (Exception exception)
                {
                    await ShowMessageAsync(exception.Message);
                }
            };
            panel.Children.Add(open);
            ContentPanel.Children.Add(new Border
            {
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(8),
                Child = panel,
            });
        }

        AddMuted("설치 순서: 개발자 모드 켜기 → 압축 해제된 확장 로드 → src/DownloadRouter.Extension/dist 선택 → Native Host 등록 → 연결 테스트");
        return Task.CompletedTask;
    }

    private async Task ShowDiagnosticsAsync()
    {
        Prepare("진단 및 문제 해결", "민감 URL과 다운로드 파일을 포함하지 않는 로컬 진단 정보만 표시합니다.");
        var test = new Button { Content = "Agent 연결 테스트" };
        test.Click += async (_, _) =>
        {
            try
            {
                var response = await agent.SendAsync("ping");
                await ShowMessageAsync(response.Success ? "Agent 연결이 정상입니다." : response.Message ?? "연결 실패");
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("연결 실패: " + exception.Message);
            }
        };
        ContentPanel.Children.Add(test);
        try
        {
            var response = await agent.SendAsync("diagnostics.status");
            AddCard("현재 진단 상태", response.Data?.GetRawText() ?? "응답 없음");
            var rasterizationScale = ContentPanel.XamlRoot?.RasterizationScale;
            AddCard(
                "UI 렌더링 배율",
                rasterizationScale is null
                    ? "현재 창의 배율을 확인할 수 없습니다."
                    : $"{rasterizationScale.Value * 100:0}% (XamlRoot RasterizationScale {rasterizationScale.Value:0.##})");
        }
        catch (Exception exception)
        {
            AddError("Agent 진단 정보를 읽지 못했습니다.", exception);
        }
    }

    private Task ShowAboutAsync()
    {
        Prepare("eslee Download Router", "Chromium 다운로드를 사이트 규칙에 따라 안전하게 정리하는 로컬 Windows 프로그램입니다.");
        AddCard("개인정보", "서버 전송, 텔레메트리, 광고 SDK가 없습니다. 다운로드 처리에 필요한 최소 정보만 로컬에 저장합니다.");
        AddCard("지원 범위", "Windows 11 x64 · Whale / Edge / Chrome 공식 지원 · Brave / Vivaldi / Opera 호환 지원 · Firefox 제외");
        AddCard("프로토콜", $"Native Messaging ↔ 현재 사용자 Named Pipe v{ProtocolConstants.CurrentVersion} ↔ 단일 Agent");
        return Task.CompletedTask;
    }

    private void Prepare(string title, string description)
    {
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(new TextBlock
        {
            Text = title,
            Style = Application.Current.Resources["TitleTextBlockStyle"] as Style,
        });
        ContentPanel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
    }

    private void AddCard(string title, string value)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
        ContentPanel.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
            Child = panel,
        });
    }

    private void AddMuted(string text)
        => ContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });

    private void AddError(string prefix, Exception exception)
        => ContentPanel.Children.Add(new InfoBar
        {
            IsOpen = true,
            Severity = InfoBarSeverity.Error,
            Title = prefix,
            Message = exception.Message,
        });

    private async Task ShowMessageAsync(string message)
    {
        blockingDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "eslee Download Router",
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "확인",
                XamlRoot = ContentPanel.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            blockingDialogOpen = false;
        }
    }
}
