using System.Diagnostics;

namespace DownloadRouter.App.Services;

/// <summary>
/// Restarts the app (for a language change) with the same data location arguments. The new
/// process is started only after this one has released its single-instance lock, and it also
/// waits for this process id, so the two never race for the lock.
/// </summary>
internal static class AppRestart
{
    private static bool pending;

    public static void Request()
    {
        pending = true;
        AppServices.Main?.ExitFromExternalRequest();
    }

    /// <summary>Called by the exit path after the mutex is disposed.</summary>
    public static void LaunchPendingRestart()
    {
        if (!pending || Environment.ProcessPath is not string executable)
        {
            return;
        }

        pending = false;
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in AppServices.Instance.ForwardedArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(App.WaitForExitArgument);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppServices.WriteDiagnostic("app-startup.log", $"restart failed: {exception.GetType().Name}");
        }
    }
}
