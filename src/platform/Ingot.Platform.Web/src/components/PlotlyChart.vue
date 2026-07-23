<template>
  <div class="plotly-chart" :style="{ minHeight: height }">
    <div v-show="hasData" ref="plotElement" class="plotly-canvas" :style="{ height }" />
    <el-empty v-if="!hasData" :description="emptyText" :image-size="64" />
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";

const props = defineProps({
  traces: { type: Array, default: () => [] },
  layout: { type: Object, default: () => ({}) },
  config: { type: Object, default: () => ({}) },
  height: { type: String, default: "320px" },
  emptyText: { type: String, default: "当前范围没有可绘制的数据" },
});

const plotElement = ref(null);
const hasData = computed(() => props.traces.some(trace =>
  (Array.isArray(trace.x) && trace.x.length) ||
  (Array.isArray(trace.y) && trace.y.length)));

let plotly;
let resizeObserver;

async function render() {
  await nextTick();
  if (!plotElement.value) return;
  if (!hasData.value) {
    plotly?.purge(plotElement.value);
    return;
  }

  if (!plotly) {
    const module = await import("plotly.js-basic-dist-min");
    plotly = module.default || module;
  }

  const baseLayout = {
    autosize: true,
    paper_bgcolor: "rgba(0,0,0,0)",
    plot_bgcolor: "#ffffff",
    margin: { l: 58, r: 24, t: 24, b: 52 },
    font: {
      family: '"Inter", "PingFang SC", "Microsoft YaHei", sans-serif',
      color: "#455166",
      size: 12,
    },
    colorway: ["#3478c9", "#2f9d78", "#e09b3d", "#8a63c7", "#d45f65", "#4b98a7"],
    hovermode: "x unified",
    hoverlabel: { bgcolor: "#172033", bordercolor: "#172033", font: { color: "#fff" } },
    legend: { orientation: "h", x: 0, y: 1.12, xanchor: "left", yanchor: "bottom" },
    xaxis: {
      automargin: true,
      gridcolor: "#edf0f4",
      linecolor: "#dfe4eb",
      zeroline: false,
      showspikes: true,
      spikemode: "across",
      spikesnap: "cursor",
    },
    yaxis: {
      automargin: true,
      gridcolor: "#edf0f4",
      linecolor: "#dfe4eb",
      zerolinecolor: "#dfe4eb",
    },
    uirevision: "ingot-analysis",
  };
  const layout = {
    ...baseLayout,
    ...props.layout,
    margin: { ...baseLayout.margin, ...(props.layout.margin || {}) },
    font: { ...baseLayout.font, ...(props.layout.font || {}) },
    legend: { ...baseLayout.legend, ...(props.layout.legend || {}) },
    xaxis: { ...baseLayout.xaxis, ...(props.layout.xaxis || {}) },
    yaxis: { ...baseLayout.yaxis, ...(props.layout.yaxis || {}) },
  };
  const config = {
    responsive: true,
    displaylogo: false,
    scrollZoom: false,
    modeBarButtonsToRemove: ["lasso2d", "select2d"],
    toImageButtonOptions: { format: "png", filename: "ingot-analysis", scale: 2 },
    ...props.config,
  };

  await plotly.react(plotElement.value, props.traces, layout, config);
}

watch(() => [props.traces, props.layout, props.config], render, { deep: true });

onMounted(() => {
  render();
  resizeObserver = new ResizeObserver(() => {
    if (plotly && plotElement.value && hasData.value)
      plotly.Plots.resize(plotElement.value);
  });
  resizeObserver.observe(plotElement.value.parentElement);
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
  if (plotly && plotElement.value) plotly.purge(plotElement.value);
});
</script>

<style scoped>
.plotly-chart { width: 100%; overflow: hidden; }
.plotly-canvas { width: 100%; }
</style>
