using System.Diagnostics;
using DownloadRouter.Core.Startup;

namespace DownloadRouter.App;

public sealed class AgentProcessManager(AgentClient client)
{
    public static void RequestShutdown()
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(AppInstance.Current.AgentShutdownEventName);
            shutdownEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public async Task<bool> EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        if (await CanPingAsync(cancellationToken))
        {
            return true;
        }

        var agentPath = FindAgentPath();
        if (agentPath is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = agentPath,
            WorkingDirectory = Path.GetDirectoryName(agentPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        // The agent must serve the same data root as this app instance.
        foreach (var argument in AppInstance.Current.ForwardedArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(150, cancellationToken);
            if (await CanPingAsync(cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> CanPingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await client.SendAsync("ping", cancellationToken: cancellationToken)).Success;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private static string? FindAgentPath()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "DownloadRouter.Agent.exe");
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
        var candidate = Path.Combine(
            directory.FullName,
            "src",
            "DownloadRouter.Agent",
            "bin",
            configuration,
            "net10.0",
            "DownloadRouter.Agent.exe");
        return File.Exists(candidate) ? candidate : null;
    }
}
