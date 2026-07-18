export function resolveUrl(url) {
  return url;
}

async function jsonRequest(url, options = {}) {
  const headers = { Accept: "application/json", ...(options.headers || {}) };
  if (options.body && !headers["Content-Type"]) headers["Content-Type"] = "application/json";
  const res = await fetch(resolveUrl(url), { ...options, headers });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`HTTP ${res.status} ${res.statusText}${text ? `: ${text}` : ""}`);
  }
  return await res.json();
}

export function getJson(url, options = {}) {
  return jsonRequest(url, options);
}

export function postJson(url, body, options = {}) {
  return jsonRequest(url, { ...options, method: "POST", body: JSON.stringify(body) });
}

export async function getBlob(url, options = {}) {
  const response = await fetch(resolveUrl(url), options);
  if (!response.ok) throw new Error(`HTTP ${response.status} ${response.statusText}`);
  return response.blob();
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
