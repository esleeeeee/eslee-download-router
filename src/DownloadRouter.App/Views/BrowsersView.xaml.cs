using DownloadRouter.App.Controls;
using DownloadRouter.App.Services;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DownloadRouter.App.Views;

/// <summary>
/// Extension setup guidance. The folder shown is the one that actually exists next to the
/// running executable (installed build or development output), never a fixed path.
/// </summary>
public sealed partial class BrowsersView : UserControl, IAppView
{
    private ExtensionFolderLocation? location;

    public BrowsersView()
    {
        InitializeComponent();
        ExtensionRefreshBar.Title = AppText.Pick("프로그램을 업데이트한 뒤에는", "After updating the app");
        ExtensionRefreshBar.Message = AppText.Pick(
            "확장 관리 화면에서 eslee Download Router 확장을 새로 고친 뒤 브라우저를 다시 시작하세요. 브라우저는 업데이트된 확장 파일을 자동으로 다시 읽지 않습니다.",
            "Reload the eslee Download Router extension on the extensions page, then restart the browser. Browsers do not reload updated extension files on their own.");
        ExtensionIdText.Text = AppText.Pick(
            $"확장을 불러온 뒤 확장 ID가 {ProtocolConstants.ExtensionId} 인지 확인하세요.",
            $"After loading, check that the extension ID is {ProtocolConstants.ExtensionId}.");
        SupportText.Text = BrowserSupportCatalog.SupportSummary;
    }

    public void OnShown(object? parameter)
    {
        location = ExtensionFolderLocator.Locate(AppContext.BaseDirectory);
        ExtensionFolderText.Text = ExtensionFolderLocator.DescribeForUser(location);
        ExtensionFolderActions.Visibility = location.Exists ? Visibility.Visible : Visibility.Collapsed;
        RenderBrowsers();
    }

    public void OnLiveUpdate(LiveSnapshot snapshot, bool changed)
    {
    }

    private void RenderBrowsers()
    {
        BrowserRows.Children.Clear();
        foreach (var browser in BrowserCatalog.Detect())
        {
            var row = new Grid
            {
                Padding = new Thickness(20, 12, 20, 12),
                ColumnSpacing = 16,
                BorderBrush = ThemeStyles.Get<Microsoft.UI.Xaml.Media.Brush>("DividerStrokeBrush"),
                BorderThickness = new Thickness(0, BrowserRows.Children.Count == 0 ? 0 : 1, 0, 0),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new IconPlate
            {
                Glyph = "",
                Kind = browser.IsInstalled ? BadgeKind.Accent : BadgeKind.Neutral,
            });

            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = browser.DisplayName, Style = ThemeStyles.Get<Style>("BodyStrongTextBlockStyle") });
            text.Children.Add(new TextBlock
            {
                Text = (browser.IsInstalled ? AppText.Pick("설치됨", "Installed") : AppText.Pick("설치되지 않음", "Not installed"))
                    + " · " + (browser.IsOfficial ? AppText.Pick("공식 지원", "Officially supported") : AppText.Pick("호환 지원", "Compatible")),
                Style = ThemeStyles.Get<Style>("CaptionSecondaryTextStyle"),
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var open = new Button
            {
                Content = AppText.Pick("확장 관리 열기", "Open extensions page"),
                IsEnabled = browser.IsInstalled,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                open,
                AppText.Pick($"{browser.DisplayName} 확장 관리 열기", $"Open {browser.DisplayName} extensions page"));
            open.Click += async (_, _) =>
            {
                try
                {
                    BrowserCatalog.OpenManagementPage(browser);
                }
                catch (Exception exception) when (exception is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    await AppDialog.ShowMessageAsync(XamlRoot, exception.Message, DialogTone.Error);
                }
            };
            Grid.SetColumn(open, 2);
            row.Children.Add(open);
            BrowserRows.Children.Add(row);
        }
    }

    private void CopyFolder_Click(object sender, RoutedEventArgs args)
    {
        if (location?.Path is not string folder)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(folder);
        Clipboard.SetContent(package);
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs args)
    {
        if (location?.Path is not string folder)
        {
            return;
        }

        if (!await ShellActions.DirectoryExistsAsync(folder))
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("폴더를 찾을 수 없습니다.", "The folder was not found."), DialogTone.Warning);
            return;
        }

        ShellActions.OpenFolder(folder);
    }
}
