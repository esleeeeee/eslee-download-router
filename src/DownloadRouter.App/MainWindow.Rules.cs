using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private Guid? editingRuleId;

    private async Task ShowRulesManagementAsync()
    {
        Prepare("사이트 규칙", "새 규칙 만들기와 저장된 규칙을 한 페이지의 구분된 섹션에서 관리합니다.");
        try
        {
            var response = await agent.SendAsync("rules.list");
            var rules = AgentClient.ReadData<List<DownloadRule>>(response) ?? [];
            var editing = editingRuleId is Guid id ? rules.FirstOrDefault(rule => rule.Id == id) : null;
            if (editingRuleId is not null && editing is null)
            {
                editingRuleId = null;
            }

            AddRuleEditor(editing);
            ContentPanel.Children.Add(new Border { Height = 1, Opacity = 0.35, Margin = new Thickness(0, 12, 0, 12) });
            ContentPanel.Children.Add(new TextBlock
            {
                Text = $"저장된 규칙 · {rules.Count}개",
                Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
            });
            foreach (var rule in rules)
            {
                ContentPanel.Children.Add(CreateRuleCard(rule));
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

    private void AddRuleEditor(DownloadRule? editing)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = editing is null ? "새 규칙 만들기" : $"규칙 편집 · {editing.Name}",
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
            TextWrapping = TextWrapping.Wrap,
        });
        var name = new TextBox
        {
            Header = "규칙 이름",
            PlaceholderText = "예: 네이버 다운로드",
            Text = editing?.Name ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var matchValue = new TextBox
        {
            Header = "대상 사이트 또는 URL 일부",
            PlaceholderText = "naver.com",
            Text = editing?.MatchValue ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var matchType = new ComboBox
        {
            Header = "매칭 방식",
            ItemsSource = Enum.GetValues<RuleMatchType>(),
            SelectedItem = editing?.MatchType ?? RuleMatchType.DomainAndSubdomains,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var matchTarget = new ComboBox
        {
            Header = "매칭 대상",
            ItemsSource = Enum.GetValues<RuleMatchTarget>(),
            SelectedItem = editing?.MatchTarget ?? RuleMatchTarget.InitiatingPage,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var storageRoot = new TextBox
        {
            Header = "저장 루트",
            Text = editing?.StorageRoot ?? "{Downloads}\\eslee\\Routed",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var chooseRoot = new Button
        {
            Content = "폴더 선택…",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        chooseRoot.Click += async (_, _) => await PickStorageRootAsync(storageRoot);
        var storageMode = new ComboBox
        {
            Header = "저장 방식",
            ItemsSource = Enum.GetValues<StorageMode>(),
            SelectedItem = editing?.StorageMode ?? StorageMode.Automatic,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var pathStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        var checkPath = new Button { Content = "경로 읽기/쓰기 진단", HorizontalAlignment = HorizontalAlignment.Left };
        checkPath.Click += (_, _) => pathStatus.Text = DiagnoseStorageRoot(storageRoot.Text);
        var preview = new TextBlock
        {
            Text = "매칭 예시가 여기에 표시됩니다.",
            TextWrapping = TextWrapping.Wrap,
        };
        matchValue.TextChanged += (_, _) => preview.Text = string.IsNullOrWhiteSpace(matchValue.Text)
            ? "매칭 예시가 여기에 표시됩니다."
            : $"{matchValue.Text.Trim()} 및 선택한 방식에 맞는 주소에 적용됩니다.";

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = new Button { Content = editing is null ? "규칙 저장" : "규칙 수정" };
        save.Click += async (_, _) =>
        {
            try
            {
                var diagnosis = DiagnoseStorageRoot(storageRoot.Text);
                pathStatus.Text = diagnosis;
                if (!diagnosis.StartsWith("사용 가능", StringComparison.Ordinal))
                {
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                var rule = new DownloadRule(
                    editing?.Id ?? Guid.NewGuid(),
                    name.Text.Trim(),
                    editing?.IsEnabled ?? true,
                    (RuleMatchType)matchType.SelectedItem,
                    matchValue.Text.Trim(),
                    (RuleMatchTarget)matchTarget.SelectedItem,
                    storageRoot.Text.Trim(),
                    (StorageMode)storageMode.SelectedItem,
                    editing?.Priority ?? 0,
                    editing?.ListOrder ?? 0,
                    editing?.CreatedAt ?? now,
                    now);
                var response = await agent.SendAsync("rules.upsert", rule);
                if (!response.Success)
                {
                    await ShowMessageAsync(response.Message ?? "규칙 저장에 실패했습니다.");
                    return;
                }

                editingRuleId = null;
                await ShowRulesManagementAsync();
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("규칙 저장 실패: " + exception.Message);
            }
        };
        actions.Children.Add(save);
        if (editing is not null)
        {
            var cancel = new Button { Content = "편집 취소" };
            cancel.Click += async (_, _) =>
            {
                editingRuleId = null;
                await ShowRulesManagementAsync();
            };
            actions.Children.Add(cancel);
        }

        panel.Children.Add(name);
        panel.Children.Add(matchValue);
        panel.Children.Add(matchType);
        panel.Children.Add(matchTarget);
        panel.Children.Add(storageRoot);
        panel.Children.Add(chooseRoot);
        panel.Children.Add(checkPath);
        panel.Children.Add(pathStatus);
        panel.Children.Add(storageMode);
        panel.Children.Add(preview);
        panel.Children.Add(actions);
        ContentPanel.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(10),
            Background = themeManager.GetThemeBrush("CardSurfaceBrush"),
            Child = panel,
        });
    }

    private Border CreateRuleCard(DownloadRule rule)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = rule.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"매칭: {rule.MatchType} · 대상: {rule.MatchTarget}\n값: {rule.MatchValue}\n저장 루트: {rule.StorageRoot}\n저장 방식: {rule.StorageMode}",
            TextWrapping = TextWrapping.Wrap,
        });
        var enabled = new ToggleSwitch { Header = "규칙 활성화", IsOn = rule.IsEnabled };
        enabled.Toggled += async (_, _) =>
        {
            var response = await agent.SendAsync(
                "rules.upsert",
                rule with { IsEnabled = enabled.IsOn, UpdatedAt = DateTimeOffset.UtcNow });
            if (!response.Success)
            {
                await ShowMessageAsync(response.Message ?? "규칙 상태 변경에 실패했습니다.");
            }
        };
        panel.Children.Add(enabled);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var edit = new Button { Content = "편집" };
        edit.Click += async (_, _) =>
        {
            editingRuleId = rule.Id;
            await ShowRulesManagementAsync();
            ContentScrollViewer.ChangeView(null, 0, null);
        };
        actions.Children.Add(edit);
        var delete = new Button { Content = "삭제" };
        delete.Click += async (_, _) =>
        {
            if (!await ShowConfirmationAsync(
                    "사이트 규칙 삭제",
                    "이 규칙을 삭제해도 이미 이동된 파일과 다운로드 이력은 삭제되지 않습니다."))
            {
                return;
            }

            var response = await agent.SendAsync("rules.delete", new RuleDeletePayload(rule.Id));
            if (!response.Success)
            {
                await ShowMessageAsync(response.Message ?? "규칙 삭제에 실패했습니다.");
                return;
            }

            if (editingRuleId == rule.Id)
            {
                editingRuleId = null;
            }
            await ShowRulesManagementAsync();
        };
        actions.Children.Add(delete);
        panel.Children.Add(actions);
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = themeManager.GetThemeBrush("CardSurfaceBrush"),
            Child = panel,
        };
    }

    private async Task PickStorageRootAsync(TextBox target)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        var selected = await picker.PickSingleFolderAsync();
        if (selected is not null)
        {
            target.Text = selected.Path;
        }
    }

    private string DiagnoseStorageRoot(string configuredPath)
    {
        try
        {
            var fullPath = pathResolver.Resolve(configuredPath.Trim());
            if (!Directory.Exists(fullPath))
            {
                return "사용 불가: 폴더가 존재하지 않습니다.";
            }

            _ = Directory.EnumerateFileSystemEntries(fullPath).Take(1).ToArray();
            var probe = Path.Combine(fullPath, $".eslee-write-test-{Guid.NewGuid():N}.tmp");
            using (new FileStream(
                       probe,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       1,
                       FileOptions.DeleteOnClose))
            {
            }

            return $"사용 가능: 읽기/쓰기 확인 · {fullPath}";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return $"사용 불가: {exception.Message}";
        }
    }
}
