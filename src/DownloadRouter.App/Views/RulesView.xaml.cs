using System.Collections.ObjectModel;
using DownloadRouter.App.Controls;
using DownloadRouter.App.Services;
using DownloadRouter.App.ViewModels;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DownloadRouter.App.Views;

/// <summary>
/// Rules list (search, filters, context menu, folder drop) and the rule editor. Rules are data
/// entries, so the editor saves explicitly; only the on/off switch of a saved rule applies at once.
/// </summary>
public sealed partial class RulesView : UserControl, IAppView
{
    private const string DefaultStorageRoot = "{Downloads}\\eslee\\Routed";
    private static readonly TimeSpan RootCheckTimeout = TimeSpan.FromSeconds(4);

    private readonly ObservableCollection<RuleRowItem> items = [];
    private readonly IReadOnlyList<Option<RuleMatchType>> matchTypes = RulePresentation.MatchTypes
        .Select(static choice => new Option<RuleMatchType>(choice.Value, choice.Label, choice.Description, choice.Example)).ToArray();
    private readonly IReadOnlyList<Option<RuleMatchTarget>> matchTargets = RulePresentation.MatchTargets
        .Select(static choice => new Option<RuleMatchTarget>(choice.Value, choice.Label, choice.Description, choice.Example)).ToArray();
    private readonly IReadOnlyList<Option<StorageMode>> storageModes = RulePresentation.StorageModes
        .Select(static choice => new Option<StorageMode>(choice.Value, choice.Label, choice.Description, choice.Example)).ToArray();
    private readonly IReadOnlyList<Option<FileTypeFilterMode>> fileTypeModes = RulePresentation.FileTypeModes
        .Select(static choice => new Option<FileTypeFilterMode>(choice.Value, choice.Label, choice.Description, choice.Example)).ToArray();
    private readonly DispatcherTimer rootCheckTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private IReadOnlyList<DownloadRule> rules = [];
    private string filter = "all";
    private string search = string.Empty;
    private DownloadRule? editingRule;
    private bool editorOpen;
    private bool dirty;
    private bool loadingEditor;
    private bool suppressSelection;
    private bool suppressToggle;
    private int rootCheckVersion;
    private StorageRootStatus? lastRootStatus;

    public RulesView()
    {
        InitializeComponent();
        RuleList.ItemsSource = items;
        MatchTypeBox.ItemsSource = matchTypes;
        MatchTargetBox.ItemsSource = matchTargets;
        StorageModeBox.ItemsSource = storageModes;
        FileTypeModeBox.ItemsSource = fileTypeModes;
        rootCheckTimer.Tick += (_, _) =>
        {
            rootCheckTimer.Stop();
            _ = CheckRootAsync();
        };
        UpdateChips();
    }

    public void OnShown(object? parameter)
    {
        if (AppServices.Live.Latest is { } latest)
        {
            ApplyRules(latest.Rules);
        }

        if (parameter is RulesNavigation navigation)
        {
            if (navigation.AttentionOnly)
            {
                AttentionChip.IsChecked = true;
            }

            if (navigation.NewRuleStorageRoot is string root)
            {
                _ = StartNewRuleAsync(root);
            }
            else if (navigation.RuleId is Guid ruleId)
            {
                SelectRule(ruleId);
            }
        }
    }

    public void OnLiveUpdate(LiveSnapshot snapshot, bool changed)
    {
        if (changed || items.Any(item => item.NeedsAttention != AppServices.RootHealth.NeedsAttention(item.Id)))
        {
            ApplyRules(snapshot.Rules);
        }
    }

    // ---------------------------------------------------------------- list

    private void ApplyRules(IReadOnlyList<DownloadRule> latest)
    {
        rules = latest;
        RebuildList();
        if (editingRule is not null)
        {
            var current = rules.FirstOrDefault(rule => rule.Id == editingRule.Id);
            if (current is null)
            {
                CloseEditor();
            }
            else if (current.UpdatedAt != editingRule.UpdatedAt && !dirty)
            {
                LoadEditor(current);
            }
            else
            {
                editingRule = current with { };
                ShowRootWarning();
            }
        }
    }

    private void RebuildList()
    {
        var selectedId = (RuleList.SelectedItem as RuleRowItem)?.Id ?? editingRule?.Id;
        var visible = rules
            .Where(MatchesFilter)
            .Where(rule => search.Length == 0
                || rule.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || rule.MatchValue.Contains(search, StringComparison.OrdinalIgnoreCase)
                || rule.StorageRoot.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static rule => rule.ListOrder)
            .ThenBy(static rule => rule.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        suppressSelection = true;
        try
        {
            for (var index = 0; index < visible.Length; index++)
            {
                var rule = visible[index];
                var attention = AppServices.RootHealth.NeedsAttention(rule.Id);
                var existing = items.Skip(index).FirstOrDefault(item => item.Id == rule.Id);
                if (existing is null)
                {
                    items.Insert(index, new RuleRowItem(rule, attention));
                }
                else
                {
                    var oldIndex = items.IndexOf(existing);
                    if (oldIndex != index)
                    {
                        items.Move(oldIndex, index);
                    }

                    existing.Update(rule, attention);
                }
            }

            while (items.Count > visible.Length)
            {
                items.RemoveAt(items.Count - 1);
            }

            RuleList.SelectedItem = selectedId is Guid id ? items.FirstOrDefault(item => item.Id == id) : null;
        }
        finally
        {
            suppressSelection = false;
        }

        ListEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListEmptyText.Text = rules.Count == 0
            ? AppText.Pick("아직 규칙이 없습니다. '새 규칙'을 누르거나 폴더를 여기에 끌어다 놓으세요.", "No rules yet. Choose 'New' or drop a folder here.")
            : AppText.Pick("조건에 맞는 규칙이 없습니다.", "No rules match this filter.");
        UpdateChips();
    }

    private bool MatchesFilter(DownloadRule rule)
        => filter switch
        {
            "enabled" => rule.IsEnabled,
            "disabled" => !rule.IsEnabled,
            "attention" => rule.IsEnabled && AppServices.RootHealth.NeedsAttention(rule.Id),
            _ => true,
        };

    private void UpdateChips()
    {
        var attention = rules.Count(rule => rule.IsEnabled && AppServices.RootHealth.NeedsAttention(rule.Id));
        AllChip.Content = AppText.Pick($"전체 {rules.Count}", $"All {rules.Count}");
        EnabledChip.Content = AppText.Pick($"사용 중 {rules.Count(static rule => rule.IsEnabled)}", $"On {rules.Count(static rule => rule.IsEnabled)}");
        DisabledChip.Content = AppText.Pick($"꺼짐 {rules.Count(static rule => !rule.IsEnabled)}", $"Off {rules.Count(static rule => !rule.IsEnabled)}");
        AttentionChip.Content = AppText.Pick($"확인 필요 {attention}", $"Needs attention {attention}");
        AttentionChip.Visibility = attention > 0 || AttentionChip.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Filter_Checked(object sender, RoutedEventArgs args)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            filter = tag;
            RebuildList();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        search = SearchBox.Text.Trim();
        RebuildList();
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void NewRule_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = StartNewRuleAsync(null);
        args.Handled = true;
    }

    private void SelectRule(Guid ruleId)
    {
        var item = items.FirstOrDefault(candidate => candidate.Id == ruleId);
        if (item is null && rules.FirstOrDefault(rule => rule.Id == ruleId) is not null)
        {
            AllChip.IsChecked = true;
            SearchBox.Text = string.Empty;
            item = items.FirstOrDefault(candidate => candidate.Id == ruleId);
        }

        if (item is not null)
        {
            RuleList.SelectedItem = item;
            RuleList.ScrollIntoView(item);
        }
    }

    private async void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (suppressSelection || RuleList.SelectedItem is not RuleRowItem item || item.Id == editingRule?.Id)
        {
            return;
        }

        if (!await ConfirmDiscardAsync())
        {
            suppressSelection = true;
            RuleList.SelectedItem = editingRule is null ? null : items.FirstOrDefault(candidate => candidate.Id == editingRule.Id);
            suppressSelection = false;
            return;
        }

        LoadEditor(item.Rule);
    }

    private void RuleList_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Delete && RuleList.SelectedItem is RuleRowItem item)
        {
            args.Handled = true;
            _ = DeleteRuleAsync(item.Rule);
        }
    }

    private void RuleRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: RuleRowItem item })
        {
            return;
        }

        args.Handled = true;
        var rule = item.Rule;
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(AppText.Pick("편집", "Edit"), "", () => SelectRule(rule.Id)));
        menu.Items.Add(MenuItem(
            rule.IsEnabled ? AppText.Pick("사용 안 함", "Turn off") : AppText.Pick("사용", "Turn on"),
            rule.IsEnabled ? "" : "",
            () => _ = SetEnabledAsync(rule, !rule.IsEnabled)));
        menu.Items.Add(MenuItem(AppText.Pick("기준 폴더 열기", "Open base folder"), "", () => _ = OpenRootAsync(rule)));
        menu.Items.Add(new MenuFlyoutSeparator());
        var delete = MenuItem(AppText.Pick("삭제", "Delete"), "", () => _ = DeleteRuleAsync(rule));
        delete.Style = ThemeStyles.Get<Style>("DangerMenuFlyoutItemStyle");
        menu.Items.Add(delete);
        if (args.TryGetPosition(sender, out var point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt((FrameworkElement)sender);
        }
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    // ---------------------------------------------------------------- drag and drop

    private void ListPane_DragOver(object sender, DragEventArgs args)
    {
        if (args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            args.AcceptedOperation = DataPackageOperation.Link;
            args.DragUIOverride.Caption = AppText.Pick("이 폴더로 새 규칙 만들기", "New rule for this folder");
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void ListPane_DragLeave(object sender, DragEventArgs args)
        => DropOverlay.Visibility = Visibility.Collapsed;

    private async void ListPane_Drop(object sender, DragEventArgs args)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var dropped = await args.DataView.GetStorageItemsAsync();
        var folder = dropped.OfType<StorageFolder>().FirstOrDefault();
        if (folder is null)
        {
            await AppDialog.ShowMessageAsync(
                XamlRoot,
                AppText.Pick("파일이 아니라 폴더를 끌어다 놓아 주세요.", "Drop a folder rather than a file."),
                DialogTone.Warning);
            return;
        }

        await StartNewRuleAsync(folder.Path);
    }

    // ---------------------------------------------------------------- editor

    private async void NewRule_Click(object sender, RoutedEventArgs args)
        => await StartNewRuleAsync(null);

    private async Task StartNewRuleAsync(string? storageRoot)
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        suppressSelection = true;
        RuleList.SelectedItem = null;
        suppressSelection = false;
        LoadEditor(null, storageRoot);
        NameBox.Focus(FocusState.Programmatic);
    }

    private void LoadEditor(DownloadRule? rule, string? storageRootOverride = null)
    {
        loadingEditor = true;
        try
        {
            editingRule = rule;
            editorOpen = true;
            NameBox.Text = rule?.Name ?? (storageRootOverride is null ? string.Empty : Path.GetFileName(storageRootOverride.TrimEnd('\\')));
            MatchValueBox.Text = rule?.MatchValue ?? string.Empty;
            MatchTypeBox.SelectedItem = matchTypes.FirstOrDefault(option => option.Value == (rule?.MatchType ?? RulePresentation.DefaultMatchType)) ?? matchTypes[0];
            MatchTargetBox.SelectedItem = matchTargets.FirstOrDefault(option => option.Value == (rule?.MatchTarget ?? RulePresentation.DefaultMatchTarget)) ?? matchTargets[0];
            StorageModeBox.SelectedItem = storageModes.FirstOrDefault(option => option.Value == (rule?.StorageMode ?? RulePresentation.DefaultStorageMode)) ?? storageModes[0];
            FileTypeModeBox.SelectedItem = fileTypeModes.FirstOrDefault(option => option.Value == (rule?.FileTypeMode ?? FileTypeFilterMode.AllFiles)) ?? fileTypeModes[0];
            ExtensionsBox.Text = (rule?.FileExtensions ?? string.Empty).Replace(";", ", ", StringComparison.Ordinal);
            StorageRootBox.Text = storageRootOverride ?? rule?.StorageRoot ?? DefaultStorageRoot;
            suppressToggle = true;
            EnabledSwitch.IsOn = rule?.IsEnabled ?? true;
            suppressToggle = false;
            RuleMoreButton.Visibility = rule is null ? Visibility.Collapsed : Visibility.Visible;
            EditorEmpty.Visibility = Visibility.Collapsed;
            EditorPanel.Visibility = Visibility.Visible;
            EditorScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        finally
        {
            loadingEditor = false;
        }

        SetDirty(rule is null);
        RefreshEditorTexts();
        ShowRootWarning();
        lastRootStatus = null;
        _ = CheckRootAsync();
    }

    private void CloseEditor()
    {
        editingRule = null;
        editorOpen = false;
        SetDirty(false);
        EditorPanel.Visibility = Visibility.Collapsed;
        EditorEmpty.Visibility = Visibility.Visible;
    }

    private void Editor_Changed(object sender, object args)
    {
        if (loadingEditor || !editorOpen)
        {
            return;
        }

        SetDirty(true);
        RefreshEditorTexts();
    }

    private void StorageRootBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        Editor_Changed(sender, args);
        if (!loadingEditor)
        {
            rootCheckTimer.Stop();
            rootCheckTimer.Start();
        }
    }

    private void SetDirty(bool value)
    {
        dirty = value;
        var isNew = editingRule is null;
        SaveButton.Content = isNew ? AppText.Pick("규칙 저장", "Save rule") : AppText.Pick("변경 사항 저장", "Save changes");
        SaveButton.IsEnabled = dirty;
        RevertButton.Content = isNew ? AppText.Pick("취소", "Cancel") : AppText.Pick("되돌리기", "Revert");
        RevertButton.IsEnabled = dirty || isNew;
        DirtyText.Text = isNew
            ? AppText.Pick("새 규칙은 저장해야 적용됩니다.", "A new rule applies once saved.")
            : dirty ? AppText.Pick("저장하지 않은 변경 사항이 있습니다.", "You have unsaved changes.") : string.Empty;
    }

    private void RefreshEditorTexts()
    {
        var matchType = (MatchTypeBox.SelectedItem as Option<RuleMatchType>) ?? matchTypes[0];
        var matchTarget = (MatchTargetBox.SelectedItem as Option<RuleMatchTarget>) ?? matchTargets[0];
        var storageMode = (StorageModeBox.SelectedItem as Option<StorageMode>) ?? storageModes[0];
        var fileTypeMode = (FileTypeModeBox.SelectedItem as Option<FileTypeFilterMode>) ?? fileTypeModes[0];
        MatchTypeHelp.Text = $"{matchType.Description}\n{matchType.Example}";
        MatchTargetHelp.Text = $"{matchTarget.Description}\n{matchTarget.Example}\n"
            + AppText.Pick("권장: ", "Recommended: ") + RulePresentation.MatchTargetLabel(RulePresentation.DefaultMatchTarget);
        StorageModeHelp.Text = $"{storageMode.Description}\n{storageMode.Example}";
        FileTypeHelp.Text = $"{fileTypeMode.Description}\n{fileTypeMode.Example}";
        ExtensionsPanel.Visibility = fileTypeMode.Value == FileTypeFilterMode.AllFiles ? Visibility.Collapsed : Visibility.Visible;

        var extensions = string.Empty;
        ExtensionsError.Visibility = Visibility.Collapsed;
        if (fileTypeMode.Value != FileTypeFilterMode.AllFiles && ExtensionsBox.Text.Trim().Length > 0)
        {
            try
            {
                extensions = FileTypeFilter.Normalize(ExtensionsBox.Text);
            }
            catch (ArgumentException exception)
            {
                ExtensionsError.Text = exception.Message;
                ExtensionsError.Visibility = Visibility.Visible;
            }
        }

        HeaderTitle.Text = string.IsNullOrWhiteSpace(NameBox.Text)
            ? (editingRule is null ? AppText.Pick("새 규칙", "New rule") : editingRule.Name)
            : NameBox.Text.Trim();
        HeaderSubtitle.Text = string.IsNullOrWhiteSpace(MatchValueBox.Text)
            ? AppText.Pick("적용할 사이트를 입력하세요", "Enter the site this rule applies to")
            : RulePresentation.MatchScopeSummary(matchType.Value, MatchValueBox.Text.Trim());
        HeaderPlate.Glyph = storageMode.Value == StorageMode.SelectSubfolder ? "" : "";
        SummaryText.Text = RulePresentation.Summarize(
            matchType.Value,
            MatchValueBox.Text,
            matchTarget.Value,
            storageMode.Value,
            AppServices.ResolveForDisplay(StorageRootBox.Text),
            fileTypeMode.Value,
            extensions);
    }

    private void ShowRootWarning()
    {
        if (editingRule is not null
            && editingRule.IsEnabled
            && AppServices.RootHealth.StatusOf(editingRule.Id) is { } status
            && status != StorageRootStatus.Available)
        {
            EditorRootWarning.Title = AppText.Pick("기준 폴더를 확인해 주세요", "Check the base folder");
            EditorRootWarning.Message = StorageRootHealth.Describe(status, AppServices.ResolveForDisplay(editingRule.StorageRoot));
            EditorRootWarning.IsOpen = true;
        }
        else
        {
            EditorRootWarning.IsOpen = false;
        }
    }

    private async Task<StorageRootStatus> CheckRootAsync()
    {
        var version = ++rootCheckVersion;
        var path = AppServices.ResolveForDisplay(StorageRootBox.Text);
        RootChecking.IsActive = true;
        RootOkIcon.Visibility = Visibility.Collapsed;
        RootBadIcon.Visibility = Visibility.Collapsed;
        RootStatusText.Text = AppText.Pick("폴더를 확인하는 중…", "Checking the folder…");
        var status = string.IsNullOrWhiteSpace(StorageRootBox.Text)
            ? StorageRootStatus.Invalid
            : await StorageRootHealth.CheckAsync(path, probeWrite: true, RootCheckTimeout, CancellationToken.None);
        if (version != rootCheckVersion)
        {
            return status;
        }

        lastRootStatus = status;
        RootChecking.IsActive = false;
        RootOkIcon.Visibility = status == StorageRootStatus.Available ? Visibility.Visible : Visibility.Collapsed;
        RootBadIcon.Visibility = status == StorageRootStatus.Available ? Visibility.Collapsed : Visibility.Visible;
        RootStatusText.Text = StorageRootHealth.Describe(status, path);
        return status;
    }

    private async void BrowseRoot_Click(object sender, RoutedEventArgs args)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (AppServices.Main is { } main)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(main));
        }

        var selected = await picker.PickSingleFolderAsync();
        if (selected is not null)
        {
            StorageRootBox.Text = selected.Path;
        }
    }

    private void AddPreset_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string preset })
        {
            return;
        }

        var current = ExtensionsBox.Text.Trim();
        ExtensionsBox.Text = current.Length == 0 ? preset.Replace(" ", ", ", StringComparison.Ordinal) : $"{current}, {preset.Replace(" ", ", ", StringComparison.Ordinal)}";
        try
        {
            ExtensionsBox.Text = FileTypeFilter.Normalize(ExtensionsBox.Text).Replace(";", ", ", StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // Keep the raw text; RefreshEditorTexts shows the problem.
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs args)
        => await SaveAsync();

    private async Task<bool> SaveAsync()
    {
        var name = NameBox.Text.Trim();
        var matchValue = MatchValueBox.Text.Trim();
        if (name.Length == 0)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("규칙 이름을 입력하세요.", "Enter a rule name."), DialogTone.Warning);
            NameBox.Focus(FocusState.Programmatic);
            return false;
        }

        if (matchValue.Length == 0)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("적용할 사이트 주소나 주소에 들어 있는 문구를 입력하세요.", "Enter the site address or text the address contains."), DialogTone.Warning);
            MatchValueBox.Focus(FocusState.Programmatic);
            return false;
        }

        var fileTypeMode = ((FileTypeModeBox.SelectedItem as Option<FileTypeFilterMode>) ?? fileTypeModes[0]).Value;
        var extensions = string.Empty;
        if (fileTypeMode != FileTypeFilterMode.AllFiles)
        {
            try
            {
                extensions = FileTypeFilter.Normalize(ExtensionsBox.Text);
            }
            catch (ArgumentException exception)
            {
                await AppDialog.ShowMessageAsync(XamlRoot, exception.Message, DialogTone.Warning);
                ExtensionsBox.Focus(FocusState.Programmatic);
                return false;
            }

            if (extensions.Length == 0)
            {
                await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("파일 종류를 하나 이상 입력하세요.", "Enter at least one file type."), DialogTone.Warning);
                ExtensionsBox.Focus(FocusState.Programmatic);
                return false;
            }
        }

        SaveButton.IsEnabled = false;
        var rootStatus = await CheckRootAsync();
        if (rootStatus != StorageRootStatus.Available)
        {
            SaveButton.IsEnabled = dirty;
            await AppDialog.ShowMessageAsync(
                XamlRoot,
                AppText.Pick("기준 폴더를 사용할 수 없습니다.", "The base folder cannot be used.")
                    + "\n\n" + RootStatusText.Text + "\n\n"
                    + AppText.Pick("'찾아보기'로 파일을 저장할 수 있는 폴더를 고르세요.", "Use Browse to pick a folder you can save files to."),
                DialogTone.Warning);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var maxOrder = rules.Count == 0 ? 0 : rules.Max(static rule => rule.ListOrder);
        var rule = new DownloadRule(
            editingRule?.Id ?? Guid.NewGuid(),
            name,
            EnabledSwitch.IsOn,
            ((MatchTypeBox.SelectedItem as Option<RuleMatchType>) ?? matchTypes[0]).Value,
            matchValue,
            ((MatchTargetBox.SelectedItem as Option<RuleMatchTarget>) ?? matchTargets[0]).Value,
            StorageRootBox.Text.Trim(),
            ((StorageModeBox.SelectedItem as Option<StorageMode>) ?? storageModes[0]).Value,
            editingRule?.Priority ?? 0,
            editingRule?.ListOrder ?? maxOrder + 1,
            editingRule?.CreatedAt ?? now,
            now,
            fileTypeMode,
            extensions);
        try
        {
            var response = await AppServices.Agent.SendAsync("rules.upsert", rule);
            if (!response.Success)
            {
                SaveButton.IsEnabled = dirty;
                await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Describe(response, AppText.Pick("규칙을 저장하지 못했습니다. 입력값을 확인해 주세요.", "Could not save the rule. Check the values.")), DialogTone.Error);
                return false;
            }
        }
        catch (Exception exception)
        {
            SaveButton.IsEnabled = dirty;
            await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Unreachable(exception), DialogTone.Error);
            return false;
        }

        editingRule = rule;
        SetDirty(false);
        if (await AppServices.Live.RefreshNowAsync() is { } latest)
        {
            ApplyRules(latest.Rules);
        }

        SelectRule(rule.Id);
        return true;
    }

    private void Revert_Click(object sender, RoutedEventArgs args)
    {
        if (editingRule is null)
        {
            CloseEditor();
        }
        else
        {
            LoadEditor(editingRule);
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!dirty || !editorOpen)
        {
            return true;
        }

        var discard = await AppDialog.ConfirmAsync(
            XamlRoot,
            AppText.Pick("저장하지 않은 변경 사항", "Unsaved changes"),
            AppText.Pick("편집 중인 규칙을 저장하지 않았습니다. 변경 사항을 버릴까요?", "The rule you are editing has not been saved. Discard the changes?"),
            AppText.Pick("버리기", "Discard"),
            destructive: true);
        if (discard)
        {
            SetDirty(false);
        }

        return discard;
    }

    private async void EnabledSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (suppressToggle || loadingEditor)
        {
            return;
        }

        if (editingRule is not { } rule)
        {
            // A new rule keeps the switch as part of the form until it is saved.
            Editor_Changed(sender, args);
            return;
        }

        if (!await SetEnabledAsync(rule, EnabledSwitch.IsOn))
        {
            // Roll the switch back so the UI never shows a state the agent rejected.
            suppressToggle = true;
            EnabledSwitch.IsOn = rule.IsEnabled;
            suppressToggle = false;
        }
    }

    private async Task<bool> SetEnabledAsync(DownloadRule rule, bool enabled)
    {
        try
        {
            var response = await AppServices.Agent.SendAsync(
                "rules.upsert",
                rule with { IsEnabled = enabled, UpdatedAt = DateTimeOffset.UtcNow });
            if (!response.Success)
            {
                await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Describe(response, AppText.Pick("규칙 상태를 바꾸지 못했습니다.", "Could not change the rule.")), DialogTone.Error);
                return false;
            }
        }
        catch (Exception exception)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Unreachable(exception), DialogTone.Error);
            return false;
        }

        if (await AppServices.Live.RefreshNowAsync() is { } latest)
        {
            var wasDirty = dirty;
            ApplyRules(latest.Rules);
            if (editingRule?.Id == rule.Id && !wasDirty)
            {
                LoadEditor(latest.Rules.First(candidate => candidate.Id == rule.Id));
            }
        }

        return true;
    }

    private void OpenRoot_Click(object sender, RoutedEventArgs args)
    {
        if (editingRule is not null)
        {
            _ = OpenRootAsync(editingRule);
        }
    }

    private async Task OpenRootAsync(DownloadRule rule)
    {
        var path = AppServices.ResolveForDisplay(rule.StorageRoot);
        if (!await ShellActions.DirectoryExistsAsync(path))
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick($"기준 폴더를 찾을 수 없습니다.\n{path}", $"The base folder was not found.\n{path}"), DialogTone.Warning);
            return;
        }

        ShellActions.OpenFolder(path);
    }

    private void ShowRuleDownloads_Click(object sender, RoutedEventArgs args)
    {
        if (editingRule is not null)
        {
            AppServices.Main?.NavigateTo("history", new DownloadsNavigation(DownloadFilter.All, Search: editingRule.Name));
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs args)
    {
        if (editingRule is not null)
        {
            _ = DeleteRuleAsync(editingRule);
        }
    }

    private async Task DeleteRuleAsync(DownloadRule rule)
    {
        var confirmed = await AppDialog.ConfirmAsync(
            XamlRoot,
            AppText.Pick($"'{rule.Name}' 규칙을 삭제할까요?", $"Delete the rule '{rule.Name}'?"),
            AppText.Pick(
                "이미 옮긴 파일과 다운로드 기록은 지워지지 않습니다. 앞으로 이 사이트의 다운로드는 다른 규칙이 없으면 브라우저 기본 위치에 저장됩니다.",
                "Files already moved and the download history stay. New downloads from this site go to the browser's default folder unless another rule matches."),
            AppText.Pick("삭제", "Delete"),
            destructive: true);
        if (!confirmed)
        {
            return;
        }

        try
        {
            var response = await AppServices.Agent.SendAsync("rules.delete", new RuleDeletePayload(rule.Id));
            if (!response.Success)
            {
                await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Describe(response, AppText.Pick("규칙을 삭제하지 못했습니다.", "Could not delete the rule.")), DialogTone.Error);
                return;
            }
        }
        catch (Exception exception)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AgentMessages.Unreachable(exception), DialogTone.Error);
            return;
        }

        if (editingRule?.Id == rule.Id)
        {
            CloseEditor();
        }

        if (await AppServices.Live.RefreshNowAsync() is { } latest)
        {
            ApplyRules(latest.Rules);
        }
    }

    // ---------------------------------------------------------------- export / import

    private async void ExportRules_Click(object sender, RoutedEventArgs args)
    {
        if (rules.Count == 0)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("내보낼 규칙이 없습니다.", "There are no rules to export."));
            return;
        }

        var picker = new FileSavePicker { SuggestedFileName = $"eslee-download-router-rules-{DateTime.Now:yyyyMMdd}" };
        picker.FileTypeChoices.Add(AppText.Pick("규칙 백업 (JSON)", "Rule backup (JSON)"), [".json"]);
        InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var json = RuleTransfer.Export(rules, DateTimeOffset.UtcNow);
            await Task.Run(() => File.WriteAllText(file.Path, json, new System.Text.UTF8Encoding(false)));
            await AppDialog.ShowMessageAsync(
                XamlRoot,
                AppText.Pick($"규칙 {rules.Count}개를 저장했습니다.\n{file.Path}", $"Saved {rules.Count} rule(s).\n{file.Path}"),
                DialogTone.Success);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("파일을 저장하지 못했습니다.", "Could not save the file.") + "\n\n" + exception.Message, DialogTone.Error);
        }
    }

    private async void ImportRules_Click(object sender, RoutedEventArgs args)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        RuleImportResult result;
        try
        {
            var existing = rules;
            result = await Task.Run(() =>
            {
                var info = new FileInfo(file.Path);
                if (info.Length > 1024 * 1024)
                {
                    throw new InvalidDataException(AppText.Pick("파일이 너무 큽니다 (1MB 초과).", "The file is too large (over 1 MB)."));
                }

                return RuleTransfer.Import(File.ReadAllText(file.Path), existing, DateTimeOffset.UtcNow);
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, AppText.Pick("규칙 파일을 읽지 못했습니다.", "Could not read the rules file.") + "\n\n" + exception.Message, DialogTone.Error);
            return;
        }

        var summary = AppText.Pick(
            $"새 규칙 {result.NewRules.Count}개를 추가합니다.\n이미 있는 규칙 {result.SkippedDuplicates.Count}개는 건너뜁니다.",
            $"{result.NewRules.Count} new rule(s) will be added.\n{result.SkippedDuplicates.Count} rule(s) already exist and will be skipped.");
        if (result.NewRules.Count > 0)
        {
            summary += "\n\n" + AppText.Pick(
                "가져온 규칙은 꺼진 상태로 추가됩니다. 내용을 확인한 뒤 직접 켜세요.",
                "Imported rules are turned off. Review them and turn them on deliberately.");
            foreach (var rule in result.NewRules.Take(10))
            {
                summary += "\n\n" + AppText.Pick(
                    $"이름: {rule.Name}\n사이트: {RulePresentation.MatchScopeSummary(rule.MatchType, rule.MatchValue)}\n저장 폴더: {rule.StorageRoot}",
                    $"Name: {rule.Name}\nSite: {RulePresentation.MatchScopeSummary(rule.MatchType, rule.MatchValue)}\nDestination folder: {rule.StorageRoot}");
            }

            if (result.NewRules.Count > 10)
            {
                summary += "\n" + AppText.Pick($"…외 {result.NewRules.Count - 10}개", $"…and {result.NewRules.Count - 10} more");
            }
        }

        if (result.Errors.Count > 0)
        {
            summary += "\n\n" + AppText.Pick($"읽을 수 없는 항목 {result.Errors.Count}개:", $"{result.Errors.Count} entry(ies) could not be read:")
                + "\n• " + string.Join("\n• ", result.Errors.Take(5));
        }

        if (result.NewRules.Count == 0)
        {
            await AppDialog.ShowMessageAsync(XamlRoot, summary, result.Errors.Count > 0 ? DialogTone.Warning : DialogTone.Information, AppText.Pick("추가할 규칙이 없습니다", "Nothing to add"));
            return;
        }

        if (!await AppDialog.ConfirmAsync(XamlRoot, AppText.Pick("규칙 가져오기", "Import rules"), summary, AppText.Pick("가져오기", "Import")))
        {
            return;
        }

        var added = 0;
        var failures = new List<string>();
        foreach (var rule in result.NewRules)
        {
            try
            {
                var response = await AppServices.Agent.SendAsync("rules.upsert", rule);
                if (response.Success)
                {
                    added++;
                }
                else
                {
                    failures.Add($"{rule.Name}: {AgentMessages.Describe(response, AppText.Pick("저장 실패", "save failed"))}");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{rule.Name}: {exception.Message}");
                break;
            }
        }

        if (await AppServices.Live.RefreshNowAsync() is { } latest)
        {
            ApplyRules(latest.Rules);
        }

        var report = AppText.Pick($"규칙 {added}개를 가져왔습니다.", $"Imported {added} rule(s).");
        if (failures.Count > 0)
        {
            report += "\n\n" + string.Join("\n", failures.Take(5));
        }

        await AppDialog.ShowMessageAsync(XamlRoot, report, failures.Count > 0 ? DialogTone.Warning : DialogTone.Success);
    }

    private static void InitializePicker(object picker)
    {
        if (AppServices.Main is { } main)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(main));
        }
    }
}
