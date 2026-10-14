using System.Runtime.InteropServices;
using DownloadRouter.App.Controls;
using DownloadRouter.App.Services;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Paths;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace DownloadRouter.App;

public enum FolderSelectionAction
{
    Apply,
    Later,
    SkipAll,
    Skip,
    Closed,
}

public sealed record FolderSelectionResult(
    FolderSelectionAction Action,
    string RelativeFolder,
    string? DestinationFolder = null,
    string? FileName = null,
    bool UseForTenMinutes = false);

/// <summary>What the window shows about the download being placed.</summary>
public sealed record FolderSelectionDetails(
    string SourceHost,
    string RuleName,
    string Status,
    string StorageRoot,
    int OtherPendingCount,
    int QueuePosition,
    int QueueTotal);

public sealed record FolderSelectionRequest(
    string StorageRoot,
    string? SelectedRelativeFolder,
    FolderSelectionDetails? Details,
    bool AllowLater,
    bool AllowSkip,
    bool AllowSkipAll = false,
    bool AllowParentNavigation = false,
    string? FileName = null,
    bool AllowTenMinuteFolder = false,
    Guid? RuleId = null,
    IReadOnlyList<string>? RecentFolders = null,
    string? Heading = null);

/// <summary>
/// Independent "choose a folder" window. It is not owned by the main window, so it can come to
/// the front while the main window is minimized or hidden in the tray.
/// </summary>
public sealed class FolderSelectionWindow(
    nint mainWindowHandle,
    Action<string>? diagnosticWriter = null)
{
    private const int DefaultWidthDip = 720;
    private const int DefaultHeightDip = 820;
    private readonly Window window = new();
    private readonly FolderTreePicker picker = new();
    private readonly TextBox fileNameInput = new()
    {
        Header = AppText.Pick("파일 이름 (확장자 포함)", "File name (with extension)"),
        MaxLength = 255,
    };
    private readonly TextBlock fileNameError = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock headerTitle = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock headerSubtitle = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 420, IsTextSelectionEnabled = true };
    private readonly WrapPanel shortcuts = new();
    private readonly ToggleButton favoriteToggle = new();
    private readonly TaskCompletionSource<FolderSelectionResult> completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private FolderSelectionRequest request = null!;
    private DispatcherQueueTimer? foregroundRetryTimer;
    private int foregroundRetryCount;
    private string? defaultFileName;
    private bool fileNameEdited;
    private bool useForTenMinutes;
    private bool updatingFavorite;
    private bool completed;

    public void UpdateFileName(string name)
    {
        if (!fileNameEdited)
        {
            defaultFileName = name;
            fileNameInput.Text = name;
            if (request.Heading is null)
            {
                headerTitle.Text = name;
            }
        }
    }

    public void UpdateDetails(FolderSelectionDetails value)
    {
        headerSubtitle.Text = AppText.Pick(
            $"{value.SourceHost} · 규칙 {value.RuleName}" + (value.QueueTotal > 1 ? $" · {value.QueuePosition}/{value.QueueTotal}" : string.Empty),
            $"{value.SourceHost} · rule {value.RuleName}" + (value.QueueTotal > 1 ? $" · {value.QueuePosition}/{value.QueueTotal}" : string.Empty));
        details.Text = AppText.Pick(
            $"출처 사이트: {value.SourceHost}\n규칙: {value.RuleName}\n상태: {value.Status}\n기준 폴더: {value.StorageRoot}\n동시에 기다리는 다른 파일: {value.OtherPendingCount}개",
            $"Source site: {value.SourceHost}\nRule: {value.RuleName}\nStatus: {value.Status}\nBase folder: {value.StorageRoot}\nOther files waiting: {value.OtherPendingCount}");
    }

    public async Task<FolderSelectionResult> ShowAsync(FolderSelectionRequest selection, CancellationToken cancellationToken = default)
    {
        request = selection;
        picker.AllowParentNavigation = selection.AllowParentNavigation;
        defaultFileName = selection.FileName;
        fileNameInput.Text = selection.FileName ?? string.Empty;
        fileNameInput.Visibility = selection.FileName is null ? Visibility.Collapsed : Visibility.Visible;
        fileNameInput.TextChanged += (_, _) =>
            fileNameEdited = !string.Equals(fileNameInput.Text, defaultFileName, StringComparison.Ordinal);
        window.Title = AppText.Pick("다운로드 저장 위치 선택", "Choose a download folder");
        window.Content = CreateContent();
        AppServices.Theme.Register(window);
        window.Closed += (_, _) =>
        {
            diagnosticWriter?.Invoke("selection-window closed-event");
            CompleteWithoutClosing(FolderSelectionAction.Closed);
        };
        picker.SelectionChanged += (_, _) => UpdateFavoriteToggle();
        await picker.InitializeAsync(selection.StorageRoot, selection.SelectedRelativeFolder, cancellationToken);
        RenderShortcuts();
        UpdateFavoriteToggle();
        window.Activate();
        SizeAndCenterWindow();
        ShowAndActivateSelectionWindow("initial");
        StartForegroundRetries();

        using var registration = cancellationToken.Register(() =>
        {
            diagnosticWriter?.Invoke("selection-window cancellation-requested");
            window.DispatcherQueue.TryEnqueue(() => Complete(FolderSelectionAction.Closed));
        });
        return await completion.Task;
    }

    private UIElement CreateContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var background = new Border { Style = ThemeStyles.Get<Style>("WindowRootBorderStyle") };
        Grid.SetRowSpan(background, 3);
        root.Children.Add(background);

        // Header: what is being placed, with the full download details one click away.
        var header = new Grid { Padding = new Thickness(24, 20, 24, 12), ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new IconPlate { Glyph = "", Kind = BadgeKind.Accent, Size = 44 });
        var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        headerTitle.Style = ThemeStyles.Get<Style>("SubtitleTextBlockStyle");
        headerTitle.FontSize = 18;
        headerTitle.Text = request.Heading ?? request.FileName ?? AppText.Pick("저장 위치 선택", "Choose a folder");
        headerTitle.TextWrapping = request.Heading is null ? TextWrapping.NoWrap : TextWrapping.Wrap;
        headerSubtitle.Style = ThemeStyles.Get<Style>("CaptionSecondaryTextStyle");
        titles.Children.Add(headerTitle);
        titles.Children.Add(headerSubtitle);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        if (request.Details is { } info)
        {
            UpdateDetails(info);
            var infoButton = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 16 },
                VerticalAlignment = VerticalAlignment.Center,
                Flyout = new Flyout
                {
                    Content = new ScrollViewer
                    {
                        Content = details,
                        MaxHeight = 320,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    },
                },
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(infoButton, AppText.Pick("다운로드 정보", "Download details"));
            ToolTipService.SetToolTip(infoButton, AppText.Pick("다운로드 정보", "Download details"));
            Grid.SetColumn(infoButton, 2);
            header.Children.Add(infoButton);
        }
        else
        {
            headerSubtitle.Visibility = Visibility.Collapsed;
        }

        root.Children.Add(header);

        // Body: file name, shortcuts, folder tree.
        var body = new Grid { Padding = new Thickness(24, 0, 24, 16), RowSpacing = 12 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var nameArea = new StackPanel { Spacing = 4 };
        nameArea.Children.Add(fileNameInput);
        fileNameError.Style = ThemeStyles.Get<Style>("CaptionTextBlockStyle");
        fileNameError.Foreground = ThemeStyles.Get<Microsoft.UI.Xaml.Media.Brush>("CriticalForegroundBrush");
        nameArea.Children.Add(fileNameError);
        body.Children.Add(nameArea);

        var shortcutArea = new StackPanel { Spacing = 6 };
        var shortcutHeader = new Grid();
        shortcutHeader.Children.Add(new TextBlock
        {
            Text = AppText.Pick("즐겨찾기와 최근 폴더", "Favorites and recent folders"),
            Style = ThemeStyles.Get<Style>("CaptionSecondaryTextStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        favoriteToggle.HorizontalAlignment = HorizontalAlignment.Right;
        favoriteToggle.Padding = new Thickness(10, 3, 10, 4);
        favoriteToggle.Checked += (_, _) => _ = SetFavoriteAsync(true);
        favoriteToggle.Unchecked += (_, _) => _ = SetFavoriteAsync(false);
        shortcutHeader.Children.Add(favoriteToggle);
        shortcutArea.Children.Add(shortcutHeader);
        shortcutArea.Children.Add(shortcuts);
        Grid.SetRow(shortcutArea, 1);
        body.Children.Add(shortcutArea);

        var treeCard = new Border { Style = ThemeStyles.Get<Style>("CardStyle"), Padding = new Thickness(12) };
        picker.HorizontalAlignment = HorizontalAlignment.Stretch;
        picker.VerticalAlignment = VerticalAlignment.Stretch;
        treeCard.Child = picker;
        Grid.SetRow(treeCard, 2);
        body.Children.Add(treeCard);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // Footer: the main decision on the right, the rest behind "More".
        var footer = new Border { Style = ThemeStyles.Get<Style>("FooterBarStyle") };
        var buttons = new Grid { ColumnSpacing = 8 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var otherActions = new MenuFlyout();
        if (request.AllowLater)
        {
            var later = new MenuFlyoutItem { Text = AppText.Pick("대기 목록에 남기기", "Keep waiting in the list") };
            ToolTipService.SetToolTip(later, AppText.Pick(
                "이 창은 다시 자동으로 뜨지 않으며, 다운로드 화면의 처리 대기에서 나중에 정할 수 있습니다.",
                "This window will not pop up again; choose later from Waiting on the Downloads page."));
            later.Click += (_, _) => Complete(FolderSelectionAction.Later);
            otherActions.Items.Add(later);
        }

        if (request.AllowSkipAll)
        {
            var skipAll = new MenuFlyoutItem { Text = AppText.Pick("대기 중인 파일 모두 옮기지 않기", "Do not move any waiting file") };
            skipAll.Click += (_, _) => Complete(FolderSelectionAction.SkipAll);
            otherActions.Items.Add(skipAll);
        }

        if (request.AllowSkip)
        {
            var skip = new MenuFlyoutItem { Text = AppText.Pick("이번 파일은 옮기지 않기", "Do not move this file") };
            skip.Click += (_, _) => Complete(FolderSelectionAction.Skip);
            otherActions.Items.Add(skip);
        }

        if (otherActions.Items.Count > 0)
        {
            otherActions.Items.Add(new MenuFlyoutSeparator());
        }

        var close = new MenuFlyoutItem
        {
            Text = request.AllowLater ? AppText.Pick("닫고 대기 목록에 남기기", "Close and keep waiting") : AppText.Pick("창 닫기", "Close"),
        };
        close.Click += (_, _) => Complete(FolderSelectionAction.Closed);
        otherActions.Items.Add(close);
        var more = new DropDownButton { Content = AppText.Pick("다른 작업", "More"), Flyout = otherActions };
        buttons.Children.Add(more);

        if (request.AllowTenMinuteFolder)
        {
            var remember = new Button { Content = AppText.Pick("10분간 이 위치로", "Use for 10 minutes") };
            ToolTipService.SetToolTip(remember, AppText.Pick(
                "이 파일과 앞으로 10분 동안 같은 규칙의 새 다운로드를 이 폴더에 저장합니다. 파일 이름은 다시 쓰지 않습니다.",
                "Saves this file and new downloads of the same rule for the next 10 minutes into this folder. The file name is not reused."));
            picker.SelectionChanged += (_, _) => remember.IsEnabled = picker.IsSelectionAccessible;
            remember.Click += (_, _) => Complete(FolderSelectionAction.Apply, rememberFolder: true);
            Grid.SetColumn(remember, 2);
            buttons.Children.Add(remember);
        }

        var apply = new Button
        {
            Content = AppText.Pick("이 위치로 보내기", "Save here"),
            Style = ThemeStyles.Get<Style>("AccentButtonStyle"),
            MinWidth = 140,
        };
        apply.Click += (_, _) => Complete(FolderSelectionAction.Apply);
        picker.SelectionChanged += (_, _) => apply.IsEnabled = picker.IsSelectionAccessible;
        Grid.SetColumn(apply, 3);
        buttons.Children.Add(apply);
        footer.Child = buttons;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        // Enter applies from the file name box; Escape keeps the file waiting.
        root.KeyboardAccelerators.Add(CreateAccelerator(Windows.System.VirtualKey.Escape, () => Complete(FolderSelectionAction.Closed)));
        return root;
    }

    private static Microsoft.UI.Xaml.Input.KeyboardAccelerator CreateAccelerator(Windows.System.VirtualKey key, Action action)
    {
        var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            action();
        };
        return accelerator;
    }

    private void RenderShortcuts()
    {
        shortcuts.Children.Clear();
        var favorites = AppServices.Preferences.FavoriteFolders ?? [];
        var recent = (request.RecentFolders ?? [])
            .Where(folder => !favorites.Contains(folder, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        foreach (var folder in favorites)
        {
            shortcuts.Children.Add(CreateShortcut(folder, ""));
        }

        foreach (var folder in recent)
        {
            shortcuts.Children.Add(CreateShortcut(folder, ""));
        }

        if (shortcuts.Children.Count == 0)
        {
            shortcuts.Children.Add(new TextBlock
            {
                Text = AppText.Pick(
                    "아직 없습니다. 자주 쓰는 폴더를 고른 뒤 ☆를 누르면 여기에 표시됩니다.",
                    "None yet. Select a folder you use often and choose ☆ to pin it here."),
                Style = ThemeStyles.Get<Style>("CaptionSecondaryTextStyle"),
            });
        }
    }

    private Button CreateShortcut(string folder, string glyph)
    {
        var reachable = picker.CanNavigateTo(folder);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12 });
        content.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : folder,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var button = new Button
        {
            Content = content,
            Style = ThemeStyles.Get<Style>("ShortcutButtonStyle"),
            IsEnabled = reachable,
        };
        ToolTipService.SetToolTip(button, reachable
            ? folder
            : folder + "\n" + AppText.Pick("이 규칙의 기준 폴더 밖이라 고를 수 없습니다.", "Outside this rule's base folder."));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, folder);
        button.Click += async (_, _) =>
        {
            if (!await picker.NavigateToAsync(folder))
            {
                await AppDialog.ShowMessageAsync(
                    window.Content.XamlRoot,
                    AppText.Pick($"폴더를 열 수 없습니다. 지워졌거나 연결되지 않았을 수 있습니다.\n{folder}", $"The folder cannot be opened. It may be deleted or offline.\n{folder}"),
                    DialogTone.Warning);
            }
        };
        return button;
    }

    private void UpdateFavoriteToggle()
    {
        var current = picker.SelectedFullPath;
        var isFavorite = (AppServices.Preferences.FavoriteFolders ?? []).Contains(current, StringComparer.OrdinalIgnoreCase);
        updatingFavorite = true;
        favoriteToggle.IsChecked = isFavorite;
        favoriteToggle.IsEnabled = picker.IsSelectionAccessible;
        updatingFavorite = false;
        var label = isFavorite
            ? AppText.Pick("★ 즐겨찾기에서 빼기", "★ Remove from favorites")
            : AppText.Pick("☆ 이 폴더 즐겨찾기", "☆ Add to favorites");
        favoriteToggle.Content = label;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(favoriteToggle, label);
    }

    private async Task SetFavoriteAsync(bool add)
    {
        if (updatingFavorite)
        {
            return;
        }

        var folder = picker.SelectedFullPath;
        try
        {
            await AppServices.UpdatePreferencesAsync(preferences =>
            {
                var list = (preferences.FavoriteFolders ?? []).Where(entry => !string.Equals(entry, folder, StringComparison.OrdinalIgnoreCase)).ToList();
                if (add)
                {
                    list.Insert(0, folder);
                }

                return preferences with { FavoriteFolders = list.Take(20).ToArray() };
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            diagnosticWriter?.Invoke($"selection-window favorite-save-failed {exception.GetType().Name}");
        }

        RenderShortcuts();
        UpdateFavoriteToggle();
    }

    private void Complete(FolderSelectionAction action, bool rememberFolder = false)
    {
        if (action == FolderSelectionAction.Apply && fileNameEdited)
        {
            try
            {
                SelectionDestination.ValidateFileName(fileNameInput.Text);
            }
            catch (ArgumentException exception)
            {
                fileNameError.Text = exception.Message;
                fileNameError.Visibility = Visibility.Visible;
                return;
            }
        }

        useForTenMinutes = action == FolderSelectionAction.Apply && rememberFolder;
        if (CompleteWithoutClosing(action))
        {
            window.Close();
        }
    }

    private bool CompleteWithoutClosing(FolderSelectionAction action)
    {
        if (completed)
        {
            return false;
        }

        completed = true;
        foregroundRetryTimer?.Stop();
        foregroundRetryTimer = null;
        diagnosticWriter?.Invoke($"selection-window completion action={action}");
        completion.TrySetResult(new FolderSelectionResult(
            action,
            picker.SelectedRelativeFolder,
            picker.AllowParentNavigation ? picker.SelectedFullPath : null,
            fileNameEdited ? fileNameInput.Text : null,
            useForTenMinutes));
        return true;
    }

    private void SizeAndCenterWindow()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        ProductBranding.ApplyWindowIcon(appWindow);
        var dpi = Math.Max(96u, GetDpiForWindow(handle));
        var scale = dpi / 96d;
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var width = Math.Max(420, Math.Min((int)Math.Round(DefaultWidthDip * scale), workArea.Width - 64));
        var height = Math.Max(520, Math.Min((int)Math.Round(DefaultHeightDip * scale), workArea.Height - 64));
        appWindow.Resize(new SizeInt32(width, height));
        appWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsAlwaysOnTop = true;
        }
    }

    private void StartForegroundRetries()
    {
        foregroundRetryTimer = window.DispatcherQueue.CreateTimer();
        foregroundRetryTimer.Interval = TimeSpan.FromMilliseconds(250);
        foregroundRetryTimer.IsRepeating = true;
        foregroundRetryTimer.Tick += (_, _) =>
        {
            if (completed || foregroundRetryCount >= 4)
            {
                foregroundRetryTimer?.Stop();
                foregroundRetryTimer = null;
                return;
            }

            foregroundRetryCount++;
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (!IsWindowVisible(handle)
                || IsIconic(handle)
                || GetForegroundWindow() != handle)
            {
                ShowAndActivateSelectionWindow($"retry-{foregroundRetryCount}");
            }
        };
        foregroundRetryTimer.Start();
    }

    private void ShowAndActivateSelectionWindow(string phase)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var operations = new NativeSelectionWindowActivationOperations(window);
        var result = new SelectionWindowActivationCoordinator(operations)
            .Activate(mainWindowHandle, handle);
        diagnosticWriter?.Invoke(FormatActivationDiagnostic(phase, result));
    }

    private static string FormatActivationDiagnostic(
        string phase,
        SelectionWindowActivationResult result)
        => $"selection-window activation phase={phase} "
            + $"main-before={FormatState(result.MainBefore)} "
            + $"selection-before={FormatState(result.SelectionBefore)} "
            + $"selection-after-show={FormatState(result.SelectionAfterShow)} "
            + $"show-normal={result.ShowNormalSucceeded} "
            + $"bring-to-top={result.BringToTopSucceeded} "
            + $"set-foreground={result.DirectForegroundSucceeded} "
            + $"attached-input={result.AttachedForegroundSucceeded} "
            + $"raised={result.RaisedForegroundSucceeded} "
            + $"flash-fallback={result.FlashFallbackUsed} "
            + $"main-after={FormatState(result.MainAfter)} "
            + $"selection-after={FormatState(result.SelectionAfter)}";

    private static string FormatState(NativeWindowState state)
        => $"[hwnd=0x{state.Handle:X},visible={state.IsVisible},iconic={state.IsIconic},"
            + $"owner=0x{state.OwnerHandle:X},foreground=0x{state.ForegroundHandle:X}]";

    private sealed class NativeSelectionWindowActivationOperations(
        Window xamlWindow) : ISelectionWindowActivationOperations
    {
        private const int ShowNormal = 1;
        private const uint GetWindowOwner = 4;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private static readonly nint HwndTopmost = new(-1);

        public NativeWindowState Capture(nint windowHandle)
        {
            var foreground = GetForegroundWindow();
            return windowHandle == 0
                ? new NativeWindowState(0, false, false, 0, foreground)
                : new NativeWindowState(
                    windowHandle,
                    IsWindowVisible(windowHandle),
                    IsIconic(windowHandle),
                    GetWindow(windowHandle, GetWindowOwner),
                    foreground);
        }

        public bool ShowSelectionNormal(nint selectionWindowHandle)
        {
            _ = ShowWindow(selectionWindowHandle, ShowNormal);
            return IsWindowVisible(selectionWindowHandle) && !IsIconic(selectionWindowHandle);
        }

        public void ActivateSelection()
            => xamlWindow.Activate();

        public bool BringSelectionToTop(nint selectionWindowHandle)
            => BringWindowToTop(selectionWindowHandle);

        public bool TrySetSelectionForeground(nint selectionWindowHandle)
        {
            _ = SetForegroundWindow(selectionWindowHandle);
            return GetForegroundWindow() == selectionWindowHandle;
        }

        public bool TryAttachInputAndActivate(nint selectionWindowHandle)
        {
            var foregroundWindowHandle = GetForegroundWindow();
            if (foregroundWindowHandle == selectionWindowHandle)
            {
                return true;
            }

            if (foregroundWindowHandle == 0)
            {
                return false;
            }

            var selectionThread = GetWindowThreadProcessId(selectionWindowHandle, out _);
            var foregroundThread = GetWindowThreadProcessId(foregroundWindowHandle, out _);
            if (selectionThread == 0 || foregroundThread == 0)
            {
                return false;
            }

            var attached = selectionThread == foregroundThread
                || AttachThreadInput(selectionThread, foregroundThread, true);
            if (!attached)
            {
                return false;
            }

            try
            {
                _ = ShowWindow(selectionWindowHandle, ShowNormal);
                _ = BringWindowToTop(selectionWindowHandle);
                _ = SetActiveWindow(selectionWindowHandle);
                _ = SetFocus(selectionWindowHandle);
                _ = SetForegroundWindow(selectionWindowHandle);
                xamlWindow.Activate();
                return GetForegroundWindow() == selectionWindowHandle;
            }
            finally
            {
                if (selectionThread != foregroundThread)
                {
                    _ = AttachThreadInput(selectionThread, foregroundThread, false);
                }
            }
        }

        public bool RaiseSelectionAboveOtherWindows(nint selectionWindowHandle)
        {
            var flags = SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow;
            var raised = SetWindowPos(selectionWindowHandle, HwndTopmost, 0, 0, 0, 0, flags);
            xamlWindow.Activate();
            _ = BringWindowToTop(selectionWindowHandle);
            _ = SetForegroundWindow(selectionWindowHandle);
            return raised && GetForegroundWindow() == selectionWindowHandle;
        }

        public void FlashSelection(nint selectionWindowHandle)
        {
            var flash = new FlashWindowInfo
            {
                Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                Window = selectionWindowHandle,
                Flags = 3,
                Count = 3,
                Timeout = 0,
            };
            _ = FlashWindowEx(ref flash);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}
