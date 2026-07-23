export const chartPalette = ["#3478c9", "#2f9d78", "#e09b3d", "#8a63c7", "#d45f65", "#4b98a7"];

export function agentChartTraces(chart) {
  const type = String(chart?.type || "line").toLowerCase();
  return (chart?.series || []).map((series, index) => {
    const values = (series.values || []).map(numberOrNull);
    const common = {
      name: series.name,
      marker: { color: chartPalette[index % chartPalette.length] },
    };
    if (type === "histogram")
      return { ...common, type: "histogram", x: values.filter(value => value != null), opacity: 0.78 };
    if (type === "boxplot")
      return { ...common, type: "box", y: values.filter(value => value != null), boxpoints: "outliers" };
    if (type === "bar")
      return { ...common, type: "bar", x: chart.labels || [], y: values, hovertemplate: "%{x}<br>%{y}<extra>%{fullData.name}</extra>" };
    return {
      ...common,
      type: "scatter",
      mode: type === "scatter" ? "markers" : "lines+markers",
      x: chart.labels || [],
      y: values,
      line: { color: chartPalette[index % chartPalette.length], width: 2 },
      hovertemplate: "%{x}<br>%{y}<extra>%{fullData.name}</extra>",
    };
  });
}

export function agentChartLayout(chart) {
  const type = String(chart?.type || "line").toLowerCase();
  return {
    barmode: type === "histogram" ? "overlay" : "group",
    hovermode: type === "scatter" || type === "boxplot" || type === "histogram" ? "closest" : "x unified",
    xaxis: { title: { text: type === "histogram" ? "数值区间" : "" } },
    yaxis: { title: { text: type === "histogram" ? "频数" : "" } },
  };
}

export function qualityStackTraces(groups) {
  const rows = groups || [];
  return [
    {
      type: "bar",
      name: "已完成",
      x: rows.map(row => row.name),
      y: rows.map(row => row.complete || 0),
      marker: { color: "#2f9d78" },
    },
    {
      type: "bar",
      name: "不合格",
      x: rows.map(row => row.name),
      y: rows.map(row => row.failed || 0),
      marker: { color: "#d45f65" },
    },
    {
      type: "bar",
      name: "待处理",
      x: rows.map(row => row.name),
      y: rows.map(row => Math.max(0, (row.total || 0) - (row.complete || 0) - (row.failed || 0))),
      marker: { color: "#d7dde6" },
    },
  ].map(trace => ({
    ...trace,
    hovertemplate: "%{x}<br>%{y} 条<extra>%{fullData.name}</extra>",
  }));
}

export function qualityOutcomeTraces(groups) {
  const rows = groups || [];
  return [
    { type: "bar", name: "合格", x: rows.map(row => row.name), y: rows.map(row => row.pass || 0), marker: { color: "#2f9d78" } },
    { type: "bar", name: "不合格", x: rows.map(row => row.name), y: rows.map(row => row.fail || 0), marker: { color: "#d45f65" } },
    { type: "bar", name: "待确认", x: rows.map(row => row.name), y: rows.map(row => row.inconclusive || 0), marker: { color: "#e09b3d" } },
  ].map(trace => ({
    ...trace,
    hovertemplate: "%{x}<br>%{y} 条<extra>%{fullData.name}</extra>",
  }));
}

export function processSignalTraces(rows, samplesById, signalCode) {
  return (rows || []).map((row, index) => {
    const samples = samplesById?.[row.correlationId] || [];
    const startedAt = new Date(row.startedAt || samples[0]?.occurredAt || 0).getTime();
    const points = samples.map(sample => {
      const value = numberOrNull(sample.values?.[signalCode]);
      const occurredAt = new Date(sample.occurredAt).getTime();
      return value == null || !Number.isFinite(occurredAt)
        ? null
        : { x: (occurredAt - startedAt) / 1000, y: value, occurredAt: sample.occurredAt, phase: sample.phase || "" };
    }).filter(Boolean);
    const color = chartPalette[index % chartPalette.length];
    return {
      type: points.length > 2000 ? "scattergl" : "scatter",
      mode: "lines",
      name: row.isBaseline
        ? `基准 · ${row.machineId || row.label || row.correlationId}`
        : (row.label || `${row.machineId || "对象"} · ${shortTime(row.startedAt)}`),
      x: points.map(point => point.x),
      y: points.map(point => point.y),
      customdata: points.map(point => [point.occurredAt, point.phase]),
      line: { color, width: row.isBaseline ? 3 : 1.7, dash: row.isBaseline ? "solid" : "dot" },
      hovertemplate: "相对时间 %{x:.1f}s<br>数值 %{y}<br>%{customdata[0]}<br>%{customdata[1]}<extra>%{fullData.name}</extra>",
    };
  }).filter(trace => trace.x.length);
}

export function extractProcessSamples(records) {
  return (records || []).map(record => record.event || record)
    .filter(event => event?.eventType === "process.sample")
    .map(event => ({
      occurredAt: event.occurredAt,
      phase: event.context?.phase || event.context?.stage || event.context?.process_stage || "",
      values: event.data?.values || event.data || {},
    }));
}

function numberOrNull(value) {
  if (value == null || value === "") return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}

function shortTime(value) {
  return value ? new Date(value).toLocaleString("zh-CN", { hour12: false }) : "";
}
