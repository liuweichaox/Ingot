// 封装带统一认证、错误解析和二进制下载能力的平台 HTTP 请求。

function resolveUrl(url) {
  return url;
}

const authTokenKey = "ingot.auth.token";

export function getAuthToken() {
  return sessionStorage.getItem(authTokenKey);
}

export function setAuthToken(token) {
  if (token) sessionStorage.setItem(authTokenKey, token);
  else sessionStorage.removeItem(authTokenKey);
}

function authenticatedHeaders(headers = {}) {
  const token = getAuthToken();
  return { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers };
}

function notifyUnauthorized(response) {
  if (response.status === 401) window.dispatchEvent(new Event("ingot:unauthorized"));
}

async function rawRequest(url, options = {}) {
  let res;
  try {
    res = await fetch(resolveUrl(url), options);
  } catch (error) {
    throw platformRequestError(error);
  }
  if (!res.ok) {
    notifyUnauthorized(res);
    const text = await res.text().catch(() => "");
    throw responseError(res, text);
  }
  return res;
}

async function jsonRequest(url, options = {}) {
  const headers = authenticatedHeaders({ Accept: "application/json", ...(options.headers || {}) });
  if (options.body && !(options.body instanceof FormData) && !headers["Content-Type"]) headers["Content-Type"] = "application/json";
  const res = await rawRequest(url, { ...options, headers });
  if (res.status === 204) return null;
  return await res.json();
}

function responseError(res, text) {
  const detail = parseErrorDetail(text);
  if (res.status >= 500 && !detail) {
    return Object.assign(new Error("平台服务暂不可用，请稍后重试或联系管理员。"), { status: res.status });
  }
  return Object.assign(
    new Error(detail || `操作未完成，请稍后重试（状态 ${res.status}）。`),
    { status: res.status },
  );
}

function platformRequestError(error) {
  if (error?.name === "AbortError") return error;
  return new Error("暂时无法连接平台服务，请稍后重试。", { cause: error });
}

function parseErrorDetail(text) {
  const value = text?.trim();
  if (!value) return "";
  try {
    const payload = JSON.parse(value);
    return payload.detail || payload.title || payload.message || value;
  } catch {
    return value;
  }
}

export function getJson(url, options = {}) {
  return jsonRequest(url, options);
}

export function postJson(url, body, options = {}) {
  return jsonRequest(url, { ...options, method: "POST", body: JSON.stringify(body) });
}

export function patchJson(url, body, options = {}) {
  return jsonRequest(url, { ...options, method: "PATCH", body: JSON.stringify(body) });
}

export function putJson(url, body, options = {}) {
  return jsonRequest(url, { ...options, method: "PUT", body: JSON.stringify(body) });
}

export function deleteJson(url, options = {}) {
  return jsonRequest(url, { ...options, method: "DELETE" });
}

export function postForm(url, formData, options = {}) {
  return jsonRequest(url, { ...options, method: "POST", body: formData });
}

export async function downloadFile(url, fallbackName = "download") {
  const res = await rawRequest(url, { headers: authenticatedHeaders({ Accept: "text/csv" }) });
  const disposition = res.headers.get("content-disposition") || "";
  const encoded = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  const plain = disposition.match(/filename="?([^";]+)"?/i)?.[1];
  const name = encoded ? decodeURIComponent(encoded) : plain || fallbackName;
  const objectUrl = URL.createObjectURL(await res.blob());
  try {
    const anchor = document.createElement("a");
    anchor.href = objectUrl;
    anchor.download = name;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

export async function getBlob(url) {
  const headers = authenticatedHeaders({ Accept: "image/*,application/pdf" });
  const res = await rawRequest(url, { headers });
  return await res.blob();
}

export async function streamSse(url, { headers = {}, signal, onEvent, lastEventId = 0 }) {
  const res = await fetch(resolveUrl(url), {
    headers: authenticatedHeaders({
      Accept: "text/event-stream",
      ...(lastEventId ? { "Last-Event-ID": String(lastEventId) } : {}),
      ...headers,
    }),
    signal,
  });
  if (!res.ok || !res.body) {
    notifyUnauthorized(res);
    throw new Error(`SSE HTTP ${res.status} ${res.statusText}`);
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let cursor = lastEventId;
  while (true) {
    const { value, done } = await reader.read();
    buffer += decoder.decode(value || new Uint8Array(), { stream: !done });
    const blocks = buffer.split(/\r?\n\r?\n/);
    buffer = blocks.pop() || "";
    for (const block of blocks) {
      const lines = block.split(/\r?\n/);
      const type = lines.find((line) => line.startsWith("event:"))?.slice(6).trim() || "message";
      const id = Number(lines.find((line) => line.startsWith("id:"))?.slice(3).trim() || cursor);
      const data = lines.filter((line) => line.startsWith("data:"))
        .map((line) => line.slice(5).trimStart()).join("\n");
      if (Number.isFinite(id)) cursor = id;
      if (data) await onEvent({ id: cursor, type, data: JSON.parse(data) });
    }
    if (done) return cursor;
  }
}
