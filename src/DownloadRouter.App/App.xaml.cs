using Microsoft.UI.Xaml;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App;

public partial class App : Application
{
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
                    Path.Combine(directory, "app-unhandled.log"),
                    $"{DateTimeOffset.UtcNow:O} {eventArgs.Exception}{Environment.NewLine}");
            }
            catch
            {
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs();
        var background = commandLine
            .Any(static argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase));
        var shutdown = commandLine
            .Any(static argument => argument.Equals("--shutdown", StringComparison.OrdinalIgnoreCase));
        singleInstance = new Mutex(initiallyOwned: true, ProtocolConstants.AppMutexName, out var createdNew);
        if (!createdNew)
        {
            if (shutdown)
            {
                SignalExistingInstance(ProtocolConstants.AppShutdownEventName);
                AgentProcessManager.RequestShutdown();
            }
            else if (!background)
            {
                SignalExistingInstance(ProtocolConstants.AppActivationEventName);
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

        var mainWindow = new MainWindow();
        window = mainWindow;
        activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ProtocolConstants.AppActivationEventName);
        activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, _) => mainWindow.DispatcherQueue.TryEnqueue(mainWindow.ShowFromTray),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        shutdownEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ProtocolConstants.AppShutdownEventName);
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
            AgentProcessManager.RequestShutdown();
            Exit();
        });
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
