<template>
  <div class="quality-view">
    <el-card shadow="never">
      <template #header>
        <div class="heading">
          <div><strong>周期数据质量</strong><span>检查采样、阶段映射和关键生产信息，不使用行业写死规则</span></div>
          <el-button :icon="Refresh" :loading="loading" @click="load">重新检查</el-button>
        </div>
      </template>
      <el-form :inline="true">
        <el-form-item label="检查范围">
          <el-date-picker v-model="range" type="datetimerange" range-separator="至" value-format="YYYY-MM-DDTHH:mm:ssZ" />
        </el-form-item>
        <el-form-item label="产品系列"><el-input v-model="productSeries" clearable /></el-form-item>
        <el-form-item label="设备"><el-input v-model="machineId" clearable /></el-form-item>
        <el-form-item><el-button type="primary" :icon="Search" @click="load">检查</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <div class="score-row">
      <el-progress type="dashboard" :percentage="score" :status="score >= 95 ? 'success' : score >= 80 ? 'warning' : 'exception'" />
      <div><strong>数据健康度</strong><span>{{ healthyCount }}/{{ overview.cycleCount || 0 }} 个周期未发现问题</span></div>
      <div class="score-stat"><strong>{{ overview.issueCycleCount || 0 }}</strong><span>异常周期</span></div>
      <div class="score-stat"><strong>{{ issueCount }}</strong><span>问题总数</span></div>
      <div class="score-stat"><strong>{{ configurationIssueCount }}</strong><span>配置问题</span></div>
    </div>
    <el-card shadow="never">
      <template #header><div class="heading"><strong>待处理问题</strong><el-tag :type="issues.length ? 'danger' : 'success'">{{ issues.length }} 条</el-tag></div></template>
      <el-table v-loading="loading" :data="issues" stripe>
        <el-table-column label="严重度" width="100"><template #default="{ row }"><el-tag :type="severityTag(row.issue.severity)">{{ severityLabel(row.issue.severity) }}</el-tag></template></el-table-column>
        <el-table-column label="发生时间" width="180"><template #default="{ row }">{{ formatTime(row.cycle.startedAt) }}</template></el-table-column>
        <el-table-column prop="cycle.machineId" label="设备" width="120" />
        <el-table-column prop="cycle.productSeries" label="产品系列" width="130" />
        <el-table-column prop="cycle.workpieceId" label="工件" min-width="150" />
        <el-table-column label="问题" min-width="280"><template #default="{ row }"><strong>{{ row.issue.message }}</strong><small>{{ row.issue.code }}</small></template></el-table-column>
        <el-table-column label="周期" min-width="190" show-overflow-tooltip><template #default="{ row }">{{ row.cycle.correlationId }}</template></el-table-column>
        <el-table-column label="操作" width="100" fixed="right"><template #default="{ row }"><el-button text type="primary" @click="openCycle(row.cycle)">查看周期</el-button></template></el-table-column>
      </el-table>
      <el-empty v-if="!loading && !issues.length" description="当前范围未发现数据质量问题" />
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { Refresh, Search } from "@element-plus/icons-vue";
import { getJson } from "../api/http";

const router = useRouter();
const end = new Date();
const range = ref([new Date(end.getTime() - 24 * 60 * 60 * 1000).toISOString(), end.toISOString()]);
const productSeries = ref("");
const machineId = ref("");
const loading = ref(false);
const error = ref("");
const cycles = ref([]);
const overview = ref({});
const issues = computed(() => cycles.value.flatMap((cycle) => (cycle.dataIssues || []).map((issue) => ({ cycle, issue })))
  .sort((a, b) => severityOrder(a.issue.severity) - severityOrder(b.issue.severity)));
const healthyCount = computed(() => Math.max(0, (overview.value.cycleCount || 0) - (overview.value.issueCycleCount || 0)));
const score = computed(() => overview.value.cycleCount ? Math.round(healthyCount.value / overview.value.cycleCount * 100) : 100);
const issueCount = computed(() => issues.value.length);
const configurationIssueCount = computed(() => issues.value.filter((row) => row.issue.code.includes("not_configured") || row.issue.code.includes("expectation_missing")).length);

async function load() {
  loading.value = true;
  error.value = "";
  try {
    const params = new URLSearchParams({ limit: "1000", status: "all" });
    if (range.value?.length === 2) { params.set("from", range.value[0]); params.set("to", range.value[1]); }
    if (productSeries.value.trim()) params.set("productSeries", productSeries.value.trim());
    if (machineId.value.trim()) params.set("machineId", machineId.value.trim());
    const result = await getJson(`/api/v1/cycles?${params}`);
    cycles.value = result.data || [];
    overview.value = result.overview || {};
  } catch (requestError) { error.value = requestError.message; }
  finally { loading.value = false; }
}
function openCycle(cycle) { router.push({ path: "/cycles", query: { cycleId: cycle.correlationId } }); }
function severityOrder(value) { return { error: 0, warning: 1, info: 2 }[value] ?? 3; }
function severityLabel(value) { return { error: "错误", warning: "警告", info: "提示" }[value] || value; }
function severityTag(value) { return { error: "danger", warning: "warning", info: "info" }[value] || "info"; }
function formatTime(value) { return value ? new Date(value).toLocaleString("zh-CN") : "-"; }
onMounted(load);
</script>

<style scoped>
.quality-view { display: grid; gap: 18px; }
.heading, .score-row { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.heading > div { display: grid; gap: 4px; }
.heading span, .score-row span { color: #8994a5; font-size: 12px; }
.score-row { justify-content: flex-start; padding: 22px 28px; border: 1px solid #e7ebf0; border-radius: 10px; background: #fff; }
.score-row > div { display: grid; gap: 5px; }
.score-row > div:first-of-type { min-width: 220px; }
.score-row > div:first-of-type strong { font-size: 20px; }
.score-stat { min-width: 140px; padding-left: 26px; border-left: 1px solid #e8ebef; }
.score-stat strong { color: #1d2a3d; font-size: 28px; }
td strong, td small { display: block; }
td small { margin-top: 3px; color: #9aa3b1; font-family: ui-monospace, monospace; }
@media (max-width: 800px) { .score-row { flex-wrap: wrap; } .score-stat { border-left: 0; padding-left: 0; } }
</style>
