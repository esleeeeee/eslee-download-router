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
