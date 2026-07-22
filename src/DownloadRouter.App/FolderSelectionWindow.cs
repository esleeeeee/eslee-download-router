using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DownloadRouter.App;

public enum FolderSelectionAction
{
    Apply,
    Later,
    Skip,
    Closed,
}

public sealed record FolderSelectionResult(
    FolderSelectionAction Action,
    string RelativeFolder);

public sealed class FolderSelectionWindow
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
    private bool completed;

    public void UpdateDescription(string description)
        => details.Text = description;

    public async Task<FolderSelectionResult> ShowAsync(
        string storageRoot,
        string? selectedRelativeFolder,
        string description,
        bool allowLater,
        bool allowSkip,
        CancellationToken cancellationToken = default)
    {
        window.Title = "다운로드 저장 위치 선택";
        window.Content = CreateContent(description, allowLater, allowSkip);
        window.Closed += (_, _) => CompleteWithoutClosing(FolderSelectionAction.Closed);
        await picker.InitializeAsync(storageRoot, selectedRelativeFolder, cancellationToken);
        window.Activate();
        SizeAndCenterWindow();
        BringToForeground();

        using var registration = cancellationToken.Register(() =>
            window.DispatcherQueue.TryEnqueue(() => Complete(FolderSelectionAction.Closed)));
        return await completion.Task;
    }

    private UIElement CreateContent(string description, bool allowLater, bool allowSkip)
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
        }
    }

    private void BringToForeground()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _ = ShowWindow(handle, 5);
        if (!SetForegroundWindow(handle))
        {
            var flash = new FlashWindowInfo
            {
                Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                Window = handle,
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
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}
