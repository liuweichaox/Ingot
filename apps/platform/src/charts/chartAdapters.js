export const chartPalette = ["#3478c9", "#2f9d78", "#e09b3d", "#8a63c7", "#d45f65", "#4b98a7"];

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
    const samples = samplesById?.[row.executionId] || [];
    const startedAt = new Date(row.startedAt || samples[0]?.occurredAt || 0).getTime();
    const points = samples.map(sample => {
      const value = numberOrNull(sample.values?.[signalCode]);
      const occurredAt = new Date(sample.occurredAt).getTime();
      return value == null || !Number.isFinite(occurredAt)
        ? null
        : { x: (occurredAt - startedAt) / 1000, y: value, occurredAt: sample.occurredAt, phase: sample.phaseCode || "" };
    }).filter(Boolean);
    const color = chartPalette[index % chartPalette.length];
    return {
      type: points.length > 2000 ? "scattergl" : "scatter",
      mode: "lines",
      name: row.isBaseline
        ? `基准 · ${row.equipmentId || row.label || row.executionId}`
        : (row.label || `${row.equipmentId || "对象"} · ${shortTime(row.startedAt)}`),
      x: points.map(point => point.x),
      y: points.map(point => point.y),
      customdata: points.map(point => [point.occurredAt, point.phase]),
      line: { color, width: row.isBaseline ? 3 : 1.7, dash: row.isBaseline ? "solid" : "dot" },
      hovertemplate: "相对时间 %{x:.1f}s<br>数值 %{y}<br>%{customdata[0]}<br>%{customdata[1]}<extra>%{fullData.name}</extra>",
    };
  }).filter(trace => trace.x.length);
}

function numberOrNull(value) {
  if (value == null || value === "") return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}

function shortTime(value) {
  return value ? new Date(value).toLocaleString("zh-CN", { hour12: false }) : "";
}
