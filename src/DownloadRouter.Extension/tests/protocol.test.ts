import assert from "node:assert/strict";
import test from "node:test";
import { createRequest, protocolVersion } from "../src/protocol.js";

void test("createRequest uses the fixed protocol and provided request ID", () => {
  const request = createRequest("ping", {}, "00000000-0000-0000-0000-000000000001");
  assert.equal(request.version, protocolVersion);
  assert.equal(request.command, "ping");
  assert.equal(request.requestId, "00000000-0000-0000-0000-000000000001");
});

void test("createRequest rejects an empty request ID", () => {
  assert.throws(() => createRequest("ping", {}, ""), /requestId/u);
});
