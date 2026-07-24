<template>
  <div class="edges-view page-stack">
    <section class="signal-strip" aria-label="采集节点状态概览">
      <article class="signal-item">
        <span class="signal-icon is-blue"><el-icon><Connection /></el-icon></span>
        <div><small>已登记节点</small><strong>{{ summary.total }}</strong><span>中心侧节点目录</span></div>
      </article>
      <article class="signal-item">
        <span class="signal-icon is-green"><el-icon><CircleCheck /></el-icon></span>
        <div><small>在线节点</small><strong>{{ summary.online }}</strong><span>90 秒内收到心跳</span></div>
      </article>
      <article class="signal-item">
        <span class="signal-icon is-gold"><el-icon><VideoPlay /></el-icon></span>
        <div><small>运行中任务</small><strong>{{ summary.runningTasks }}</strong><span>现场正在执行采集</span></div>
      </article>
      <article class="signal-item" :class="{ 'has-problem': summary.problem }">
        <span class="signal-icon is-red"><el-icon><Warning /></el-icon></span>
        <div><small>需要处理</small><strong>{{ summary.problem }}</strong><span>离线或运行异常</span></div>
      </article>
    </section>

    <el-card shadow="never" class="node-card">
      <template #header>
        <div class="card-heading">
          <div>
            <strong>节点运行概览</strong>
            <span>查看节点心跳、任务运行与数据上行状态</span>
          </div>
          <div class="update-state">
            <span v-if="lastRefreshedAt">更新于 {{ formatClock(lastRefreshedAt) }}</span>
          </div>
        </div>
      </template>

      <div class="filter-bar">
        <el-input
          v-model="keyword"
          :prefix-icon="Search"
          clearable
          placeholder="搜索节点 ID、主机名或访问地址"
        />
        <el-select v-model="statusFilter" aria-label="节点状态" style="width: 170px">
          <el-option label="全部状态" value="all" />
          <el-option label="在线" value="online" />
          <el-option label="运行异常" value="degraded" />
          <el-option label="离线" value="offline" />
        </el-select>
      </div>

      <el-alert
        v-if="error"
        :title="error"
        type="error"
        :closable="false"
        show-icon
        class="page-error"
      />

      <el-table
        v-if="pagedEdges.length"
        v-loading="loading"
        :data="pagedEdges"
        style="width: 100%"
        element-loading-text="加载中..."
      >
        <el-table-column label="采集节点" min-width="170">
          <template #default="{ row }">
            <div class="primary-cell">
              <strong>{{ row.edgeId }}</strong>
              <span>{{ row.hostname || "未上报主机名" }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="连接状态" width="125">
          <template #default="{ row }">
            <div class="status-cell">
              <el-tag :type="healthType(edgeHealthFor(row))" effect="light" round>
                <span class="status-dot" :class="`is-${edgeHealthFor(row)}`" />
                {{ healthLabel(edgeHealthFor(row)) }}
              </el-tag>
              <small>{{ relativeTime(row.lastSeen) }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="采集任务" min-width="155">
          <template #default="{ row }">
            <div v-if="runtimeByEdge[row.edgeId]?.reachable === false" class="secondary-cell is-danger">
              <strong>状态不可用</strong><span>无法连接现场采集服务</span>
            </div>
            <div v-else class="secondary-cell">
              <strong>
                {{ runtimeSummary(row).runningTasks }}/{{ runtimeSummary(row).totalTasks }} 运行中
              </strong>
              <span>累计 {{ formatInteger(runtimeSummary(row).samplesCollected) }} 条采样</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="数据适配器地址" min-width="175">
          <template #default="{ row }">
            <el-link v-if="row.hostBaseUrl" :href="row.hostBaseUrl" target="_blank" type="primary">
              {{ row.hostBaseUrl }}
            </el-link>
            <span v-else class="muted">未上报</span>
          </template>
        </el-table-column>
        <el-table-column label="最近问题" min-width="160">
          <template #default="{ row }">
            <el-popover
              v-if="nodeProblem(row)"
              placement="top"
              :width="400"
              trigger="hover"
            >
              <template #reference>
                <el-text type="danger" truncated class="problem-text">{{ nodeProblem(row) }}</el-text>
              </template>
              <pre class="problem-detail">{{ nodeProblem(row) }}</pre>
            </el-popover>
            <span v-else class="healthy-copy"><el-icon><CircleCheck /></el-icon> 未发现异常</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="152" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-button link type="primary" @click="openTasks(row)">任务</el-button>
              <el-button link type="primary" @click="openMetrics(row)">指标</el-button>
              <el-button link type="primary" @click="openLogs(row)">日志</el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>

      <el-empty
        v-if="!loading && !error && filteredEdges.length === 0"
        :description="edges.length ? '没有符合筛选条件的节点' : '暂无数据接入节点'"
      />
      <TablePagination
        v-if="filteredEdges.length"
        v-model:page="edgePage"
        v-model:page-size="edgePageSize"
        :total="edgeTotal"
      />
    </el-card>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { useRouter } from "vue-router";
import { CircleCheck, Connection, Search, VideoPlay, Warning } from "@element-plus/icons-vue";
import { ElMessage } from "element-plus";
import { getJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";
import { edgeHealth, summarizeRuntime } from "../presentation/operations";

const router = useRouter();
const edges = ref([]);
const runtimeByEdge = ref({});
const loading = ref(false);
const error = ref("");
const keyword = ref("");
const statusFilter = ref("all");
const lastRefreshedAt = ref(null);
let pollTimer;

const edgeHealthFor = row => edgeHealth(row, runtimeByEdge.value[row.edgeId]);
const runtimeSummary = row => summarizeRuntime(runtimeByEdge.value[row.edgeId]);
const filteredEdges = computed(() => {
  const query = keyword.value.trim().toLowerCase();
  return edges.value.filter(row => {
    if (statusFilter.value !== "all" && edgeHealthFor(row) !== statusFilter.value) return false;
    if (!query) return true;
    return [row.edgeId, row.hostname, row.hostBaseUrl].some(value => String(value || "").toLowerCase().includes(query));
  });
});
const summary = computed(() => {
  const health = edges.value.map(row => edgeHealthFor(row));
  return {
    total: edges.value.length,
    online: health.filter(value => value === "online").length,
    problem: health.filter(value => value !== "online").length,
    runningTasks: edges.value.reduce((total, row) => total + runtimeSummary(row).runningTasks, 0),
  };
});
const {
  page: edgePage,
  pageSize: edgePageSize,
  total: edgeTotal,
  pagedItems: pagedEdges,
  resetPage,
} = useClientPagination(filteredEdges);

function healthLabel(value) {
  return ({ online: "在线", degraded: "运行异常", offline: "离线" })[value] || value;
}

function healthType(value) {
  return ({ online: "success", degraded: "warning", offline: "danger" })[value] || "info";
}

function formatClock(value) {
  return value ? new Date(value).toLocaleTimeString("zh-CN", { hour12: false }) : "-";
}

function relativeTime(value) {
  if (!value) return "从未在线";
  const seconds = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 1000));
  if (seconds < 60) return `${seconds} 秒前`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)} 分钟前`;
  return new Date(value).toLocaleString("zh-CN", { hour12: false });
}

function formatInteger(value) {
  return new Intl.NumberFormat("zh-CN", { maximumFractionDigits: 0 }).format(Number(value) || 0);
}

function nodeProblem(row) {
  const runtime = runtimeByEdge.value[row.edgeId];
  return row.lastError || runtime?.lastError || (runtime?.reachable === false ? "节点诊断接口不可访问" : "");
}

async function loadRuntime(edgeRows) {
  const next = {};
  await Promise.all(edgeRows.map(async row => {
    try {
      const payload = await getJson(`/api/edges/${encodeURIComponent(row.edgeId)}/acquisition/status`);
      next[row.edgeId] = { ...payload, reachable: true };
    } catch {
      next[row.edgeId] = { reachable: false, tasks: [] };
    }
  }));
  runtimeByEdge.value = next;
}

async function load({ silent = false } = {}) {
  if (!silent) loading.value = true;
  error.value = "";
  try {
    const payload = await getJson("/api/edges");
    const rows = Array.isArray(payload) ? payload : payload.data || [];
    edges.value = rows;
    await loadRuntime(rows);
    lastRefreshedAt.value = new Date();
  } catch (cause) {
    error.value = cause?.message || String(cause);
    if (!silent) ElMessage.error("加载采集节点失败");
  } finally {
    if (!silent) loading.value = false;
  }
}

function openTasks(row) {
  router.push({ path: "/configuration/acquisition-profiles", query: { edgeId: row.edgeId } });
}

function openMetrics(row) {
  router.push({ path: "/platform-metrics", query: { edgeId: row.edgeId } });
}

function openLogs(row) {
  router.push({ path: "/logs", query: { edgeId: row.edgeId } });
}

watch([keyword, statusFilter], resetPage);
onMounted(() => {
  load();
  pollTimer = window.setInterval(() => load({ silent: true }), 15000);
});
onBeforeUnmount(() => window.clearInterval(pollTimer));
</script>

<style scoped>
.edges-view { width: 100%; }
.signal-strip { display: grid; overflow: hidden; grid-template-columns: repeat(4, minmax(0, 1fr)); border: 1px solid var(--ingot-border); border-radius: 14px; background: var(--ingot-surface); }
.signal-item { display: flex; min-height: 98px; align-items: center; gap: 13px; padding: 17px 20px; border-right: 1px solid var(--ingot-border); }
.signal-item:last-child { border-right: 0; }
.signal-item.has-problem { background: #fffafa; }
.signal-item > div { display: grid; gap: 2px; }
.signal-item small { color: #7b8697; }
.signal-item strong { color: var(--ingot-ink); font-size: 24px; line-height: 1.15; }
.signal-item span { color: #9aa3b1; font-size: 12px; }
.signal-icon { display: inline-flex; width: 38px; height: 38px; flex: 0 0 38px; align-items: center; justify-content: center; border-radius: 11px; font-size: 18px; }
.signal-icon.is-blue { color: #2f7bc3; background: #edf6ff; }
.signal-icon.is-green { color: #31956f; background: #edf9f4; }
.signal-icon.is-gold { color: #c9821b; background: #fff6e8; }
.signal-icon.is-red { color: #d85d5d; background: #fff0f0; }
.node-card { overflow: hidden; }
.card-heading, .update-state, .filter-bar, .row-actions { display: flex; align-items: center; }
.card-heading { justify-content: space-between; gap: 16px; }
.card-heading > div:first-child { display: grid; gap: 3px; }
.card-heading strong { color: var(--ingot-ink); font-size: 16px; }
.card-heading span, .update-state span { color: #8b95a5; font-size: 12px; }
.filter-bar { gap: 10px; padding-bottom: 16px; }
.filter-bar .el-input { max-width: 360px; }
.page-error { margin-bottom: 16px; }
.primary-cell, .secondary-cell, .status-cell { display: grid; gap: 4px; }
.primary-cell strong, .secondary-cell strong { color: #2a3446; }
.primary-cell span, .secondary-cell span, .status-cell small { color: #8a95a6; font-size: 12px; }
.secondary-cell.is-danger strong, .secondary-cell.is-danger span { color: #c45656; }
.status-cell { justify-items: start; }
.status-dot { display: inline-block; width: 6px; height: 6px; margin-right: 5px; border-radius: 50%; vertical-align: 1px; }
.status-dot.is-online { background: #36a175; }
.status-dot.is-degraded { background: #d99425; }
.status-dot.is-offline { background: #d85d5d; }
.problem-text { max-width: 190px; }
.problem-detail { margin: 0; white-space: pre-wrap; word-break: break-word; }
.healthy-copy { display: inline-flex; align-items: center; gap: 5px; color: #4d9b79; font-size: 13px; }
.muted { color: #9aa3b1; }
.row-actions { gap: 3px; }
.row-actions .el-button + .el-button { margin-left: 0; }
@media (max-width: 1100px) {
  .signal-strip { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .signal-item { border-bottom: 1px solid var(--ingot-border); }
  .signal-item:nth-child(2) { border-right: 0; }
}
@media (max-width: 680px) {
  .signal-strip { grid-template-columns: 1fr; }
  .signal-item { border-right: 0; }
  .card-heading { align-items: flex-start; flex-direction: column; }
  .filter-bar { align-items: stretch; flex-direction: column; }
  .filter-bar .el-input { max-width: none; }
}
</style>
