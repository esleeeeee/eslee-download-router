/**
 * Chromium fires `chrome.downloads.onCreated` for every item already in the browser's
 * download history when the download manager loads at browser startup. Those replayed
 * events look identical to a real new download, so they must be filtered out before the
 * extension asks the agent to track anything. Only a genuinely live transfer may create
 * a job.
 */

/** A newly started transfer reaches the listener within milliseconds. */
export const liveDownloadWindowMs = 5 * 60 * 1000;

export interface CreatedDownloadLike {
  state?: string;
  startTime?: string;
  exists?: boolean;
}

export type DownloadOriginDecision =
  | { track: true }
  | { track: false; reason: "not-in-progress" | "started-before-session" };

/**
 * Decides whether an `onCreated` item is a live download or a history replay.
 * `nowMs` is passed in so the rule stays deterministic under test.
 */
export function classifyCreatedDownload(
  item: CreatedDownloadLike,
  nowMs: number,
): DownloadOriginDecision {
  // A transfer that is already finished cannot be starting now.
  if (item.state !== "in_progress") {
    return { track: false, reason: "not-in-progress" };
  }

  // A restored-but-unfinished item keeps its original start time, which is in the past.
  const startedAtMs = parseStartTime(item.startTime);
  if (startedAtMs !== null && nowMs - startedAtMs > liveDownloadWindowMs) {
    return { track: false, reason: "started-before-session" };
  }

  return { track: true };
}

export function parseStartTime(value: string | undefined): number | null {
  if (typeof value !== "string" || value.trim().length === 0) {
    return null;
  }

  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Normalised transfer state for the agent payload; never a raw browser string. */
export function reportableState(value: string | undefined): "in_progress" | "complete" | "interrupted" | null {
  return value === "in_progress" || value === "complete" || value === "interrupted" ? value : null;
}
