using System.Runtime.InteropServices;
using DownloadRouter.Core.Jobs;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DownloadRouter.App;

public enum FolderSelectionAction
{
    Apply,
    Later,
    LaterAll,
    Skip,
    Closed,
}

public sealed record FolderSelectionResult(
    FolderSelectionAction Action,
    string RelativeFolder);

public sealed class FolderSelectionWindow(
    ThemeManager themeManager,
    nint mainWindowHandle,
    Action<string>? diagnosticWriter = null)
{
    private const int DefaultWidthDip = 560;
    private const int DefaultHeightDip = 760;
    private readonly Window window = new();
    private readonly FolderTreePicker picker = new();
    private readonly TextBlock details = new()
    {
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MaxWidth = DefaultWidthDip - 40,
    };
    private readonly TaskCompletionSource<FolderSelectionResult> completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private DispatcherQueueTimer? foregroundRetryTimer;
    private int foregroundRetryCount;
    private bool completed;

    public void UpdateDescription(string description)
        => details.Text = description;

    public async Task<FolderSelectionResult> ShowAsync(
        string storageRoot,
        string? selectedRelativeFolder,
        string description,
        bool allowLater,
        bool allowSkip,
        CancellationToken cancellationToken = default,
        bool allowLaterAll = false)
    {
        window.Title = "다운로드 저장 위치 선택";
        window.Content = CreateContent(description, allowLater, allowSkip, allowLaterAll);
        themeManager.RegisterWindow(window);
        window.Closed += (_, _) =>
        {
            diagnosticWriter?.Invoke("selection-window closed-event");
            CompleteWithoutClosing(FolderSelectionAction.Closed);
        };
        await picker.InitializeAsync(storageRoot, selectedRelativeFolder, cancellationToken);
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

    private UIElement CreateContent(string description, bool allowLater, bool allowSkip, bool allowLaterAll)
    {
        var root = new Grid
        {
            Padding = new Thickness(20),
            RowSpacing = 12,
            MinWidth = 420,
            MaxWidth = DefaultWidthDip,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        details.Text = description;
        var detailsScroll = new ScrollViewer
        {
            Content = details,
            MaxHeight = 150,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        root.Children.Add(detailsScroll);

        picker.HorizontalAlignment = HorizontalAlignment.Stretch;
        picker.VerticalAlignment = VerticalAlignment.Stretch;
        picker.MinHeight = 240;
        Grid.SetRow(picker, 1);
        root.Children.Add(picker);

        var buttons = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var apply = new Button
        {
            Content = "이 위치로 보내기",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        apply.Click += (_, _) => Complete(FolderSelectionAction.Apply);
        picker.SelectionChanged += (_, _) => apply.IsEnabled = picker.IsSelectionAccessible;
        buttons.Children.Add(apply);

        if (allowLater)
        {
            var later = new Button
            {
                Content = "나중에 선택",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            later.Click += (_, _) => Complete(FolderSelectionAction.Later);
            buttons.Children.Add(later);
        }

        if (allowLaterAll)
        {
            var laterAll = new Button
            {
                Content = "모두 나중에 선택",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            laterAll.Click += (_, _) => Complete(FolderSelectionAction.LaterAll);
            buttons.Children.Add(laterAll);
        }

        if (allowSkip)
        {
            var skip = new Button
            {
                Content = "이번 파일은 이동하지 않기",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            skip.Click += (_, _) => Complete(FolderSelectionAction.Skip);
            buttons.Children.Add(skip);
        }

        var close = new Button
        {
            Content = "창 닫기",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        close.Click += (_, _) => Complete(FolderSelectionAction.Closed);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        return root;
    }

    private void Complete(FolderSelectionAction action)
    {
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
        completion.TrySetResult(new FolderSelectionResult(action, picker.SelectedRelativeFolder));
        return true;
    }

    private void SizeAndCenterWindow()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
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
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
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
