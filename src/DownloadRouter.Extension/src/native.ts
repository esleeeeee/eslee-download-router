import {
  nativeHostName,
  type AgentRequest,
  type AgentResponse,
} from "./protocol.js";

export function sendNative<TPayload, TData = unknown>(
  request: AgentRequest<TPayload>,
): Promise<AgentResponse<TData> | null> {
  const context = nativeDiagnosticContext(request);
  return new Promise((resolve) => {
    chrome.runtime.sendNativeMessage(nativeHostName, request, (response: unknown) => {
      if (chrome.runtime.lastError) {
        // Fail open: browser downloads must never depend on the local host.
        reportNativeFailure(context, classifyNativeRuntimeError(chrome.runtime.lastError.message));
        resolve(null);
        return;
      }

      if (!isAgentResponse(response)) {
        reportNativeFailure(context, "invalid-response");
        resolve(null);
        return;
      }

      if (!response.success) {
        reportNativeFailure(context, safeAgentErrorCode(response.errorCode));
      } else {
        console.debug(`[Download Router] native-send ${context} result=success`);
      }

      resolve(response as AgentResponse<TData>);
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

function reportNativeFailure(context: string, code: string): void {
  console.warn(`[Download Router] native-send ${context} result=failure code=${code}; browser download unchanged.`);
}

export function nativeDiagnosticContext<TPayload>(request: AgentRequest<TPayload>): string {
  const payload = request.payload as { downloadId?: unknown };
  const rawId = typeof payload.downloadId === "string" ? payload.downloadId : "none";
  const downloadId = /^[0-9]{1,20}$/u.test(rawId) ? rawId : "invalid";
  return `command=${request.command} downloadId=${downloadId}`;
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
