import { invoke } from "@tauri-apps/api/core";
import { save } from "@tauri-apps/plugin-dialog";
import type { ApiConnection, ApiResponse, StreamEnvelope } from "../types";

interface NativeApiRequest {
  centralUrl: string;
  actor: string;
  token: string;
  method: "GET" | "POST";
  path: string;
  body?: unknown;
}

export async function apiRequest<T>(
  connection: ApiConnection,
  method: "GET" | "POST",
  path: string,
  body?: unknown
): Promise<T> {
  const response = await invoke<ApiResponse<T>>("api_request", {
    request: { ...connection, method, path, body } satisfies NativeApiRequest
  });
  if (response.status < 200 || response.status >= 300) {
    const payload = response.body as { error?: string } | string | undefined;
    const message = typeof payload === "string" ? payload : payload?.error;
    throw new Error(message || `Central API 返回 HTTP ${response.status}`);
  }
  return response.body;
}

export async function startAgentStream(
  connection: ApiConnection,
  runId: string,
  afterSequence = 0
): Promise<void> {
  await invoke("stream_agent_run", {
    connection,
    runId,
    afterSequence
  });
}

export async function downloadVerifiedPackage(
  connection: ApiConnection,
  workspaceId: string,
  suggestedName: string,
  expectedSha256: string
): Promise<string | null> {
  const targetPath = await save({
    title: "保存已校验的连接器包",
    defaultPath: suggestedName,
    filters: [{ name: "ZIP archive", extensions: ["zip"] }]
  });
  if (!targetPath) return null;

  return invoke<string>("download_package", {
    connection,
    workspaceId,
    targetPath,
    expectedSha256
  });
}

export type { StreamEnvelope };

