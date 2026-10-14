using System.Globalization;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

/// <summary>
/// Automatic history cleanup. Only finished history is ever eligible; anything the user may
/// still act on (pending, downloading, moving, retrying, or a failed move of a complete
/// download) is always kept. Cleanup removes history rows only and never touches files.
/// </summary>
public static class HistoryRetentionPolicy
{
    public const string SettingKey = "history-retention-days";
    public const int KeepForever = 0;

    public static IReadOnlyList<int> AllowedDays { get; } = [0, 7, 30, 90, 180, 365];

    /// <summary>The stored setting, or <see cref="KeepForever"/> when it is missing or invalid.</summary>
    public static int Parse(string? storedValue)
        => int.TryParse(storedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            && AllowedDays.Contains(days)
                ? days
                : KeepForever;

    /// <exception cref="ArgumentException"><paramref name="days"/> is not one of <see cref="AllowedDays"/>.</exception>
    public static void Validate(int days)
    {
        if (!AllowedDays.Contains(days))
        {
            throw new ArgumentException(AppText.Pick(
                "기록 보관 기간은 0(계속 보관), 7, 30, 90, 180, 365일 가운데 하나여야 합니다.",
                "History retention must be 0 (keep forever), 7, 30, 90, 180 or 365 days."));
        }
    }

    public static DateTimeOffset Cutoff(int days, DateTimeOffset now)
        => now - TimeSpan.FromDays(days);

    /// <summary>
    /// Mirrors the repository's cleanup query: a cancelled or interrupted transfer, or a
    /// complete download whose routing finished or was skipped, older than the cutoff.
    /// </summary>
    public static bool IsEligible(DownloadJob job, DateTimeOffset cutoff)
    {
        ArgumentNullException.ThrowIfNull(job);
        var finished = job.RoutingState is not (RoutingState.Moving or RoutingState.RetryPending)
            && (job.BrowserState is BrowserTransferState.Cancelled or BrowserTransferState.Interrupted
                || (job.BrowserState == BrowserTransferState.Complete
                    && job.RoutingState is RoutingState.Completed or RoutingState.Skipped));
        return finished && (job.CompletedAt ?? job.CreatedAt) < cutoff;
    }
}
