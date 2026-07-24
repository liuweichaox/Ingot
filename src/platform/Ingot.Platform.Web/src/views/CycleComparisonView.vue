<template>
  <div class="comparison-view">
    <el-card shadow="never" class="query-card">
      <div class="comparison-mode"><el-segmented v-model="comparisonMode" :options="modeOptions" /></div>
      <el-form v-if="comparisonMode === 'cycle'" :inline="true" @submit.prevent>
        <el-form-item label="产品系列">
          <el-select v-model="selectedSeries" placeholder="选择产品系列" class="series-input" @change="resetSelection">
            <el-option v-for="series in seriesOptions" :key="series" :label="series" :value="series" />
          </el-select>
        </el-form-item>
        <el-form-item label="比较周期">
          <el-tree-select
            v-model="selectedCycleIds"
            :data="cycleTree"
            :default-expanded-keys="defaultExpandedCycleKeys"
            node-key="value"
            multiple
            show-checkbox
            filterable
            collapse-tags
            collapse-tags-tooltip
            :max-collapse-tags="2"
            :render-after-expand="false"
            :disabled="!selectedSeries"
            popper-class="cycle-tree-popper"
            placeholder="选择两个或更多周期"
            class="cycles-input"
          >
            <template #default="{ data }">
              <span :class="['cycle-tree-node', `is-${data.type}`]">
                <span>{{ data.label }}</span>
                <small v-if="data.meta">{{ data.meta }}</small>
              </span>
            </template>
          </el-tree-select>
        </el-form-item>
        <el-form-item label="基准周期">
          <el-select v-model="baselineCycleId" :disabled="selectedCycleIds.length < 2" placeholder="从已选周期指定" class="baseline-input">
            <el-option v-for="cycle in selectedCycles" :key="cycle.correlationId" :label="shortCycleLabel(cycle)" :value="cycle.correlationId" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" :disabled="selectedCycleIds.length < 2 || !baselineCycleId" @click="loadComparison">
            开始比较
          </el-button>
        </el-form-item>
      </el-form>
      <div v-else class="window-query">
        <div class="window-query-heading">
          <span>直接选择运行对象和时间范围，不要求存在生产周期。</span>
          <el-button @click="addWindow">添加窗口</el-button>
        </div>
        <div v-for="(window, index) in analysisWindows" :key="window.windowId" class="window-row">
          <el-radio v-model="baselineWindowId" :value="window.windowId">基准</el-radio>
          <el-input v-model="window.label" placeholder="窗口名称" />
          <el-input v-model="window.subjectType" placeholder="对象类型" />
          <el-input v-model="window.subjectId" placeholder="设备或对象 ID" />
          <el-date-picker v-model="window.range" type="datetimerange" start-placeholder="开始时间" end-placeholder="结束时间" value-format="YYYY-MM-DDTHH:mm:ss.SSSZ" />
          <el-button v-if="analysisWindows.length > 2" link type="danger" @click="removeWindow(index)">删除</el-button>
        </div>
        <el-button type="primary" :loading="loading" :disabled="!windowQueryValid" @click="loadWindowComparison">开始比较</el-button>
      </div>
      <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    </el-card>

    <template v-if="comparison">
      <div class="summary-strip">
        <article v-for="item in summaryCards" :key="item.label">
          <span>{{ item.label }}</span>
          <strong>{{ item.value }}</strong>
        </article>
      </div>

      <el-card v-if="signalRows.length" shadow="never">
        <template #header><strong>工艺数据项对比</strong></template>
        <div class="signal-comparison">
          <nav class="signal-list" aria-label="选择工艺数据项">
            <button
              v-for="signal in signalRows"
              :key="signal.code"
              type="button"
              :class="{ active: selectedSignalCode === signal.code }"
              @click="selectedSignalCode = signal.code"
            >
              <strong>{{ signal.name }}</strong>
              <small>{{ signal.code }}{{ signal.unit ? ` · ${displayUnit(signal.unit)}` : '' }}</small>
            </button>
          </nav>
          <section v-if="selectedSignal" class="signal-detail">
            <div class="signal-heading">
              <div><strong>{{ selectedSignal.name }}</strong><span>{{ displayUnit(selectedSignal.unit) || '无单位' }}</span></div>
              <small>相对差异以所选{{ comparisonMode === 'cycle' ? '基准周期' : '基准窗口' }}的均值计算</small>
            </div>
            <div v-loading="traceLoading" class="signal-chart">
              <PlotlyChart
                :traces="signalChartTraces"
                :layout="signalChartLayout"
                :empty-text="traceEmptyText"
                height="360px"
              />
            </div>
            <article v-if="baselineSignalRow" class="baseline-signal-card">
              <div class="baseline-identity">
                <span>基准{{ comparisonMode === 'cycle' ? '周期' : '窗口' }}</span>
                <strong>{{ baselineSignalRow.machineId }} · {{ time(baselineSignalRow.startedAt) }}</strong>
                <small>{{ baselineSignalRow.correlationId }}</small>
              </div>
              <dl>
                <div><dt>均值</dt><dd>{{ number(baselineSignalRow.statistic?.average) }}</dd></div>
                <div><dt>最小值</dt><dd>{{ number(baselineSignalRow.statistic?.minimum) }}</dd></div>
                <div><dt>最大值</dt><dd>{{ number(baselineSignalRow.statistic?.maximum) }}</dd></div>
              </dl>
            </article>
            <div class="comparison-section-label">对比{{ comparisonMode === 'cycle' ? '周期' : '窗口' }}</div>
            <el-table :data="comparisonSignalRows" stripe>
              <el-table-column :label="comparisonMode === 'cycle' ? '生产周期' : '分析窗口'" min-width="250">
                <template #default="{ row }">
                  <strong>{{ row.machineId }} · {{ time(row.startedAt) }}</strong>
                  <small>{{ row.correlationId }}</small>
                </template>
              </el-table-column>
              <el-table-column label="均值" width="110"><template #default="{ row }">{{ number(row.statistic?.average) }}</template></el-table-column>
              <el-table-column label="最小—最大" width="170">
                <template #default="{ row }">{{ number(row.statistic?.minimum) }} — {{ number(row.statistic?.maximum) }}</template>
              </el-table-column>
              <el-table-column label="相对基准" width="120">
                <template #default="{ row }"><span class="delta-value">{{ formatDelta(row.deltaPercent) }}</span></template>
              </el-table-column>
            </el-table>
          </section>
        </div>
      </el-card>

      <el-card v-if="comparisonMode === 'cycle'" shadow="never">
        <template #header>
          <div class="card-heading">
            <div>
              <strong>{{ comparison.productSeries }} 周期明细</strong>
              <p>生产上下文与质量结果用于解释工艺差异。</p>
            </div>
          </div>
        </template>
        <article v-if="baselineCycle" class="baseline-cycle-card">
          <div class="cycle-identity">
            <span>基准周期</span>
            <strong>{{ baselineCycle.machineId }} · {{ time(baselineCycle.startedAt) }}</strong>
            <small>{{ baselineCycle.correlationId }}</small>
          </div>
          <div class="cycle-context">
            <div><span>采样完整率</span><strong>{{ percent(baselineCycle.sampleCompleteness) }}</strong></div>
            <div><span>阶段</span><strong>{{ phaseLabel(baselineCycle) }}</strong></div>
            <div><span>配方</span><strong>{{ versionLabel(baselineCycle.recipeId, baselineCycle.recipeVersion) }}</strong></div>
            <div><span>工装组合</span><strong>{{ versionLabel(baselineCycle.toolingId || baselineCycle.moldId, baselineCycle.assemblyRevision) }}</strong></div>
            <div><span>检测</span><strong>{{ inspectionLabel(baselineCycle) }}</strong></div>
            <div><span>原图复核</span><strong>{{ reviewLabel(baselineCycle.visualReviewDecision) }}</strong></div>
          </div>
        </article>
        <div class="comparison-section-label cycle-list-label">对比周期（{{ comparisonCycles.length }}）</div>
        <div class="comparison-cycle-list">
          <article v-for="cycle in comparisonCycles" :key="cycle.correlationId" class="comparison-cycle-row">
            <div class="cycle-identity">
              <strong>{{ cycle.machineId }} · {{ time(cycle.startedAt) }}</strong>
              <small>{{ cycle.correlationId }}</small>
            </div>
            <div class="cycle-context compact">
              <div><span>采样</span><strong>{{ percent(cycle.sampleCompleteness) }}</strong></div>
              <div><span>阶段</span><strong>{{ phaseLabel(cycle) }}</strong></div>
              <div><span>配方</span><strong>{{ versionLabel(cycle.recipeId, cycle.recipeVersion) }}</strong></div>
              <div><span>工装组合</span><strong>{{ versionLabel(cycle.toolingId || cycle.moldId, cycle.assemblyRevision) }}</strong></div>
              <div><span>检测</span><strong>{{ inspectionLabel(cycle) }}</strong></div>
              <div><span>原图复核</span><strong>{{ reviewLabel(cycle.visualReviewDecision) }}</strong></div>
            </div>
            <el-button text type="primary" @click="useAsBaseline(cycle.correlationId)">设为基准</el-button>
          </article>
        </div>
      </el-card>
      <el-card v-else shadow="never">
        <template #header><strong>分析窗口明细</strong></template>
        <el-table :data="comparisonRows" stripe>
          <el-table-column label="窗口" min-width="220"><template #default="{ row }"><strong>{{ row.label || row.correlationId }}</strong><small>{{ row.correlationId }}</small></template></el-table-column>
          <el-table-column prop="machineId" label="设备或对象" min-width="180" />
          <el-table-column label="开始时间" min-width="180"><template #default="{ row }">{{ time(row.startedAt) }}</template></el-table-column>
          <el-table-column label="结束时间" min-width="180"><template #default="{ row }">{{ time(row.completedAt) }}</template></el-table-column>
          <el-table-column prop="sampleCount" label="样本组" width="100" />
          <el-table-column label="检测记录" width="100"><template #default="{ row }">{{ row.quality?.inspectionCount || 0 }}</template></el-table-column>
          <el-table-column label="合格率" width="100"><template #default="{ row }">{{ percent(row.quality?.passRate) }}</template></el-table-column>
          <el-table-column label="角色" width="100"><template #default="{ row }">{{ row.isBaseline ? '基准' : '对比' }}</template></el-table-column>
        </el-table>
      </el-card>
    </template>

    <el-empty v-else-if="!loading" :description="comparisonMode === 'cycle' ? '选择两个或更多同类生产周期' : '配置两个或更多时间窗口'" />
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElDatePicker, ElRadio } from "element-plus";
import { ElTreeSelect } from "element-plus";
import { getJson, postJson } from "../api/http";
import { extractProcessSamples, processSignalTraces } from "../charts/chartAdapters";
import PlotlyChart from "../components/PlotlyChart.vue";

const route = useRoute();
const router = useRouter();
const comparisonMode = ref(route.query.mode === "window" ? "window" : "cycle");
const modeOptions = [{ label: "生产周期", value: "cycle" }, { label: "运行段 / 时间窗口", value: "window" }];
const baselineCycleId = ref("");
const selectedSeries = ref("");
const selectedCycleIds = ref([]);
const selectedSignalCode = ref("");
const comparison = ref(null);
const recentCycles = ref([]);
const loading = ref(false);
const error = ref("");
const rawSamplesById = ref({});
const traceLoading = ref(false);
const traceError = ref("");
const baselineWindowId = ref("window-1");
const defaultWindowRange = (offsetHours) => {
  const to = new Date(Date.now() - offsetHours * 60 * 60 * 1000);
  const from = new Date(to.getTime() - 60 * 60 * 1000);
  return [from.toISOString(), to.toISOString()];
};
const analysisWindows = ref([
  { windowId: "window-1", label: "基准窗口", subjectType: "equipment", subjectId: "", range: defaultWindowRange(1) },
  { windowId: "window-2", label: "对比窗口", subjectType: "equipment", subjectId: "", range: defaultWindowRange(2) },
]);
const windowQueryValid = computed(() => analysisWindows.value.length >= 2 && baselineWindowId.value && analysisWindows.value.every(window => window.subjectType.trim() && window.subjectId.trim() && window.range?.length === 2));

const comparisonRows = computed(() => comparison.value ? [
  { ...comparison.value.baseline, isBaseline: true },
  ...(comparison.value.historicalCycles || []).map((row) => ({ ...row, isBaseline: false })),
] : []);
const activeBaselineId = computed(() => comparisonMode.value === "cycle" ? baselineCycleId.value : baselineWindowId.value);
const seriesOptions = computed(() => [...new Set(recentCycles.value.map(cycle => cycle.productSeries).filter(Boolean))].sort());
const filteredCycles = computed(() => recentCycles.value.filter(cycle => cycle.productSeries === selectedSeries.value));
const cycleTree = computed(() => buildCycleTree(filteredCycles.value));
const defaultExpandedCycleKeys = computed(() => {
  const machine = cycleTree.value[0];
  const recipe = machine?.children[0];
  return [machine?.value, recipe?.value, recipe?.children[0]?.value].filter(Boolean);
});
const selectedCycles = computed(() => selectedCycleIds.value
  .map(id => recentCycles.value.find(cycle => cycle.correlationId === id))
  .filter(Boolean));
const signalRows = computed(() => {
  const signals = new Map();
  for (const cycle of comparisonRows.value) {
    for (const signal of cycle.signals || []) {
      if (!signals.has(signal.code)) {
        signals.set(signal.code, { code: signal.code, name: signal.name || signal.code, unit: signal.unit, values: {} });
      }
      signals.get(signal.code).values[cycle.correlationId] = signal;
    }
  }
  return [...signals.values()];
});
const selectedSignal = computed(() => signalRows.value.find(signal => signal.code === selectedSignalCode.value));
const selectedSignalRows = computed(() => {
  if (!selectedSignal.value) return [];
  const baselineAverage = selectedSignal.value.values[activeBaselineId.value]?.average;
  return comparisonRows.value.map(cycle => {
    const statistic = selectedSignal.value.values[cycle.correlationId];
    const deltaPercent = statistic?.average != null && baselineAverage != null && baselineAverage !== 0
      ? (statistic.average - baselineAverage) / Math.abs(baselineAverage) * 100
      : null;
    return { ...cycle, statistic, deltaPercent };
  });
});
const baselineSignalRow = computed(() => selectedSignalRows.value.find(row => row.isBaseline));
const comparisonSignalRows = computed(() => selectedSignalRows.value.filter(row => !row.isBaseline));
const baselineCycle = computed(() => comparisonRows.value.find(row => row.isBaseline));
const comparisonCycles = computed(() => comparisonRows.value.filter(row => !row.isBaseline));
const signalChartTraces = computed(() => processSignalTraces(
  comparisonRows.value,
  rawSamplesById.value,
  selectedSignalCode.value,
));
const signalChartLayout = computed(() => ({
  hovermode: "x unified",
  xaxis: { title: { text: "相对时间（秒）" }, rangemode: "tozero" },
  yaxis: {
    title: {
      text: signalAxisTitle(selectedSignal.value),
    },
  },
}));
const traceEmptyText = computed(() => traceError.value || (traceLoading.value
  ? "正在加载完整采样轨迹"
  : "当前数据项没有可绘制的采样轨迹"));

const summaryCards = computed(() => {
  if (comparisonMode.value === "window" && comparison.value) {
    const linkedWindows = comparisonRows.value.filter(row => (row.quality?.inspectionCount || 0) > 0).length;
    return [
      { label: "分析方案", value: comparison.value.analysisPlanId || "-" },
      { label: "分析窗口", value: comparisonRows.value.length },
      { label: "工艺数据项", value: signalRows.value.length },
      { label: "质量已关联", value: `${linkedWindows}/${comparisonRows.value.length}` },
    ];
  }
  const acceptance = comparison.value?.acceptance;
  if (!acceptance) return [];
  return [
    { label: "产品系列", value: comparison.value.productSeries },
    { label: "比较周期", value: acceptance.cycleCount },
    { label: "采样完整", value: `${acceptance.completeCycleCount}/${acceptance.cycleCount}` },
    { label: "阶段完整", value: `${acceptance.phaseCompleteCycleCount}/${acceptance.cycleCount}` },
    { label: "质量已关联", value: `${acceptance.qualityLinkedCycleCount}/${acceptance.cycleCount}` },
    { label: "原图已复核", value: `${acceptance.visualReviewCompletedCycleCount}/${acceptance.cycleCount}` },
  ];
});

function addWindow() {
  const index = analysisWindows.value.length + 1;
  const previous = analysisWindows.value.at(-1);
  analysisWindows.value.push({
    windowId: `window-${index}`,
    label: `对比窗口 ${index - 1}`,
    subjectType: previous?.subjectType || "equipment",
    subjectId: previous?.subjectId || "",
    range: defaultWindowRange(index),
  });
}

function removeWindow(index) {
  const [removed] = analysisWindows.value.splice(index, 1);
  if (removed?.windowId === baselineWindowId.value) baselineWindowId.value = analysisWindows.value[0]?.windowId || "";
}

async function loadWindowComparison() {
  loading.value = true;
  error.value = "";
  try {
    const result = await postJson("/api/v1/process-window-comparisons", {
      analysisScope: "analysis-window",
      baselineWindowId: baselineWindowId.value,
      windows: analysisWindows.value.map(window => ({
        windowId: window.windowId,
        label: window.label,
        subjectType: window.subjectType,
        subjectId: window.subjectId,
        from: window.range[0],
        to: window.range[1],
      })),
    });
    const normalize = row => ({
      correlationId: row.windowId,
      label: row.label,
      machineId: row.subjectId,
      startedAt: row.from,
      completedAt: row.to,
      sampleCount: row.sampleCount,
      quality: row.quality,
      signals: row.signals || [],
    });
    comparison.value = {
      ...result,
      productSeries: result.analysisPlanId,
      baseline: normalize(result.baseline),
      historicalCycles: (result.comparisonWindows || []).map(normalize),
    };
    await loadRawTraces();
  } catch (requestError) {
    comparison.value = null;
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

async function loadComparison() {
  loading.value = true;
  error.value = "";
  try {
    comparison.value = await postJson("/api/v1/cycle-comparisons", {
      baselineCycleId: baselineCycleId.value,
      cycleIds: selectedCycleIds.value,
    });
    await loadRawTraces();
    await router.replace({ path: "/comparisons", query: { cycleId: baselineCycleId.value } });
  } catch (requestError) {
    comparison.value = null;
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

async function loadRawTraces() {
  rawSamplesById.value = {};
  traceError.value = "";
  if (!comparisonRows.value.length) return;
  traceLoading.value = true;
  try {
    const entries = comparisonMode.value === "cycle"
      ? await Promise.all(comparisonRows.value.map(async row => {
          const result = await getJson(`/api/v1/cycles/${encodeURIComponent(row.correlationId)}`);
          return [row.correlationId, extractProcessSamples(result.events)];
        }))
      : await Promise.all(analysisWindows.value.map(async window => [
          window.windowId,
          await fetchWindowSamples(window),
        ]));
    rawSamplesById.value = Object.fromEntries(entries);
  } catch {
    rawSamplesById.value = {};
    traceError.value = "完整采样轨迹暂不可用";
  } finally {
    traceLoading.value = false;
  }
}

async function fetchWindowSamples(window) {
  const records = [];
  let cursor = 0;
  while (true) {
    const params = new URLSearchParams({
      type: "process.sample",
      subjectType: window.subjectType.trim(),
      subjectId: window.subjectId.trim(),
      from: window.range[0],
      to: window.range[1],
      afterIngestId: String(cursor),
      limit: "500",
    });
    const result = await getJson(`/api/v1/events?${params}`);
    const page = result.data || [];
    records.push(...page);
    if (page.length < 500) break;
    const nextCursor = Number(result.nextIngestId);
    if (!Number.isFinite(nextCursor) || nextCursor <= cursor)
      throw new Error("分析窗口采样游标没有前进");
    cursor = nextCursor;
  }
  return extractProcessSamples(records);
}

async function loadRecentCycles() {
  try {
    const result = await getJson("/api/v1/cycles?limit=1000&status=all");
    recentCycles.value = result.data || [];
  } catch {
    recentCycles.value = [];
  }
}

async function useAsBaseline(cycleId) {
  baselineCycleId.value = cycleId;
  await loadComparison();
}

function resetSelection() {
  selectedCycleIds.value = [];
  baselineCycleId.value = "";
  comparison.value = null;
  rawSamplesById.value = {};
  traceError.value = "";
  error.value = "";
}

function percent(value) { return `${((value || 0) * 100).toFixed(1)}%`; }
function number(value) { return value == null ? "-" : Number(value).toFixed(3); }
function formatDelta(value) {
  if (value == null) return "-";
  return `${value > 0 ? "+" : ""}${value.toFixed(2)}%`;
}
function displayUnit(value) {
  return { Cel: "℃", "Cel/s": "℃/s" }[value] || value || "";
}
function signalAxisTitle(signal) {
  if (!signal) return "数值";
  const unit = displayUnit(signal.unit);
  return unit && !signal.name.includes(unit) ? `${signal.name}（${unit}）` : signal.name;
}
function time(value) { return value ? new Date(value).toLocaleString("zh-CN") : "-"; }
function buildCycleTree(cycles) {
  const machines = new Map();
  const sortedCycles = [...cycles].sort((left, right) => new Date(right.startedAt) - new Date(left.startedAt));

  for (const cycle of sortedCycles) {
    const machineId = cycle.machineId || "未指定设备";
    const recipeLabel = cycle.recipeId ? `${cycle.recipeId} v${cycle.recipeVersion || "-"}` : "未指定配方";
    const machineKey = `machine:${machineId}`;
    const recipeKey = `${machineKey}:recipe:${recipeLabel}`;
    const dateKey = localDateKey(cycle.startedAt);

    if (!machines.has(machineKey)) {
      machines.set(machineKey, {
        value: machineKey,
        label: machineId,
        type: "machine",
        children: new Map(),
      });
    }
    const machine = machines.get(machineKey);
    if (!machine.children.has(recipeKey)) {
      machine.children.set(recipeKey, {
        value: recipeKey,
        label: `配方 ${recipeLabel}`,
        type: "recipe",
        children: new Map(),
      });
    }
    const recipe = machine.children.get(recipeKey);
    const dayKey = `${recipeKey}:date:${dateKey}`;
    if (!recipe.children.has(dayKey)) {
      recipe.children.set(dayKey, {
        value: dayKey,
        label: localDateLabel(cycle.startedAt),
        type: "date",
        children: [],
      });
    }
    recipe.children.get(dayKey).children.push({
      value: cycle.correlationId,
      label: localTimeLabel(cycle.startedAt),
      meta: cycle.correlationId,
      type: "cycle",
    });
  }

  return [...machines.values()]
    .sort((left, right) => left.label.localeCompare(right.label, "zh-CN"))
    .map(machine => ({
      ...machine,
      children: [...machine.children.values()]
        .sort((left, right) => left.label.localeCompare(right.label, "zh-CN"))
        .map(recipe => ({
          ...recipe,
          children: [...recipe.children.values()].map(day => ({
            ...day,
            meta: `${day.children.length} 个周期`,
          })),
        })),
    }));
}
function localDateKey(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "unknown";
  return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
}
function localDateLabel(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "时间未知";
  return `${date.getFullYear()}/${date.getMonth() + 1}/${date.getDate()}`;
}
function localTimeLabel(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "时间未知";
  return date.toLocaleTimeString("zh-CN", { hour12: false });
}
function shortCycleLabel(cycle) { return `${cycle.machineId || "-"} · ${time(cycle.startedAt)}`; }
function phaseLabel(cycle) { return cycle.requiredPhaseCount ? `${cycle.phaseCount}/${cycle.requiredPhaseCount}` : `${cycle.phaseCount}/未配置`; }
function versionLabel(id, version) { return id ? `${id} v${version || "-"}` : "-"; }
function inspectionLabel(cycle) { return (cycle.inspectionOutcomes || []).join(" / ") || "待检"; }
function reviewLabel(value) {
  return { CONFIRMED: "已确认", REJECTED: "已驳回", REINSPECTION_REQUIRED: "要求重检" }[value] || "待复核";
}
onMounted(async () => {
  await loadRecentCycles();
  const routeSubjectType = String(route.query.subjectType || "").trim();
  const routeSubjectId = String(route.query.subjectId || "").trim();
  if (comparisonMode.value === "window" && routeSubjectId) {
    analysisWindows.value = analysisWindows.value.map(window => ({
      ...window,
      subjectType: routeSubjectType || "equipment",
      subjectId: routeSubjectId,
    }));
  }
  const routeCycleId = String(route.query.cycleId || "").trim();
  const routeCycle = recentCycles.value.find(cycle => cycle.correlationId === routeCycleId);
  if (routeCycle) {
    selectedSeries.value = routeCycle.productSeries;
    selectedCycleIds.value = [routeCycleId];
    baselineCycleId.value = routeCycleId;
  }
});

watch(selectedCycleIds, (ids) => {
  if (!ids.includes(baselineCycleId.value)) baselineCycleId.value = ids[0] || "";
  if (comparison.value && !ids.every(id => comparisonRows.value.some(row => row.correlationId === id))) comparison.value = null;
});
watch(comparisonMode, () => {
  comparison.value = null;
  rawSamplesById.value = {};
  traceError.value = "";
  error.value = "";
  selectedSignalCode.value = "";
});
watch(signalRows, (rows) => {
  if (!rows.some(signal => signal.code === selectedSignalCode.value)) selectedSignalCode.value = rows[0]?.code || "";
});
</script>

<style scoped>
.comparison-view { display: grid; gap: 18px; }
.comparison-mode { margin-bottom: 16px; }
.window-query { display: grid; gap: 10px; }
.window-query-heading { display: flex; align-items: center; justify-content: space-between; color: #6f7c8f; font-size: 13px; }
.window-row { display: grid; grid-template-columns: 76px 150px 140px 180px minmax(360px, 1fr) 48px; align-items: center; gap: 8px; }
.card-heading { display: flex; align-items: center; justify-content: space-between; }
.card-heading p { margin: 5px 0 0; color: #8490a3; font-size: 12px; }
.series-input { width: 160px; }
.cycles-input { width: 500px; }
.baseline-input { width: 230px; }
.cycle-tree-node { display: flex; min-width: 0; flex: 1; align-items: center; justify-content: space-between; gap: 18px; }
.cycle-tree-node > span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.cycle-tree-node small { flex: none; color: #98a2b2; font-size: 11px; }
.cycle-tree-node.is-machine > span { color: #25324a; font-weight: 600; }
.cycle-tree-node.is-recipe > span { color: #45536a; font-weight: 600; }
.cycle-tree-node.is-date > span { color: #697588; font-weight: 600; }
.cycle-tree-node.is-cycle > span { color: #34445d; font-variant-numeric: tabular-nums; }
:global(.cycle-tree-popper .el-tree-node__content:has(.cycle-tree-node:not(.is-cycle)) > .el-checkbox) { visibility: hidden; }
.summary-strip { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); overflow: hidden; border: 1px solid #e7ebf0; border-radius: 12px; background: #fff; }
.summary-strip article { display: grid; gap: 6px; padding: 16px 18px; border-right: 1px solid #edf0f4; }
.summary-strip article:last-child { border-right: 0; }
.summary-strip span { color: #8a95a5; font-size: 12px; }
.summary-strip strong { color: #182238; font-size: 22px; }
.signal-comparison { display: grid; grid-template-columns: 220px minmax(0, 1fr); min-height: 300px; }
.signal-list { display: grid; align-content: start; gap: 3px; padding-right: 14px; border-right: 1px solid #e8ebef; }
.signal-list button { display: grid; gap: 4px; width: 100%; padding: 11px 12px; border: 0; border-radius: 8px; background: transparent; color: #3f4b5f; cursor: pointer; text-align: left; }
.signal-list button:hover { background: #f4f7fb; }
.signal-list button.active { background: #eaf4ff; color: #2878c8; }
.signal-list small { overflow: hidden; color: #8a95a5; text-overflow: ellipsis; white-space: nowrap; }
.signal-detail { min-width: 0; padding-left: 18px; }
.signal-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; margin-bottom: 14px; }
.signal-heading > div { display: flex; align-items: baseline; gap: 8px; }
.signal-heading span, .signal-heading small { color: #8a95a5; font-size: 12px; }
.signal-chart { min-height: 360px; margin: 0 0 16px; border: 1px solid #edf0f4; border-radius: 10px; background: #fff; }
.baseline-signal-card { display: grid; grid-template-columns: minmax(240px, 1fr) minmax(320px, 1.2fr); gap: 24px; padding: 16px 18px; border: 1px solid #cfe4fa; border-radius: 10px; background: #f5faff; }
.baseline-identity, .cycle-identity { display: grid; align-content: center; gap: 5px; min-width: 0; }
.baseline-identity > span, .cycle-identity > span { color: #2878c8; font-size: 12px; font-weight: 600; }
.baseline-identity small, .cycle-identity small { overflow: hidden; color: #8a95a5; font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }
.baseline-signal-card dl { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); margin: 0; }
.baseline-signal-card dl div { padding: 2px 18px; border-left: 1px solid #dceaf8; }
.baseline-signal-card dt, .cycle-context span { color: #8a95a5; font-size: 11px; }
.baseline-signal-card dd { margin: 6px 0 0; color: #182238; font-size: 20px; font-weight: 600; font-variant-numeric: tabular-nums; }
.comparison-section-label { margin: 18px 0 8px; color: #697588; font-size: 12px; font-weight: 600; }
.delta-value { color: #34445d; font-variant-numeric: tabular-nums; font-weight: 600; }
td small { display: block; margin-top: 4px; color: #8a95a5; font-size: 11px; }
.baseline-cycle-card { display: grid; grid-template-columns: minmax(260px, .8fr) minmax(640px, 2fr); gap: 24px; padding: 18px; border: 1px solid #cfe4fa; border-radius: 10px; background: #f5faff; }
.cycle-context { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 16px; }
.cycle-context > div { display: grid; align-content: center; gap: 5px; min-width: 0; }
.cycle-context strong { overflow: hidden; color: #34445d; font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.cycle-list-label { margin-top: 20px; }
.comparison-cycle-list { display: grid; gap: 8px; }
.comparison-cycle-row { display: grid; grid-template-columns: minmax(240px, .8fr) minmax(600px, 2fr) 82px; align-items: center; gap: 20px; padding: 13px 16px; border: 1px solid #e7ebf0; border-radius: 9px; background: #fff; }
.comparison-cycle-row:hover { border-color: #cfd9e6; background: #fafcff; }
@media (max-width: 1100px) { .summary-strip { grid-template-columns: repeat(3, minmax(0, 1fr)); } .summary-strip article:nth-child(3n) { border-right: 0; } .summary-strip article:nth-child(-n + 3) { border-bottom: 1px solid #edf0f4; } }
@media (max-width: 1100px) { .baseline-cycle-card, .comparison-cycle-row { grid-template-columns: 1fr; } .comparison-cycle-row > .el-button { justify-self: start; } }
@media (max-width: 800px) { .series-input, .cycles-input, .baseline-input { width: 100%; } .summary-strip { grid-template-columns: repeat(2, minmax(0, 1fr)); } .signal-comparison { grid-template-columns: 1fr; } .signal-list { grid-template-columns: repeat(2, minmax(0, 1fr)); margin-bottom: 14px; padding: 0 0 14px; border-right: 0; border-bottom: 1px solid #e8ebef; } .signal-detail { padding-left: 0; } .baseline-signal-card { grid-template-columns: 1fr; } .baseline-signal-card dl div:first-child { border-left: 0; } .cycle-context { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 1200px) { .window-row { grid-template-columns: 76px 1fr 1fr; }.window-row > :nth-child(5) { grid-column: 1 / -1; width: 100%; } }
</style>
