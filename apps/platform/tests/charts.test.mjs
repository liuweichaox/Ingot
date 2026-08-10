import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  extractProcessSamples,
  processSignalTraces,
  qualityOutcomeTraces,
} from "../src/charts/chartAdapters.js";

test("quality charts retain measured outcomes", () => {
  const outcomes = qualityOutcomeTraces([{ name: "系列 A", pass: 7, fail: 2, inconclusive: 1 }]);
  assert.deepEqual(outcomes.map(trace => trace.y[0]), [7, 2, 1]);
});

test("process traces preserve elapsed time, phase context, and baseline emphasis", () => {
  const samples = extractProcessSamples([
    { event: { eventType: "process.sample", occurredAt: "2026-07-23T08:00:00Z", context: { phase: "加热" }, data: { values: { temperature: 500 } } } },
    { event: { eventType: "process.sample", occurredAt: "2026-07-23T08:00:01Z", context: { phase: "保压" }, data: { values: { temperature: 505 } } } },
  ]);
  const traces = processSignalTraces([{ correlationId: "cycle-1", machineId: "PRESS-01", startedAt: "2026-07-23T08:00:00Z", isBaseline: true }], { "cycle-1": samples }, "temperature");
  assert.deepEqual(traces[0].x, [0, 1]);
  assert.deepEqual(traces[0].customdata[1], ["2026-07-23T08:00:01Z", "保压"]);
  assert.equal(traces[0].line.width, 3);
});

test("React Plotly renderer is responsive, lazy, and used by quality analysis", async () => {
  const component = await readFile(new URL("../src/components/PlotlyChart.jsx", import.meta.url), "utf8");
  const pages = await readFile(new URL("../src/pages/index.jsx", import.meta.url), "utf8");
  assert.match(component, /import\("plotly\.js-basic-dist-min"\)/);
  assert.match(component, /plotly\.react/);
  assert.match(component, /responsive: true/);
  assert.match(component, /ResizeObserver/);
  assert.match(pages, /<PlotlyChart/);
  assert.match(pages, /qualityOutcomeTraces/);
});
