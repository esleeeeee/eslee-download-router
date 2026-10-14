using DownloadRouter.Core.Localization;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DownloadRouter.App.Services;

/// <summary>
/// Windows notifications for "file moved". Registration happens only after the user turns the
/// setting on, so an app that never notifies leaves no notification registration behind. When
/// registration is impossible the caller falls back to the tray balloon.
/// </summary>
public sealed class NotificationService
{
    public const string UnregisterArgument = "--unregister-notifications";
    private const string ActionKey = "action";
    private const string JobKey = "job";
    private const string OpenJobAction = "open-job";

    private readonly DispatcherQueue dispatcher;
    private bool registered;

    public NotificationService(DispatcherQueue dispatcher)
    {
        this.dispatcher = dispatcher;
    }

    /// <summary>Raised on the UI thread when the user clicks a notification about a job.</summary>
    public event EventHandler<Guid>? JobNotificationInvoked;

    public bool IsRegistered => registered;

    public bool EnsureRegistered()
    {
        if (registered)
        {
            return true;
        }

        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "notification-icon.png");
            if (File.Exists(icon))
            {
                manager.Register("eslee Download Router", new Uri(icon));
            }
            else
            {
                manager.Register();
            }

            registered = true;
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"notification registration failed: {exception.GetType().Name} {exception.HResult:X8}");
        }

        return registered;
    }

    /// <summary>Returns false when Windows notifications are unavailable so the caller can fall back.</summary>
    public bool ShowMoveCompleted(Guid jobId, string fileName, string destinationFolder)
    {
        if (!EnsureRegistered())
        {
            return false;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument(ActionKey, OpenJobAction)
                .AddArgument(JobKey, jobId.ToString("N"))
                .AddText(AppText.Pick("파일을 옮겼습니다", "File moved"))
                .AddText(fileName)
                .AddText(destinationFolder)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"notification show failed: {exception.GetType().Name} {exception.HResult:X8}");
            return false;
        }
    }

    /// <summary>Turns notifications off and removes this app's notification registration.</summary>
    public void Disable()
    {
        if (registered)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        }

        UnregisterAll();
        registered = false;
    }

    /// <summary>Removes the registration (used by the uninstaller and when the setting is turned off).</summary>
    public static void UnregisterAll()
    {
        try
        {
            AppNotificationManager.Default.UnregisterAll();
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"notification unregister failed: {exception.GetType().Name} {exception.HResult:X8}");
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue(ActionKey, out var action)
            && action == OpenJobAction
            && args.Arguments.TryGetValue(JobKey, out var job)
            && Guid.TryParseExact(job, "N", out var jobId))
        {
            dispatcher.TryEnqueue(() => JobNotificationInvoked?.Invoke(this, jobId));
        }
    }
}
