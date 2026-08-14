import { useCallback, useEffect, useRef, useState } from "react";
import { getJson } from "../api/http";

const identity = value => value;

export function useApi(url, { enabled = true, interval = 0, transform = identity } = {}) {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(enabled);
  const [error, setError] = useState("");
  const mounted = useRef(false);
  const requestIdRef = useRef(0);
  const controllerRef = useRef(null);
  const dataRef = useRef(data);
  const transformRef = useRef(transform);

  useEffect(() => {
    dataRef.current = data;
    transformRef.current = transform;
  }, [data, transform]);

  const load = useCallback(async () => {
    if (!enabled || !url) {
      setLoading(false);
      return;
    }
    const requestId = ++requestIdRef.current;
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    setLoading(current => dataRef.current === null ? true : current);
    try {
      const result = transformRef.current(await getJson(url, { signal: controller.signal }));
      if (mounted.current && requestId === requestIdRef.current) {
        dataRef.current = result;
        setData(result);
        setError("");
      }
    } catch (requestError) {
      if (requestError?.name !== "AbortError" && mounted.current && requestId === requestIdRef.current) {
        setError(requestError.message);
      }
    } finally {
      if (controllerRef.current === controller) controllerRef.current = null;
      if (mounted.current && requestId === requestIdRef.current) setLoading(false);
    }
  }, [enabled, url]);

  useEffect(() => {
    mounted.current = true;
    requestIdRef.current += 1;
    dataRef.current = null;
    setData(null);
    setError("");
    setLoading(Boolean(enabled && url));
    void load();
    const timer = interval && enabled && url ? window.setInterval(load, interval) : null;
    return () => {
      mounted.current = false;
      requestIdRef.current += 1;
      controllerRef.current?.abort();
      controllerRef.current = null;
      if (timer) window.clearInterval(timer);
    };
  }, [enabled, interval, load, url]);

  return { data, setData, loading, error, reload: load };
}

export function extractRows(payload) {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.data)) return payload.data;
  if (Array.isArray(payload?.items)) return payload.items;
  return [];
}
