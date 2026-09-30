import assert from "node:assert/strict";
import test from "node:test";

type Message = { command: string; payload: Record<string, unknown>; requestId: string };
const flush = async (): Promise<void> => { await new Promise<void>((resolve) => setImmediate(resolve)); };

async function harness(active: unknown[] = [], holdRegistration = false) {
  let created: (item: chrome.downloads.DownloadItem) => void = () => {};
  let changed: (delta: chrome.downloads.DownloadDelta) => void = () => {};
  let searchItem: chrome.downloads.DownloadItem | undefined;
  let release = (): void => {};
  const messages: Message[] = [];
  const previous = Object.getOwnPropertyDescriptor(globalThis, "chrome");
  Object.defineProperty(globalThis, "chrome", { configurable: true, value: {
    runtime: { sendNativeMessage: (_host: string, request: Message, callback: (reply: unknown) => void) => {
      messages.push(request);
      const respond = () => callback({ version: 1, requestId: request.requestId, success: true,
        data: request.command === "downloads.active" ? active : {} });
      if (holdRegistration && request.command === "download.started") release = respond;
      else respond();
    } },
    downloads: {
      onCreated: { addListener: (listener: typeof created) => { created = listener; } },
      onChanged: { addListener: (listener: typeof changed) => { changed = listener; } },
      onErased: { addListener: () => {} },
      search: (_query: { id: number }, callback: (items: chrome.downloads.DownloadItem[]) => void) =>
        callback(searchItem ? [searchItem] : []),
    },
  } });
  return {
    load: async () => { await import(new URL(`../src/background.js?test=${crypto.randomUUID()}`, import.meta.url).href); await flush(); },
    created: (item: chrome.downloads.DownloadItem) => created(item),
    changed: (delta: chrome.downloads.DownloadDelta) => changed(delta),
    setItem: (item: chrome.downloads.DownloadItem) => { searchItem = item; },
    release: () => release(), messages,
    dispose: () => { if (previous) Object.defineProperty(globalThis, "chrome", previous); else Reflect.deleteProperty(globalThis, "chrome"); },
  };
}

function item(startTime: string): chrome.downloads.DownloadItem {
  return { id: 850, startTime, filename: "fixture.bin", url: "https://example.test/fixture.bin",
    state: "in_progress", incognito: false };
}

void test("completion waits for registration and keeps the browser start time", async () => {
  const context = await harness([], true);
  try {
    await context.load();
    const download = item(new Date().toISOString());
    context.created(download);
    context.setItem({ ...download, state: "complete" });
    context.changed({ id: 850, state: { current: "complete" } });
    await flush();
    assert.equal(context.messages.filter((message) => message.command === "download.changed").length, 0);
    context.release();
    await flush();
    const events = context.messages.filter((message) => message.command.startsWith("download."));
    assert.deepEqual(events.map((message) => message.command), ["download.started", "download.changed"]);
    assert.ok(events.every((message) => message.payload.startedAt === download.startTime));
  } finally { context.dispose(); }
});

void test("reused IDs report cancellation separately and immediately for each instance", async () => {
  const context = await harness();
  try {
    await context.load();
    const first = item(new Date(Date.now() - 1000).toISOString());
    const second = item(new Date().toISOString());
    for (const download of [first, second]) {
      context.created(download);
      context.changed({ id: 850, error: { current: "USER_CANCELED" } });
      await flush();
    }
    const cancellations = context.messages.filter((message) => message.command === "download.cancelled");
    assert.deepEqual(cancellations.map((message) => message.payload.startedAt), [first.startTime, second.startTime]);
  } finally { context.dispose(); }
});

void test("reconciliation of a reused ID marks only the old job stale", async () => {
  const context = await harness([{ jobId: "old-job", downloadId: "850", startedAt: "2026-01-01T00:00:00Z" }]);
  try {
    context.setItem({ ...item(new Date().toISOString()), state: "complete" });
    await context.load();
    const events = context.messages.filter((message) => message.command === "download.changed");
    assert.equal(events.length, 1);
    assert.equal(events[0]?.payload.state, "stale");
    assert.equal(events[0]?.payload.jobId, "old-job");
    assert.equal(events[0]?.payload.startedAt, "2026-01-01T00:00:00Z");
  } finally { context.dispose(); }
});
