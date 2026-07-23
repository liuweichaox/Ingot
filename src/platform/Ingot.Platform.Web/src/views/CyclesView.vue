<template>
  <div class="cycles-view">
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" class="filters">
        <el-form-item label="生产时间">
          <el-date-picker
            v-model="filters.range"
            type="datetimerange"
            range-separator="至"
            start-placeholder="开始时间"
            end-placeholder="结束时间"
            value-format="YYYY-MM-DDTHH:mm:ssZ"
            :clearable="true"
          />
        </el-form-item>
        <el-form-item label="产品系列">
          <el-input v-model="filters.productSeries" clearable placeholder="全部系列" />
        </el-form-item>
        <el-form-item label="设备">
          <el-input v-model="filters.machineId" clearable placeholder="全部设备" />
        </el-form-item>
        <el-form-item label="周期状态">
          <el-select v-model="filters.status" style="width: 130px">
            <el-option label="全部" value="all" />
            <el-option label="已完成" value="completed" />
            <el-option label="生产中" value="active" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" :loading="loading" @click="search">查询</el-button>
          <el-button @click="reset">重置</el-button>
        </el-form-item>
      </el-form>
      <el-form :inline="true" class="filters secondary-filters">
        <el-form-item label="周期号">
          <el-input v-model="filters.correlationId" clearable placeholder="精确查询" />
        </el-form-item>
        <el-form-item label="工件">
          <el-input v-model="filters.workpieceId" clearable placeholder="工件 ID" />
        </el-form-item>
        <el-form-item label="产品型号">
          <el-input v-model="filters.productCode" clearable placeholder="全部型号" />
        </el-form-item>
        <el-form-item label="配方">
          <el-input v-model="filters.recipeId" clearable placeholder="全部配方" />
        </el-form-item>
      </el-form>
    </el-card>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <div class="overview-grid">
      <article class="metric-card">
        <span>符合条件</span><strong>{{ overview.cycleCount || 0 }}</strong><small>生产周期总数</small>
      </article>
      <article class="metric-card">
        <span>周期完成</span><strong>{{ overview.completedCount || 0 }}</strong><small>{{ percent(overview.completedCount, overview.cycleCount) }}</small>
      </article>
      <article class="metric-card">
        <span>采样完整</span><strong>{{ overview.sampleCompleteCount || 0 }}</strong><small>当前页 {{ percent(overview.sampleCompleteCount, cycles.length) }}</small>
      </article>
      <article class="metric-card">
        <span>阶段完整</span><strong>{{ overview.phaseCompleteCount || 0 }}</strong><small>当前页，按阶段配置</small>
      </article>
      <article class="metric-card" :class="{ warning: overview.issueCycleCount }">
        <span>数据异常</span><strong>{{ overview.issueCycleCount || 0 }}</strong><small>当前页需要核查</small>
      </article>
    </div>

    <el-card shadow="never">
      <el-table v-loading="loading" :data="cycles" stripe @row-click="openDetail">
        <el-table-column label="开始时间" width="180">
          <template #default="{ row }">{{ formatTime(row.startedAt) }}</template>
        </el-table-column>
        <el-table-column prop="machineId" label="设备" width="120" />
        <el-table-column prop="productSeries" label="产品系列" width="120">
          <template #default="{ row }">{{ row.productSeries || '-' }}</template>
        </el-table-column>
        <el-table-column prop="productCode" label="产品型号" min-width="130">
          <template #default="{ row }">{{ row.productCode || '-' }}</template>
        </el-table-column>
        <el-table-column prop="workpieceId" label="工件" min-width="150" show-overflow-tooltip />
        <el-table-column label="模具" min-width="145" show-overflow-tooltip>
          <template #default="{ row }">{{ toolingText(row) }}</template>
        </el-table-column>
        <el-table-column label="周期" min-width="190" show-overflow-tooltip>
          <template #default="{ row }"><el-link type="primary">{{ row.correlationId }}</el-link></template>
        </el-table-column>
        <el-table-column label="时长" width="100">
          <template #default="{ row }">{{ duration(row.durationMs) }}</template>
        </el-table-column>
        <el-table-column label="采样" width="128">
          <template #default="{ row }">
            <el-progress
              :percentage="completeness(row)"
              :status="completeness(row) >= 100 ? 'success' : 'exception'"
              :stroke-width="8"
            />
          </template>
        </el-table-column>
        <el-table-column label="阶段" width="105">
          <template #default="{ row }">
            <el-tag :type="row.phaseComplete === true ? 'success' : row.phaseComplete === false ? 'danger' : 'info'">
              {{ row.phaseCount }}/{{ row.requiredPhaseCount || '-' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="质检" width="125">
          <template #default="{ row }"><el-tag :type="qualityTag(row.qualityStatus)">{{ qualityLabel(row.qualityStatus) }}</el-tag></template>
        </el-table-column>
        <el-table-column label="数据" width="100" fixed="right">
          <template #default="{ row }">
            <el-tag v-if="row.dataIssues?.length" type="danger">{{ row.dataIssues.length }} 项</el-tag>
            <el-tag v-else type="success">正常</el-tag>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loading && !cycles.length" description="当前条件下没有生产周期" />
      <TablePagination v-model:page="cyclePage" v-model:page-size="cyclePageSize" :total="cycleTotal" />
    </el-card>

    <el-drawer v-model="detailVisible" title="生产周期详情" size="min(760px, 92vw)">
      <template v-if="selected">
        <div class="detail-title">
          <div><strong>{{ selected.workpieceId || selected.correlationId }}</strong><span>{{ selected.correlationId }}</span></div>
          <el-tag :type="selected.status === 'completed' ? 'success' : 'primary'">{{ selected.status === 'completed' ? '已完成' : '生产中' }}</el-tag>
        </div>
        <el-descriptions :column="2" border class="detail-block">
          <el-descriptions-item label="设备">{{ selected.machineId }}</el-descriptions-item>
          <el-descriptions-item label="产品">{{ [selected.productSeries, selected.productCode].filter(Boolean).join(' · ') || '-' }}</el-descriptions-item>
          <el-descriptions-item label="配方">{{ recipeText(selected) }}</el-descriptions-item>
          <el-descriptions-item label="模具组合">{{ toolingText(selected) }}</el-descriptions-item>
          <el-descriptions-item label="装模记录">{{ selected.toolingInstallationId || '-' }}</el-descriptions-item>
          <el-descriptions-item label="持续时间">{{ duration(selected.durationMs) }}</el-descriptions-item>
          <el-descriptions-item label="采样完整度">{{ selected.sampleCount }}/{{ selected.expectedSampleCount || '?' }}</el-descriptions-item>
          <el-descriptions-item label="质量方案">{{ planText(selected) }}</el-descriptions-item>
        </el-descriptions>

        <section class="detail-block">
          <div class="section-heading"><strong>生产阶段</strong><span>来自阶段与配方步骤映射配置</span></div>
          <el-table :data="selected.phases || []" size="small" stripe>
            <el-table-column prop="name" label="阶段" min-width="150" />
            <el-table-column label="代码" min-width="130"><template #default="{ row }"><code>{{ row.code }}</code></template></el-table-column>
            <el-table-column prop="sampleCount" label="样本" width="90" />
            <el-table-column label="开始" width="170"><template #default="{ row }">{{ formatTime(row.startedAt) }}</template></el-table-column>
            <el-table-column label="要求" width="80"><template #default="{ row }"><el-tag :type="row.required ? 'primary' : 'info'">{{ row.required ? '必需' : '可选' }}</el-tag></template></el-table-column>
          </el-table>
          <el-empty v-if="!selected.phases?.length" description="未识别到阶段数据" :image-size="70" />
        </section>

        <section class="detail-block">
          <div class="section-heading"><strong>数据质量</strong><span>由当前配置实时判断</span></div>
          <div v-if="selected.dataIssues?.length" class="issue-list">
            <div v-for="issue in selected.dataIssues" :key="issue.code" class="issue-row">
              <el-tag :type="issueType(issue.severity)" size="small">{{ issue.code }}</el-tag>
              <span>{{ issue.message }}</span>
            </div>
          </div>
          <div v-else class="quality-ok"><span class="quality-dot" />周期数据完整</div>
        </section>

        <div class="drawer-actions">
          <el-button @click="openEvents(selected)">查看事件</el-button>
          <el-button @click="openComparison(selected)">历史对比</el-button>
          <el-button type="primary" :disabled="selected.qualityStatus === 'NOT_APPLICABLE'" @click="openInspection(selected)">进入质检</el-button>
        </div>
      </template>
    </el-drawer>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElDatePicker, ElDescriptions, ElDescriptionsItem, ElProgress } from "element-plus";
import { Search } from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";

const route = useRoute();
const router = useRouter();
const loading = ref(false);
const error = ref("");
const cycles = ref([]);
const cyclePage = ref(1);
const cyclePageSize = ref(50);
const cycleTotal = ref(0);
const overview = ref({});
const detailVisible = ref(false);
const selected = ref(null);

function defaultRange() {
  const end = new Date();
  const start = new Date(end.getTime() - 7 * 24 * 60 * 60 * 1000);
  return [start.toISOString(), end.toISOString()];
}
const filters = reactive({
  range: defaultRange(),
  productSeries: "",
  productCode: "",
  recipeId: "",
  machineId: String(route.query.machineId || ""),
  workpieceId: "",
  correlationId: String(route.query.cycleId || ""),
  status: ["all", "completed", "active"].includes(String(route.query.status || "")) ? String(route.query.status) : "all",
});

async function load() {
  loading.value = true;
  error.value = "";
  try {
    const params = new URLSearchParams({
      limit: String(cyclePageSize.value),
      offset: String((cyclePage.value - 1) * cyclePageSize.value),
      status: filters.status,
    });
    if (!filters.correlationId && filters.range?.length === 2) {
      params.set("from", filters.range[0]);
      params.set("to", filters.range[1]);
    }
    for (const key of ["productSeries", "productCode", "recipeId", "machineId", "workpieceId", "correlationId"]) {
      if (filters[key]?.trim()) params.set(key, filters[key].trim());
    }
    const result = await getJson(`/api/v1/cycles?${params}`);
    cycles.value = result.data || [];
    cycleTotal.value = Number(result.total ?? cycles.value.length);
    overview.value = result.overview || {};
    if (filters.correlationId && cycles.value.length === 1) openDetail(cycles.value[0]);
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

async function search() {
  if (cyclePage.value !== 1) cyclePage.value = 1;
  else await load();
}

function reset() {
  Object.assign(filters, {
    range: defaultRange(), productSeries: "", productCode: "", recipeId: "",
    machineId: "", workpieceId: "", correlationId: "", status: "all",
  });
  search();
}
function openDetail(row) { selected.value = row; detailVisible.value = true; }
function openEvents(row) { router.push({ path: "/events", query: { cycleId: row.correlationId } }); }
function openComparison(row) { router.push({ path: "/comparisons", query: { cycleId: row.correlationId } }); }
function openInspection(row) {
  router.push({ path: "/inspections", query: { operationRunId: row.correlationId, workpieceId: row.workpieceId || row.correlationId } });
}
function completeness(row) { return Math.min(100, Math.round((row.sampleCompleteness || 0) * 100)); }
function percent(value, total) { return total ? `${Math.round(value / total * 100)}%` : "-"; }
function duration(ms) {
  if (ms == null) return "-";
  const seconds = Math.round(ms / 1000);
  return seconds >= 60 ? `${Math.floor(seconds / 60)}分${seconds % 60}秒` : `${seconds}秒`;
}
function formatTime(value) { return value ? new Date(value).toLocaleString("zh-CN") : "-"; }
function recipeText(row) { return [row.recipeId, row.recipeVersion ? `v${row.recipeVersion}` : ""].filter(Boolean).join(" · ") || "-"; }
function toolingText(row) { return [row.moldId, row.assemblyRevision ? `v${row.assemblyRevision}` : ""].filter(Boolean).join(" · ") || "-"; }
function planText(row) { return row.inspectionPlanName ? `${row.inspectionPlanName} · v${row.inspectionPlanVersion}` : "不适用"; }
function qualityLabel(value) {
  return { NOT_APPLICABLE: "不适用", PENDING: "待检", IN_PROGRESS: "检验中", REVIEW_PENDING: "待复核", COMPLETE: "已完成", FAILED: "不合格", INCONCLUSIVE: "待确认" }[value] || value;
}
function qualityTag(value) {
  return { NOT_APPLICABLE: "info", PENDING: "warning", IN_PROGRESS: "warning", REVIEW_PENDING: "primary", COMPLETE: "success", FAILED: "danger", INCONCLUSIVE: "warning" }[value] || "info";
}
function issueType(value) { return { error: "error", warning: "warning", info: "info" }[value] || "info"; }

watch([cyclePage, cyclePageSize], load);
onMounted(load);
</script>

<style scoped>
.cycles-view { display: grid; gap: 18px; }
.filter-card :deep(.el-card__body) { padding-bottom: 6px; }
.filters { margin: 0; }
.secondary-filters { padding-top: 2px; border-top: 1px dashed #edf0f4; }
.overview-grid { display: grid; overflow: hidden; grid-template-columns: repeat(5, minmax(0, 1fr)); border: 1px solid var(--ingot-border); border-radius: 13px; background: #fff; }
.metric-card { display: grid; gap: 4px; padding: 14px 18px; border-right: 1px solid #e9edf2; background: #fff; }
.metric-card:last-child { border-right: 0; }
.metric-card span { color: #6f7a8c; font-size: 13px; }
.metric-card strong { color: #172033; font-size: 23px; line-height: 1.1; }
.metric-card small { color: #9aa3b1; }
.metric-card.warning { background: #fffafa; }
.metric-card.warning strong { color: #d94d4d; }
.heading, .detail-title, .section-heading, .drawer-actions { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.heading > div, .detail-title > div, .section-heading { gap: 5px; }
.heading > div, .detail-title > div { display: grid; }
.heading span, .detail-title span, .section-heading span { color: #8b95a5; font-size: 12px; }
.detail-title { margin-bottom: 18px; }
.detail-title strong { font-size: 20px; }
.detail-block { margin-bottom: 24px; }
.section-heading { justify-content: flex-start; margin-bottom: 12px; }
.issue-list { display: grid; gap: 8px; }
.issue-row { display: flex; align-items: center; gap: 10px; padding: 9px 0; border-bottom: 1px solid #eef1f5; color: #4d596b; }
.quality-ok { display: flex; align-items: center; gap: 9px; padding: 12px 0; color: #4d596b; }
.quality-dot { width: 8px; height: 8px; border-radius: 50%; background: #67c23a; }
.drawer-actions { position: sticky; bottom: 0; justify-content: flex-end; padding: 14px 0 4px; background: #fff; }
code { color: #516277; font-family: ui-monospace, monospace; }
@media (max-width: 1050px) { .overview-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); } .metric-card { border-bottom: 1px solid #e9edf2; } }
@media (max-width: 650px) { .overview-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
