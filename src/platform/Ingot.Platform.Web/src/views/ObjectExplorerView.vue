<template>
  <div class="object-explorer">
    <aside class="object-sidebar">
      <div class="object-sidebar-head">
        <div><span class="eyebrow">数据目录</span><strong>运行对象</strong></div>
        <el-tag effect="plain" round>{{ filteredMachines.length }}</el-tag>
      </div>
      <div class="object-search">
        <el-input v-model="keyword" :prefix-icon="Search" clearable placeholder="搜索设备、产品或配方" />
      </div>
      <div v-loading="loading" class="object-list">
        <button
          v-for="item in pagedMachines"
          :key="item.objectKey"
          type="button"
          class="object-item"
          :class="{ 'is-active': item.objectKey === selectedObjectKey }"
          @click="selectObject(item)"
        >
          <span class="object-state" :class="item.lastObservedAt ? 'is-online' : 'is-idle'" />
          <span class="object-copy"><strong>{{ item.subjectId }}</strong><small>{{ subjectTypeLabel(item.subjectType) }} · {{ item.product || '未关联产品' }}</small></span>
          <span class="object-count">{{ item.operationCount }}</span>
        </button>
        <div v-if="!loading && !filteredMachines.length" class="empty-object">没有匹配的运行对象</div>
      </div>
      <TablePagination v-model:page="machinePage" v-model:page-size="machinePageSize" :total="machineTotal" :page-sizes="[10, 20, 50]" compact />
    </aside>

    <main class="object-detail">
      <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
      <template v-if="selectedMachine">
        <header class="object-header">
          <div class="object-identity">
            <span class="machine-glyph"><el-icon><Monitor /></el-icon></span>
            <div><span class="eyebrow">{{ subjectTypeLabel(selectedMachine.subjectType) }}</span><h1>{{ selectedMachine.subjectId }}</h1></div>
          </div>
          <div class="object-actions">
            <el-button @click="router.push({ path: '/events', query: { subjectType: selectedMachine.subjectType, subjectId: selectedMachine.subjectId } })">查看事件</el-button>
            <el-button type="primary" @click="askAboutMachine">询问 Ingot</el-button>
          </div>
        </header>

        <section class="context-band">
          <div><small>最近数据时间</small><strong><span class="status-dot" :class="selectedMachine.lastObservedAt ? 'is-green' : 'is-gray'" />{{ formatTime(selectedMachine.lastObservedAt) }}</strong></div>
          <div><small>事件记录</small><strong>{{ selectedMachine.eventCount }}</strong></div>
          <div><small>采样记录</small><strong>{{ selectedMachine.sampleCount }}</strong></div>
          <div><small>关联运行</small><strong>{{ selectedMachine.operationCount }}</strong></div>
        </section>

        <el-tabs v-model="activeTab" class="object-tabs">
          <el-tab-pane label="概览" name="overview">
            <div class="overview-layout">
              <section class="detail-panel">
                <div class="panel-heading"><div><span class="eyebrow">现场上下文</span><h2>当前配置</h2></div><el-button link type="primary" @click="router.push('/production/changeover')">维护</el-button></div>
                <el-descriptions v-if="selectedMachine.context" :column="2" border>
                  <el-descriptions-item label="产品系列">{{ selectedMachine.context.productSeries || '-' }}</el-descriptions-item>
                  <el-descriptions-item label="产品型号">{{ selectedMachine.context.productCode || '-' }}</el-descriptions-item>
                  <el-descriptions-item label="配方">{{ recipeText(selectedMachine.context) }}</el-descriptions-item>
                  <el-descriptions-item label="工装安装">{{ selectedMachine.context.toolingInstallationId || '-' }}</el-descriptions-item>
                  <el-descriptions-item label="来源">{{ sourceLabel(selectedMachine.context.source) }}</el-descriptions-item>
                  <el-descriptions-item label="生效时间">{{ formatTime(selectedMachine.context.validFrom) }}</el-descriptions-item>
                </el-descriptions>
                <div v-else class="empty-inline">当前没有生效中的生产配置</div>
              </section>
              <section class="detail-panel health-panel">
                <div class="panel-heading"><div><span class="eyebrow">数据健康</span><h2>采集概况</h2></div><el-button link type="primary" @click="router.push({ path: '/data-quality', query: { subjectType: selectedMachine.subjectType, subjectId: selectedMachine.subjectId } })">查看详情</el-button></div>
                <div class="health-summary">
                  <div><strong>{{ selectedMachine.sampleCount }}</strong><span>样本</span></div>
                  <div><strong>{{ formatGap(selectedMachine.maximumSampleGapSeconds) }}</strong><span>最大采样间隔</span></div>
                  <div><strong>{{ inspectionCompleteCount }}</strong><span>质检完成周期</span></div>
                </div>
              </section>
            </div>
          </el-tab-pane>
          <el-tab-pane label="运行记录" name="records">
            <section class="detail-panel table-panel">
              <div class="panel-heading"><div><span class="eyebrow">历史</span><h2>运行记录</h2></div><el-button link type="primary" @click="openAllCycles">完整查询</el-button></div>
              <el-table :data="pagedMachineCycles" @row-click="openCycle">
                <el-table-column label="开始时间" width="180"><template #default="{ row }">{{ formatTime(row.startedAt) }}</template></el-table-column>
                <el-table-column label="产品" min-width="170"><template #default="{ row }">{{ productText(row) }}</template></el-table-column>
                <el-table-column prop="workpieceId" label="工件" min-width="180" show-overflow-tooltip />
                <el-table-column label="周期" min-width="210" show-overflow-tooltip><template #default="{ row }"><el-link type="primary">{{ row.correlationId }}</el-link></template></el-table-column>
                <el-table-column label="数据" width="110"><template #default="{ row }"><el-tag :type="row.dataIssues?.length ? 'danger' : 'success'">{{ row.dataIssues?.length ? `${row.dataIssues.length} 项` : '完整' }}</el-tag></template></el-table-column>
                <el-table-column label="质量" width="110"><template #default="{ row }">{{ qualityLabel(row.qualityStatus) }}</template></el-table-column>
              </el-table>
              <div v-if="!machineCycles.length" class="empty-inline">
                该对象没有离散生产周期，可按运行段或时间窗口分析。
                <div><el-button type="primary" link @click="openWindowComparison">创建时间窗口对比</el-button></div>
              </div>
              <TablePagination v-model:page="machineCyclePage" v-model:page-size="machineCyclePageSize" :total="machineCycleTotal" />
            </section>
          </el-tab-pane>
          <el-tab-pane label="关联关系" name="relations">
            <section class="detail-panel relation-panel">
              <div class="panel-heading"><div><span class="eyebrow">上下文图谱</span><h2>当前关联</h2></div></div>
              <div class="relation-flow">
                <button type="button" class="relation-node is-primary"><small>{{ subjectTypeLabel(selectedMachine.subjectType) }}</small><strong>{{ selectedMachine.subjectId }}</strong></button>
                <span class="relation-line">关联上下文</span>
                <button type="button" class="relation-node"><small>产品</small><strong>{{ selectedMachine.product || '未关联' }}</strong></button>
                <span class="relation-line">使用</span>
                <button type="button" class="relation-node"><small>配方</small><strong>{{ selectedMachine.recipe || '未关联' }}</strong></button>
                <span class="relation-line">装入</span>
                <button type="button" class="relation-node"><small>工装</small><strong>{{ selectedMachine.context?.toolingInstallationId || '未关联' }}</strong></button>
              </div>
            </section>
          </el-tab-pane>
        </el-tabs>
      </template>
      <div v-else-if="!loading" class="blank-detail">
        <el-icon><Monitor /></el-icon><strong>选择一个运行对象</strong><span>查看它的上下文、运行记录、质量与数据关系</span>
      </div>
    </main>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElDescriptions, ElDescriptionsItem } from "element-plus";
import { Monitor, Search } from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const route = useRoute();
const router = useRouter();
const loading = ref(false);
const error = ref("");
const keyword = ref("");
const activeTab = ref("overview");
const initialSubjectType = String(route.query.subjectType || "equipment");
const initialSubjectId = String(route.query.subjectId || route.query.machineId || "");
const selectedObjectKey = ref(initialSubjectId ? `${initialSubjectType}/${initialSubjectId}` : "");
const cycles = ref([]);
const contexts = ref([]);
const dataObjects = ref([]);

const machines = computed(() => {
  const map = new Map();
  const ensure = (type, id) => {
    if (!id) return null;
    const objectKey = `${type || "equipment"}/${id}`;
    if (!map.has(objectKey)) map.set(objectKey, {
      objectKey,
      subjectType: type || "equipment",
      subjectId: id,
      machineId: id,
      cycles: [],
      context: null,
      eventCount: 0,
      sampleCount: 0,
      operationCount: 0,
    });
    return map.get(objectKey);
  };
  for (const summary of dataObjects.value) {
    const { context: eventContext, ...fields } = summary;
    const item = ensure(summary.subjectType, summary.subjectId);
    if (item) Object.assign(item, fields, { eventContext: eventContext || {} });
  }
  const findById = id => [...map.values()].find(item => item.subjectId === id);
  for (const cycle of cycles.value) (findById(cycle.machineId) || ensure("equipment", cycle.machineId))?.cycles.push(cycle);
  for (const context of contexts.value) {
    const item = findById(context.machineId) || ensure("equipment", context.machineId);
    if (item && !context.validTo && (!item.context || new Date(context.validFrom) > new Date(item.context.validFrom))) item.context = context;
  }
  return [...map.values()].map(item => {
    const latestCycle = item.cycles[0];
    const source = item.context || latestCycle || item.eventContext || {};
    return {
      ...item,
      product: productText(source),
      recipe: recipeText(source),
      cycleCount: item.cycles.length,
      issueCount: item.cycles.filter(row => row.dataIssues?.length).length,
      completeCount: item.cycles.filter(row => !row.dataIssues?.length).length,
    };
  }).sort((a, b) => new Date(b.lastObservedAt || 0) - new Date(a.lastObservedAt || 0) || a.subjectId.localeCompare(b.subjectId));
});
const filteredMachines = computed(() => {
  const search = keyword.value.trim().toLowerCase();
  if (!search) return machines.value;
  return machines.value.filter(item => [item.subjectType, item.subjectId, item.product, item.recipe].some(value => String(value || "").toLowerCase().includes(search)));
});
const selectedMachine = computed(() => machines.value.find(item => item.objectKey === selectedObjectKey.value));
const machineCycles = computed(() => selectedMachine.value?.cycles || []);
const { page: machinePage, pageSize: machinePageSize, total: machineTotal, pagedItems: pagedMachines, resetPage: resetMachinePage } = useClientPagination(filteredMachines, 10);
const { page: machineCyclePage, pageSize: machineCyclePageSize, total: machineCycleTotal, pagedItems: pagedMachineCycles, resetPage: resetMachineCyclePage } = useClientPagination(machineCycles, 20);
const inspectionCompleteCount = computed(() => machineCycles.value.filter(row => ["complete", "completed"].includes(String(row.qualityStatus).toLowerCase())).length);

async function load() {
  loading.value = true;
  error.value = "";
  try {
    const [contextResult, objectResult] = await Promise.all([
      getJson("/api/v1/production-contexts"),
      getJson("/api/v1/data-objects?limit=500"),
    ]);
    const allCycles = [];
    let offset = 0;
    while (true) {
      const cycleResult = await getJson(`/api/v1/cycles?limit=1000&offset=${offset}`);
      const page = cycleResult.data || [];
      allCycles.push(...page);
      if (page.length < 1000) break;
      offset += page.length;
    }
    cycles.value = allCycles;
    contexts.value = contextResult.data || [];
    dataObjects.value = objectResult.data || [];
    if (!selectedObjectKey.value || !machines.value.some(item => item.objectKey === selectedObjectKey.value)) selectedObjectKey.value = machines.value[0]?.objectKey || "";
  } catch (requestError) { error.value = requestError.message; }
  finally { loading.value = false; }
}

function selectObject(item) {
  selectedObjectKey.value = item.objectKey;
  resetMachineCyclePage();
  activeTab.value = "overview";
  router.replace({ path: "/explorer", query: { subjectType: item.subjectType, subjectId: item.subjectId } });
}
function openCycle(row) { router.push({ path: "/cycles", query: { cycleId: row.correlationId } }); }
function openAllCycles() { router.push({ path: "/cycles", query: { machineId: selectedMachine.value?.subjectId } }); }
function openWindowComparison() { router.push({ path: "/comparisons", query: { mode: "window", subjectType: selectedMachine.value?.subjectType, subjectId: selectedMachine.value?.subjectId } }); }
function askAboutMachine() { router.push({ path: "/chat", query: { subject: selectedMachine.value?.subjectId } }); }
function productText(item) { return [item?.productSeries || item?.product_series, item?.productCode || item?.product_code].filter(Boolean).join(" · ") || ""; }
function recipeText(item) {
  const recipeId = item?.recipeId || item?.recipe_id;
  const recipeVersion = item?.recipeVersion || item?.recipe_version;
  return recipeId ? `${recipeId}${recipeVersion ? ` · v${recipeVersion}` : ""}` : "";
}
function qualityLabel(value) { return { not_applicable: "不适用", pending: "待检", in_progress: "检验中", review_pending: "待复核", complete: "已完成", completed: "已完成", failed: "不合格", inconclusive: "待确认" }[String(value || "pending").toLowerCase()] || value; }
function sourceLabel(value) { return { manual: "现场录入", mes: "MES 同步", device: "设备上报", import: "数据导入" }[value] || value || "-"; }
function subjectTypeLabel(value) { return { equipment: "设备", machine: "设备", line: "产线", station: "工位", sensor: "传感器", asset: "资产" }[String(value || "").toLowerCase()] || value || "运行对象"; }
function formatGap(value) { return value == null ? "-" : value < 60 ? `${Number(value).toFixed(1)} 秒` : `${(Number(value) / 60).toFixed(1)} 分`; }
function formatTime(value) { return value ? new Date(value).toLocaleString("zh-CN", { hour12: false }) : "-"; }

watch(keyword, resetMachinePage);

onMounted(load);
</script>

<style scoped>
.object-explorer { display: grid; min-height: calc(100vh - 112px); grid-template-columns: 300px minmax(0, 1fr); overflow: hidden; border: 1px solid var(--ingot-border); border-radius: 15px; background: var(--ingot-surface); }
.object-sidebar { display: flex; min-width: 0; flex-direction: column; border-right: 1px solid var(--ingot-border); background: #fbfcfe; }
.object-sidebar-head { display: flex; min-height: 72px; align-items: center; justify-content: space-between; padding: 14px 17px; border-bottom: 1px solid var(--ingot-border); }
.object-sidebar-head > div { display: grid; gap: 3px; }
.object-sidebar-head strong { color: var(--ingot-ink); font-size: 17px; }
.eyebrow { color: #909aaa; font-size: 10px; font-weight: 700; letter-spacing: .12em; text-transform: uppercase; }
.object-search { padding: 12px; border-bottom: 1px solid #edf0f4; }
.object-list { min-height: 200px; flex: 1; overflow-y: auto; padding: 7px; }
.object-item { display: grid; width: 100%; grid-template-columns: 9px minmax(0, 1fr) auto; align-items: center; gap: 11px; padding: 12px 11px; border: 1px solid transparent; border-radius: 10px; background: transparent; text-align: left; cursor: pointer; }
.object-item:hover { background: #f4f7fa; }
.object-item.is-active { border-color: #c9def4; background: #eaf4ff; }
.object-state { width: 8px; height: 8px; border-radius: 50%; }
.object-state.is-online { background: #3ca477; box-shadow: 0 0 0 4px #e7f6ef; }
.object-state.is-idle { background: #a6afbc; }
.object-copy { display: grid; min-width: 0; gap: 3px; }
.object-copy strong, .object-copy small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.object-copy strong { color: #29364a; }
.object-copy small { color: #8b95a4; }
.object-count { min-width: 26px; padding: 2px 7px; border-radius: 999px; color: #798494; background: #edf0f4; font-size: 11px; text-align: center; }
.empty-object { padding: 44px 12px; color: #98a2b1; text-align: center; }
.object-detail { min-width: 0; padding: 22px; background: #f6f8fb; }
.object-header { display: flex; align-items: center; justify-content: space-between; gap: 20px; margin-bottom: 18px; }
.object-identity { display: flex; align-items: center; gap: 13px; }
.machine-glyph { display: inline-flex; width: 48px; height: 48px; align-items: center; justify-content: center; border: 1px solid #dce4ed; border-radius: 14px; color: #347ac0; background: #fff; font-size: 23px; }
.object-identity h1 { margin: 3px 0 0; color: var(--ingot-ink); font-size: 25px; letter-spacing: -.02em; }
.context-band { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); overflow: hidden; margin-bottom: 18px; border: 1px solid var(--ingot-border); border-radius: 13px; background: #fff; }
.context-band > div { display: grid; gap: 7px; padding: 15px 18px; border-right: 1px solid var(--ingot-border); }
.context-band > div:last-child { border-right: 0; }
.context-band small { color: #8b95a4; }
.context-band strong { overflow: hidden; color: #273448; text-overflow: ellipsis; white-space: nowrap; }
.status-dot { display: inline-block; width: 7px; height: 7px; margin-right: 7px; border-radius: 50%; }
.status-dot.is-green { background: #3ca477; }.status-dot.is-gray { background: #a6afbc; }
.object-tabs { padding: 0 18px 18px; border: 1px solid var(--ingot-border); border-radius: 14px; background: #fff; }
:deep(.object-tabs > .el-tabs__header) { margin-bottom: 18px; }
.overview-layout { display: grid; grid-template-columns: minmax(0, 1.35fr) minmax(300px, .65fr); gap: 16px; }
.detail-panel { overflow: hidden; border: 1px solid #e6eaf0; border-radius: 12px; background: #fff; }
.panel-heading { display: flex; min-height: 64px; align-items: center; justify-content: space-between; padding: 13px 16px; border-bottom: 1px solid #edf0f4; }
.panel-heading > div { display: grid; gap: 3px; }.panel-heading h2 { margin: 0; color: var(--ingot-ink); font-size: 16px; }
.detail-panel :deep(.el-descriptions) { margin: 16px; }
.health-summary { display: grid; grid-template-columns: repeat(3, 1fr); padding: 30px 14px; }
.health-summary div { display: grid; gap: 6px; text-align: center; }
.health-summary strong { color: var(--ingot-ink); font-size: 25px; }.health-summary span { color: #8a95a4; font-size: 12px; }
.table-panel :deep(.el-table) { width: calc(100% - 28px); margin: 0 14px 14px; }
.relation-panel { min-height: 360px; }
.relation-flow { display: flex; min-height: 270px; align-items: center; justify-content: center; gap: 12px; padding: 32px; }
.relation-node { display: grid; min-width: 138px; max-width: 220px; gap: 6px; padding: 17px 18px; border: 1px solid #dce3eb; border-radius: 12px; color: #263448; background: #fff; text-align: left; }
.relation-node.is-primary { border-color: #a9cceF; background: #edf6ff; }
.relation-node small { color: #8b95a4; }.relation-node strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.relation-line { position: relative; color: #8f99a8; font-size: 11px; }
.relation-line::after { display: block; width: 44px; height: 1px; margin: 5px auto 0; background: #cfd7e1; content: ""; }
.blank-detail { display: flex; min-height: 520px; align-items: center; justify-content: center; flex-direction: column; gap: 8px; color: #97a1af; }
.blank-detail > .el-icon { margin-bottom: 8px; font-size: 38px; }.blank-detail strong { color: #536073; }
.empty-inline { padding: 44px 18px; color: #9aa3b1; text-align: center; }
@media (max-width: 1050px) { .object-explorer { grid-template-columns: 250px minmax(0, 1fr); } .context-band { grid-template-columns: repeat(2, 1fr); }.overview-layout { grid-template-columns: 1fr; }.relation-flow { align-items: stretch; flex-direction: column; }.relation-line::after { width: 1px; height: 20px; } }
@media (max-width: 760px) { .object-explorer { grid-template-columns: 1fr; }.object-sidebar { max-height: 310px; border-right: 0; border-bottom: 1px solid var(--ingot-border); }.object-detail { padding: 14px; }.object-header { align-items: start; flex-direction: column; }.context-band { grid-template-columns: 1fr; }.context-band > div { border-right: 0; border-bottom: 1px solid var(--ingot-border); } }
</style>
