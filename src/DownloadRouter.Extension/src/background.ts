import { detectBrowser } from "./browser.js";
import { reportedDownloadState } from "./download-state.js";
import { trustedDownloadFileName } from "./file-name.js";
import { sendNative } from "./native.js";
import { createRequest } from "./protocol.js";
import { sourceMetadata } from "./source-attribution.js";

const browser = detectBrowser(navigator.userAgent);

interface ActiveBrowserDownload {
  jobId: string;
  downloadId: string;
}

chrome.downloads.onCreated.addListener((item) => {
  const metadata = sourceMetadata(item);
  void sendNative(
    createRequest("download.started", {
      browser,
      downloadId: item.id.toString(),
      ...metadata,
    }),
  );
});

chrome.downloads.onChanged.addListener((delta) => {
  const state = delta.state?.current;
  if (!delta.filename && state !== "complete" && state !== "interrupted") {
    return;
  }

  chrome.downloads.search({ id: delta.id }, (items) => {
    const item = items[0];
    if (!item) {
      return;
    }

    if (delta.filename) {
      reportDownloadMetadata(item);
    }

    if (state === "complete" || state === "interrupted") {
      reportChangedDownload(item, state, delta.error?.current ?? item.error ?? null);
    }
  });
});

void reconcileActiveDownloads();

function reportChangedDownload(
  item: chrome.downloads.DownloadItem,
  state: "complete" | "interrupted",
  error: string | null,
): void {
  void sendNative(
    createRequest("download.changed", {
      browser,
      downloadId: item.id.toString(),
      state: reportedDownloadState(state, error),
      filePath: item.filename || null,
      fileName: trustedDownloadFileName(item.filename),
      error,
    }),
  );
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
      if (!item || (item.state !== "complete" && item.state !== "interrupted")) {
        return;
      }

      reportChangedDownload(item, item.state, item.error ?? null);
    });
  }
}
