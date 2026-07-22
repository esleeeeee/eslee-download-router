import test from "node:test";
import assert from "node:assert/strict";

import { reportedDownloadState } from "../src/download-state.js";

test("USER_CANCELED is reported as an explicit cancellation", () => {
  assert.equal(reportedDownloadState("interrupted", "USER_CANCELED"), "cancelled");
});

test("other browser errors remain interrupted", () => {
  assert.equal(reportedDownloadState("interrupted", "NETWORK_FAILED"), "interrupted");
});
