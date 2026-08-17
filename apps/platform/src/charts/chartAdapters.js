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

export function processCurveTraces(series, signalDefinitions, startedAt) {
  const origin = new Date(startedAt || series?.[0]?.points?.[0]?.occurredAt || 0).getTime();
  const definitions = new Map((signalDefinitions || []).map(signal => [signal.code, signal]));
  return (series || []).map((item, index) => {
    const definition = definitions.get(item.signalCode) || {};
    const points = (item.points || []).map(point => {
      const occurredAt = new Date(point.occurredAt).getTime();
      const value = numberOrNull(point.value);
      return value == null || !Number.isFinite(occurredAt)
        ? null
        : { x: (occurredAt - origin) / 1000, y: value, occurredAt: point.occurredAt, phase: point.phaseCode || "" };
    }).filter(Boolean);
    return {
      type: points.length > 2000 ? "scattergl" : "scatter",
      mode: "lines",
      name: definition.name || item.signalCode,
      x: points.map(point => point.x),
      y: points.map(point => point.y),
      yaxis: index ? `y${index + 1}` : "y",
      customdata: points.map(point => [point.occurredAt, point.phase]),
      line: { color: chartPalette[index % chartPalette.length], width: 2 },
      connectgaps: false,
      hovertemplate: `%{fullData.name}<br>相对时间 %{x:.1f}s<br>数值 %{y}${definition.unit ? ` ${definition.unit}` : ""}<br>%{customdata[0]}<br>阶段：%{customdata[1]}<extra></extra>`,
    };
  }).filter(trace => trace.x.length);
}

function numberOrNull(value) {
  if (value == null || value === "") return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}
