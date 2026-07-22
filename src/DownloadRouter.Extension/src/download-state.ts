export type ReportedDownloadState = "complete" | "interrupted" | "cancelled";

export function reportedDownloadState(
  browserState: "complete" | "interrupted",
  error: string | null | undefined,
): ReportedDownloadState {
  if (browserState === "interrupted" && error?.toUpperCase() === "USER_CANCELED") {
    return "cancelled";
  }

  return browserState;
}

export function preferredDownloadError(
  deltaError: string | null | undefined,
  itemError: string | null | undefined,
  previousError: string | null | undefined,
): string | null {
  return deltaError ?? itemError ?? previousError ?? null;
}

export function isUserCancelled(error: string | null | undefined): boolean {
  return error?.toUpperCase() === "USER_CANCELED";
}

export function safeDownloadError(error: string | null | undefined): string {
  const normalized = error?.toUpperCase() ?? "NONE";
  return /^(USER_(?:CANCELED|SHUTDOWN)|CRASH|NETWORK_[A-Z_]+|FILE_[A-Z_]+|SERVER_[A-Z_]+)$/u.test(normalized)
    ? normalized
    : "UNKNOWN";
}
