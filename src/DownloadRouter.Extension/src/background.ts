import { detectBrowser } from "./browser.js";
import {
  classifyCreatedDownload,
  extensionBuild,
  reportableState,
} from "./download-origin.js";
import {
  isUserCancelled,
  preferredDownloadError,
  safeDownloadError,
} from "./download-state.js";
import { trustedDownloadFileName } from "./file-name.js";
import { sendNative } from "./native.js";
import { createRequest, type AgentCommandName } from "./protocol.js";
import { sourceMetadata } from "./source-attribution.js";

const browser = detectBrowser(navigator.userAgent);
const lastErrors = new Map<number, string>();
const reportedTerminalStates = new Map<number, "cancelled" | "interrupted" | "complete">();

interface ActiveBrowserDownload {
  jobId: string;
  downloadId: string;
}

chrome.downloads.onCreated.addListener((item) => {
  // Browser startup replays onCreated for the whole download history. Registering those
  // would recreate jobs for files the user already handled, so only live transfers pass.
  const decision = classifyCreatedDownload(item, Date.now());
  if (!decision.track) {
    console.debug(
      `[Download Router] onCreated ignored downloadId=${safeDownloadId(item.id)} reason=${decision.reason}`,
    );
    return;
  }

  lastErrors.delete(item.id);
  reportedTerminalStates.delete(item.id);
  const metadata = sourceMetadata(item);
  void sendNative(
    createRequest("download.started", {
      browser,
      downloadId: item.id.toString(),
      ...metadata,
      state: reportableState(item.state),
      startedAt: item.startTime ?? null,
      extensionBuild,
    }),
  );
});

chrome.downloads.onChanged.addListener((delta) => {
  const state = delta.state?.current;
  const deltaError = delta.error?.current ?? null;
  if (deltaError) {
    lastErrors.set(delta.id, deltaError);
  }

  console.debug(
    `[Download Router] onChanged downloadId=${safeDownloadId(delta.id)} state=${safeDownloadState(state)} error=${safeDownloadError(deltaError)}`,
  );

  if (isUserCancelled(deltaError)) {
    reportTerminalWithoutItem(delta.id, "download.cancelled", "cancelled", deltaError);
    return;
  }

  if (!delta.filename && state !== "complete" && state !== "interrupted") {
    return;
  }

  chrome.downloads.search({ id: delta.id }, (items) => {
    const item = items[0];
    if (!item) {
      if (state === "interrupted") {
        reportStale(delta.id);
      }
      return;
    }

    if (delta.filename) {
      reportDownloadMetadata(item);
    }

    if (state === "complete") {
      reportTerminal(item, "download.changed", "complete", null);
      return;
    }

    if (state === "interrupted") {
      const error = preferredDownloadError(deltaError, item.error, lastErrors.get(delta.id));
      reportTerminal(
        item,
        isUserCancelled(error) ? "download.cancelled" : "download.interrupted",
        isUserCancelled(error) ? "cancelled" : "interrupted",
        error,
      );
    }
  });
});

chrome.downloads.onErased.addListener((downloadId) => {
  console.debug(`[Download Router] onErased downloadId=${safeDownloadId(downloadId)} event=history-erased`);
  lastErrors.delete(downloadId);
  reportedTerminalStates.delete(downloadId);
});

void reconcileActiveDownloads();

function reportTerminal(
  item: chrome.downloads.DownloadItem,
  command: AgentCommandName,
  state: "complete" | "interrupted" | "cancelled",
  error: string | null,
  isReconciliation = false,
): void {
  if (!markTerminalReported(item.id, state)) {
    return;
  }

  void sendNative(
    createRequest(command, {
      browser,
      downloadId: item.id.toString(),
      state,
      filePath: item.filename || null,
      fileName: trustedDownloadFileName(item.filename),
      error,
      isReconciliation,
    }),
  );
}

function reportTerminalWithoutItem(
  downloadId: number,
  command: AgentCommandName,
  state: "cancelled" | "interrupted",
  error: string | null,
): void {
  if (!markTerminalReported(downloadId, state)) {
    return;
  }

  void sendNative(createRequest(command, {
    browser,
    downloadId: downloadId.toString(),
    state,
    filePath: null,
    fileName: null,
    error,
  }));
}

function reportDownloadMetadata(item: chrome.downloads.DownloadItem): void {
  void sendNative(
    createRequest("download.metadata", {
      browser,
      downloadId: item.id.toString(),
      filePath: item.filename || null,
      fileName: trustedDownloadFileName(item.filename),
    }),
  );
}

function reportReconciledState(item: chrome.downloads.DownloadItem): void {
  if (item.state === "complete") {
    reportTerminal(item, "download.changed", "complete", null, true);
    return;
  }

  if (item.state === "interrupted") {
    const error = preferredDownloadError(null, item.error, lastErrors.get(item.id));
    reportTerminal(
      item,
      isUserCancelled(error) ? "download.cancelled" : "download.interrupted",
      isUserCancelled(error) ? "cancelled" : "interrupted",
      error,
      true,
    );
    return;
  }

  void sendNative(createRequest("download.changed", {
    browser,
    downloadId: item.id.toString(),
    state: "in_progress",
    filePath: null,
    fileName: null,
    error: null,
    isReconciliation: true,
  }));
}

function reportStale(downloadId: number): void {
  void sendNative(createRequest("download.changed", {
    browser,
    downloadId: downloadId.toString(),
    state: "stale",
    filePath: null,
    fileName: null,
    error: null,
    isReconciliation: true,
  }));
}

async function reconcileActiveDownloads(): Promise<void> {
  const response = await sendNative<{ browser: string }, ActiveBrowserDownload[]>(
    createRequest("downloads.active", { browser }),
  );
  if (!response?.success || !Array.isArray(response.data)) {
    return;
  }

  for (const tracked of response.data) {
    const id = Number.parseInt(tracked.downloadId, 10);
    if (!Number.isSafeInteger(id) || id < 0) {
      continue;
    }

    chrome.downloads.search({ id }, (items) => {
      const item = items[0];
      if (!item) {
        reportStale(id);
        return;
      }

      reportReconciledState(item);
    });
  }
}

function markTerminalReported(id: number, state: "cancelled" | "interrupted" | "complete"): boolean {
  if (reportedTerminalStates.get(id) === state) {
    return false;
  }

  reportedTerminalStates.set(id, state);
  return true;
}

function safeDownloadId(id: number): string {
  return Number.isSafeInteger(id) && id >= 0 ? id.toString() : "invalid";
}

function safeDownloadState(state: string | undefined): string {
  return state === "in_progress" || state === "complete" || state === "interrupted" ? state : "none";
}
