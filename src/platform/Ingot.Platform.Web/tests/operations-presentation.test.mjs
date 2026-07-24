import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  edgeHealth,
  latestMetricValue,
  metricScope,
  summarizeRuntime,
} from "../src/presentation/operations.js";

test("edge health separates connectivity from runtime degradation", () => {
  const now = Date.parse("2026-07-23T08:40:00Z");
  const recentEdge = { lastSeen: "2026-07-23T08:39:30Z", lastError: null };

  assert.equal(edgeHealth(recentEdge, { reachable: true, state: "running", tasks: [] }, now), "online");
  assert.equal(edgeHealth(recentEdge, { reachable: true, state: "degraded", tasks: [] }, now), "degraded");
  assert.equal(edgeHealth(recentEdge, { reachable: false, tasks: [] }, now), "offline");
  assert.equal(edgeHealth({ ...recentEdge, lastSeen: "2026-07-23T08:35:00Z" }, null, now), "offline");
});

test("runtime summary reports task coverage and collected samples", () => {
  assert.deepEqual(summarizeRuntime({
    samplesCollected: 1200,
    tasks: [{ state: "running" }, { state: "degraded" }],
  }), {
    totalTasks: 2,
    runningTasks: 1,
    samplesCollected: 1200,
  });
});

test("metric presentation prioritizes business pipeline values over histogram buckets", () => {
  const metrics = {
    event_outbox_backlog: {
      data: [
        { value: 7 },
        { value: 99, labels: { le: "+Inf" } },
      ],
    },
  };

  assert.equal(latestMetricValue(metrics, ["event_outbox_backlog"]), 7);
  assert.equal(metricScope("event_shipped_total"), "ingot");
  assert.equal(metricScope("process_working_set_bytes"), "runtime");
  assert.equal(metricScope("http_requests_received_total"), "http");
});

test("system operations pages expose status-first maintenance workflows", async () => {
  const subscriptions = await readFile(new URL("../src/views/SubscriptionsView.vue", import.meta.url), "utf8");
  const logs = await readFile(new URL("../src/views/LogsView.vue", import.meta.url), "utf8");
  const dataQuality = await readFile(new URL("../src/views/DataQualityView.vue", import.meta.url), "utf8");

  assert.match(subscriptions, /事件订阅状态概览/);
  assert.match(subscriptions, /投递异常/);
  assert.match(subscriptions, /startAfterIngestId/);
  assert.match(subscriptions, /startMode: "new"/);
  assert.match(subscriptions, /<el-drawer/);
  assert.match(logs, /@current-change="onPageChange"/);
  assert.match(logs, /page\.value > 1 && autoRefresh\.value/);
  assert.match(logs, /autoRefresh \? "实时追踪" : "已暂停"/);
  assert.doesNotMatch(logs, /@current-change="applyFilters"/);
  assert.match(dataQuality, /\.quality-view \{[^}]*min-width: 0/);
  assert.match(dataQuality, /\.filter-grid/);
});
