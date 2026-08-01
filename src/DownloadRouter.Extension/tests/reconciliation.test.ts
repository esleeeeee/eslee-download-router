import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import test from "node:test";

const backgroundSource = readFileSync(
  fileURLToPath(new URL("../../src/background.ts", import.meta.url)),
  "utf8",
);

void test("startup reconciliation only inspects downloads the agent already tracks", () => {
  const reconcile = backgroundSource.slice(
    backgroundSource.indexOf("async function reconcileActiveDownloads"),
  );

  // The agent returns only its own in-progress jobs; the extension must not widen that set.
  assert.match(reconcile, /createRequest\("downloads\.active", \{ browser \}\)/u);
  assert.match(reconcile, /chrome\.downloads\.search\(\{ id \}/u);
});

void test("the extension never scans the whole browser download history", () => {
  const searchCalls = backgroundSource.match(/chrome\.downloads\.search\(([^)]*)/gu) ?? [];

  assert.ok(searchCalls.length > 0);
  for (const call of searchCalls) {
    // Every lookup is keyed by a single download id, so past history is never re-registered.
    assert.match(call, /\{\s*id/u);
  }
  assert.doesNotMatch(backgroundSource, /chrome\.downloads\.search\(\{\}/u);
  assert.doesNotMatch(backgroundSource, /chrome\.downloads\.search\(\{\s*limit/u);
  assert.doesNotMatch(backgroundSource, /chrome\.downloads\.search\(\{\s*orderBy/u);
});

void test("reconciliation is flagged so the agent keeps its own activity timestamps", () => {
  const reconciled = backgroundSource.slice(
    backgroundSource.indexOf("function reportReconciledState"),
    backgroundSource.indexOf("function reportStale"),
  );

  assert.match(reconciled, /isReconciliation: true/u);
  assert.match(reconciled, /reportTerminal\(item, "download\.changed", "complete", null, true\)/u);
});

void test("only download.started creates work, and it is driven by onCreated", () => {
  const startedCalls = backgroundSource.match(/createRequest\("download\.started"/gu) ?? [];
  assert.equal(startedCalls.length, 1);

  const onCreated = backgroundSource.slice(
    backgroundSource.indexOf("chrome.downloads.onCreated.addListener"),
    backgroundSource.indexOf("chrome.downloads.onChanged.addListener"),
  );
  assert.match(onCreated, /createRequest\("download\.started"/u);

  // Reconciliation must not be able to register a job for an untracked download.
  const reconcile = backgroundSource.slice(
    backgroundSource.indexOf("async function reconcileActiveDownloads"),
  );
  assert.doesNotMatch(reconcile, /download\.started/u);
});
