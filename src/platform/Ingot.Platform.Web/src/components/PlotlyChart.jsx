import { useEffect, useRef } from "react";

export default function PlotlyChart({ traces, layout = {}, height = 320, className = "" }) {
  const element = useRef(null);

  useEffect(() => {
    let disposed = false;
    let plotly;
    const node = element.current;
    async function render() {
      const module = await import("plotly.js-basic-dist-min");
      plotly = module.default || module;
      if (disposed || !node) return;
      await plotly.react(node, traces || [], {
        margin: { l: 50, r: 20, t: 24, b: 56, ...(layout.margin || {}) },
        paper_bgcolor: "transparent",
        plot_bgcolor: "transparent",
        font: { family: "Inter, Noto Sans SC, sans-serif", color: "#475569", size: 12 },
        legend: { orientation: "h", y: 1.12, x: 0 },
        autosize: true,
        ...layout,
      }, {
        responsive: true,
        displaylogo: false,
        modeBarButtonsToRemove: ["lasso2d", "select2d"],
      });
    }
    void render();
    const observer = new ResizeObserver(() => {
      if (plotly && node) plotly.Plots.resize(node);
    });
    if (node) observer.observe(node);
    return () => {
      disposed = true;
      observer.disconnect();
      if (plotly && node) plotly.purge(node);
    };
  }, [layout, traces]);

  return <div ref={element} className={className} style={{ width: "100%", height }} aria-label="数据图表" />;
}
