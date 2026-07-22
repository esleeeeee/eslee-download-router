using Microsoft.UI.Xaml;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App;

public partial class App : Application
{
    private Window? window;
    private Mutex? singleInstance;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        singleInstance = new Mutex(initiallyOwned: true, ProtocolConstants.AppMutexName, out var createdNew);
        if (!createdNew)
        {
            singleInstance.Dispose();
            singleInstance = null;
            Exit();
            return;
        }

        window = new MainWindow();
        window.Activate();
    }
}
