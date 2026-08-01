using DownloadRouter.Core.Models;
using DownloadRouter.Core.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private Guid? editingRuleId;
    private DispatcherTimer? storageRootProbeTimer;

    private async Task ShowRulesManagementAsync()
    {
        Prepare("사이트 규칙", "어떤 사이트의 다운로드를 어디에 저장할지 정합니다.");
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
            ContentPanel.Children.Add(CreateSeparator(12));
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
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = editing is null ? "새 규칙 만들기" : $"규칙 편집 · {editing.Name}",
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
            TextWrapping = TextWrapping.Wrap,
        });

        // 1. 규칙 기본 정보
        var name = new TextBox
        {
            Header = "규칙 이름",
            PlaceholderText = "예: 사내 자료실 다운로드",
            Text = editing?.Name ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var enabled = new ToggleSwitch
        {
            Header = "이 규칙 사용",
            IsOn = editing?.IsEnabled ?? true,
            OnContent = "사용 중",
            OffContent = "사용 안 함",
        };
        var basics = new StackPanel { Spacing = 12 };
        basics.Children.Add(name);
        basics.Children.Add(enabled);
        panel.Children.Add(CreateSection(
            "1. 규칙 기본 정보",
            "나중에 알아볼 수 있는 이름을 정해 주세요.",
            basics));

        // 2. 어떤 다운로드에 적용할까요
        var matchValue = new TextBox
        {
            Header = "사이트 주소 또는 주소에 포함된 문구",
            PlaceholderText = "naver.com",
            Text = editing?.MatchValue ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var matchTypeChoices = RulePresentation.MatchTypes;
        var matchType = new ComboBox
        {
            Header = "적용 범위",
            ItemsSource = matchTypeChoices.Select(static choice => choice.Label).ToArray(),
            SelectedIndex = IndexOf(matchTypeChoices, editing?.MatchType ?? RulePresentation.DefaultMatchType),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var matchTypeHelp = CreateHelpText(string.Empty);
        var scope = new StackPanel { Spacing = 12 };
        scope.Children.Add(matchValue);
        scope.Children.Add(matchType);
        scope.Children.Add(matchTypeHelp);
        panel.Children.Add(CreateSection(
            "2. 어떤 다운로드에 적용할까요",
            "이 규칙이 반응할 사이트를 정합니다.",
            scope));

        // 3. 어떤 주소를 확인할까요
        var matchTargetChoices = RulePresentation.MatchTargets;
        var matchTarget = new ComboBox
        {
            Header = "확인할 주소",
            ItemsSource = matchTargetChoices.Select(static choice => choice.Label).ToArray(),
            SelectedIndex = IndexOf(matchTargetChoices, editing?.MatchTarget ?? RulePresentation.DefaultMatchTarget),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var matchTargetHelp = CreateHelpText(string.Empty);
        var targetPanel = new StackPanel { Spacing = 12 };
        targetPanel.Children.Add(matchTarget);
        targetPanel.Children.Add(matchTargetHelp);
        targetPanel.Children.Add(CreateHelpText(
            $"권장: {RulePresentation.MatchTargetLabel(RulePresentation.DefaultMatchTarget)}"));
        panel.Children.Add(CreateSection(
            "3. 어떤 주소를 확인할까요",
            "같은 파일이라도 어떤 주소를 기준으로 판단할지 정합니다.",
            targetPanel));

        // 4. 어디로 보낼까요
        var storageRoot = new TextBox
        {
            Header = "저장할 기준 폴더",
            Text = editing?.StorageRoot ?? "{Downloads}\\eslee\\Routed",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var chooseRoot = new Button
        {
            Content = "폴더 선택…",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var pathStatus = CreateHelpText("폴더 사용 가능 여부를 확인하는 중입니다.");
        var storageModeChoices = RulePresentation.StorageModes;
        var storageMode = new ComboBox
        {
            Header = "저장 방식",
            ItemsSource = storageModeChoices.Select(static choice => choice.Label).ToArray(),
            SelectedIndex = IndexOf(storageModeChoices, editing?.StorageMode ?? RulePresentation.DefaultStorageMode),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var storageModeHelp = CreateHelpText(string.Empty);
        var destination = new StackPanel { Spacing = 12 };
        destination.Children.Add(storageRoot);
        destination.Children.Add(chooseRoot);
        destination.Children.Add(pathStatus);
        destination.Children.Add(CreateSeparator(2));
        destination.Children.Add(storageMode);
        destination.Children.Add(storageModeHelp);
        panel.Children.Add(CreateSection(
            "4. 어디로 보낼까요",
            "기준 폴더와 저장 방식은 함께 동작합니다.",
            destination));

        // 5. 규칙 요약
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(CreateSection("5. 규칙 요약", null, summary));

        void RefreshDescriptions()
        {
            var selectedMatchType = matchTypeChoices[Math.Max(matchType.SelectedIndex, 0)];
            var selectedMatchTarget = matchTargetChoices[Math.Max(matchTarget.SelectedIndex, 0)];
            var selectedStorageMode = storageModeChoices[Math.Max(storageMode.SelectedIndex, 0)];
            matchTypeHelp.Text = $"{selectedMatchType.Description}\n{selectedMatchType.Example}";
            matchTargetHelp.Text = $"{selectedMatchTarget.Description}\n{selectedMatchTarget.Example}";
            storageModeHelp.Text = $"{selectedStorageMode.Description}\n{selectedStorageMode.Example}";
            summary.Text = RulePresentation.Summarize(
                selectedMatchType.Value,
                matchValue.Text,
                selectedMatchTarget.Value,
                selectedStorageMode.Value,
                ResolveForDisplay(storageRoot.Text));
        }

        void QueueStorageRootProbe()
        {
            storageRootProbeTimer?.Stop();
            storageRootProbeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            storageRootProbeTimer.Tick += (timerSender, _) =>
            {
                (timerSender as DispatcherTimer)?.Stop();
                pathStatus.Text = DiagnoseStorageRoot(storageRoot.Text);
                RefreshDescriptions();
            };
            storageRootProbeTimer.Start();
        }

        matchValue.TextChanged += (_, _) => RefreshDescriptions();
        matchType.SelectionChanged += (_, _) => RefreshDescriptions();
        matchTarget.SelectionChanged += (_, _) => RefreshDescriptions();
        storageMode.SelectionChanged += (_, _) => RefreshDescriptions();
        storageRoot.TextChanged += (_, _) => QueueStorageRootProbe();
        chooseRoot.Click += async (_, _) =>
        {
            await PickStorageRootAsync(storageRoot);
            pathStatus.Text = DiagnoseStorageRoot(storageRoot.Text);
            RefreshDescriptions();
        };

        pathStatus.Text = DiagnoseStorageRoot(storageRoot.Text);
        RefreshDescriptions();

        // Separated save area: the primary action must read as the most important control.
        var saveArea = new StackPanel { Spacing = 8 };
        var save = CreatePrimaryButton(editing is null ? "규칙 저장" : "변경 사항 저장");
        save.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    await ShowMessageAsync("규칙 이름을 입력하세요.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(matchValue.Text))
                {
                    await ShowMessageAsync("적용할 사이트 주소 또는 주소에 포함된 문구를 입력하세요.");
                    return;
                }

                var diagnosis = DiagnoseStorageRoot(storageRoot.Text);
                pathStatus.Text = diagnosis;
                if (!diagnosis.StartsWith(StorageRootUsablePrefix, StringComparison.Ordinal))
                {
                    await ShowMessageAsync($"기준 폴더를 사용할 수 없습니다.\n\n{diagnosis}\n\n폴더 선택 버튼으로 접근 가능한 폴더를 지정하세요.");
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                var rule = new DownloadRule(
                    editing?.Id ?? Guid.NewGuid(),
                    name.Text.Trim(),
                    enabled.IsOn,
                    matchTypeChoices[Math.Max(matchType.SelectedIndex, 0)].Value,
                    matchValue.Text.Trim(),
                    matchTargetChoices[Math.Max(matchTarget.SelectedIndex, 0)].Value,
                    storageRoot.Text.Trim(),
                    storageModeChoices[Math.Max(storageMode.SelectedIndex, 0)].Value,
                    editing?.Priority ?? 0,
                    editing?.ListOrder ?? 0,
                    editing?.CreatedAt ?? now,
                    now);
                var response = await agent.SendAsync("rules.upsert", rule);
                if (!response.Success)
                {
                    await ShowMessageAsync(response.Message ?? "규칙을 저장하지 못했습니다. 입력값을 확인한 뒤 다시 시도하세요.");
                    return;
                }

                editingRuleId = null;
                await ShowRulesManagementAsync();
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("규칙을 저장하지 못했습니다: " + exception.Message);
            }
        };
        saveArea.Children.Add(save);
        if (editing is not null)
        {
            var cancel = new Button
            {
                Content = "편집 취소",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            cancel.Click += async (_, _) =>
            {
                editingRuleId = null;
                await ShowRulesManagementAsync();
            };
            saveArea.Children.Add(cancel);
        }

        panel.Children.Add(CreateSeparator(4));
        panel.Children.Add(saveArea);
        ContentPanel.Children.Add(CreateCard(panel, 20));
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
        panel.Children.Add(CreateAccentBadge(rule.IsEnabled ? "사용 중" : "사용 안 함"));
        panel.Children.Add(new TextBlock
        {
            Text = $"적용 사이트: {RulePresentation.MatchScopeSummary(rule.MatchType, rule.MatchValue)}\n"
                + $"확인할 주소: {RulePresentation.MatchTargetLabel(rule.MatchTarget)}\n"
                + $"저장 방식: {RulePresentation.StorageModeLabel(rule.StorageMode)}\n"
                + $"기준 폴더: {ResolveForDisplay(rule.StorageRoot)}",
            TextWrapping = TextWrapping.Wrap,
        });

        var enabled = new ToggleSwitch
        {
            Header = "이 규칙 사용",
            IsOn = rule.IsEnabled,
            OnContent = "사용 중",
            OffContent = "사용 안 함",
        };
        var suppressToggle = false;
        enabled.Toggled += async (_, _) =>
        {
            if (suppressToggle)
            {
                return;
            }

            var response = await agent.SendAsync(
                "rules.upsert",
                rule with { IsEnabled = enabled.IsOn, UpdatedAt = DateTimeOffset.UtcNow });
            if (!response.Success)
            {
                // Roll the switch back so the UI never shows a state the agent rejected.
                suppressToggle = true;
                enabled.IsOn = rule.IsEnabled;
                suppressToggle = false;
                await ShowMessageAsync(response.Message ?? "규칙 상태를 변경하지 못했습니다. 잠시 후 다시 시도하세요.");
                return;
            }

            await ShowRulesManagementAsync();
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
        var delete = new Button
        {
            Content = "삭제",
            Foreground = themeManager.GetThemeBrush("DangerTextBrush"),
        };
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
                await ShowMessageAsync(response.Message ?? "규칙을 삭제하지 못했습니다.");
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
        return CreateCard(panel);
    }

    private static int IndexOf<T>(IReadOnlyList<RuleChoice<T>> choices, T value)
        where T : struct, Enum
    {
        for (var index = 0; index < choices.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(choices[index].Value, value))
            {
                return index;
            }
        }

        return 0;
    }

    private string ResolveForDisplay(string configuredPath)
    {
        try
        {
            return pathResolver.Resolve(configuredPath.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return configuredPath.Trim();
        }
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

    private const string StorageRootUsablePrefix = "사용 가능";

    private string DiagnoseStorageRoot(string configuredPath)
    {
        try
        {
            var fullPath = pathResolver.Resolve(configuredPath.Trim());
            if (!Directory.Exists(fullPath))
            {
                return $"사용할 수 없음: 폴더가 없습니다. 폴더 선택 버튼으로 기존 폴더를 지정하세요. ({fullPath})";
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

            return $"{StorageRootUsablePrefix}: 이 폴더에 파일을 저장할 수 있습니다. ({fullPath})";
        }
        catch (UnauthorizedAccessException)
        {
            return "사용할 수 없음: 이 폴더에 저장할 권한이 없습니다. 다른 폴더를 선택하세요.";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return $"사용할 수 없음: {exception.Message}";
        }
    }
}
