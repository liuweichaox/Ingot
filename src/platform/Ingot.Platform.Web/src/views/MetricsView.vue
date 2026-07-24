<template>
  <div class="metrics-view page-stack">
    <el-card v-loading="loading" shadow="never" class="context-card">
      <div class="context-toolbar">
        <div class="node-selector">
          <span>监控节点</span>
          <el-select
            v-model="edgeId"
            placeholder="请选择采集节点"
            :loading="edgesLoading"
            @change="onEdgeChange"
          >
            <el-option
              v-for="edge in edges"
              :key="edge.edgeId"
              :label="edge.edgeId + (edge.hostname ? ` · ${edge.hostname}` : '')"
              :value="edge.edgeId"
            />
          </el-select>
        </div>
        <div v-if="selectedEdge" class="node-state">
          <el-tag :type="healthType(selectedHealth)" effect="light" round>
            <span class="status-dot" :class="`is-${selectedHealth}`" />
            {{ healthLabel(selectedHealth) }}
          </el-tag>
          <span>{{ selectedEdge.hostBaseUrl || "未上报访问地址" }}</span>
        </div>
        <div class="update-state">
          <span v-if="lastUpdate">数据更新于 {{ lastUpdate }}</span>
        </div>
      </div>

      <el-alert
        v-if="error"
        :title="error"
        type="error"
        :closable="false"
        show-icon
        class="page-error"
      />

      <el-empty v-if="!edgesLoading && !error && !edges.length" description="尚无可监控的采集节点">
        <el-button type="primary" plain @click="$router.push('/edges')">查看采集节点</el-button>
      </el-empty>

      <section v-if="edgeId && metrics" class="metric-strip" aria-label="关键运行指标">
        <article class="metric-card">
          <span>已上传事件</span>
          <strong>{{ formatInteger(shippedEvents) }}</strong>
          <small>当前进程累计确认</small>
        </article>
        <article class="metric-card" :class="{ warning: outboxBacklog > 0 }">
          <span>待上传队列</span>
          <strong>{{ formatInteger(outboxBacklog) }}</strong>
          <small>断网续传等待量</small>
        </article>
        <article class="metric-card" :class="{ warning: shipFailures > 0 }">
          <span>上传失败</span>
          <strong>{{ formatInteger(shipFailures) }}</strong>
          <small>当前进程累计次数</small>
        </article>
        <article class="metric-card">
          <span>进程内存</span>
          <strong>{{ formatBytes(workingSet) }}</strong>
          <small>现场采集进程工作集</small>
        </article>
        <article class="metric-card">
          <span>可用指标</span>
          <strong>{{ metricRows.length }}</strong>
          <small>来自节点诊断端点</small>
        </article>
      </section>
    </el-card>

    <el-card v-if="edgeId && metrics" shadow="never" class="detail-card">
      <template #header>
        <div class="card-heading">
          <div>
            <strong>指标明细</strong>
            <span>默认聚焦 Ingot 数据链路，可切换查看运行时与 HTTP 服务</span>
          </div>
          <span class="result-count">{{ filteredMetricRows.length }} 项</span>
        </div>
      </template>

      <div class="filter-bar">
        <el-input
          v-model="keyword"
          :prefix-icon="Search"
          clearable
          placeholder="搜索指标名称"
        />
        <el-select v-model="scopeFilter" aria-label="指标范围" style="width: 190px">
          <el-option
            v-for="scope in scopeOptions"
            :key="scope"
            :label="metricScopeLabel(scope)"
            :value="scope"
          />
        </el-select>
        <el-select v-model="typeFilter" aria-label="指标类型" style="width: 150px">
          <el-option label="全部类型" value="all" />
          <el-option v-for="type in metricTypes" :key="type" :label="type" :value="type" />
        </el-select>
      </div>

      <el-table :data="pagedMetricRows" style="width: 100%" max-height="610">
        <el-table-column label="指标" min-width="330">
          <template #default="{ row }">
            <div class="metric-name">
              <code>{{ row.name }}</code>
              <span>{{ row.help || "节点未提供指标说明" }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="范围" width="150">
          <template #default="{ row }">
            <el-tag :type="scopeType(row.scope)" effect="plain" size="small">
              {{ metricScopeLabel(row.scope) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="115">
          <template #default="{ row }">
            <span class="metric-type">{{ row.type || "-" }}</span>
          </template>
        </el-table-column>
        <el-table-column label="最新值" width="145" align="right">
          <template #default="{ row }">
            <strong class="metric-value">{{ fmtLatest(row.data) }}</strong>
          </template>
        </el-table-column>
        <el-table-column label="标签" min-width="150">
          <template #default="{ row }">
            <el-popover
              v-if="latestLabels(row.data)"
              placement="top"
              :width="400"
              trigger="hover"
            >
              <template #reference>
                <el-button link type="primary">
                  {{ Object.keys(latestLabels(row.data) || {}).length }} 个标签
                </el-button>
              </template>
              <pre class="label-detail">{{ JSON.stringify(latestLabels(row.data), null, 2) }}</pre>
            </el-popover>
            <span v-else class="muted">无</span>
          </template>
        </el-table-column>
      </el-table>

      <el-empty v-if="filteredMetricRows.length === 0" description="暂无匹配的指标" />
      <TablePagination
        v-if="filteredMetricRows.length"
        v-model:page="metricPage"
        v-model:page-size="metricPageSize"
        :total="metricTotal"
        :page-sizes="[50, 100, 200]"
      />
    </el-card>

    <el-empty v-else-if="!loading && !error && edgeId" description="该节点暂无指标数据" />
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { Search } from "@element-plus/icons-vue";
import { ElMessage } from "element-plus";
import { getJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";
import {
  edgeHealth,
  latestMetricValue,
  metricScope,
  metricScopeLabel,
} from "../presentation/operations";

const route = useRoute();
const router = useRouter();
const edgesLoading = ref(false);
const edges = ref([]);
const edgeId = ref("");
const loading = ref(false);
const error = ref("");
const lastUpdate = ref("");
const metrics = ref(null);
const keyword = ref("");
const scopeFilter = ref("ingot");
const typeFilter = ref("all");
const scopeOptions = ["all", "ingot", "runtime", "http", "other"];
let pollTimer;

const selectedEdge = computed(() => edges.value.find(edge => edge.edgeId === edgeId.value) || null);
const selectedHealth = computed(() => edgeHealth(selectedEdge.value));
const metricRows = computed(() => Object.entries(metrics.value || {}).map(([name, value]) => ({
  name,
  ...value,
  scope: metricScope(name),
})).sort((left, right) => left.name.localeCompare(right.name)));
const metricTypes = computed(() => [...new Set(metricRows.value.map(row => row.type).filter(Boolean))].sort());
const filteredMetricRows = computed(() => {
  const query = keyword.value.trim().toLowerCase();
  return metricRows.value.filter(row => {
    if (scopeFilter.value !== "all" && row.scope !== scopeFilter.value) return false;
    if (typeFilter.value !== "all" && row.type !== typeFilter.value) return false;
    return !query || row.name.toLowerCase().includes(query) || String(row.help || "").toLowerCase().includes(query);
  });
});
const shippedEvents = computed(() => latestMetricValue(metrics.value, ["event_shipped_total", "ingot_event_shipped_total"]) || 0);
const outboxBacklog = computed(() => Math.max(0, latestMetricValue(metrics.value, ["event_outbox_backlog", "ingot_event_outbox_backlog"]) || 0));
const shipFailures = computed(() => latestMetricValue(metrics.value, ["event_ship_failures_total", "ingot_event_ship_failures_total"]) || 0);
const workingSet = computed(() => latestMetricValue(metrics.value, ["process_working_set_bytes", "system_runtime_dotnet_process_memory_working_set"]) || 0);
const {
  page: metricPage,
  pageSize: metricPageSize,
  total: metricTotal,
  pagedItems: pagedMetricRows,
  resetPage,
} = useClientPagination(filteredMetricRows, 50);

function latestPoint(data) {
  if (!Array.isArray(data)) return null;
  return [...data].reverse().find(point => !point?.labels?.le) || null;
}

function latestLabels(data) {
  return latestPoint(data)?.labels || null;
}

function fmtLatest(data) {
  const point = latestPoint(data);
  if (!point) return "-";
  const value = Number(point.value);
  return Number.isFinite(value)
    ? new Intl.NumberFormat("zh-CN", { maximumFractionDigits: 2 }).format(value)
    : String(point.value);
}

function formatInteger(value) {
  return new Intl.NumberFormat("zh-CN", { maximumFractionDigits: 0 }).format(Number(value) || 0);
}

function formatBytes(value) {
  const bytes = Number(value) || 0;
  if (bytes < 1024) return `${formatInteger(bytes)} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
}

function healthLabel(value) {
  return ({ online: "节点在线", degraded: "节点有告警", offline: "节点离线" })[value] || value;
}

function healthType(value) {
  return ({ online: "success", degraded: "warning", offline: "danger" })[value] || "info";
}

function scopeType(scope) {
  return ({ ingot: "primary", runtime: "success", http: "warning", other: "info" })[scope] || "info";
}

async function loadEdges() {
  edgesLoading.value = true;
  try {
    const payload = await getJson("/api/edges");
    edges.value = Array.isArray(payload) ? payload : payload.data || [];
  } catch {
    ElMessage.error("加载采集节点失败");
  } finally {
    edgesLoading.value = false;
  }
}

async function loadMetrics({ silent = false } = {}) {
  if (!edgeId.value) return;
  if (!silent) loading.value = true;
  error.value = "";
  try {
    const data = await getJson(`/api/edges/${encodeURIComponent(edgeId.value)}/metrics/json`);
    metrics.value = data.metrics || {};
    lastUpdate.value = new Date(data.timestamp || Date.now()).toLocaleTimeString("zh-CN", { hour12: false });
  } catch (cause) {
    metrics.value = null;
    error.value = cause?.message || String(cause);
    if (!silent) ElMessage.error("加载节点指标失败");
  } finally {
    if (!silent) loading.value = false;
  }
}

async function onEdgeChange() {
  router.replace({ path: "/platform-metrics", query: { edgeId: edgeId.value } }).catch(() => {});
  await loadMetrics();
}

watch([keyword, scopeFilter, typeFilter], resetPage);
onMounted(async () => {
  await loadEdges();
  const fromQuery = route.query.edgeId;
  edgeId.value = typeof fromQuery === "string" && edges.value.some(edge => edge.edgeId === fromQuery)
    ? fromQuery
    : edges.value[0]?.edgeId || "";
  if (edgeId.value) await loadMetrics();
  pollTimer = window.setInterval(() => loadMetrics({ silent: true }), 5000);
});
onBeforeUnmount(() => window.clearInterval(pollTimer));
</script>

<style scoped>
.metrics-view { width: 100%; }
.context-card, .detail-card { overflow: hidden; }
.context-toolbar, .node-selector, .node-state, .update-state, .card-heading, .filter-bar { display: flex; align-items: center; }
.context-toolbar { min-height: 42px; gap: 22px; }
.node-selector { min-width: 420px; gap: 12px; }
.node-selector > span { flex: 0 0 auto; color: #667286; font-size: 13px; font-weight: 600; }
.node-selector .el-select { flex: 1; }
.node-state { min-width: 0; gap: 10px; }
.node-state > span:last-child { overflow: hidden; color: #8a95a5; font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.update-state { margin-left: auto; }
.update-state > span { color: #8a95a5; font-size: 12px; }
.page-error { margin-top: 16px; }
.status-dot { display: inline-block; width: 6px; height: 6px; margin-right: 5px; border-radius: 50%; vertical-align: 1px; }
.status-dot.is-online { background: #36a175; }
.status-dot.is-degraded { background: #d99425; }
.status-dot.is-offline { background: #d85d5d; }
.metric-strip { display: grid; overflow: hidden; grid-template-columns: repeat(5, minmax(0, 1fr)); margin-top: 18px; border: 1px solid var(--ingot-border); border-radius: 12px; }
.metric-card { display: grid; min-width: 0; gap: 4px; padding: 15px 18px; border-right: 1px solid var(--ingot-border); background: #fff; }
.metric-card:last-child { border-right: 0; }
.metric-card.warning { background: #fffaf2; }
.metric-card span { color: #748093; font-size: 12px; }
.metric-card strong { overflow: hidden; color: var(--ingot-ink); font-size: 23px; line-height: 1.2; text-overflow: ellipsis; white-space: nowrap; }
.metric-card.warning strong { color: #c77a16; }
.metric-card small { color: #9aa3b1; }
.card-heading { justify-content: space-between; gap: 16px; }
.card-heading > div { display: grid; gap: 3px; }
.card-heading strong { color: var(--ingot-ink); font-size: 16px; }
.card-heading span, .result-count { color: #8a95a5; font-size: 12px; }
.result-count { padding: 4px 9px; border-radius: 99px; background: #f3f6f9; }
.filter-bar { gap: 10px; padding-bottom: 16px; }
.filter-bar .el-input { max-width: 360px; }
.metric-name { display: grid; gap: 4px; }
.metric-name code { overflow: hidden; padding: 0; color: #304056; background: transparent; font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.metric-name span { overflow: hidden; color: #949eac; font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }
.metric-type { color: #637084; font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace; font-size: 12px; }
.metric-value { color: #2d4f72; font-variant-numeric: tabular-nums; }
.label-detail { margin: 0; white-space: pre-wrap; word-break: break-word; }
.muted { color: #9aa3b1; }
@media (max-width: 1120px) {
  .context-toolbar { align-items: flex-start; flex-wrap: wrap; }
  .update-state { margin-left: 0; }
  .metric-strip { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .metric-card { border-bottom: 1px solid var(--ingot-border); }
}
@media (max-width: 720px) {
  .node-selector { width: 100%; min-width: 0; align-items: stretch; flex-direction: column; }
  .metric-strip { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .filter-bar { align-items: stretch; flex-direction: column; }
  .filter-bar .el-input { max-width: none; }
}
</style>
