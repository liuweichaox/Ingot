import assert from "node:assert/strict";
import test from "node:test";
import { loadAllProcessSamples } from "../src/hooks/useProcessSamples.js";

test("process sample loader follows the frame cursor until every page is loaded", async () => {
  const urls = [];
  const pages = [
    {
      data: [{ frameId: 101, occurredAt: "2026-08-17T10:00:00Z", values: { temperature: 601 } }],
      nextCursor: { occurredAt: "2026-08-17T10:00:00Z", frameId: 101 },
    },
    {
      data: [{ frameId: 102, occurredAt: "2026-08-17T10:00:01Z", values: { temperature: 602 } }],
      nextCursor: null,
    },
  ];
  const request = async url => {
    urls.push(url);
    return pages.shift();
  };

  const result = await loadAllProcessSamples("execution/01", undefined, request);

  assert.deepEqual(result.map(frame => frame.frameId), [101, 102]);
  assert.match(urls[0], /process-executions\/execution%2F01\/samples\?limit=10000/);
  assert.match(urls[1], /afterOccurredAt=2026-08-17T10%3A00%3A00Z/);
  assert.match(urls[1], /afterFrameId=101/);
});

test("process sample loader rejects a cursor that does not advance", async () => {
  const repeated = {
    data: [{ frameId: 101, occurredAt: "2026-08-17T10:00:00Z", values: {} }],
    nextCursor: { occurredAt: "2026-08-17T10:00:00Z", frameId: 101 },
  };

  await assert.rejects(
    loadAllProcessSamples("execution-01", undefined, async () => repeated),
    /游标没有前进/,
  );
});
