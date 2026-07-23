function resolveUrl(url) {
  return url;
}

async function jsonRequest(url, options = {}) {
  const headers = { Accept: "application/json", ...(options.headers || {}) };
  if (options.body && !headers["Content-Type"]) headers["Content-Type"] = "application/json";
  let res;
  try {
    res = await fetch(resolveUrl(url), { ...options, headers });
  } catch (error) {
    throw platformRequestError(error);
  }
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw responseError(res, text);
  }
  return await res.json();
}

function responseError(res, text) {
  const detail = parseErrorDetail(text);
  if (res.status >= 500 && !detail) {
    return new Error("Platform API 暂不可用，请确认后端服务和 PostgreSQL/TimescaleDB 已启动，然后重试。");
  }
  return new Error(detail || `请求失败（HTTP ${res.status} ${res.statusText}）`);
}

function platformRequestError(error) {
  if (error?.name === "AbortError") return error;
  return new Error("无法连接 Platform API，请确认后端服务已在端口 8000 启动。", { cause: error });
}

function parseErrorDetail(text) {
  const value = text?.trim();
  if (!value) return "";
  try {
    const payload = JSON.parse(value);
    return payload.error || payload.message || value;
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

export async function putJson(url, body, options = {}) {
  const headers = { Accept: "application/json", "Content-Type": "application/json", ...(options.headers || {}) };
  let res;
  try {
    res = await fetch(resolveUrl(url), { ...options, method: "PUT", headers, body: JSON.stringify(body) });
  } catch (error) {
    throw platformRequestError(error);
  }
  if (!res.ok) {
    const detail = await res.text().catch(() => "");
    throw responseError(res, detail);
  }
  if (res.status === 204) return null;
  return await res.json();
}

export async function deleteJson(url, options = {}) {
  const headers = { Accept: "application/json", ...(options.headers || {}) };
  let res;
  try {
    res = await fetch(resolveUrl(url), { ...options, method: "DELETE", headers });
  } catch (error) {
    throw platformRequestError(error);
  }
  if (!res.ok) {
    const detail = await res.text().catch(() => "");
    throw responseError(res, detail);
  }
  if (res.status === 204) return null;
  return await res.json();
}

export async function postForm(url, formData, options = {}) {
  const headers = { Accept: "application/json", ...(options.headers || {}) };
  let res;
  try {
    res = await fetch(resolveUrl(url), { ...options, method: "POST", headers, body: formData });
  } catch (error) {
    throw platformRequestError(error);
  }
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw responseError(res, text);
  }
  return await res.json();
}

export async function streamSse(url, { headers = {}, signal, onEvent, lastEventId = 0 }) {
  const res = await fetch(resolveUrl(url), {
    headers: {
      Accept: "text/event-stream",
      ...(lastEventId ? { "Last-Event-ID": String(lastEventId) } : {}),
      ...headers,
    },
    signal,
  });
  if (!res.ok || !res.body) throw new Error(`SSE HTTP ${res.status} ${res.statusText}`);

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
