using System.Runtime.InteropServices;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Settings;
using Microsoft.UI.Windowing;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private readonly AppPreferencesStore preferencesStore;
    private readonly ThemeManager themeManager;
    private AppPreferences preferences;
    private AppWindow? appWindow;
    private TrayIconHost? trayIcon;
    private TrayFolderLink? trayFolderLink;
    private bool exitRequested;
    private Action? exitCallback;

    public void InitializeHost(bool background, Action onExit)
    {
        exitCallback = onExit;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        appWindow = AppWindow.GetFromWindowId(windowId);
        ProductBranding.ApplyWindowIcon(appWindow);
        appWindow.Closing += AppWindow_Closing;
        trayIcon = new TrayIconHost(
            DispatcherQueue,
            ShowFromTray,
            () => NavigateFromTray("pending"),
            () => NavigateFromTray("settings"),
            ExitFromTray);
        trayFolderLink = new TrayFolderLink(
            TrayFolderLink.BuildDefaultPipeName(),
            "eslee.downloadrouter",
            "eslee Download Router",
            Environment.ProcessId,
            visible => RunOnUiAsync(() =>
            {
                trayIcon?.SetIconVisible(visible);
                return true;
            }),
            () => RunOnUiAsync(() =>
            {
                ShowFromTray();
                return true;
            }),
            () => RunOnUiAsync(BuildTrayFolderMenuItems),
            actionId => RunOnUiAsync(() => TryStartTrayFolderMenuAction(actionId)),
            (eventName, message) => WriteAppDiagnostic($"tray-host {eventName}: {message}"),
            (eventName, message) => WriteAppDiagnostic($"tray-host error {eventName}: {message}"));
        trayFolderLink.Start();
        Closed += (_, _) =>
        {
            trayFolderLink?.Dispose();
            trayFolderLink = null;
            trayIcon?.Dispose();
            exitCallback?.Invoke();
        };
        _ = EnsureAgentForHostAsync();
        StartUpdateChecks();
        if (!background)
        {
            ShowFromTray();
        }
    }

    private IReadOnlyList<TrayFolderMenuItem> BuildTrayFolderMenuItems() =>
    [
        TrayFolderMenuItem.Action("open-app", "eslee Download Router 열기"),
        TrayFolderMenuItem.Action("open-pending", "저장 위치 선택 대기 열기"),
        TrayFolderMenuItem.Action("open-settings", "일반 설정"),
        TrayFolderMenuItem.Separator,
        TrayFolderMenuItem.Action("exit-app", "종료"),
    ];

    private bool TryStartTrayFolderMenuAction(string actionId)
    {
        switch (actionId)
        {
            case "open-app":
                ShowFromTray();
                return true;
            case "open-pending":
                NavigateFromTray("pending");
                return true;
            case "open-settings":
                NavigateFromTray("settings");
                return true;
            case "exit-app":
                _ = DispatcherQueue.TryEnqueue(ExitFromExternalRequest);
                return true;
            default:
                return false;
        }
    }

    private Task<T> RunOnUiAsync<T>(Func<T> callback)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                completion.TrySetResult(callback());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        if (!queued)
        {
            completion.TrySetCanceled();
        }

        return completion.Task;
    }

    public void ShowFromTray()
    {
        appWindow?.Show();
        Activate();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = ShowWindow(handle, 9);
        _ = SetForegroundWindow(handle);
    }

    private void NavigateFromTray(string tag)
    {
        ShowFromTray();
        var item = Navigation.MenuItems
            .OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (item is not null)
        {
            Navigation.SelectedItem = item;
        }
    }

    private void ExitFromTray()
        => ExitFromExternalRequest();

    public void ExitFromExternalRequest()
    {
        exitRequested = true;
        StopLiveUpdates();
        trayIcon?.Dispose();
        trayIcon = null;
        Close();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (exitRequested)
        {
            return;
        }

        if (preferences.CloseBehavior == WindowCloseBehavior.MinimizeToTray)
        {
            args.Cancel = true;
            sender.Hide();
            return;
        }

        exitRequested = true;
        StopLiveUpdates();
        trayIcon?.Dispose();
        trayIcon = null;
    }

    private async Task EnsureAgentForHostAsync()
    {
        var running = await new AgentProcessManager(agent).EnsureRunningAsync();
        if (!running)
        {
            WriteAppDiagnostic("Agent start failed; Native Host fallback remains enabled.");
        }
    }

    private static void WriteAppDiagnostic(string message)
        => WriteDiagnostic("app-startup.log", message);

    private static void WriteWindowActivationDiagnostic(string message)
        => WriteDiagnostic("window-activation.log", message);

    private FolderSelectionWindow CreateFolderSelectionWindow()
        => new(
            themeManager,
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            WriteWindowActivationDiagnostic);

    private static void WriteDiagnostic(string fileName, string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "eslee",
                "DownloadRouter",
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, fileName),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
