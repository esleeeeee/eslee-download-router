import test from "node:test";
import assert from "node:assert/strict";

import {
  isUserCancelled,
  preferredDownloadError,
  reportedDownloadState,
  safeDownloadError,
} from "../src/download-state.js";

void test("USER_CANCELED is reported as an explicit cancellation", () => {
  assert.equal(reportedDownloadState("interrupted", "USER_CANCELED"), "cancelled");
});

void test("other browser errors remain interrupted", () => {
  assert.equal(reportedDownloadState("interrupted", "NETWORK_FAILED"), "interrupted");
});

void test("browser error priority is delta then search result then last observed error", () => {
  assert.equal(preferredDownloadError("USER_CANCELED", "NETWORK_FAILED", "FILE_FAILED"), "USER_CANCELED");
  assert.equal(preferredDownloadError(null, "NETWORK_FAILED", "FILE_FAILED"), "NETWORK_FAILED");
  assert.equal(preferredDownloadError(undefined, undefined, "FILE_FAILED"), "FILE_FAILED");
  assert.equal(preferredDownloadError(null, null, null), null);
});

void test("USER_CANCELED matching and diagnostics are normalized without raw values", () => {
  assert.equal(isUserCancelled("user_canceled"), true);
  assert.equal(isUserCancelled("USER_SHUTDOWN"), false);
  assert.equal(safeDownloadError("NETWORK_FAILED"), "NETWORK_FAILED");
  assert.equal(safeDownloadError("https://example.test/?token=secret"), "UNKNOWN");
});
