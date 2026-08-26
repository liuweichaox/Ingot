// 验证前端 charts 的渲染、交互、错误和边界状态。

import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";
import {
  processCurveTraces,
  qualityOutcomeTraces,
} from "../src/charts/chartAdapters.js";

test("quality charts retain measured outcomes", () => {
  const outcomes = qualityOutcomeTraces([{ name: "系列 A", pass: 7, fail: 2, inconclusive: 1 }]);
  assert.deepEqual(outcomes.map(trace => trace.y[0]), [7, 2, 1]);
});

test("process traces preserve elapsed time, phase context, and signal metadata", () => {
  const series = [{
    signalCode: "temperature",
    points: [
      { frameId: 1, occurredAt: "2026-07-23T08:00:00Z", phaseCode: "加热", value: 500 },
      { frameId: 2, occurredAt: "2026-07-23T08:00:01Z", phaseCode: "保压", value: 505 },
    ],
  }];
  const traces = processCurveTraces(series, [{ code: "temperature", name: "模温", unit: "℃" }], "2026-07-23T08:00:00Z");
  assert.deepEqual(traces[0].x, [0, 1]);
  assert.deepEqual(traces[0].customdata[1], ["2026-07-23T08:00:01Z", "保压"]);
  assert.equal(traces[0].name, "模温");
  assert.equal(traces[0].yaxis, "y");
});

test("process traces share one coordinate system", () => {
  const series = Array.from({ length: 12 }, (_, index) => ({
    signalCode: `signal-${index}`,
    points: [{ occurredAt: "2026-07-23T08:00:00Z", value: index + 1 }],
  }));
  const traces = processCurveTraces(series, [], "2026-07-23T08:00:00Z");

  assert.deepEqual(new Set(traces.map(trace => trace.yaxis)), new Set(["y"]));
  assert.equal(new Set(traces.map(trace => trace.line.color)).size, traces.length);
});

test("React Plotly renderer is responsive, lazy, and used by quality analysis", async () => {
  const component = await readFile(new URL("../src/components/PlotlyChart.jsx", import.meta.url), "utf8");
  const pageDirectory = new URL("../src/pages/", import.meta.url);
  const pages = (await Promise.all(
    (await readdir(pageDirectory, { withFileTypes: true }))
      .filter(entry => entry.isFile() && entry.name.endsWith(".jsx"))
      .map(entry => readFile(new URL(entry.name, pageDirectory), "utf8")),
  )).join("\n");
  assert.match(component, /import\("plotly\.js-basic-dist-min"\)/);
  assert.match(component, /plotly\.react/);
  assert.match(component, /responsive: true/);
  assert.match(component, /ResizeObserver/);
  assert.match(pages, /<PlotlyChart/);
  assert.match(pages, /qualityOutcomeTraces/);
});
