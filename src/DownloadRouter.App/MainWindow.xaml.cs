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

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
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
            "rules" => ShowRulesAsync(),
            "history" => ShowHistoryAsync(),
            "pending" => ShowPendingAsync(),
            "browsers" => ShowBrowsersAsync(),
            "settings" => ShowSettingsAsync(),
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

    private async Task ShowRulesAsync()
    {
        Prepare("사이트 규칙", "도메인, 정확한 호스트 또는 URL 일부를 기준으로 저장 위치를 지정합니다.");
        var name = new TextBox { Header = "규칙 이름", PlaceholderText = "예: 네이버 다운로드", HorizontalAlignment = HorizontalAlignment.Stretch };
        var matchValue = new TextBox { Header = "대상 사이트 또는 URL 일부", PlaceholderText = "naver.com", HorizontalAlignment = HorizontalAlignment.Stretch };
        var storageRoot = new TextBox { Header = "저장 루트", Text = "{Downloads}\\eslee\\Routed", HorizontalAlignment = HorizontalAlignment.Stretch };
        var matchType = new ComboBox { Header = "매칭 방식", ItemsSource = Enum.GetValues<RuleMatchType>(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var matchTarget = new ComboBox { Header = "매칭 대상", ItemsSource = Enum.GetValues<RuleMatchTarget>(), SelectedItem = RuleMatchTarget.InitiatingPage, HorizontalAlignment = HorizontalAlignment.Stretch };
        var storageMode = new ComboBox { Header = "저장 방식", ItemsSource = Enum.GetValues<StorageMode>(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var preview = new TextBlock { Text = "naver.com은 naver.com과 모든 하위 도메인에 일치합니다.", TextWrapping = TextWrapping.Wrap };
        matchValue.TextChanged += (_, _) => preview.Text = string.IsNullOrWhiteSpace(matchValue.Text)
            ? "매칭 예시가 여기에 표시됩니다."
            : $"{matchValue.Text.Trim()} 및 선택한 방식에 맞는 주소에 적용됩니다.";
        var save = new Button { Content = "규칙 저장", HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var rule = new DownloadRule(
                    Guid.NewGuid(),
                    name.Text.Trim(),
                    true,
                    (RuleMatchType)matchType.SelectedItem,
                    matchValue.Text.Trim(),
                    (RuleMatchTarget)matchTarget.SelectedItem,
                    storageRoot.Text.Trim(),
                    (StorageMode)storageMode.SelectedItem,
                    0,
                    0,
                    now,
                    now);
                var response = await agent.SendAsync("rules.upsert", rule);
                await ShowMessageAsync(response.Success ? "규칙을 저장했습니다." : response.Message ?? "규칙 저장에 실패했습니다.");
                await ShowRulesAsync();
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("규칙 저장 실패: " + exception.Message);
            }
        };

        ContentPanel.Children.Add(name);
        ContentPanel.Children.Add(matchValue);
        ContentPanel.Children.Add(matchType);
        ContentPanel.Children.Add(matchTarget);
        ContentPanel.Children.Add(storageRoot);
        ContentPanel.Children.Add(storageMode);
        ContentPanel.Children.Add(preview);
        ContentPanel.Children.Add(save);
        ContentPanel.Children.Add(new Border { Height = 1, Opacity = 0.4 });

        try
        {
            var response = await agent.SendAsync("rules.list");
            var rules = AgentClient.ReadData<List<DownloadRule>>(response) ?? [];
            foreach (var rule in rules)
            {
                AddCard(rule.Name, $"{rule.MatchType}: {rule.MatchValue} → {rule.StorageRoot} ({rule.StorageMode})");
            }

            if (rules.Count == 0)
            {
                AddMuted("아직 저장된 규칙이 없습니다.");
            }
        }
        catch (Exception exception)
        {
            AddError("규칙 목록을 불러오지 못했습니다.", exception);
        }
    }

    private Task ShowBrowsersAsync()
    {
        Prepare("브라우저 연결", "설치 상태와 개발자 모드 확장 설치 경로를 안내합니다.");
        foreach (var browser in BrowserCatalog.Detect())
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = browser.DisplayName, Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style });
            panel.Children.Add(new TextBlock
            {
                Text = browser.IsInstalled
                    ? $"설치됨 · {(browser.IsOfficial ? "정식 지원" : "호환 지원")} · 확장 연결은 브라우저에서 확인 필요"
                    : "설치되지 않음",
                TextWrapping = TextWrapping.Wrap,
            });
            var open = new Button { Content = "확장 관리 페이지 열기", IsEnabled = browser.IsInstalled };
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
            ContentPanel.Children.Add(new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Child = panel });
        }

        AddMuted("설치 순서: 개발자 모드 켜기 → 압축 해제된 확장 로드 → src/DownloadRouter.Extension/dist 선택 → Native Host 등록 → 연결 테스트.");
        return Task.CompletedTask;
    }

    private Task ShowSettingsAsync()
    {
        Prepare("일반 설정", "모든 설정과 데이터는 현재 사용자 LocalAppData 아래에만 저장됩니다.");
        ContentPanel.Children.Add(new ToggleSwitch { Header = "Windows 로그인 시 Agent 실행", IsOn = false });
        ContentPanel.Children.Add(new ComboBox { Header = "테마", ItemsSource = new[] { "시스템 설정", "라이트", "다크" }, SelectedIndex = 0 });
        AddMuted("시작 프로그램 등록은 기본값이 아니며, 현재 화면의 토글은 다음 단계에서 Agent 설정 저장과 연결됩니다.");
        return Task.CompletedTask;
    }

    private async Task ShowDiagnosticsAsync()
    {
        Prepare("진단 및 문제 해결", "민감 URL과 다운로드 파일을 포함하지 않는 로컬 진단 정보를 표시합니다.");
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
        AddCard("개인정보", "서버 전송, 텔레메트리, 광고 SDK가 없습니다. 다운로드 처리에 필요한 최소 정보만 로컬에 정제해 저장합니다.");
        AddCard("지원 범위", "Windows 11 x64 · Whale / Edge / Chrome 정식 지원 · Brave / Vivaldi / Opera 호환 지원 · Firefox 제외");
        AddCard("프로토콜", $"Native Messaging → 현재 사용자 Named Pipe v{ProtocolConstants.CurrentVersion} → 단일 Agent");
        return Task.CompletedTask;
    }

    private List<string> EnumerateSafeFolders(string root)
    {
        var result = new List<string> { "." };
        if (!Directory.Exists(root))
        {
            return result;
        }

        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0 && result.Count < 1000)
        {
            var current = queue.Dequeue();
            foreach (var child in Directory.EnumerateDirectories(current).OrderBy(static path => path, StringComparer.CurrentCultureIgnoreCase))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                boundaryValidator.EnsureWithin(root, child);
                result.Add(Path.GetRelativePath(root, child));
                queue.Enqueue(child);
            }
        }

        return result;
    }

    private void Prepare(string title, string description)
    {
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(new TextBlock { Text = title, Style = Application.Current.Resources["TitleTextBlockStyle"] as Style });
        ContentPanel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
    }

    private void AddCard(string title, string value)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
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
        => ContentPanel.Children.Add(new TextBlock { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

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
