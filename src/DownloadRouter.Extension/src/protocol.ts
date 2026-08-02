export const protocolVersion = 1;
export const nativeHostName = "com.eslee.download_router";

export type AgentCommandName =
  | "ping"
  | "download.started"
  | "download.metadata"
  | "download.changed"
  | "download.cancelled"
  | "download.interrupted"
  | "downloads.active"
  | "extension.hello";

export interface AgentRequest<TPayload> {
  version: number;
  requestId: string;
  command: AgentCommandName;
  payload: TPayload;
}

export interface AgentResponse<TData = unknown> {
  version: number;
  requestId: string;
  success: boolean;
  errorCode?: string;
  message?: string;
  data?: TData;
}

export function createRequest<TPayload>(
  command: AgentCommandName,
  payload: TPayload,
  requestId: string = crypto.randomUUID(),
): AgentRequest<TPayload> {
  if (requestId.length === 0) {
    throw new Error("requestId is required");
  }

  return { version: protocolVersion, requestId, command, payload };
}
