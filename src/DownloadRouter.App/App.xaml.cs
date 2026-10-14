using System.Diagnostics;
using DownloadRouter.App.Services;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Settings;
using DownloadRouter.Core.Startup;
using Microsoft.UI.Xaml;

namespace DownloadRouter.App;

public partial class App : Application
{
    /// <summary>Passed by <see cref="AppRestart"/> so the new process waits for the old one to release the instance lock.</summary>
    public const string WaitForExitArgument = "--wait-for-exit";

    private Window? window;
    private Mutex? singleInstance;
    private EventWaitHandle? activationEvent;
    private RegisteredWaitHandle? activationRegistration;
    private EventWaitHandle? shutdownEvent;
    private RegisteredWaitHandle? shutdownRegistration;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, eventArgs) =>
            AppServices.WriteDiagnostic("app-unhandled.log", eventArgs.Exception.ToString());
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var instance = AppInstance.Resolve(
            commandLine,
            Environment.GetEnvironmentVariable("DOWNLOAD_ROUTER_DATA_DIR"));
        AppInstance.Initialize(instance);
        AppServices.Instance = instance;

        if (HasArgument(commandLine, NotificationService.UnregisterArgument))
        {
            // Used by the uninstaller: remove the Windows notification registration and stop.
            NotificationService.UnregisterAll();
            Exit();
            return;
        }

        var background = HasArgument(commandLine, "--background");
        var shutdown = HasArgument(commandLine, "--shutdown");
        WaitForPreviousProcess(commandLine);

        singleInstance = new Mutex(initiallyOwned: true, instance.AppMutexName, out var createdNew);
        if (!createdNew)
        {
            if (shutdown)
            {
                SignalExistingInstance(instance.AppShutdownEventName);
                AgentProcessManager.RequestShutdown();
            }
            else if (!background)
            {
                SignalExistingInstance(instance.AppActivationEventName);
            }

            singleInstance.Dispose();
            singleInstance = null;
            Exit();
            return;
        }

        if (shutdown)
        {
            AgentProcessManager.RequestShutdown();
            singleInstance.Dispose();
            singleInstance = null;
            Exit();
            return;
        }

        var preferencesStore = new AppPreferencesStore(instance.ConfigPath);
        var preferences = preferencesStore.Load();
        AppText.SetLanguage(preferences.LanguagePreference);
        AppServices.InitializePreferences(preferencesStore, preferences);

        var mainWindow = new MainWindow();
        window = mainWindow;
        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instance.AppActivationEventName);
        activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, _) => mainWindow.DispatcherQueue.TryEnqueue(mainWindow.ShowFromTray),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instance.AppShutdownEventName);
        shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            shutdownEvent,
            (_, _) => mainWindow.DispatcherQueue.TryEnqueue(mainWindow.ExitFromExternalRequest),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);
        mainWindow.InitializeHost(background, () =>
        {
            activationRegistration?.Unregister(null);
            shutdownRegistration?.Unregister(null);
            activationEvent?.Dispose();
            shutdownEvent?.Dispose();
            singleInstance?.Dispose();
            singleInstance = null;
            AgentProcessManager.RequestShutdown();
            AppRestart.LaunchPendingRestart();
            Exit();
        });
    }

    private static bool HasArgument(IEnumerable<string> commandLine, string name)
        => commandLine.Any(argument => argument.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>After an in-app restart the previous process may still hold the instance mutex for a moment.</summary>
    private static void WaitForPreviousProcess(IReadOnlyList<string> commandLine)
    {
        for (var index = 0; index < commandLine.Count - 1; index++)
        {
            if (commandLine[index].Equals(WaitForExitArgument, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(commandLine[index + 1], out var processId))
            {
                try
                {
                    using var previous = Process.GetProcessById(processId);
                    previous.WaitForExit(TimeSpan.FromSeconds(15));
                }
                catch (ArgumentException)
                {
                    // Already gone.
                }

                return;
            }
        }
    }

    private static void SignalExistingInstance(string eventName)
    {
        try
        {
            using var existingEvent = EventWaitHandle.OpenExisting(eventName);
            existingEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }
}
