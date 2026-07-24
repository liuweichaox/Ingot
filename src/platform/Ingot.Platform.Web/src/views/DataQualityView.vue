<template>
  <div class="quality-view">
    <el-card shadow="never" class="filter-card">
      <el-form class="filter-grid">
        <el-form-item label="检查范围">
          <el-date-picker v-model="range" type="datetimerange" range-separator="至" value-format="YYYY-MM-DDTHH:mm:ssZ" />
        </el-form-item>
        <el-form-item label="对象类型"><el-input v-model="subjectType" clearable placeholder="全部类型" /></el-form-item>
        <el-form-item label="对象 ID"><el-input v-model="subjectId" clearable placeholder="全部对象" /></el-form-item>
        <el-form-item label="产品系列"><el-input v-model="productSeries" clearable placeholder="用于周期检查" /></el-form-item>
        <el-form-item><el-button type="primary" :icon="Search" :loading="loading" @click="load">检查</el-button></el-form-item>
      </el-form>
    </el-card>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <section class="summary-strip">
      <article><small>运行对象</small><strong>{{ objects.length }}</strong></article>
      <article><small>事件记录</small><strong>{{ compactNumber(objectSummary.events) }}</strong></article>
      <article><small>采样记录</small><strong>{{ compactNumber(objectSummary.samples) }}</strong></article>
      <article><small>关联运行</small><strong>{{ compactNumber(objectSummary.operations) }}</strong></article>
      <article><small>周期问题</small><strong :class="{ danger: issues.length }">{{ issues.length }}</strong></article>
    </section>

    <el-tabs v-model="activeTab" class="quality-tabs">
      <el-tab-pane label="运行对象" name="objects">
        <el-table v-loading="loading" :data="pagedObjects" stripe>
          <el-table-column label="运行对象" min-width="210">
            <template #default="{ row }"><strong>{{ row.subjectId }}</strong><small>{{ subjectTypeLabel(row.subjectType) }}</small></template>
          </el-table-column>
          <el-table-column prop="eventCount" label="事件" width="100" />
          <el-table-column prop="sampleCount" label="样本" width="110" />
          <el-table-column prop="operationCount" label="关联运行" width="110" />
          <el-table-column label="首次记录" width="180"><template #default="{ row }">{{ formatTime(row.firstObservedAt) }}</template></el-table-column>
          <el-table-column label="最近记录" width="180"><template #default="{ row }">{{ formatTime(row.lastObservedAt) }}</template></el-table-column>
          <el-table-column label="最近采样" width="180"><template #default="{ row }">{{ formatTime(row.lastSampleAt) }}</template></el-table-column>
          <el-table-column label="最大采样间隔" width="140"><template #default="{ row }">{{ formatGap(row.maximumSampleGapSeconds) }}</template></el-table-column>
          <el-table-column label="操作" width="100" fixed="right">
            <template #default="{ row }"><el-button text type="primary" @click="openEvents(row)">查看事件</el-button></template>
          </el-table-column>
        </el-table>
        <el-empty v-if="!loading && !objects.length" description="当前范围没有运行对象数据" />
        <TablePagination v-model:page="objectPage" v-model:page-size="objectPageSize" :total="objectTotal" />
      </el-tab-pane>

      <el-tab-pane :label="`周期问题 ${issues.length}`" name="cycles">
        <el-table v-loading="loading" :data="pagedIssues" stripe>
          <el-table-column label="严重度" width="100"><template #default="{ row }"><el-tag :type="severityTag(row.issue.severity)">{{ severityLabel(row.issue.severity) }}</el-tag></template></el-table-column>
          <el-table-column label="发生时间" width="180"><template #default="{ row }">{{ formatTime(row.cycle.startedAt) }}</template></el-table-column>
          <el-table-column prop="cycle.machineId" label="运行对象" width="140" />
          <el-table-column prop="cycle.productSeries" label="产品系列" width="130" />
          <el-table-column prop="cycle.workpieceId" label="质量对象" min-width="150" />
          <el-table-column label="问题" min-width="280"><template #default="{ row }"><strong>{{ row.issue.message }}</strong><small>{{ row.issue.code }}</small></template></el-table-column>
          <el-table-column label="周期" min-width="190" show-overflow-tooltip><template #default="{ row }">{{ row.cycle.correlationId }}</template></el-table-column>
          <el-table-column label="操作" width="100" fixed="right"><template #default="{ row }"><el-button text type="primary" @click="openCycle(row.cycle)">查看周期</el-button></template></el-table-column>
        </el-table>
        <el-empty v-if="!loading && !issues.length" description="当前范围的生产周期未发现结构化问题" />
        <TablePagination v-model:page="issuePage" v-model:page-size="issuePageSize" :total="issueTotal" />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElDatePicker } from "element-plus";
import { Search } from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const router = useRouter();
const route = useRoute();
const end = new Date();
const range = ref([new Date(end.getTime() - 7 * 24 * 60 * 60 * 1000).toISOString(), end.toISOString()]);
const subjectType = ref(String(route.query.subjectType || ""));
const subjectId = ref(String(route.query.subjectId || route.query.machineId || ""));
const productSeries = ref("");
const activeTab = ref("objects");
const loading = ref(false);
const error = ref("");
const cycles = ref([]);
const objects = ref([]);
const issues = computed(() => cycles.value.flatMap((cycle) => (cycle.dataIssues || []).map((issue) => ({ cycle, issue })))
  .sort((a, b) => severityOrder(a.issue.severity) - severityOrder(b.issue.severity)));
const objectSummary = computed(() => objects.value.reduce((result, row) => {
  result.events += Number(row.eventCount || 0);
  result.samples += Number(row.sampleCount || 0);
  result.operations += Number(row.operationCount || 0);
  return result;
}, { events: 0, samples: 0, operations: 0 }));
const { page: objectPage, pageSize: objectPageSize, total: objectTotal, pagedItems: pagedObjects, resetPage: resetObjectPage } = useClientPagination(objects, 50);
const { page: issuePage, pageSize: issuePageSize, total: issueTotal, pagedItems: pagedIssues, resetPage: resetIssuePage } = useClientPagination(issues, 50);

async function load() {
  resetObjectPage();
  resetIssuePage();
  loading.value = true;
  error.value = "";
  try {
    const common = new URLSearchParams();
    if (range.value?.length === 2) {
      common.set("from", range.value[0]);
      common.set("to", range.value[1]);
    }
    if (subjectType.value.trim()) common.set("subjectType", subjectType.value.trim());
    if (subjectId.value.trim()) common.set("subjectId", subjectId.value.trim());
    const allObjects = [];
    let objectOffset = 0;
    while (true) {
      const params = new URLSearchParams(common);
      params.set("limit", "500");
      params.set("offset", String(objectOffset));
      const result = await getJson(`/api/v1/data-objects?${params}`);
      const page = result.data || [];
      allObjects.push(...page);
      objectOffset += page.length;
      if (!page.length || objectOffset >= Number(result.total || 0)) break;
    }
    objects.value = allObjects;

    const cycleParams = new URLSearchParams({ limit: "1000", status: "all" });
    if (range.value?.length === 2) {
      cycleParams.set("from", range.value[0]);
      cycleParams.set("to", range.value[1]);
    }
    if (productSeries.value.trim()) cycleParams.set("productSeries", productSeries.value.trim());
    if (subjectId.value.trim() && (!subjectType.value.trim() || ["equipment", "machine"].includes(subjectType.value.trim().toLowerCase()))) {
      cycleParams.set("machineId", subjectId.value.trim());
    }
    const allCycles = [];
    let cycleOffset = 0;
    while (true) {
      cycleParams.set("offset", String(cycleOffset));
      const result = await getJson(`/api/v1/cycles?${cycleParams}`);
      const page = result.data || [];
      allCycles.push(...page);
      if (page.length < 1000) break;
      cycleOffset += page.length;
    }
    cycles.value = allCycles;
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

function openEvents(row) { router.push({ path: "/events", query: { subjectType: row.subjectType, subjectId: row.subjectId } }); }
function openCycle(cycle) { router.push({ path: "/cycles", query: { cycleId: cycle.correlationId } }); }
function subjectTypeLabel(value) { return { equipment: "设备", machine: "设备", line: "产线", station: "工位", sensor: "传感器", asset: "资产" }[String(value || "").toLowerCase()] || value || "运行对象"; }
function severityOrder(value) { return { error: 0, warning: 1, info: 2 }[value] ?? 3; }
function severityLabel(value) { return { error: "错误", warning: "警告", info: "提示" }[value] || value; }
function severityTag(value) { return { error: "danger", warning: "warning", info: "info" }[value] || "info"; }
function formatTime(value) { return value ? new Date(value).toLocaleString("zh-CN", { hour12: false }) : "-"; }
function formatGap(value) { return value == null ? "-" : value < 60 ? `${Number(value).toFixed(1)} 秒` : `${(Number(value) / 60).toFixed(1)} 分`; }
function compactNumber(value) { return new Intl.NumberFormat("zh-CN", { notation: value >= 10000 ? "compact" : "standard", maximumFractionDigits: 1 }).format(value || 0); }
onMounted(load);
</script>

<style scoped>
.quality-view { display: grid; width: 100%; min-width: 0; gap: 18px; }
.filter-card { overflow: hidden; }
.filter-grid { display: grid; grid-template-columns: minmax(300px, 2fr) repeat(3, minmax(130px, 1fr)) auto; align-items: end; gap: 12px; }
.filter-grid :deep(.el-form-item) { min-width: 0; margin: 0; }
.filter-grid :deep(.el-date-editor), .filter-grid :deep(.el-input) { width: 100%; }
.summary-strip { display: grid; overflow: hidden; grid-template-columns: repeat(5, minmax(0, 1fr)); border: 1px solid var(--ingot-border); border-radius: 14px; background: #fff; }
.summary-strip article { display: grid; gap: 5px; padding: 16px 18px; border-right: 1px solid var(--ingot-border); }
.summary-strip article:last-child { border-right: 0; }
.summary-strip small { color: #8994a5; font-size: 12px; }
.summary-strip strong { color: var(--ingot-ink); font-size: 24px; }
.danger { color: #d25559 !important; }
.quality-tabs { min-width: 0; overflow: hidden; padding: 0 18px 18px; border: 1px solid var(--ingot-border); border-radius: 14px; background: #fff; }
.quality-tabs :deep(.el-tabs__content), .quality-tabs :deep(.el-tab-pane) { min-width: 0; }
.quality-tabs :deep(.el-table) { margin-bottom: 12px; }
td strong, td small { display: block; }
td small { margin-top: 3px; color: #9aa3b1; font-family: ui-monospace, monospace; }
@media (max-width: 1150px) {
  .filter-grid { grid-template-columns: minmax(280px, 2fr) repeat(2, minmax(150px, 1fr)); }
}
@media (max-width: 800px) {
  .filter-grid { grid-template-columns: 1fr 1fr; }
  .summary-strip { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
@media (max-width: 560px) { .filter-grid { grid-template-columns: 1fr; } }
</style>
