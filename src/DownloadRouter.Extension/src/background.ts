import { detectBrowser } from "./browser.js";
import { sendNative } from "./native.js";
import { createRequest } from "./protocol.js";
import { sourceMetadata } from "./source-attribution.js";

const browser = detectBrowser(navigator.userAgent);

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
  if (state !== "complete" && state !== "interrupted") {
    return;
  }

  chrome.downloads.search({ id: delta.id }, (items) => {
    const item = items[0];
    if (!item) {
      return;
    }

    void sendNative(
      createRequest("download.changed", {
        browser,
        downloadId: delta.id.toString(),
        state,
        filePath: item.filename || null,
        error: delta.error?.current ?? item.error ?? null,
      }),
    );
  });
});
