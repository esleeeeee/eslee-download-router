using System.Security;
using DownloadRouter.Core.Localization;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DownloadRouter.App.Services;

/// <summary>
/// Windows notifications for "file moved". The app identity is a per-user AppUserModelId key
/// (display name and icon) used with the classic toast API: the Windows App SDK
/// AppNotificationManager.Register fails with 0x8007007E in this self-contained, unpackaged app.
/// The key is written only after the user turns the setting on and only by the installed copy;
/// turning the setting off or uninstalling removes it. When showing fails the caller falls back
/// to the tray balloon.
/// </summary>
public sealed class NotificationService
{
    public const string UnregisterArgument = "--unregister-notifications";
    public const string AppUserModelId = "eslee.DownloadRouter";
    private const string RegistryPath = @"Software\Classes\AppUserModelId\" + AppUserModelId;

    /// <summary>Id the v2.0 development builds left behind through AppNotificationManager.Register.</summary>
    private const string LegacyRegistrationDisplayName = "eslee Download Router";

    private readonly DispatcherQueue dispatcher;
    private bool registered;

    public NotificationService(DispatcherQueue dispatcher)
    {
        this.dispatcher = dispatcher;
    }

    /// <summary>Raised on the UI thread when the user clicks a notification about a job while the app runs.</summary>
    public event EventHandler<Guid>? JobNotificationInvoked;

    public bool IsRegistered => registered;

    public bool EnsureRegistered()
    {
        // A copy on another data folder must not create or change the installed app's identity.
        if (!AppServices.Instance.IsDefault)
        {
            return false;
        }

        if (registered)
        {
            return true;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue("DisplayName", "eslee Download Router");
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "notification-icon.png");
            if (File.Exists(icon))
            {
                key.SetValue("IconUri", icon);
            }

            registered = true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
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
            var document = new XmlDocument();
            document.LoadXml(
                "<toast launch=\"open-job=" + jobId.ToString("N") + "\"><visual><binding template=\"ToastGeneric\">"
                + "<text>" + SecurityElement.Escape(AppText.Pick("파일을 옮겼습니다", "File moved")) + "</text>"
                + "<text>" + SecurityElement.Escape(fileName) + "</text>"
                + "<text>" + SecurityElement.Escape(destinationFolder) + "</text>"
                + "</binding></visual></toast>");
            var toast = new ToastNotification(document);
            toast.Activated += (_, _) => dispatcher.TryEnqueue(() => JobNotificationInvoked?.Invoke(this, jobId));
            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
            return true;
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"notification show failed: {exception.GetType().Name} {exception.HResult:X8}");
            return false;
        }
    }

    /// <summary>Turns notifications off and removes this app's notification identity.</summary>
    public void Disable()
    {
        if (!AppServices.Instance.IsDefault)
        {
            return;
        }

        UnregisterAll();
        registered = false;
    }

    /// <summary>Removes the notification identity and history (setting turned off, or the uninstaller).</summary>
    public static void UnregisterAll()
    {
        try
        {
            ToastNotificationManager.History.Clear(AppUserModelId);
        }
        catch (Exception exception)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"notification history clear failed: {exception.GetType().Name} {exception.HResult:X8}");
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
            RemoveLegacyRegistration();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"notification unregister failed: {exception.GetType().Name} {exception.HResult:X8}");
        }
    }

    /// <summary>
    /// Development builds of v2.0 called AppNotificationManager.Register, which stored a GUID-named
    /// AppUserModelId pointing at this install before failing. Remove only entries whose icon lives
    /// in this app's folder.
    /// </summary>
    private static void RemoveLegacyRegistration()
    {
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\AppUserModelId", writable: true);
        if (root is null)
        {
            return;
        }

        foreach (var name in root.GetSubKeyNames().Where(static name => name.StartsWith('{')))
        {
            using var entry = root.OpenSubKey(name);
            if (entry?.GetValue("DisplayName") is string displayName
                && displayName == LegacyRegistrationDisplayName
                && entry.GetValue("IconUri") is string icon
                && icon.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                entry.Dispose();
                root.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
        }
    }
}
