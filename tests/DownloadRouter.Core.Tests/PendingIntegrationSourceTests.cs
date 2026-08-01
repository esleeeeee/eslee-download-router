namespace DownloadRouter.Core.Tests;

/// <summary>
/// Source-level guards for the v1.1.0 UI integration. These keep the pending workflow
/// inside the download history page and stop internal enum names from leaking into the UI.
/// </summary>
public sealed class PendingIntegrationSourceTests
{
    [Fact]
    public void PendingNavigationTabIsRemovedAndTheBadgeMovesToDownloadHistory()
    {
        var xaml = ReadAppFile("MainWindow.xaml");
        var shell = ReadAppFile("MainWindow.xaml.cs");

        Assert.DoesNotContain("Tag=\"pending\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("저장 위치 선택 대기\" Tag", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("\"pending\" => ShowPendingAsync()", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoryNavigationItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PendingInfoBadge\"", xaml, StringComparison.Ordinal);

        // The badge must sit on the history item, not on a separate pending item.
        var historyIndex = xaml.IndexOf("HistoryNavigationItem", StringComparison.Ordinal);
        var badgeIndex = xaml.IndexOf("PendingInfoBadge", StringComparison.Ordinal);
        Assert.True(historyIndex >= 0 && badgeIndex > historyIndex);
    }

    [Fact]
    public void TheStandalonePendingPageFileIsGone()
        => Assert.False(File.Exists(Path.Combine(AppSourceRoot(), "MainWindow.Pending.cs")));

    [Fact]
    public void DownloadHistoryOwnsThePendingFilterAndItsBulkActions()
    {
        var history = ReadAppFile("MainWindow.History.cs");

        Assert.Contains("HistoryFilter.Pending", history, StringComparison.Ordinal);
        Assert.Contains("\"처리 대기\"", history, StringComparison.Ordinal);
        Assert.Contains("RenderPendingWorkspace", history, StringComparison.Ordinal);
        Assert.Contains("CreatePendingRuleGroup", history, StringComparison.Ordinal);
        Assert.Contains("이 규칙의 대기 파일에 같은 폴더 적용", history, StringComparison.Ordinal);
        Assert.Contains("이 규칙의 대기 파일 모두 이동하지 않기", history, StringComparison.Ordinal);
        Assert.Contains("선택 항목 저장 위치 지정", history, StringComparison.Ordinal);
        Assert.Contains("선택 항목 이동하지 않기", history, StringComparison.Ordinal);
        Assert.Contains("선택 항목 이력 삭제", history, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkFolderApplyIsRestrictedToASingleRuleSoStorageRootsCannotMix()
    {
        var history = ReadAppFile("MainWindow.History.cs");

        Assert.Contains("var singleRule = selectedRules.Length == 1", history, StringComparison.Ordinal);
        Assert.Contains("applySelected.IsEnabled = singleRule", history, StringComparison.Ordinal);
        Assert.Contains(
            "서로 다른 규칙은 기준 폴더가 다르므로 함께 적용할 수 없습니다.",
            history,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheDashboardPendingCardOpensTheHistoryPendingFilter()
    {
        var live = ReadAppFile("MainWindow.Live.cs");

        Assert.Contains("AddPendingDashboardCard", live, StringComparison.Ordinal);
        Assert.Contains("historyFilter = HistoryFilter.Pending", live, StringComparison.Ordinal);
        Assert.Contains("Navigation.SelectedItem = HistoryNavigationItem", live, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticPromptDecisionsArePersistedInsteadOfKeptInMemory()
    {
        var live = ReadAppFile("MainWindow.Live.cs");

        Assert.DoesNotContain("deferredSelectionPrompts", live, StringComparison.Ordinal);
        Assert.Contains("selection.prompt-state", live, StringComparison.Ordinal);
        Assert.Contains("SelectionPromptState.Shown", live, StringComparison.Ordinal);
        Assert.Contains("SelectionPromptState.Deferred", live, StringComparison.Ordinal);
        // The prompt is only opened after the Shown state is durably recorded.
        var recordIndex = live.IndexOf("RecordSelectionPromptStateAsync([job.Id], SelectionPromptState.Shown)", StringComparison.Ordinal);
        var showIndex = live.IndexOf("await ShowSelectionPromptAsync(", StringComparison.Ordinal);
        Assert.True(recordIndex >= 0 && showIndex > recordIndex);
    }

    [Fact]
    public void OnlyOneSelectionWindowCanBeStartedEvenWhileThePromptStateIsBeingPersisted()
    {
        var live = ReadAppFile("MainWindow.Live.cs");
        var process = live[live.IndexOf("private async Task ProcessNextSelectionPromptAsync", StringComparison.Ordinal)..];
        var guardIndex = process.IndexOf("selectionPromptInProgress = true;", StringComparison.Ordinal);
        var awaitIndex = process.IndexOf("await RecordSelectionPromptStateAsync", StringComparison.Ordinal);

        // The single-prompt slot must be claimed before the first await in the method.
        Assert.True(guardIndex >= 0 && awaitIndex > guardIndex);
        Assert.Contains("if (selectionPromptInProgress)", process, StringComparison.Ordinal);
        Assert.Contains("if (!promptOpened)", process, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingTheSelectionWindowKeepsTheFileWaitingWithoutReopeningTheProhibitedPrompt()
    {
        var live = ReadAppFile("MainWindow.Live.cs");
        var selectionWindow = ReadAppFile("FolderSelectionWindow.cs");

        Assert.Contains("대기 목록에 남기기", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("닫고 대기 목록에 남기기", selectionWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("\"나중에 선택\"", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("DeferSelectionPromptAsync", live, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleScreenUsesFriendlyLabelsAndASeparatedPrimarySaveAction()
    {
        var rules = ReadAppFile("MainWindow.Rules.cs");

        Assert.Contains("1. 규칙 기본 정보", rules, StringComparison.Ordinal);
        Assert.Contains("2. 어떤 다운로드에 적용할까요", rules, StringComparison.Ordinal);
        Assert.Contains("3. 어떤 주소를 확인할까요", rules, StringComparison.Ordinal);
        Assert.Contains("4. 어디로 보낼까요", rules, StringComparison.Ordinal);
        Assert.Contains("5. 규칙 요약", rules, StringComparison.Ordinal);
        Assert.Contains("CreatePrimaryButton(editing is null ? \"규칙 저장\" : \"변경 사항 저장\")", rules, StringComparison.Ordinal);
        Assert.Contains("RulePresentation.Summarize", rules, StringComparison.Ordinal);

        // Enum collections must never be bound directly to a ComboBox again.
        Assert.DoesNotContain("Enum.GetValues<RuleMatchType>()", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.GetValues<RuleMatchTarget>()", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.GetValues<StorageMode>()", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("경로 읽기/쓰기 진단", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleToggleFailureRollsBackTheVisibleState()
    {
        var rules = ReadAppFile("MainWindow.Rules.cs");

        Assert.Contains("suppressToggle = true;", rules, StringComparison.Ordinal);
        Assert.Contains("enabled.IsOn = rule.IsEnabled;", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void LightThemeIsWhiteBasedAndDarkThemeIsPreserved()
    {
        var appXaml = ReadAppFile("App.xaml");

        Assert.Contains("x:Key=\"Light\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Dark\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AccentTextBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CardBorderBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DangerTextBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#FFFFFF\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#202020\"", appXaml, StringComparison.Ordinal);
        // The warning surface stays on the established colour.
        Assert.Contains("Color=\"#FFF4CE\"", appXaml, StringComparison.Ordinal);
    }

    private static string AppSourceRoot()
        => Path.Combine(FindRepositoryRoot(), "src", "DownloadRouter.App");

    private static string ReadAppFile(string fileName)
        => File.ReadAllText(Path.Combine(AppSourceRoot(), fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DownloadRouter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
