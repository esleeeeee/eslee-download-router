using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace DownloadRouter.App;

public sealed partial class MainWindow : Window
{
    private readonly AgentClient agent = new();
    private readonly PathTokenResolver pathResolver = new(new WindowsKnownPathProvider());
    private readonly PathBoundaryValidator boundaryValidator = new();

    public MainWindow(
        AppPreferencesStore preferencesStore,
        ThemeManager themeManager,
        AppPreferences preferences)
    {
        this.preferencesStore = preferencesStore;
        this.themeManager = themeManager;
        this.preferences = preferences;
        InitializeComponent();
        themeManager.RegisterWindow(this);
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

        AddExtensionFolderGuidance();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shows the extension folder that actually exists on this machine. An installed build
    /// points at its own folder; a source clone points at the development build output.
    /// </summary>
    private void AddExtensionFolderGuidance()
    {
        var location = ExtensionFolderLocator.Locate(AppContext.BaseDirectory);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "확장 연결 순서",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "1. 브라우저의 확장 관리 페이지를 엽니다.\n"
                + "2. 개발자 모드를 켭니다.\n"
                + "3. 압축 해제된 확장 로드를 선택합니다.\n"
                + "4. 아래 폴더를 지정합니다.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = ExtensionFolderLocator.DescribeForUser(location),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });

        if (location.Exists && location.Path is string folder)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var copy = new Button { Content = "폴더 경로 복사" };
            copy.Click += (_, _) =>
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(folder);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            };
            actions.Children.Add(copy);

            var open = new Button { Content = "폴더 열기" };
            open.Click += async (_, _) =>
            {
                try
                {
                    var storageFolder = await StorageFolder.GetFolderFromPathAsync(folder);
                    _ = await Launcher.LaunchFolderAsync(storageFolder);
                }
                catch (Exception exception)
                {
                    await ShowMessageAsync("폴더를 열지 못했습니다: " + exception.Message);
                }
            };
            actions.Children.Add(open);
            panel.Children.Add(actions);
        }

        panel.Children.Add(CreateHelpText(
            "확장을 불러온 뒤 표시된 ID가 "
                + $"{ProtocolConstants.ExtensionId} 인지 확인하세요. "
                + "프로그램을 업데이트한 뒤에는 같은 화면에서 확장을 새로 고치고 브라우저를 다시 시작해야 합니다."));
        ContentPanel.Children.Add(CreateCard(panel));
    }

    private async Task ShowDiagnosticsAsync()
    {
        Prepare(
            "진단 및 문제 해결",
            "다운로드 주소와 파일 이름은 표시하지 않습니다. 다만 데이터 저장 위치에는 Windows 계정 이름이 들어가므로, "
                + "이 화면을 캡처해 공유하기 전에 확인해 주세요.");
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
            AddExtensionRefreshGuidance(response);
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

    private async Task ShowAboutAsync()
    {
        var version = ProductVersionInfo.Read(typeof(App).Assembly, AppContext.BaseDirectory);
        Prepare(version.ProductName, "Chromium 다운로드를 사이트 규칙에 따라 안전하게 정리하는 로컬 Windows 프로그램입니다.");
        AddCard("현재 버전", version.DisplayVersion);
        AddCard("빌드", $"{version.BuildDescription} · {version.InformationalVersion}");
        AddCard("배포 형태", version.DistributionDescription);
        AddCard("개인정보", "서버 전송, 텔레메트리, 광고 SDK가 없습니다. 다운로드 처리에 필요한 최소 정보만 로컬에 저장합니다.");
        AddCard("지원 범위", BrowserSupportCatalog.SupportSummary);
        AddCard("프로토콜", $"Native Messaging ↔ 현재 사용자 Named Pipe v{ProtocolConstants.CurrentVersion} ↔ 단일 Agent");

        var openData = new Button
        {
            Content = "데이터 저장 위치 열기",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        openData.Click += async (_, _) =>
        {
            try
            {
                var dataDirectory = Path.GetDirectoryName(preferencesStore.ConfigPath)
                    ?? throw new InvalidOperationException("데이터 저장 위치를 확인할 수 없습니다.");
                Directory.CreateDirectory(dataDirectory);
                var folder = await StorageFolder.GetFolderFromPathAsync(dataDirectory);
                _ = await Launcher.LaunchFolderAsync(folder);
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("데이터 저장 위치 열기 실패: " + exception.Message);
            }
        };
        ContentPanel.Children.Add(openData);

        var openGitHub = new Button
        {
            Content = "GitHub 저장소 열기",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        openGitHub.Click += async (_, _) =>
            _ = await Launcher.LaunchUriAsync(new Uri("https://github.com/esleeeeee/eslee-download-router"));
        ContentPanel.Children.Add(openGitHub);
        await Task.CompletedTask;
    }

    /// <summary>
    /// The browser can keep serving a cached older extension after an upgrade. When the
    /// agent refuses downloads from an unrecognised build, tell the user how to fix it.
    /// </summary>
    private void AddExtensionRefreshGuidance(AgentResponse response)
    {
        var rejections = 0;
        if (response.Data is System.Text.Json.JsonElement data
            && data.TryGetProperty("unsupportedExtensionRejections", out var value))
        {
            _ = value.TryGetInt32(out rejections);
        }

        ContentPanel.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = rejections > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational,
            Title = rejections > 0 ? "브라우저 확장을 새로 고쳐야 합니다" : "프로그램을 업데이트한 뒤에는",
            Message = rejections > 0
                ? DownloadRouter.Core.Jobs.DownloadRegistrationPolicy.ExtensionRefreshGuidance
                : "확장 관리 화면에서 eslee Download Router를 새로 고친 뒤 브라우저를 다시 시작하세요. 브라우저는 업데이트된 확장 파일을 자동으로 다시 읽지 않습니다.",
        });
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
        ContentPanel.Children.Add(CreateCard(panel));
    }

    /// <summary>White card surface with a soft blue-grey outline.</summary>
    private Border CreateCard(UIElement child, double padding = 16)
        => new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(padding),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = themeManager.GetThemeBrush("CardBorderBrush"),
            Background = themeManager.GetThemeBrush("CardSurfaceBrush"),
            Child = child,
        };

    /// <summary>Grouped area inside a card, used to tie related inputs together.</summary>
    private Border CreateSection(string title, string? helpText, UIElement child)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(helpText))
        {
            panel.Children.Add(new TextBlock
            {
                Text = helpText,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        panel.Children.Add(child);
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

    private Border CreateSeparator(double verticalMargin = 8)
        => new()
        {
            Height = 1,
            Background = themeManager.GetThemeBrush("SeparatorBrush"),
            Margin = new Thickness(0, verticalMargin, 0, verticalMargin),
        };

    private static Button CreatePrimaryButton(string content)
        => new()
        {
            Content = content,
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    private static TextBlock CreateHelpText(string text)
        => new()
        {
            Text = text,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
        };

    private Border CreateAccentBadge(string text)
        => new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(12),
            Background = themeManager.GetThemeBrush("AccentStatusSurfaceBrush"),
            Child = new TextBlock
            {
                Text = text,
                Foreground = themeManager.GetThemeBrush("AccentTextBrush"),
                TextWrapping = TextWrapping.Wrap,
            },
        };

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
