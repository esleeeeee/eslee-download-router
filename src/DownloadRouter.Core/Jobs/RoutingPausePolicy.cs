using System.Globalization;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

/// <summary>
/// Stored form of "pause routing": an ISO-8601 UTC end time, or <see cref="IndefiniteValue"/>
/// for a pause that lasts until the user resumes. Expired or unreadable values mean routing
/// is active, so a damaged setting can never leave downloads unrouted forever.
/// </summary>
public static class RoutingPausePolicy
{
    public const string SettingKey = "routing-paused-until";
    public const string IndefiniteValue = "indefinite";
    public const int MaximumMinutes = 1440;

    public static RoutingPauseStatus Evaluate(string? storedValue, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return new RoutingPauseStatus(false, null);
        }

        if (string.Equals(storedValue.Trim(), IndefiniteValue, StringComparison.OrdinalIgnoreCase))
        {
            return new RoutingPauseStatus(true, null);
        }

        return DateTimeOffset.TryParse(
                storedValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var until)
            && until > now
                ? new RoutingPauseStatus(true, until.ToUniversalTime())
                : new RoutingPauseStatus(false, null);
    }

    /// <summary>True when a stored value exists but no longer pauses anything and can be removed.</summary>
    public static bool IsStale(string? storedValue, DateTimeOffset now)
        => !string.IsNullOrWhiteSpace(storedValue) && !Evaluate(storedValue, now).IsPaused;

    /// <exception cref="ArgumentException">The duration is outside 1 to 1440 minutes.</exception>
    public static string ToStoredValue(int? minutes, DateTimeOffset now)
    {
        if (minutes is null)
        {
            return IndefiniteValue;
        }

        if (minutes is < 1 or > MaximumMinutes)
        {
            throw new ArgumentException(AppText.Pick(
                $"일시 중지 시간은 1분부터 {MaximumMinutes}분까지 지정할 수 있습니다.",
                $"A pause must last between 1 and {MaximumMinutes} minutes."));
        }

        return (now + TimeSpan.FromMinutes(minutes.Value)).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
