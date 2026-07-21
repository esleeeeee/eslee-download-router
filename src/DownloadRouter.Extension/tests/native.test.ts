import assert from "node:assert/strict";
import test from "node:test";
import { classifyNativeRuntimeError, safeAgentErrorCode } from "../src/native.js";

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
