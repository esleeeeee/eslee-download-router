using System.Diagnostics;
using DownloadRouter.Core.Models;
using Microsoft.Extensions.Logging;

namespace DownloadRouter.Agent;

public interface ISelectionUiLauncher
{
    bool RequestSelectionUi();
}

public sealed class SelectionUiLauncher(ILogger<SelectionUiLauncher> logger) : ISelectionUiLauncher
{
    private readonly object gate = new();
    private string? cachedAppPath;

    public bool RequestSelectionUi()
    {
        lock (gate)
        {
            try
            {
                if (Mutex.TryOpenExisting(ProtocolConstants.AppMutexName, out var existing))
                {
                    existing.Dispose();
                    return true;
                }

                var appPath = cachedAppPath ??= FindAppPath();
                if (appPath is null)
                {
                    logger.LogInformation("Selection UI executable was not found; the download remains pending and unchanged");
                    return false;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = appPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(appPath)!,
                });
                return true;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Selection UI could not be opened; the browser download remains unchanged");
                return false;
            }
        }
    }

    private static string? FindAppPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("DOWNLOAD_ROUTER_APP_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)
            && Path.IsPathFullyQualified(overridePath)
            && File.Exists(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var installed = Path.Combine(AppContext.BaseDirectory, "DownloadRouter.App.exe");
        if (File.Exists(installed))
        {
            return installed;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DownloadRouter.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var development = Path.Combine(
            directory.FullName,
            "src",
            "DownloadRouter.App",
            "bin",
            configuration,
            "net10.0-windows10.0.22621.0",
            "win-x64",
            "DownloadRouter.App.exe");
        return File.Exists(development) ? development : null;
    }
}
