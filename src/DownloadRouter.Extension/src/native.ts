import {
  nativeHostName,
  type AgentRequest,
  type AgentResponse,
} from "./protocol.js";

export function sendNative<TPayload>(request: AgentRequest<TPayload>): Promise<AgentResponse | null> {
  return new Promise((resolve) => {
    chrome.runtime.sendNativeMessage(nativeHostName, request, (response: unknown) => {
      if (chrome.runtime.lastError) {
        // Fail open: browser downloads must never depend on the local host.
        reportNativeFailure(classifyNativeRuntimeError(chrome.runtime.lastError.message));
        resolve(null);
        return;
      }

      if (!isAgentResponse(response)) {
        reportNativeFailure("invalid-response");
        resolve(null);
        return;
      }

      if (!response.success) {
        reportNativeFailure(safeAgentErrorCode(response.errorCode));
      }

      resolve(response);
    });
  });
}

export function classifyNativeRuntimeError(message: string | undefined): string {
  const normalized = message?.toLowerCase() ?? "";
  if (normalized.includes("not found")) {
    return "host-not-found";
  }

  if (normalized.includes("not permitted") || normalized.includes("forbidden") || normalized.includes("access")) {
    return "host-access-denied";
  }

  if (normalized.includes("exited") || normalized.includes("closed")) {
    return "host-exited";
  }

  return "native-message-failed";
}

export function safeAgentErrorCode(errorCode: string | undefined): string {
  if (errorCode && errorCode.length <= 80 && /^[a-z0-9]+(?:[.-][a-z0-9]+)*$/u.test(errorCode)) {
    return errorCode;
  }

  return "agent-request-failed";
}

function reportNativeFailure(code: string): void {
  console.warn(`[Download Router] Native host communication failed (${code}); the browser download remains unchanged.`);
}

function isAgentResponse(value: unknown): value is AgentResponse {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Partial<AgentResponse>;
  return (
    typeof candidate.version === "number" &&
    typeof candidate.requestId === "string" &&
    typeof candidate.success === "boolean"
  );
}
