using System.Text.RegularExpressions;

namespace DownloadRouter.Core.Tests;

/// <summary>
/// Source-level guards for the WinUI app. The UI cannot be instantiated in unit tests, so these
/// pin the behaviour that past fixes depended on (pending work lives in the downloads page,
/// prompt decisions are persisted first, bulk folders never mix rules) and the 2.0 rules
/// (palette tokens only, test copies never touch real registrations, Tray Folder ids).
/// </summary>
public sealed class PendingIntegrationSourceTests
{
    [Fact]
    public void PendingNavigationTabIsRemovedAndTheBadgeMovesToDownloadHistory()
    {
        var xaml = ReadAppFile("MainWindow.xaml");
        var shell = ReadAppFile("MainWindow.xaml.cs");

        Assert.DoesNotContain("Tag=\"pending\"", xaml, StringComparison.Ordinal);
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
        var code = ReadAppFile("Views", "DownloadsView.xaml.cs");
        var xaml = ReadAppFile("Views", "DownloadsView.xaml");

        Assert.Contains("DownloadFilter.Pending => job.IsSelectionPending", code, StringComparison.Ordinal);
        Assert.Contains("\"처리 대기", code, StringComparison.Ordinal);
        Assert.Contains("RenderPendingWorkspace", code, StringComparison.Ordinal);
        Assert.Contains("new PendingRuleGroup(", code, StringComparison.Ordinal);
        Assert.Contains("이 규칙의 대기 파일에 같은 폴더 적용", xaml, StringComparison.Ordinal);
        Assert.Contains("모두 옮기지 않기", xaml, StringComparison.Ordinal);
        Assert.Contains("선택 항목 저장 위치 지정", xaml, StringComparison.Ordinal);
        Assert.Contains("선택 항목 옮기지 않기", xaml, StringComparison.Ordinal);
        Assert.Contains("선택 항목 기록 삭제", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkFolderApplyIsRestrictedToASingleRuleSoStorageRootsCannotMix()
    {
        var code = ReadAppFile("Views", "DownloadsView.xaml.cs");

        Assert.Contains("var singleRule = pending.Length == selected.Count && selectedRules.Length == 1", code, StringComparison.Ordinal);
        Assert.Contains("ApplySelectedButton.IsEnabled = singleRule", code, StringComparison.Ordinal);
        Assert.Contains("서로 다른 규칙은 기준 폴더가 다르므로 함께 적용할 수 없습니다.", code, StringComparison.Ordinal);

        // The click handler re-checks the rule instead of trusting the button state.
        var handler = code[code.IndexOf("private async void ApplySelected_Click", StringComparison.Ordinal)..];
        Assert.Contains("if (!singleRule)", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHomePendingTileAndTheTrayOpenTheDownloadsPendingFilter()
    {
        var home = ReadAppFile("Views", "HomeView.xaml.cs");
        var shell = ReadAppFile("MainWindow.xaml.cs");
        var lifetime = ReadAppFile("MainWindow.Lifetime.cs");

        Assert.Contains("DownloadFilter.Pending, attentionWhenPositive: true", home, StringComparison.Ordinal);
        Assert.Contains("NavigateTo(\"history\", new DownloadsNavigation(tile.Filter))", home, StringComparison.Ordinal);
        Assert.Contains("=> NavigateTo(\"history\", new DownloadsNavigation(DownloadFilter.Pending));", shell, StringComparison.Ordinal);

        // v1.3.x looked up a navigation tag that no longer existed, so "open pending" only
        // showed the window. The tray and Tray Folder now route through OpenPendingHistory.
        var navigateFromTray = lifetime[lifetime.IndexOf("private void NavigateFromTray", StringComparison.Ordinal)..];
        Assert.Contains("if (tag == \"pending\")", navigateFromTray, StringComparison.Ordinal);
        Assert.Contains("OpenPendingHistory();", navigateFromTray, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticPromptDecisionsArePersistedInsteadOfKeptInMemory()
    {
        var selection = ReadAppFile("MainWindow.Selection.cs");

        Assert.DoesNotContain("deferredSelectionPrompts", selection, StringComparison.Ordinal);
        Assert.Contains("selection.prompt-state", selection, StringComparison.Ordinal);
        Assert.Contains("SelectionPromptState.Shown", selection, StringComparison.Ordinal);
        Assert.Contains("SelectionPromptState.Deferred", selection, StringComparison.Ordinal);
        // The prompt is only opened after the Shown state is durably recorded.
        var recordIndex = selection.IndexOf("RecordSelectionPromptStateAsync([job.Id], SelectionPromptState.Shown)", StringComparison.Ordinal);
        var showIndex = selection.IndexOf("await ShowSelectionPromptAsync(", StringComparison.Ordinal);
        Assert.True(recordIndex >= 0 && showIndex > recordIndex);
    }

    [Fact]
    public void OnlyOneSelectionWindowCanBeStartedEvenWhileThePromptStateIsBeingPersisted()
    {
        var selection = ReadAppFile("MainWindow.Selection.cs");
        var process = selection[selection.IndexOf("private async Task ProcessNextSelectionPromptAsync", StringComparison.Ordinal)..];
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
        var selection = ReadAppFile("MainWindow.Selection.cs");
        var selectionWindow = ReadAppFile("FolderSelectionWindow.cs");

        Assert.Contains("대기 목록에 남기기", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("닫고 대기 목록에 남기기", selectionWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("\"나중에 선택\"", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("await DeferSelectionPromptAsync(job.Id);", selection, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleScreenUsesFriendlyLabelsAndASeparatedPrimarySaveAction()
    {
        var code = ReadAppFile("Views", "RulesView.xaml.cs");
        var xaml = ReadAppFile("Views", "RulesView.xaml");

        Assert.Contains("1. 규칙 기본 정보", xaml, StringComparison.Ordinal);
        Assert.Contains("2. 어떤 다운로드에 적용할까요", xaml, StringComparison.Ordinal);
        Assert.Contains("3. 어떤 파일에 적용할까요", xaml, StringComparison.Ordinal);
        Assert.Contains("4. 어디로 보낼까요", xaml, StringComparison.Ordinal);
        Assert.Contains("5. 규칙 요약", xaml, StringComparison.Ordinal);
        Assert.Contains("Separated save area", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SaveButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AppText.Pick(\"규칙 저장\", \"Save rule\")", code, StringComparison.Ordinal);
        Assert.Contains("AppText.Pick(\"변경 사항 저장\", \"Save changes\")", code, StringComparison.Ordinal);
        Assert.Contains("RulePresentation.Summarize", code, StringComparison.Ordinal);

        // Enum collections must never be bound directly to a ComboBox again.
        Assert.DoesNotContain("Enum.GetValues<", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleToggleFailureRollsBackTheVisibleState()
    {
        var code = ReadAppFile("Views", "RulesView.xaml.cs");

        Assert.Contains("suppressToggle = true;", code, StringComparison.Ordinal);
        Assert.Contains("EnabledSwitch.IsOn = rule.IsEnabled;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePaletteMatchesTheSeriesTokensInBothThemes()
    {
        var palette = ReadAppFile("Themes", "Palette.xaml");

        Assert.Contains("x:Key=\"Light\"", palette, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Dark\"", palette, StringComparison.Ordinal);
        foreach (var token in new[]
                 {
                     "WindowBackgroundBrush", "CardBackgroundBrush", "CardBackgroundSecondaryBrush", "CardStrokeBrush",
                     "TextPrimaryBrush", "TextSecondaryBrush", "AccentBrush", "AccentSubtleBrush",
                     "SuccessForegroundBrush", "CautionBackgroundBrush", "CriticalForegroundBrush",
                 })
        {
            Assert.Contains($"x:Key=\"{token}\"", palette, StringComparison.Ordinal);
        }

        // eslee Folder Locker v2.0.0 values (light / dark).
        foreach (var color in new[] { "#F3F3F3", "#202020", "#FFFFFF", "#2B2B2B", "#E3E3E3", "#363636", "#005FB8", "#60CDFF", "#E5EFF9", "#1F3443", "#FFF4CE", "#433519" })
        {
            Assert.Contains($"\"{color}\"", palette, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WindowsAndCodeReferencePaletteTokensInsteadOfColorValues()
    {
        var root = AppSourceRoot();
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(path) && !path.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var literal = new Regex("=\"#[0-9A-Fa-f]{3,8}\"|Color\\.FromArgb|Colors\\.[A-Z]", RegexOptions.CultureInvariant);

        foreach (var file in files)
        {
            Assert.False(literal.IsMatch(File.ReadAllText(file)), $"Color literal in {Path.GetRelativePath(root, file)}");
        }
    }

    [Fact]
    public void SettingsSwitchesSaveOnToggleSoAnyInputMethodIsPersisted()
    {
        var settings = ReadAppFile("Views", "SettingsView.xaml");

        Assert.Contains("Toggled=\"AutoStartSwitch_Toggled\"", settings, StringComparison.Ordinal);
        Assert.Contains("Toggled=\"NotifySwitch_Toggled\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Switch_Click", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void ACopyOnAnotherDataFolderNeverTakesOverRealRegistrations()
    {
        var lifetime = ReadAppFile("MainWindow.Lifetime.cs");
        var settings = ReadAppFile("Views", "SettingsView.xaml.cs");

        var guard = lifetime.IndexOf("if (AppServices.Instance.IsDefault)", StringComparison.Ordinal);
        var link = lifetime.IndexOf("trayFolderLink = new TrayFolderLink(", StringComparison.Ordinal);
        Assert.True(guard >= 0 && link > guard);
        Assert.Contains("AutoStartSwitch.IsEnabled = AppServices.Instance.IsDefault", settings, StringComparison.Ordinal);
        Assert.Contains("|| !AppServices.Instance.IsDefault)", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayFolderIdentityAndExistingMenuActionIdsAreUnchanged()
    {
        var lifetime = ReadAppFile("MainWindow.Lifetime.cs");

        Assert.Contains("\"eslee.downloadrouter\"", lifetime, StringComparison.Ordinal);
        foreach (var id in new[] { "open-app", "open-pending", "open-settings", "exit-app", "pause-routing", "resume-routing" })
        {
            Assert.Contains($"= \"{id}\";", lifetime, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IconAndTextButtonsHaveScreenReaderNames()
    {
        foreach (var view in new[] { "HomeView.xaml", "RulesView.xaml", "DownloadsView.xaml", "BrowsersView.xaml", "SettingsView.xaml" })
        {
            var xaml = ReadAppFile("Views", view);
            var buttonsWithoutText = Regex.Matches(xaml, "<(Button|DropDownButton)(?<attributes>[^>]*)>\\s*<(FontIcon|StackPanel)", RegexOptions.Singleline);
            foreach (Match button in buttonsWithoutText)
            {
                Assert.Contains("AutomationProperties.Name=", button.Groups["attributes"].Value, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void CodeUsesThemeAwareStylesInsteadOfFrozenBrushes()
    {
        foreach (var file in Directory.EnumerateFiles(AppSourceRoot(), "*.cs", SearchOption.AllDirectories)
                     .Where(static path => !IsBuildOutput(path)))
        {
            Assert.DoesNotContain("Get<Microsoft.UI.Xaml.Media.Brush>", File.ReadAllText(file), StringComparison.Ordinal);
            Assert.DoesNotContain("Get<Brush>", File.ReadAllText(file), StringComparison.Ordinal);
        }

        var styles = ReadAppFile("Themes", "Controls.xaml");
        foreach (var key in new[] { "ErrorCaptionTextStyle", "CautionCaptionTextStyle", "DividerRowBorderStyle", "DangerMenuFlyoutItemStyle" })
        {
            Assert.Contains($"x:Key=\"{key}\"", styles, StringComparison.Ordinal);
        }
        Assert.Contains("{ThemeResource CriticalForegroundBrush}", styles, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CautionForegroundBrush}", styles, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource DividerStrokeBrush}", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalFolderValidationIsOffTheUiThread()
    {
        var tree = ReadAppFile("FolderTreePicker.cs");
        Assert.Contains("Task.Run(() => SelectionDestination.ValidateFolder(parent.FullName))", tree, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => SelectionDestination.ValidateFolder(full))", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRecoveryIsThrottledAndRunsOutsideTheUiThread()
    {
        var main = ReadAppFile("MainWindow.xaml.cs");
        var lifetime = ReadAppFile("MainWindow.Lifetime.cs");
        Assert.Contains("if (!snapshot.AgentReachable)", main, StringComparison.Ordinal);
        Assert.Contains("_ = EnsureAgentForHostAsync();", main, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(10)", lifetime, StringComparison.Ordinal);
        Assert.Contains("agentStartInProgress", lifetime, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => new AgentProcessManager(AppServices.Agent).EnsureRunningAsync())", lifetime, StringComparison.Ordinal);
    }

    [Fact]
    public void IsolatedNotificationsNeverRegisterOrDisableInstalledNotifications()
    {
        var source = ReadAppFile("Services", "NotificationService.cs");
        var ensure = source[source.IndexOf("public bool EnsureRegistered()", StringComparison.Ordinal)..];
        ensure = ensure[..ensure.IndexOf("public bool ShowMoveCompleted", StringComparison.Ordinal)];
        var disable = source[source.IndexOf("public void Disable()", StringComparison.Ordinal)..];
        disable = disable[..disable.IndexOf("public static void UnregisterAll()", StringComparison.Ordinal)];
        Assert.True(ensure.IndexOf("if (!AppServices.Instance.IsDefault)", StringComparison.Ordinal)
            < ensure.IndexOf("Registry.CurrentUser.CreateSubKey(RegistryPath)", StringComparison.Ordinal));
        Assert.Contains("return false;", ensure, StringComparison.Ordinal);
        Assert.Contains("if (!AppServices.Instance.IsDefault)", disable, StringComparison.Ordinal);
        Assert.True(disable.IndexOf("return;", StringComparison.Ordinal) < disable.IndexOf("UnregisterAll();", StringComparison.Ordinal));
        Assert.Contains("NotificationService.UnregisterAll();", ReadAppFile("App.xaml.cs"), StringComparison.Ordinal);

        // AppNotificationManager.Register fails (0x8007007E) in this self-contained unpackaged app;
        // the classic toast API with a per-user AppUserModelId key is used instead.
        Assert.DoesNotContain("AppNotificationManager.Default", source, StringComparison.Ordinal);
        Assert.Contains("ToastNotificationManager.CreateToastNotifier(AppUserModelId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PauseRefreshForcingIsConsumedInsideThePollGate()
    {
        var monitor = ReadAppFile("Services", "LiveMonitor.cs");
        Assert.DoesNotContain("tick = 0", monitor, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref forcePauseStatus, 1)", monitor, StringComparison.Ordinal);
        var gate = monitor.IndexOf("await pollGate.WaitAsync", StringComparison.Ordinal);
        var consume = monitor.IndexOf("Interlocked.Exchange(ref forcePauseStatus, 0)", StringComparison.Ordinal);
        Assert.True(gate >= 0 && consume > gate);
    }

    [Fact]
    public void ImportPreviewIncludesRuleDetailsDisabledStateAndDuplicateCount()
    {
        var view = ReadAppFile("Views", "RulesView.xaml.cs");
        var import = view[view.IndexOf("private async void ImportRules_Click", StringComparison.Ordinal)..];
        Assert.Contains("result.SkippedDuplicates.Count", import, StringComparison.Ordinal);
        Assert.DoesNotContain("{result.SkippedDuplicates}", import, StringComparison.Ordinal);
        Assert.Contains("result.NewRules.Take(10)", import, StringComparison.Ordinal);
        Assert.Contains("RulePresentation.MatchScopeSummary(rule.MatchType, rule.MatchValue)", import, StringComparison.Ordinal);
        Assert.Contains("{rule.StorageRoot}", import, StringComparison.Ordinal);
        Assert.Contains("Imported rules are turned off", import, StringComparison.Ordinal);
        Assert.Contains("…and {result.NewRules.Count - 10} more", import, StringComparison.Ordinal);
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string AppSourceRoot()
        => Path.Combine(FindRepositoryRoot(), "src", "DownloadRouter.App");

    private static string ReadAppFile(params string[] parts)
        => File.ReadAllText(Path.Combine([AppSourceRoot(), .. parts]));

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
