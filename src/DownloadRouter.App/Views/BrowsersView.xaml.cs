using System.Globalization;
using System.Text.Json;
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
    private bool connectionStatusLoading;

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
        _ = RefreshConnectionStatusAsync();
    }

    public void OnLiveUpdate(LiveSnapshot snapshot, bool changed)
    {
        _ = RefreshConnectionStatusAsync();
    }

    private async Task RefreshConnectionStatusAsync()
    {
        if (connectionStatusLoading)
        {
            return;
        }

        connectionStatusLoading = true;
        try
        {
            var response = await AppServices.Agent.SendAsync("diagnostics.status");
            if (!response.Success || response.Data is not JsonElement data
                || !data.TryGetProperty("extensionEverConnected", out var connected)
                || connected.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                RenderConnectionUnavailable();
                return;
            }

            // The "after updating, reload the extension" tip only concerns users who already added it.
            ExtensionRefreshBar.IsOpen = connected.GetBoolean();
            if (!connected.GetBoolean())
            {
                ConnectionPlate.Kind = BadgeKind.Caution;
                ConnectionStatusText.Text = AppText.Pick(
                    "아직 연결되지 않음 - 아래 순서대로 확장을 추가하세요",
                    "Not connected yet - add the extension with the steps below");
                return;
            }

            ConnectionPlate.Kind = BadgeKind.Success;
            ConnectionStatusText.Text = data.TryGetProperty("extensionLastSeenAt", out var lastSeen)
                && lastSeen.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(lastSeen.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
                ? AppText.Pick($"연결됨 · 마지막 연결 {timestamp.ToLocalTime():yyyy-MM-dd HH:mm}",
                    $"Connected · last contact {timestamp.ToLocalTime():yyyy-MM-dd HH:mm}")
                : AppText.Pick("연결됨 · 이전 다운로드 기록이 있습니다", "Connected · previous downloads found");
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"browser connection status read failed: {exception.GetType().Name}");
            RenderConnectionUnavailable();
        }
        finally
        {
            connectionStatusLoading = false;
        }
    }

    private void RenderConnectionUnavailable()
    {
        ConnectionPlate.Kind = BadgeKind.Neutral;
        ConnectionStatusText.Text = AppText.Pick(
            "백그라운드 서비스에 연결할 수 없어 연결 상태를 확인하지 못했습니다.",
            "Connection status is unavailable because the background service is not responding.");
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
            BrowserRows.Children.Add(new Border
            {
                Style = ThemeStyles.Get<Style>("DividerRowBorderStyle"),
                BorderThickness = new Thickness(0, BrowserRows.Children.Count == 0 ? 0 : 1, 0, 0),
                Child = row,
            });
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
