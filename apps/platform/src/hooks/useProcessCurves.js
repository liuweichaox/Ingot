import { useEffect, useMemo, useState } from "react";
import { getJson } from "../api/http.js";

export function useProcessCurves(executionId, signalCodes, { enabled = true, maxPoints = 2000, siteId = "" } = {}) {
  const signalKey = useMemo(() => (signalCodes || []).join(","), [signalCodes]);
  const [state, setState] = useState({ data: null, loading: Boolean(enabled && executionId && signalKey), error: "" });

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    if (!enabled || !executionId || !signalKey) {
      setState({ data: null, loading: false, error: "" });
      return () => {
        active = false;
        controller.abort();
      };
    }

    setState({ data: null, loading: true, error: "" });
    const timer = window.setTimeout(() => {
      void loadProcessCurves(executionId, signalKey.split(","), maxPoints, controller.signal, getJson, siteId)
        .then(data => {
          if (active) setState({ data, loading: false, error: "" });
        })
        .catch(error => {
          if (active && error?.name !== "AbortError") {
            setState({ data: null, loading: false, error: error.message });
          }
        });
    }, 150);
    return () => {
      active = false;
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [enabled, executionId, maxPoints, signalKey, siteId]);

  return state;
}

export function loadProcessCurves(executionId, signalCodes, maxPoints = 2000, signal, request = getJson, siteId = "") {
  const query = new URLSearchParams({
    signalCodes: signalCodes.join(","),
    maxPoints: String(maxPoints),
    siteId,
  });
  return request(
    `/api/v1/process-executions/${encodeURIComponent(executionId)}/curves?${query}`,
    { signal },
  );
}
