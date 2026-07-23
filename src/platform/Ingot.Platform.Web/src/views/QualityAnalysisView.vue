<template>
  <div class="quality-analysis page-stack">
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true">
        <el-form-item label="检测时间">
          <el-date-picker v-model="filters.range" type="datetimerange" range-separator="至" value-format="YYYY-MM-DDTHH:mm:ssZ" style="width: 360px" />
        </el-form-item>
        <el-form-item label="产品系列"><el-input v-model="filters.productSeries" clearable placeholder="全部系列" /></el-form-item>
        <el-form-item label="对象类型"><el-input v-model="filters.subjectType" clearable placeholder="全部类型" /></el-form-item>
        <el-form-item label="对象 ID"><el-input v-model="filters.subjectId" clearable placeholder="全部对象" /></el-form-item>
        <el-form-item><el-button type="primary" :icon="Search" :loading="loading" @click="load">分析</el-button></el-form-item>
      </el-form>
    </el-card>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <section class="quality-strip">
      <div><small>有效检测记录</small><strong>{{ records.length }}</strong></div>
      <div><small>合格</small><strong>{{ summary.pass }}</strong><span>{{ ratio(summary.pass, records.length) }}</span></div>
      <div><small>不合格</small><strong :class="{ danger: summary.fail }">{{ summary.fail }}</strong><span>{{ ratio(summary.fail, records.length) }}</span></div>
      <div><small>待确认</small><strong>{{ summary.inconclusive }}</strong><span>{{ ratio(summary.inconclusive, records.length) }}</span></div>
      <div><small>原始附件</small><strong>{{ summary.attachments }}</strong><span>{{ summary.withAttachments }} 条记录已关联</span></div>
    </section>

    <div class="analysis-grid">
      <section class="analysis-panel">
        <div class="panel-heading"><div><span>质量分层</span><strong>按产品系列</strong></div></div>
        <PlotlyChart :traces="qualityOutcomeTraces(productChartGroups)" :layout="qualityChartLayout" height="280px" />
        <el-table v-loading="loading" :data="pagedProductGroups">
          <el-table-column prop="name" label="产品系列" min-width="150" />
          <el-table-column prop="total" label="检测" width="80" />
          <el-table-column label="合格率" min-width="150"><template #default="{ row }"><el-progress :percentage="percent(row.pass, row.total)" :stroke-width="7" /></template></el-table-column>
          <el-table-column prop="fail" label="不合格" width="90"><template #default="{ row }"><span :class="{ danger: row.fail }">{{ row.fail }}</span></template></el-table-column>
        </el-table>
        <TablePagination v-model:page="productGroupPage" v-model:page-size="productGroupPageSize" :total="productGroupTotal" :page-sizes="[10, 20, 50]" />
      </section>
      <section class="analysis-panel">
        <div class="panel-heading"><div><span>工艺上下文</span><strong>按配方版本</strong></div></div>
        <PlotlyChart :traces="qualityOutcomeTraces(recipeChartGroups)" :layout="qualityChartLayout" height="280px" />
        <el-table v-loading="loading" :data="pagedRecipeGroups">
          <el-table-column prop="name" label="配方" min-width="170" />
          <el-table-column prop="total" label="检测" width="80" />
          <el-table-column label="合格率" min-width="150"><template #default="{ row }"><el-progress :percentage="percent(row.pass, row.total)" :stroke-width="7" /></template></el-table-column>
          <el-table-column prop="fail" label="不合格" width="90"><template #default="{ row }"><span :class="{ danger: row.fail }">{{ row.fail }}</span></template></el-table-column>
        </el-table>
        <TablePagination v-model:page="recipeGroupPage" v-model:page-size="recipeGroupPageSize" :total="recipeGroupTotal" :page-sizes="[10, 20, 50]" />
      </section>
    </div>

    <section class="analysis-panel record-panel">
      <div class="panel-heading">
        <div><span>明细</span><strong>质量结果与工艺范围</strong></div>
        <el-segmented v-model="statusFilter" :options="statusOptions" />
      </div>
      <el-table v-loading="loading" :data="pagedQualityRecords">
        <el-table-column label="检测时间" width="180"><template #default="{ row }">{{ formatTime(row.measuredAt) }}</template></el-table-column>
        <el-table-column label="分析范围" min-width="160"><template #default="{ row }"><strong>{{ scopeTypeLabel(row.analysisScopeType) }}</strong><small>{{ row.analysisScopeId }}</small></template></el-table-column>
        <el-table-column label="运行对象" min-width="160"><template #default="{ row }"><strong>{{ row.subjectId }}</strong><small>{{ subjectTypeLabel(row.subjectType) }}</small></template></el-table-column>
        <el-table-column label="产品" min-width="170"><template #default="{ row }">{{ productText(row) }}</template></el-table-column>
        <el-table-column prop="qualityObjectId" label="质量对象" min-width="170" show-overflow-tooltip />
        <el-table-column label="检测定义" min-width="155"><template #default="{ row }">{{ row.definitionCode }} · v{{ row.definitionVersion }}</template></el-table-column>
        <el-table-column label="结果" width="100"><template #default="{ row }"><el-tag :type="outcomeTag(row.outcome)">{{ outcomeLabel(row.outcome) }}</el-tag></template></el-table-column>
        <el-table-column label="附件" width="80"><template #default="{ row }">{{ row.attachmentCount }}</template></el-table-column>
        <el-table-column label="操作" width="100" fixed="right"><template #default="{ row }"><el-button text type="primary" @click="openScope(row)">查看</el-button></template></el-table-column>
      </el-table>
      <div v-if="!loading && !filteredRecords.length" class="empty-inline">当前范围没有匹配的质量结果</div>
      <TablePagination v-model:page="qualityRecordPage" v-model:page-size="qualityRecordPageSize" :total="qualityRecordTotal" />
    </section>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from "vue";
import { useRouter } from "vue-router";
import { ElDatePicker, ElProgress } from "element-plus";
import { Search } from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import { qualityOutcomeTraces } from "../charts/chartAdapters";
import PlotlyChart from "../components/PlotlyChart.vue";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const router = useRouter();
const loading = ref(false);
const error = ref("");
const records = ref([]);
const statusFilter = ref("all");
const statusOptions = [
  { label: "全部", value: "all" },
  { label: "合格", value: "pass" },
  { label: "不合格", value: "fail" },
  { label: "待确认", value: "inconclusive" },
];
const now = new Date();
const filters = reactive({
  range: [new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000).toISOString(), now.toISOString()],
  productSeries: "",
  subjectType: "",
  subjectId: "",
});

const normalized = value => String(value || "INCONCLUSIVE").toUpperCase();
const summary = computed(() => records.value.reduce((result, row) => {
  const outcome = normalized(row.outcome);
  if (outcome === "PASS") result.pass += 1;
  else if (outcome === "FAIL") result.fail += 1;
  else result.inconclusive += 1;
  result.attachments += Number(row.attachmentCount || 0);
  if (row.attachmentCount) result.withAttachments += 1;
  return result;
}, { pass: 0, fail: 0, inconclusive: 0, attachments: 0, withAttachments: 0 }));
const productGroups = computed(() => groupBy(records.value, row => row.productSeries || "未关联产品系列"));
const recipeGroups = computed(() => groupBy(records.value, row => recipeText(row)));
const productChartGroups = computed(() => productGroups.value.slice(0, 12));
const recipeChartGroups = computed(() => recipeGroups.value.slice(0, 12));
const qualityChartLayout = {
  barmode: "stack",
  hovermode: "x unified",
  margin: { l: 52, r: 18, t: 20, b: 82 },
  xaxis: { type: "category", tickangle: -24 },
  yaxis: { title: { text: "检测记录数" }, rangemode: "tozero", nticks: 6 },
};
const filteredRecords = computed(() => statusFilter.value === "all"
  ? records.value
  : records.value.filter(row => normalized(row.outcome).toLowerCase() === statusFilter.value));
const { page: productGroupPage, pageSize: productGroupPageSize, total: productGroupTotal, pagedItems: pagedProductGroups } = useClientPagination(productGroups, 10);
const { page: recipeGroupPage, pageSize: recipeGroupPageSize, total: recipeGroupTotal, pagedItems: pagedRecipeGroups } = useClientPagination(recipeGroups, 10);
const { page: qualityRecordPage, pageSize: qualityRecordPageSize, total: qualityRecordTotal, pagedItems: pagedQualityRecords, resetPage: resetQualityRecordPage } = useClientPagination(filteredRecords, 50);

async function load() {
  resetQualityRecordPage();
  loading.value = true;
  error.value = "";
  try {
    const params = new URLSearchParams({ limit: "1000" });
    if (filters.range?.length === 2) {
      params.set("from", filters.range[0]);
      params.set("to", filters.range[1]);
    }
    if (filters.productSeries.trim()) params.set("productSeries", filters.productSeries.trim());
    if (filters.subjectType.trim()) params.set("subjectType", filters.subjectType.trim());
    if (filters.subjectId.trim()) params.set("subjectId", filters.subjectId.trim());
    const allRecords = [];
    let offset = 0;
    while (true) {
      params.set("offset", String(offset));
      const result = await getJson(`/api/v1/quality-analysis?${params}`);
      const page = result.data || [];
      allRecords.push(...page);
      offset += page.length;
      if (page.length < 1000 || offset >= Number(result.total || 0)) break;
    }
    records.value = allRecords;
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

function groupBy(rows, keySelector) {
  const groups = new Map();
  for (const row of rows) {
    const name = keySelector(row) || "未关联";
    if (!groups.has(name)) groups.set(name, { name, total: 0, pass: 0, fail: 0, inconclusive: 0 });
    const group = groups.get(name);
    group.total += 1;
    const outcome = normalized(row.outcome).toLowerCase();
    if (outcome === "pass") group.pass += 1;
    else if (outcome === "fail") group.fail += 1;
    else group.inconclusive += 1;
  }
  return [...groups.values()].sort((a, b) => b.total - a.total || a.name.localeCompare(b.name));
}
function openScope(row) {
  if (row.analysisScopeType === "production-cycle") {
    router.push({ path: "/cycles", query: { cycleId: row.analysisScopeId } });
    return;
  }
  router.push({ path: "/events", query: {
    subjectType: row.subjectType,
    subjectId: row.subjectId,
    from: row.scopeFrom,
    to: row.scopeTo,
  } });
}
function percent(value, total) { return total ? Math.round(value / total * 100) : 0; }
function ratio(value, total) { return total ? `${percent(value, total)}%` : "-"; }
function productText(row) { return [row.productSeries, row.productCode].filter(Boolean).join(" · ") || "未关联"; }
function recipeText(row) { return [row.recipeId, row.recipeVersion ? `v${row.recipeVersion}` : ""].filter(Boolean).join(" · ") || "未关联配方"; }
function outcomeLabel(value) { return { PASS: "合格", FAIL: "不合格", INCONCLUSIVE: "待确认" }[normalized(value)] || value; }
function outcomeTag(value) { return { PASS: "success", FAIL: "danger", INCONCLUSIVE: "warning" }[normalized(value)] || "info"; }
function scopeTypeLabel(value) { return { "production-cycle": "生产周期", "production-run": "生产运行段", "analysis-window": "时间窗口", "material-lot": "物料批次", "operation-run": "关联运行" }[value] || value; }
function subjectTypeLabel(value) { return { equipment: "设备", machine: "设备", line: "产线", station: "工位", sensor: "传感器", asset: "资产" }[String(value || "").toLowerCase()] || value || "运行对象"; }
function formatTime(value) { return value ? new Date(value).toLocaleString("zh-CN", { hour12: false }) : "-"; }

watch(statusFilter, resetQualityRecordPage);
onMounted(load);
</script>

<style scoped>
.quality-strip { display: grid; overflow: hidden; grid-template-columns: repeat(5, minmax(0, 1fr)); border: 1px solid var(--ingot-border); border-radius: 14px; background: #fff; }
.quality-strip > div { display: grid; gap: 4px; padding: 15px 18px; border-right: 1px solid var(--ingot-border); }
.quality-strip > div:last-child { border-right: 0; }
.quality-strip small, .quality-strip span { color: #8b95a4; font-size: 11px; }
.quality-strip strong { color: var(--ingot-ink); font-size: 23px; }
.danger { color: #d25559 !important; font-weight: 650; }
.analysis-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; }
.analysis-panel { overflow: hidden; border: 1px solid var(--ingot-border); border-radius: 14px; background: #fff; }
.panel-heading { display: flex; min-height: 68px; align-items: center; justify-content: space-between; padding: 14px 18px; border-bottom: 1px solid #edf0f4; }
.panel-heading > div { display: grid; gap: 3px; }
.panel-heading span { color: #8b95a4; font-size: 11px; }
.panel-heading strong { color: var(--ingot-ink); font-size: 16px; }
.analysis-panel > :deep(.plotly-chart) { padding: 8px 14px 0; }
.record-panel :deep(.el-table), .analysis-grid :deep(.el-table) { width: calc(100% - 28px); margin: 0 14px 14px; }
.record-panel td strong, .record-panel td small { display: block; }
.record-panel td small { margin-top: 3px; color: #9aa3b1; font-family: ui-monospace, monospace; }
.empty-inline { padding: 46px 18px; color: #99a3b1; text-align: center; }
@media (max-width: 980px) { .quality-strip { grid-template-columns: repeat(3, 1fr); } .analysis-grid { grid-template-columns: 1fr; } }
@media (max-width: 650px) { .quality-strip { grid-template-columns: repeat(2, 1fr); } }
</style>
