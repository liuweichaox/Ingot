import { useEffect, useState } from "react";
import { getJson } from "../api/http.js";

const pageSize = 10000;

export function useProcessSamples(executionId) {
  const [state, setState] = useState({ data: [], loading: Boolean(executionId), error: "" });

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    if (!executionId) {
      setState({ data: [], loading: false, error: "" });
      return () => {
        active = false;
        controller.abort();
      };
    }

    setState({ data: [], loading: true, error: "" });
    void loadAllProcessSamples(executionId, controller.signal)
      .then(data => {
        if (active) setState({ data, loading: false, error: "" });
      })
      .catch(error => {
        if (active && error?.name !== "AbortError") {
          setState({ data: [], loading: false, error: error.message });
        }
      });
    return () => {
      active = false;
      controller.abort();
    };
  }, [executionId]);

  return state;
}

export async function loadAllProcessSamples(executionId, signal, request = getJson) {
  const result = [];
  let cursor = null;
  while (true) {
    const query = new URLSearchParams({ limit: String(pageSize) });
    if (cursor) {
      query.set("afterOccurredAt", cursor.occurredAt);
      query.set("afterFrameId", String(cursor.frameId));
    }
    const page = await request(
      `/api/v1/process-executions/${encodeURIComponent(executionId)}/samples?${query}`,
      { signal },
    );
    result.push(...(page?.data || []));
    if (!page?.nextCursor) return result;
    if (cursor?.occurredAt === page.nextCursor.occurredAt &&
        cursor?.frameId === page.nextCursor.frameId) {
      throw new Error("采集帧分页游标没有前进。");
    }
    cursor = page.nextCursor;
  }
}
