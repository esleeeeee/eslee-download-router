using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

public enum DownloadRegistrationDecision
{
    /// <summary>A live transfer. A new job may be created.</summary>
    Track,

    /// <summary>The browser already finished this transfer, so it cannot be starting now.</summary>
    RejectAlreadyFinished,

    /// <summary>The transfer began before this session; it is a replayed history record.</summary>
    RejectStartedBeforeSession,
}

/// <summary>
/// Guards job creation against replayed browser history.
///
/// Chromium raises <c>downloads.onCreated</c> for every item in the download history when
/// the browser starts. The extension filters those out, but the agent repeats the check so
/// an older or third-party extension build cannot recreate jobs for files the user already
/// handled. Existing jobs are never affected by this policy.
/// </summary>
public static class DownloadRegistrationPolicy
{
    /// <summary>
    /// A real <c>onCreated</c> notification reaches the agent within seconds. The allowance
    /// is generous enough to absorb agent startup and native messaging latency.
    /// </summary>
    public static readonly TimeSpan LiveDownloadWindow = TimeSpan.FromMinutes(10);

    public static DownloadRegistrationDecision Classify(
        DownloadStartedPayload payload,
        DateTimeOffset now)
    {
        var state = payload.State?.Trim().ToLowerInvariant();
        if (state is "complete" or "interrupted" or "cancelled")
        {
            return DownloadRegistrationDecision.RejectAlreadyFinished;
        }

        if (payload.StartedAt is DateTimeOffset startedAt
            && now - startedAt > LiveDownloadWindow)
        {
            return DownloadRegistrationDecision.RejectStartedBeforeSession;
        }

        // A missing state keeps the previous fail-open behaviour for older extension builds.
        return DownloadRegistrationDecision.Track;
    }

    public static string DescribeRejection(DownloadRegistrationDecision decision)
        => decision switch
        {
            DownloadRegistrationDecision.RejectAlreadyFinished => "already-finished",
            DownloadRegistrationDecision.RejectStartedBeforeSession => "started-before-session",
            _ => "tracked",
        };
}
