<template>
  <div class="workbench page-stack">
    <section class="workbench-hero">
      <div>
        <span class="eyebrow">今日运行</span>
        <h1>从需要处理的事情开始</h1>
        <p>{{ dateLabel }} · 汇总生产、质量与数据接入状态</p>
      </div>
      <div class="hero-actions">
        <el-button @click="router.push('/explorer')">浏览运行对象</el-button>
        <el-button type="primary" @click="router.push('/chat')">询问 Ingot</el-button>
      </div>
    </section>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <section class="signal-strip" aria-label="关键状态">
      <button type="button" class="signal-item" @click="router.push('/cycles?status=active')">
        <span class="signal-icon is-blue"><el-icon><VideoPlay /></el-icon></span>
        <span><small>正在运行</small><strong>{{ summary.activeCycles }}</strong></span>
      </button>
      <button type="button" class="signal-item" @click="router.push('/inspections')">
        <span class="signal-icon is-amber"><el-icon><DocumentChecked /></el-icon></span>
        <span><small>待处理质检</small><strong>{{ summary.pendingInspections }}</strong></span>
      </button>
      <button type="button" class="signal-item" @click="router.push('/data-quality')">
        <span class="signal-icon" :class="summary.issueCycles ? 'is-red' : 'is-green'"><el-icon><DataLine /></el-icon></span>
        <span><small>数据异常记录</small><strong>{{ summary.issueCycles }}</strong></span>
      </button>
      <button type="button" class="signal-item" @click="router.push('/edges')">
        <span class="signal-icon" :class="summary.offlineEdges ? 'is-red' : 'is-green'"><el-icon><Connection /></el-icon></span>
        <span><small>接入节点在线</small><strong>{{ summary.onlineEdges }}/{{ summary.edgeCount }}</strong></span>
      </button>
    </section>

    <div class="workbench-grid">
      <section class="workspace-panel attention-panel">
        <div class="panel-heading">
          <div><span class="eyebrow">待办</span><h2>需要关注</h2></div>
        </div>
        <div v-loading="loading" class="attention-list">
          <button
            v-for="item in attentionItems"
            :key="item.key"
            type="button"
            class="attention-row"
            @click="router.push(item.to)"
          >
            <span class="attention-marker" :class="`is-${item.level}`" />
            <span class="attention-copy"><strong>{{ item.title }}</strong><small>{{ item.detail }}</small></span>
            <el-icon><ArrowRight /></el-icon>
          </button>
          <div v-if="!loading && !attentionItems.length" class="calm-state">
            <el-icon><CircleCheck /></el-icon>
            <div><strong>当前没有待处理事项</strong><span>生产、质检和数据接入均无待办</span></div>
          </div>
        </div>
      </section>

      <section class="workspace-panel current-panel">
        <div class="panel-heading">
          <div><span class="eyebrow">现场</span><h2>当前生产配置</h2></div>
          <el-button link type="primary" @click="router.push('/production/changeover')">生产切换</el-button>
        </div>
        <div v-loading="loading" class="context-list">
          <button
            v-for="item in activeContexts.slice(0, 5)"
            :key="item.contextId"
            type="button"
            class="context-row"
            @click="router.push({ path: '/explorer', query: { machineId: item.machineId } })"
          >
            <span class="machine-state" />
            <span class="context-machine">{{ item.machineId }}</span>
            <span class="context-product">{{ productText(item) }}</span>
            <span class="context-recipe">{{ recipeText(item) }}</span>
          </button>
          <div v-if="!loading && !activeContexts.length" class="empty-inline">当前没有生效中的生产配置</div>
        </div>
      </section>
    </div>

    <section class="workspace-panel recent-panel">
      <div class="panel-heading">
        <div><span class="eyebrow">追溯</span><h2>最近运行记录</h2></div>
        <el-button link type="primary" @click="router.push('/cycles')">查看全部</el-button>
      </div>
      <el-table v-loading="loading" :data="cycles.slice(0, 8)" @row-click="openCycle">
        <el-table-column label="开始时间" width="176"><template #default="{ row }">{{ formatTime(row.startedAt) }}</template></el-table-column>
        <el-table-column prop="machineId" label="设备" min-width="130" />
        <el-table-column label="产品" min-width="170"><template #default="{ row }">{{ productText(row) }}</template></el-table-column>
        <el-table-column prop="workpieceId" label="工件" min-width="170" show-overflow-tooltip />
        <el-table-column label="周期状态" width="110"><template #default="{ row }"><span class="status-dot" :class="row.status === 'completed' ? 'is-green' : 'is-blue'" />{{ row.status === 'completed' ? '已完成' : '运行中' }}</template></el-table-column>
        <el-table-column label="数据" width="110"><template #default="{ row }"><el-tag :type="row.dataIssues?.length ? 'danger' : 'success'" effect="light">{{ row.dataIssues?.length ? `${row.dataIssues.length} 项异常` : '完整' }}</el-tag></template></el-table-column>
        <el-table-column label="质量" width="110"><template #default="{ row }">{{ qualityLabel(row.qualityStatus) }}</template></el-table-column>
      </el-table>
      <div v-if="!loading && !cycles.length" class="empty-inline">暂无运行记录</div>
    </section>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { ArrowRight, CircleCheck, Connection, DataLine, DocumentChecked, VideoPlay } from "@element-plus/icons-vue";
import { getJson } from "../api/http";

const router = useRouter();
const loading = ref(false);
const error = ref("");
const cycles = ref([]);
const taskSummary = ref({ actionRequired: 0 });
const events = ref([]);
const edges = ref([]);
const contexts = ref([]);

const dateLabel = new Intl.DateTimeFormat("zh-CN", { year: "numeric", month: "long", day: "numeric", weekday: "long" }).format(new Date());
const activeContexts = computed(() => contexts.value.filter(item => !item.validTo));
const summary = computed(() => {
  const edgeRows = Array.isArray(edges.value) ? edges.value : [];
  const isOnline = item => {
    if (item.online === true) return true;
    if (["online", "connected", "healthy"].includes(String(item.status).toLowerCase())) return true;
    return item.lastSeen && Date.now() - new Date(item.lastSeen).getTime() < 90_000 && !item.lastError;
  };
  return {
    activeCycles: cycles.value.filter(item => item.status === "active").length,
    pendingInspections: taskSummary.value.actionRequired || 0,
    issueCycles: cycles.value.filter(item => item.dataIssues?.length).length,
    edgeCount: edgeRows.length,
    onlineEdges: edgeRows.filter(isOnline).length,
    offlineEdges: edgeRows.filter(item => !isOnline(item)).length,
  };
});
const attentionItems = computed(() => {
  const result = [];
  const pending = summary.value.pendingInspections;
  const issues = summary.value.issueCycles;
  const offline = summary.value.offlineEdges;
  if (pending) result.push({ key: "quality", level: "amber", title: `${pending} 项质量任务待处理`, detail: "视觉检查、人工检验或原图复核", to: "/inspections" });
  if (issues) result.push({ key: "data", level: "red", title: `${issues} 条运行记录存在数据异常`, detail: "检查采样连续性、阶段映射和上下文完整性", to: "/data-quality" });
  if (offline) result.push({ key: "edge", level: "red", title: `${offline} 个数据接入节点离线`, detail: "现场数据可能无法持续写入", to: "/edges" });
  if (events.value.length) {
    const latest = events.value[0];
    result.push({ key: "event", level: "blue", title: "查看最新生产事件", detail: `${latest.event?.eventType || latest.eventType || "事件"} · ${formatTime(latest.event?.occurredAt || latest.occurredAt)}`, to: "/events" });
  }
  return result.slice(0, 5);
});

async function load() {
  loading.value = true;
  error.value = "";
  const requests = await Promise.allSettled([
    getJson("/api/v1/cycles?limit=50"),
    getJson("/api/v1/inspection-tasks/summary"),
    getJson("/api/v1/events?limit=20"),
    getJson("/api/edges"),
    getJson("/api/v1/production-contexts"),
  ]);
  const [cycleResult, taskResult, eventResult, edgeResult, contextResult] = requests;
  if (cycleResult.status === "fulfilled") cycles.value = cycleResult.value.data || [];
  if (taskResult.status === "fulfilled") taskSummary.value = taskResult.value || { actionRequired: 0 };
  if (eventResult.status === "fulfilled") events.value = eventResult.value.data || [];
  if (edgeResult.status === "fulfilled") edges.value = Array.isArray(edgeResult.value) ? edgeResult.value : edgeResult.value.data || [];
  if (contextResult.status === "fulfilled") contexts.value = contextResult.value.data || [];
  const failed = requests.filter(item => item.status === "rejected");
  if (failed.length === requests.length) error.value = failed[0].reason?.message || "工作台数据暂不可用";
  loading.value = false;
}

function openCycle(row) { router.push({ path: "/cycles", query: { cycleId: row.correlationId } }); }
function productText(item) { return [item.productSeries, item.productCode].filter(Boolean).join(" · ") || "未关联产品"; }
function recipeText(item) { return item.recipeId ? `${item.recipeId}${item.recipeVersion ? ` · v${item.recipeVersion}` : ""}` : "未关联配方"; }
function qualityLabel(value) { return { not_applicable: "不适用", pending: "待检", in_progress: "检验中", review_pending: "待复核", complete: "已完成", completed: "已完成", failed: "不合格", inconclusive: "待确认" }[String(value || "pending").toLowerCase()] || value; }
function formatTime(value) { return value ? new Date(value).toLocaleString("zh-CN", { hour12: false }) : "-"; }

onMounted(load);
</script>

<style scoped>
.workbench { max-width: 1500px; margin: 0 auto; }
.workbench-hero { display: flex; align-items: end; justify-content: space-between; gap: 24px; padding: 8px 2px 2px; }
.workbench-hero h1 { margin: 5px 0 5px; color: var(--ingot-ink); font-size: clamp(25px, 3vw, 34px); letter-spacing: -.03em; }
.workbench-hero p { margin: 0; color: var(--ingot-muted); }
.hero-actions { display: flex; gap: 10px; }
.eyebrow { color: #8d98a8; font-size: 11px; font-weight: 700; letter-spacing: .12em; text-transform: uppercase; }
.signal-strip { display: grid; overflow: hidden; grid-template-columns: repeat(4, minmax(0, 1fr)); border: 1px solid var(--ingot-border); border-radius: 14px; background: var(--ingot-surface); }
.signal-item { display: flex; align-items: center; gap: 13px; min-height: 84px; padding: 16px 20px; border: 0; border-right: 1px solid var(--ingot-border); background: transparent; text-align: left; cursor: pointer; }
.signal-item:last-child { border-right: 0; }
.signal-item:hover { background: #fafbfd; }
.signal-item > span:last-child { display: grid; gap: 2px; }
.signal-item small { color: var(--ingot-muted); }
.signal-item strong { color: var(--ingot-ink); font-size: 24px; line-height: 1.1; }
.signal-icon { display: inline-flex; width: 40px; height: 40px; align-items: center; justify-content: center; border-radius: 12px; font-size: 19px; }
.signal-icon.is-blue { color: #2778c8; background: #eaf4ff; }
.signal-icon.is-amber { color: #c77714; background: #fff5e6; }
.signal-icon.is-red { color: #c34f53; background: #fff0f0; }
.signal-icon.is-green { color: #358665; background: #eaf8f1; }
.workbench-grid { display: grid; grid-template-columns: minmax(0, 1.05fr) minmax(0, .95fr); gap: 18px; }
.workspace-panel { overflow: hidden; border: 1px solid var(--ingot-border); border-radius: 14px; background: var(--ingot-surface); }
.panel-heading { display: flex; min-height: 70px; align-items: center; justify-content: space-between; padding: 15px 20px; border-bottom: 1px solid #edf0f4; }
.panel-heading > div { display: grid; gap: 3px; }
.panel-heading h2 { margin: 0; color: var(--ingot-ink); font-size: 17px; }
.attention-list, .context-list { min-height: 230px; }
.attention-row, .context-row { display: grid; width: 100%; min-height: 58px; align-items: center; border: 0; border-bottom: 1px solid #f0f2f5; color: inherit; background: transparent; text-align: left; cursor: pointer; }
.attention-row { grid-template-columns: 8px minmax(0, 1fr) auto; gap: 13px; padding: 11px 20px; }
.attention-row:hover, .context-row:hover { background: #fafbfd; }
.attention-marker { width: 7px; height: 7px; border-radius: 50%; }
.attention-marker.is-red { background: #d35b60; box-shadow: 0 0 0 4px #fff0f0; }
.attention-marker.is-amber { background: #d58a2e; box-shadow: 0 0 0 4px #fff5e7; }
.attention-marker.is-blue { background: #347fca; box-shadow: 0 0 0 4px #eaf4ff; }
.attention-copy { display: grid; gap: 3px; min-width: 0; }
.attention-copy strong { color: #2a3445; font-size: 14px; }
.attention-copy small { overflow: hidden; color: #8993a2; text-overflow: ellipsis; white-space: nowrap; }
.context-row { grid-template-columns: 10px minmax(110px, .8fr) minmax(150px, 1.1fr) minmax(120px, 1fr); gap: 10px; padding: 10px 20px; }
.machine-state { width: 8px; height: 8px; border-radius: 50%; background: #42a77b; box-shadow: 0 0 0 4px #eaf8f1; }
.context-machine { color: #253247; font-weight: 650; }
.context-product, .context-recipe { overflow: hidden; color: #6f7a8a; text-overflow: ellipsis; white-space: nowrap; }
.calm-state { display: flex; min-height: 230px; align-items: center; justify-content: center; gap: 14px; color: #3b906e; }
.calm-state > .el-icon { font-size: 30px; }
.calm-state div { display: grid; gap: 3px; }
.calm-state span { color: #8d97a6; font-size: 12px; }
.empty-inline { padding: 44px 20px; color: #9aa3b1; text-align: center; }
.status-dot { display: inline-block; width: 7px; height: 7px; margin-right: 7px; border-radius: 50%; }
.status-dot.is-green { background: #42a77b; }
.status-dot.is-blue { background: #347fca; }
:deep(.recent-panel .el-table) { --el-table-border-color: #eef1f4; }
@media (max-width: 1000px) { .signal-strip { grid-template-columns: repeat(2, 1fr); } .signal-item:nth-child(2) { border-right: 0; } .signal-item:nth-child(-n+2) { border-bottom: 1px solid var(--ingot-border); } .workbench-grid { grid-template-columns: 1fr; } }
@media (max-width: 680px) { .workbench-hero { align-items: start; flex-direction: column; } .signal-strip { grid-template-columns: 1fr; } .signal-item { border-right: 0; border-bottom: 1px solid var(--ingot-border); } .context-row { grid-template-columns: 10px 1fr; } .context-product, .context-recipe { grid-column: 2; } }
</style>
