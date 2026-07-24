import { useCallback, useEffect, useRef, useState } from "react";
import { getJson } from "../api/http";

const identity = value => value;

export function useApi(url, { enabled = true, interval = 0, transform = identity } = {}) {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(enabled);
  const [error, setError] = useState("");
  const mounted = useRef(true);
  const dataRef = useRef(data);
  const transformRef = useRef(transform);

  useEffect(() => {
    dataRef.current = data;
    transformRef.current = transform;
  }, [data, transform]);

  const load = useCallback(async () => {
    if (!enabled || !url) return;
    setLoading(current => dataRef.current === null ? true : current);
    try {
      const result = transformRef.current(await getJson(url));
      if (mounted.current) {
        setData(result);
        setError("");
      }
    } catch (requestError) {
      if (mounted.current) setError(requestError.message);
    } finally {
      if (mounted.current) setLoading(false);
    }
  }, [enabled, url]);

  useEffect(() => {
    mounted.current = true;
    void load();
    if (!interval) return () => { mounted.current = false; };
    const timer = window.setInterval(load, interval);
    return () => {
      mounted.current = false;
      window.clearInterval(timer);
    };
  }, [interval, load]);

  return { data, setData, loading, error, reload: load };
}

export function extractRows(payload) {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.data)) return payload.data;
  if (Array.isArray(payload?.items)) return payload.items;
  return [];
}
