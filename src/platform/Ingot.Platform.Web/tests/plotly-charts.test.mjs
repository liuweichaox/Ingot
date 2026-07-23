import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import {
  agentChartTraces,
  extractProcessSamples,
  processSignalTraces,
  qualityOutcomeTraces,
  qualityStackTraces,
} from "../src/charts/chartAdapters.js";

test("agent chart data is converted to Plotly traces without losing null samples", () => {
  const traces = agentChartTraces({
    type: "line",
    labels: ["阶段 1", "阶段 2", "阶段 3"],
    series: [{ name: "温度", values: [510.2, null, "525.7"] }],
  });

  assert.equal(traces.length, 1);
  assert.equal(traces[0].type, "scatter");
  assert.deepEqual(traces[0].x, ["阶段 1", "阶段 2", "阶段 3"]);
  assert.deepEqual(traces[0].y, [510.2, null, 525.7]);
});

test("quality chart separates complete, failed and pending records", () => {
  const traces = qualityStackTraces([
    { name: "系列 A", total: 10, complete: 7, failed: 1 },
  ]);

  assert.deepEqual(traces.map(trace => trace.y[0]), [7, 1, 2]);
  assert.deepEqual(traces.map(trace => trace.name), ["已完成", "不合格", "待处理"]);
});

test("quality outcome chart keeps inspection outcomes as first-class data", () => {
  const traces = qualityOutcomeTraces([
    { name: "系列 A", pass: 7, fail: 2, inconclusive: 1 },
  ]);

  assert.deepEqual(traces.map(trace => trace.y[0]), [7, 2, 1]);
  assert.deepEqual(traces.map(trace => trace.name), ["合格", "不合格", "待确认"]);
});

test("process traces use the full elapsed-time sequence and emphasize the baseline", () => {
  const samples = extractProcessSamples([
    {
      event: {
        eventType: "process.sample",
        occurredAt: "2026-07-23T08:00:00Z",
        context: { phase: "加热" },
        data: { values: { temperature: 500 } },
      },
    },
    {
      event: {
        eventType: "process.sample",
        occurredAt: "2026-07-23T08:00:01Z",
        context: { phase: "保压" },
        data: { values: { temperature: 505 } },
      },
    },
  ]);
  const traces = processSignalTraces(
    [{
      correlationId: "cycle-1",
      machineId: "PRESS-01",
      startedAt: "2026-07-23T08:00:00Z",
      isBaseline: true,
    }],
    { "cycle-1": samples },
    "temperature",
  );

  assert.deepEqual(traces[0].x, [0, 1]);
  assert.deepEqual(traces[0].y, [500, 505]);
  assert.deepEqual(traces[0].customdata[1], ["2026-07-23T08:00:01Z", "保压"]);
  assert.equal(traces[0].line.width, 3);
});

test("analysis pages share a responsive Plotly renderer and keep data tables", async () => {
  const component = await readFile(new URL("../src/components/PlotlyChart.vue", import.meta.url), "utf8");
  const chat = await readFile(new URL("../src/views/ChatView.vue", import.meta.url), "utf8");
  const comparison = await readFile(new URL("../src/views/CycleComparisonView.vue", import.meta.url), "utf8");
  const quality = await readFile(new URL("../src/views/QualityAnalysisView.vue", import.meta.url), "utf8");

  assert.match(component, /import\("plotly\.js-basic-dist-min"\)/);
  assert.match(component, /plotly\.react/);
  assert.match(component, /responsive: true/);
  assert.match(chat, /<PlotlyChart/);
  assert.match(chat, /查看图表数据/);
  assert.match(comparison, /fetchWindowSamples/);
  assert.match(comparison, /afterIngestId/);
  assert.match(quality, /qualityOutcomeTraces/);
});
