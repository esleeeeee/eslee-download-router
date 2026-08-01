using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

/// <summary>
/// Decides whether the automatic folder selection window may open for a job.
///
/// The decision is derived from the persisted <see cref="SelectionPromptState"/> only.
/// Browser activity timestamps describe transfer progress and must never re-arm a prompt
/// the user already saw, postponed, or resolved.
/// </summary>
public static class SelectionPromptPolicy
{
    public static bool IsAutoPromptEligible(DownloadJob job)
        => job.IsSelectionPending
            && !job.IsBrowserRecordStale
            && job.SelectionPromptState == SelectionPromptState.NeverShown;

    /// <summary>
    /// A job that still needs a folder but is no longer an automatic prompt candidate.
    /// These stay visible in the download history "처리 대기" filter.
    /// </summary>
    public static bool IsPreviousSessionPending(DownloadJob job)
        => job.IsSelectionPending && !IsAutoPromptEligible(job);

    /// <summary>
    /// Prompt states may only move forward. A reconciliation or a late browser event
    /// must not pull a resolved job back into the automatic queue.
    /// </summary>
    public static bool CanAdvanceTo(SelectionPromptState current, SelectionPromptState next)
        => Rank(next) > Rank(current);

    private static int Rank(SelectionPromptState state)
        => state switch
        {
            SelectionPromptState.NeverShown => 0,
            SelectionPromptState.Shown => 1,
            SelectionPromptState.Deferred => 2,
            SelectionPromptState.Resolved => 3,
            _ => 0,
        };
}
