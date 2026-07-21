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
        resolve(null);
        return;
      }

      resolve(isAgentResponse(response) ? response : null);
    });
  });
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
