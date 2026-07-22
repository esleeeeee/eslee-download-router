using System.Runtime.InteropServices;
using DownloadRouter.Core.Models;
using Microsoft.UI.Windowing;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    private readonly AppPreferencesStore preferencesStore = new();
    private AppPreferences preferences = new();
    private AppWindow? appWindow;
    private TrayIconHost? trayIcon;
    private bool exitRequested;
    private Action? exitCallback;

    public void InitializeHost(bool background, Action onExit)
    {
        exitCallback = onExit;
        preferences = preferencesStore.Load();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Closing += AppWindow_Closing;
        trayIcon = new TrayIconHost(
            DispatcherQueue,
            ShowFromTray,
            () => NavigateFromTray("pending"),
            () => NavigateFromTray("settings"),
            ExitFromTray);
        Closed += (_, _) =>
        {
            trayIcon?.Dispose();
            exitCallback?.Invoke();
        };
        _ = EnsureAgentForHostAsync();
        if (!background)
        {
            ShowFromTray();
        }
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
                Path.Combine(directory, "app-startup.log"),
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
