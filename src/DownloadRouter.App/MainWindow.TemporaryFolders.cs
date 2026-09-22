using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private List<ActiveTemporaryFolder> activeTemporaryFolders = [];

    private async Task ReadTemporaryFoldersAsync()
    {
        var response = await agent.SendAsync("temporary-folder.list");
        if (!response.Success)
            throw new InvalidOperationException(response.Message ?? "10분 자동 저장 상태를 읽지 못했습니다.");
        activeTemporaryFolders = AgentClient.ReadData<List<ActiveTemporaryFolder>>(response) ?? [];
    }

    private void AddTemporaryFoldersCard()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "10분 자동 저장",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(CreateHelpText(activeTemporaryFolders.Count == 0
            ? "현재 적용 중인 10분 자동 저장 설정이 없습니다."
            : "취소하면 이후 다운로드부터 저장 위치를 다시 묻습니다. 이미 지정된 파일의 경로는 바뀌지 않습니다."));
        foreach (var choice in activeTemporaryFolders)
        {
            var item = new StackPanel { Spacing = 8 };
            item.Children.Add(new TextBlock
            {
                Text = $"규칙: {choice.RuleName}\n저장 위치: {choice.DestinationFolder}\n종료 시각: {choice.ExpiresAt.ToLocalTime():HH:mm:ss}",
                TextWrapping = TextWrapping.Wrap,
            });
            var cancel = new Button
            {
                Content = "10분 자동 저장 취소",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cancel, $"{choice.RuleName} 10분 자동 저장 취소");
            cancel.Click += async (_, _) =>
            {
                cancel.IsEnabled = false;
                try
                {
                    var response = await agent.SendAsync("temporary-folder.cancel", new TemporaryFolderCancelPayload(choice.RuleId));
                    if (!response.Success)
                        throw new InvalidOperationException(response.Message ?? "10분 자동 저장을 취소하지 못했습니다.");
                    activeTemporaryFolders.RemoveAll(entry => entry.RuleId == choice.RuleId);
                    livePageSnapshot = null;
                    if ((Navigation.SelectedItem as NavigationViewItem)?.Tag as string == "dashboard")
                        await ShowDashboardAsync();
                }
                catch (Exception exception)
                {
                    cancel.IsEnabled = true;
                    await ShowMessageAsync(exception.Message);
                }
            };
            item.Children.Add(cancel);
            panel.Children.Add(item);
        }
        ContentPanel.Children.Add(CreateCard(panel));
    }
}
