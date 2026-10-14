using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DownloadRouter.Agent;

public interface IHistoryCleanup
{
    Task<int> RunHistoryCleanupAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Runs the automatic history cleanup shortly after the agent starts and then periodically.
/// A failed run is logged and retried at the next interval; it never stops the agent.
/// </summary>
public sealed class HistoryRetentionService(
    IHistoryCleanup cleanup,
    TimeProvider timeProvider,
    ILogger<HistoryRetentionService> logger) : BackgroundService
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, timeProvider, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await cleanup.RunHistoryCleanupAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Automatic history cleanup failed; it will run again later");
                }

                await Task.Delay(Interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
