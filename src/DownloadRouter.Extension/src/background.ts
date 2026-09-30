import { detectBrowser } from "./browser.js";
import {
  classifyCreatedDownload,
  extensionBuild,
  parseStartTime,
  reportableState,
} from "./download-origin.js";
import {
  isUserCancelled,
  preferredDownloadError,
  safeDownloadError,
} from "./download-state.js";
import { trustedDownloadFileName } from "./file-name.js";
import { sendNative } from "./native.js";
import { createRequest, type AgentCommandName, type AgentRequest } from "./protocol.js";
import { sourceMetadata } from "./source-attribution.js";

const browser = detectBrowser(navigator.userAgent);
const lastErrors = new Map<number, string>();
const reportedTerminalStates = new Map<number, "cancelled" | "interrupted" | "complete">();
const startedTimes = new Map<number, string>();
const pendingSends = new Map<number, Promise<void>>();

interface ActiveBrowserDownload {
  jobId: string;
  downloadId: string;
  startedAt?: string | null;
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
  rememberIdentity(item);
  const metadata = sourceMetadata(item);
  sendDownload(item.id,
    createRequest("download.started", {
      browser,
      downloadId: item.id.toString(),
      ...metadata,
      state: reportableState(item.state),
      startedAt: instanceStart(item),
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
    if (startedTimes.has(delta.id)) {
      reportTerminalWithoutItem(delta.id, "download.cancelled", "cancelled", deltaError);
    } else {
      chrome.downloads.search({ id: delta.id }, (items) => {
        if (items[0]) rememberIdentity(items[0]);
        reportTerminalWithoutItem(delta.id, "download.cancelled", "cancelled", deltaError);
      });
    }
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
    rememberIdentity(item);

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
  startedTimes.delete(downloadId);
});

void announceExtensionBuild();
void reconcileActiveDownloads();

/**
 * Tells the agent which extension build is running so the app can warn about a browser
 * that is still serving a cached older service worker, before any download is attempted.
 */
async function announceExtensionBuild(): Promise<void> {
  const response = await sendNative<{ browser: string; extensionBuild: string }, { supported?: boolean }>(
    createRequest("extension.hello", { browser, extensionBuild }),
  );
  if (response?.success && response.data?.supported === false) {
    console.warn(
      "[Download Router] This extension build is not supported by the installed app."
        + " Refresh the extension and restart the browser.",
    );
  }
}

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

  sendDownload(item.id,
    createRequest(command, {
      browser,
      downloadId: item.id.toString(),
      state,
      filePath: item.filename || null,
      fileName: trustedDownloadFileName(item.filename),
      error,
      isReconciliation,
      startedAt: instanceStart(item),
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

  sendDownload(downloadId, createRequest(command, {
    browser,
    downloadId: downloadId.toString(),
    state,
    filePath: null,
    fileName: null,
    error,
    startedAt: startedTimes.get(downloadId) ?? null,
  }));
}

function reportDownloadMetadata(item: chrome.downloads.DownloadItem): void {
  sendDownload(item.id,
    createRequest("download.metadata", {
      browser,
      downloadId: item.id.toString(),
      filePath: item.filename || null,
      fileName: trustedDownloadFileName(item.filename),
      startedAt: instanceStart(item),
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

  sendDownload(item.id, createRequest("download.changed", {
    browser,
    downloadId: item.id.toString(),
    state: "in_progress",
    filePath: null,
    fileName: null,
    error: null,
    isReconciliation: true,
    startedAt: instanceStart(item),
  }));
}

function reportStale(downloadId: number, jobId?: string, startedAt?: string | null): void {
  sendDownload(downloadId, createRequest("download.changed", {
    browser,
    downloadId: downloadId.toString(),
    state: "stale",
    filePath: null,
    fileName: null,
    error: null,
    isReconciliation: true,
    jobId,
    startedAt: startedAt ?? startedTimes.get(downloadId) ?? null,
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
      if (!item || !tracked.startedAt || !instanceStart(item)
          || parseStartTime(tracked.startedAt) !== parseStartTime(item.startTime)) {
        reportStale(id, tracked.jobId, tracked.startedAt);
        return;
      }

      rememberIdentity(item);
      reportReconciledState(item);
    });
  }
}

function instanceStart(item: chrome.downloads.DownloadItem): string | null {
  const value = parseStartTime(item.startTime);
  return value === null ? null : new Date(value).toISOString();
}

function rememberIdentity(item: chrome.downloads.DownloadItem): void {
  const startedAt = instanceStart(item);
  if (startedAt === null) return;
  if (startedTimes.get(item.id) !== startedAt) {
    reportedTerminalStates.delete(item.id);
    lastErrors.delete(item.id);
  }
  startedTimes.set(item.id, startedAt);
}

// onChanged can arrive before native registration completes. Preserve per-ID order.
function sendDownload<TPayload>(id: number, request: AgentRequest<TPayload>): void {
  const previous = pendingSends.get(id) ?? Promise.resolve();
  const next = previous.then(async () => { await sendNative(request); });
  pendingSends.set(id, next);
  void next.finally(() => {
    if (pendingSends.get(id) === next) pendingSends.delete(id);
  });
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
