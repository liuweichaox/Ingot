// 共享可重连 SSE 订阅生命周期。
import { useEffect, useRef } from "react";
import { streamSse } from "../api/http";

/**
 * 在 enabled 时保持 SSE 连接；断线后自动重连并保留 cursor。
 * initialLastEventId 仅在 effect 因 enabled/url 重启时作为起点读取。
 */
export function useSseReconnect(url, {
  enabled = true,
  initialLastEventId = 0,
  onEvent,
  onError,
  reconnectDelayMs = 1000,
} = {}) {
  const onEventRef = useRef(onEvent);
  const onErrorRef = useRef(onError);
  const initialCursorRef = useRef(initialLastEventId);
  onEventRef.current = onEvent;
  onErrorRef.current = onError;
  initialCursorRef.current = initialLastEventId;

  useEffect(() => {
    if (!enabled || !url) return undefined;
    const cancellation = new AbortController();
    let cursor = initialCursorRef.current;
    let reconnectTimer = 0;
    void (async () => {
      while (!cancellation.signal.aborted) {
        try {
          cursor = await streamSse(url, {
            signal: cancellation.signal,
            lastEventId: cursor,
            onEvent: async (event) => {
              if (Number.isFinite(event?.id)) cursor = Math.max(cursor, event.id);
              await onEventRef.current?.(event);
            },
          });
        } catch (error) {
          if (cancellation.signal.aborted || error?.name === "AbortError") return;
          onErrorRef.current?.(error);
        }
        if (cancellation.signal.aborted) return;
        await new Promise((resolve) => {
          reconnectTimer = window.setTimeout(resolve, reconnectDelayMs);
        });
      }
    })();
    return () => {
      cancellation.abort();
      if (reconnectTimer) window.clearTimeout(reconnectTimer);
    };
  }, [enabled, url, reconnectDelayMs]);
}
