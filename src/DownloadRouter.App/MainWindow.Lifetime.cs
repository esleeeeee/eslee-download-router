using System.Runtime.InteropServices;
using DownloadRouter.App.Services;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private const int DefaultWidthDip = 1180;
    private const int DefaultHeightDip = 780;
    private const int MinimumWidthDip = 720;
    private const int MinimumHeightDip = 520;

    /// <summary>Tray Folder menu action ids. Existing ids are part of the Tray Folder catalog contract.</summary>
    private const string OpenAppAction = "open-app";
    private const string OpenPendingAction = "open-pending";
    private const string OpenSettingsAction = "open-settings";
    private const string PauseRoutingAction = "pause-routing";
    private const string ResumeRoutingAction = "resume-routing";
    private const string ExitAppAction = "exit-app";

    private AppWindow? appWindow;
    private TrayIconHost? trayIcon;
    private TrayFolderLink? trayFolderLink;
    private bool exitRequested;
    private bool sizedOnce;
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
            () => _ = PauseRoutingAsync(60),
            () => _ = ResumeRoutingAsync(),
            ExitFromTray);

        // A copy started on another data folder (tests) must not register with Tray Folder:
        // it would take over the installed app's tile and menu.
        if (AppServices.Instance.IsDefault)
        {
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
        }

        Closed += (_, _) =>
        {
            trayFolderLink?.Dispose();
            trayFolderLink = null;
            trayIcon?.Dispose();
            exitCallback?.Invoke();
        };
        _ = EnsureAgentForHostAsync();
        AppServices.Live.Start();
        StartUpdateChecks();
        if (!background)
        {
            ShowFromTray();
        }
    }

    private IReadOnlyList<TrayFolderMenuItem> BuildTrayFolderMenuItems()
    {
        var paused = AppServices.Live.Latest?.Pause.IsPaused == true;
        return
        [
            TrayFolderMenuItem.Action(OpenAppAction, AppText.Pick("eslee Download Router 열기", "Open eslee Download Router")),
            TrayFolderMenuItem.Action(OpenPendingAction, AppText.Pick("저장 위치 선택 대기 열기", "Open files waiting for a folder")),
            TrayFolderMenuItem.Action(OpenSettingsAction, AppText.Pick("설정", "Settings")),
            TrayFolderMenuItem.Separator,
            paused
                ? TrayFolderMenuItem.Action(ResumeRoutingAction, AppText.Pick("자동 분류 다시 켜기", "Resume sorting"))
                : TrayFolderMenuItem.Action(PauseRoutingAction, AppText.Pick("자동 분류 1시간 멈추기", "Pause sorting for 1 hour")),
            TrayFolderMenuItem.Separator,
            TrayFolderMenuItem.Action(ExitAppAction, AppText.Pick("종료", "Exit")),
        ];
    }

    private bool TryStartTrayFolderMenuAction(string actionId)
    {
        switch (actionId)
        {
            case OpenAppAction:
                ShowFromTray();
                return true;
            case OpenPendingAction:
                NavigateFromTray("pending");
                return true;
            case OpenSettingsAction:
                NavigateFromTray("settings");
                return true;
            case PauseRoutingAction:
                _ = PauseRoutingAsync(60);
                return true;
            case ResumeRoutingAction:
                _ = ResumeRoutingAsync();
                return true;
            case ExitAppAction:
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
        SizeOnFirstShow();
        appWindow?.Show();
        Activate();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = ShowWindow(handle, 9);
        _ = SetForegroundWindow(handle);
    }

    /// <summary>
    /// "pending" opens the downloads page on its pending filter. (v1.3.x looked for a navigation
    /// tag that no longer existed, so the tray item only showed the window.)
    /// </summary>
    private void NavigateFromTray(string tag)
    {
        ShowFromTray();
        if (tag == "pending")
        {
            OpenPendingHistory();
        }
        else
        {
            NavigateTo(tag);
        }
    }

    private void ExitFromTray()
        => ExitFromExternalRequest();

    public void ExitFromExternalRequest()
    {
        exitRequested = true;
        currentSelectionCancellation?.Cancel();
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

        if (AppServices.Preferences.CloseBehavior == WindowCloseBehavior.MinimizeToTray)
        {
            args.Cancel = true;
            sender.Hide();
            return;
        }

        exitRequested = true;
        currentSelectionCancellation?.Cancel();
        trayIcon?.Dispose();
        trayIcon = null;
    }

    private void SizeOnFirstShow()
    {
        if (sizedOnce || appWindow is null)
        {
            return;
        }

        sizedOnce = true;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = Math.Max(96u, GetDpiForWindow(handle)) / 96d;
        var workArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)Math.Round(DefaultWidthDip * scale), workArea.Width - 48);
        var height = Math.Min((int)Math.Round(DefaultHeightDip * scale), workArea.Height - 48);
        appWindow.Resize(new SizeInt32(width, height));
        appWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)Math.Round(MinimumWidthDip * scale);
            presenter.PreferredMinimumHeight = (int)Math.Round(MinimumHeightDip * scale);
        }
    }

    private DateTimeOffset lastAgentStartAttempt = DateTimeOffset.MinValue;
    private bool agentStartInProgress;

    private async Task EnsureAgentForHostAsync()
    {
        if (exitRequested || agentStartInProgress
            || DateTimeOffset.UtcNow - lastAgentStartAttempt < TimeSpan.FromSeconds(10))
        {
            return;
        }

        lastAgentStartAttempt = DateTimeOffset.UtcNow;
        agentStartInProgress = true;
        try
        {
            var running = await Task.Run(() => new AgentProcessManager(AppServices.Agent).EnsureRunningAsync());
            if (!running)
            {
                WriteAppDiagnostic("Agent start failed; Native Host fallback remains enabled.");
            }
        }
        catch (Exception exception)
        {
            WriteAppDiagnostic($"Agent recovery failed: {exception.GetType().Name}");
        }
        finally
        {
            agentStartInProgress = false;
        }
    }

    private static void WriteAppDiagnostic(string message)
        => AppServices.WriteDiagnostic("app-startup.log", message);

    private static void WriteWindowActivationDiagnostic(string message)
        => AppServices.WriteDiagnostic("window-activation.log", message);

    private FolderSelectionWindow CreateFolderSelectionWindow()
        => new(WinRT.Interop.WindowNative.GetWindowHandle(this), WriteWindowActivationDiagnostic);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
