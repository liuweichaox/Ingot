// 共享 SSE 生命周期：一次性消费与可重连订阅。
import { useEffect, useRef } from "react";
import { streamSse } from "../api/http";

/**
 * 消费单次 SSE 流直到结束或中止。
 * @returns {Promise<number>} 最终 cursor（Last-Event-ID）
 */
export async function consumeSse(url, { signal, onEvent, lastEventId = 0 } = {}) {
  return streamSse(url, { signal, onEvent, lastEventId });
}

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
    void (async () => {
      while (!cancellation.signal.aborted) {
        try {
          cursor = await streamSse(url, {
            signal: cancellation.signal,
            lastEventId: cursor,
            onEvent: async (event) => {
              await onEventRef.current?.(event);
            },
          });
        } catch (error) {
          if (cancellation.signal.aborted || error?.name === "AbortError") return;
          onErrorRef.current?.(error);
        }
        if (cancellation.signal.aborted) return;
        await new Promise((resolve) => window.setTimeout(resolve, reconnectDelayMs));
      }
    })();
    return () => cancellation.abort();
  }, [enabled, url, reconnectDelayMs]);
}
