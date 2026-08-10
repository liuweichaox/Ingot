import assert from "node:assert/strict";
import test from "node:test";
import { edgeHealth, latestMetricValue, summarizeRuntime } from "../src/presentation/operations.js";

test("edge health separates connectivity from runtime degradation", () => {
  const now = Date.parse("2026-07-23T08:40:00Z");
  const recentEdge = { lastSeen: "2026-07-23T08:39:30Z", lastError: null };
  assert.equal(edgeHealth(recentEdge, { reachable: true, state: "running", tasks: [] }, now), "online");
  assert.equal(edgeHealth(recentEdge, { reachable: true, state: "degraded", tasks: [] }, now), "degraded");
  assert.equal(edgeHealth(recentEdge, { reachable: false, tasks: [] }, now), "offline");
});

test("runtime summary reports task coverage and collected samples", () => {
  assert.deepEqual(summarizeRuntime({ samplesCollected: 1200, tasks: [{ state: "running" }, { state: "degraded" }] }), {
    totalTasks: 2,
    runningTasks: 1,
    samplesCollected: 1200,
  });
});

test("metric presentation prioritizes business pipeline values over histogram buckets", () => {
  const metrics = { event_outbox_backlog: { data: [{ value: 7 }, { value: 99, labels: { le: "+Inf" } }] } };
  assert.equal(latestMetricValue(metrics, ["event_outbox_backlog"]), 7);
});
