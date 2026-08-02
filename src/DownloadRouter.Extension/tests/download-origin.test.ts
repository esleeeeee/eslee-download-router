import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import test from "node:test";
import {
  classifyCreatedDownload,
  liveDownloadWindowMs,
  parseStartTime,
  reportableState,
} from "../src/download-origin.js";

const now = Date.parse("2026-08-02T03:08:27.000Z");
const iso = (offsetMs: number): string => new Date(now + offsetMs).toISOString();

void test("a live transfer starting now is tracked", () => {
  const decision = classifyCreatedDownload({ state: "in_progress", startTime: iso(-50) }, now);
  assert.deepEqual(decision, { track: true });
});

void test("browser startup replay of finished history is never tracked", () => {
  for (const state of ["complete", "interrupted"]) {
    const decision = classifyCreatedDownload(
      { state, startTime: "2026-07-26T09:57:00.000Z" },
      now,
    );
    assert.equal(decision.track, false);
    assert.equal(decision.track === false && decision.reason, "not-in-progress");
  }
});

void test("an unfinished history item restored at startup is not tracked", () => {
  // A paused or resumable download keeps its original start time.
  const decision = classifyCreatedDownload(
    { state: "in_progress", startTime: "2026-08-01T03:00:00.000Z" },
    now,
  );
  assert.equal(decision.track, false);
  assert.equal(decision.track === false && decision.reason, "started-before-session");
});

void test("the live window boundary is inclusive and bounded", () => {
  assert.equal(
    classifyCreatedDownload({ state: "in_progress", startTime: iso(-liveDownloadWindowMs) }, now).track,
    true,
  );
  assert.equal(
    classifyCreatedDownload({ state: "in_progress", startTime: iso(-liveDownloadWindowMs - 1) }, now).track,
    false,
  );
});

void test("a missing or malformed start time still allows a live in-progress transfer", () => {
  assert.equal(classifyCreatedDownload({ state: "in_progress" }, now).track, true);
  assert.equal(classifyCreatedDownload({ state: "in_progress", startTime: "" }, now).track, true);
  assert.equal(classifyCreatedDownload({ state: "in_progress", startTime: "nonsense" }, now).track, true);
  assert.equal(parseStartTime("nonsense"), null);
  assert.equal(parseStartTime(undefined), null);
});

void test("an unknown state is treated as not startable", () => {
  assert.equal(classifyCreatedDownload({}, now).track, false);
  assert.equal(classifyCreatedDownload({ state: "unknown" }, now).track, false);
});

void test("only known transfer states are forwarded to the agent", () => {
  assert.equal(reportableState("in_progress"), "in_progress");
  assert.equal(reportableState("complete"), "complete");
  assert.equal(reportableState("interrupted"), "interrupted");
  assert.equal(reportableState("weird"), null);
  assert.equal(reportableState(undefined), null);
});

const backgroundSource = readFileSync(
  fileURLToPath(new URL("../../src/background.ts", import.meta.url)),
  "utf8",
);

void test("onCreated filters replayed history before asking the agent to track anything", () => {
  const onCreated = backgroundSource.slice(
    backgroundSource.indexOf("chrome.downloads.onCreated.addListener"),
    backgroundSource.indexOf("chrome.downloads.onChanged.addListener"),
  );

  const guardIndex = onCreated.indexOf("classifyCreatedDownload(item, Date.now())");
  const sendIndex = onCreated.indexOf('createRequest("download.started"');
  assert.ok(guardIndex >= 0, "onCreated must classify the item");
  assert.ok(sendIndex > guardIndex, "the guard must run before the agent request");
  assert.match(onCreated, /if \(!decision\.track\) \{[\s\S]*?return;/u);
  assert.match(onCreated, /state: reportableState\(item\.state\)/u);
  assert.match(onCreated, /startedAt: item\.startTime \?\? null/u);
});

void test("no other code path can send download.started", () => {
  const startedCalls = backgroundSource.match(/createRequest\("download\.started"/gu) ?? [];
  assert.equal(startedCalls.length, 1);

  const reconcile = backgroundSource.slice(
    backgroundSource.indexOf("async function reconcileActiveDownloads"),
  );
  assert.doesNotMatch(reconcile, /download\.started/u);
});
