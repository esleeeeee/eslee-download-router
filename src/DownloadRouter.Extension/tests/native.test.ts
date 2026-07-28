import assert from "node:assert/strict";
import test from "node:test";
import { classifyNativeRuntimeError, nativeDiagnosticContext, safeAgentErrorCode } from "../src/native.js";
import { createRequest } from "../src/protocol.js";

void test("native runtime failures are classified without retaining raw messages", () => {
  assert.equal(classifyNativeRuntimeError("Specified native messaging host not found."), "host-not-found");
  assert.equal(classifyNativeRuntimeError("Access to the native messaging host is not permitted."), "host-access-denied");
  assert.equal(classifyNativeRuntimeError("Native host has exited."), "host-exited");
  assert.equal(classifyNativeRuntimeError("C:\\Users\\person\\private.exe?token=secret"), "native-message-failed");
});

void test("only bounded protocol-style agent error codes are logged", () => {
  assert.equal(safeAgentErrorCode("agent.unavailable"), "agent.unavailable");
  assert.equal(safeAgentErrorCode("C:\\Users\\person\\private.exe"), "agent-request-failed");
  assert.equal(safeAgentErrorCode("https://example.test/?token=secret"), "agent-request-failed");
});

void test("native diagnostics contain only command and numeric download ID", () => {
  const safe = nativeDiagnosticContext(createRequest(
    "download.cancelled",
    { downloadId: "123", filePath: "C:\\Users\\person\\private.bin", token: "secret" },
    "request-1",
  ));
  const rejected = nativeDiagnosticContext(createRequest(
    "download.changed",
    { downloadId: "https://example.test/?token=secret" },
    "request-2",
  ));

  assert.equal(safe, "command=download.cancelled downloadId=123");
  assert.equal(rejected, "command=download.changed downloadId=invalid");
  assert.doesNotMatch(safe, /private|secret|Users/u);
});
