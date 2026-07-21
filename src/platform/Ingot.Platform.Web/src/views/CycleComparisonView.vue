<template>
  <div class="comparison-view">
    <el-card shadow="never" class="query-card">
      <template #header>
        <div class="card-heading">
          <div>
            <strong>同产品系列历史周期比较</strong>
            <p>所有秒级采样均参与服务端确定性计算，500 只作为传输分页大小。</p>
          </div>
        </div>
      </template>
      <el-form :inline="true" @submit.prevent>
        <el-form-item label="基准周期号">
          <el-input v-model="baselineCycleId" placeholder="CYCLE-20260720-P01-0001" clearable class="cycle-input" />
        </el-form-item>
        <el-form-item label="历史周期数">
          <el-input-number v-model="historyLimit" :min="1" :max="50" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" :disabled="!baselineCycleId.trim()" @click="loadComparison">
            开始比较
          </el-button>
        </el-form-item>
      </el-form>
      <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    </el-card>

    <template v-if="comparison">
      <el-row :gutter="16" class="summary-row">
        <el-col v-for="item in summaryCards" :key="item.label" :xs="12" :sm="8" :lg="4">
          <el-card shadow="never" class="summary-card">
            <span>{{ item.label }}</span>
            <strong>{{ item.value }}</strong>
          </el-card>
        </el-col>
      </el-row>

      <el-card shadow="never">
        <template #header>
          <div class="card-heading">
            <div>
              <strong>{{ comparison.productSeries }} 周期明细</strong>
              <p>基准周期与同系列最近 {{ comparison.historicalCycles.length }} 个周期。</p>
            </div>
          </div>
        </template>
        <el-table :data="comparisonRows" stripe>
          <el-table-column type="expand" width="44">
            <template #default="{ row }">
              <div class="signal-grid">
                <div v-for="signal in row.signals || []" :key="signal.code" class="signal-item">
                  <strong>{{ signal.name || signal.code }}</strong>
                  <span>均值 {{ number(signal.average) }} {{ signal.unit || '' }}</span>
                  <span>最小 {{ number(signal.minimum) }} / 最大 {{ number(signal.maximum) }} {{ signal.unit || '' }}</span>
                  <span>{{ signal.sampleCount }} 个有效值</span>
                </div>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="角色" width="90">
            <template #default="{ row }">
              <el-tag :type="row.isBaseline ? 'primary' : 'info'">{{ row.isBaseline ? '基准' : '历史' }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="correlationId" label="周期" min-width="210" show-overflow-tooltip />
          <el-table-column prop="machineId" label="设备" width="150" />
          <el-table-column prop="startedAt" label="开始时间" width="180">
            <template #default="{ row }">{{ time(row.startedAt) }}</template>
          </el-table-column>
          <el-table-column label="采样完整率" width="130">
            <template #default="{ row }">
              <el-tag :type="row.sampleCompleteness >= 1 ? 'success' : 'danger'">
                {{ percent(row.sampleCompleteness) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="阶段" width="90">
            <template #default="{ row }">
              <el-tag :type="row.phaseComplete ? 'success' : 'warning'">
                {{ row.requiredPhaseCount ? `${row.phaseCount}/${row.requiredPhaseCount}` : `${row.phaseCount}/未配置` }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="配方" width="150">
            <template #default="{ row }">{{ row.recipeId || '-' }} v{{ row.recipeVersion || '-' }}</template>
          </el-table-column>
          <el-table-column label="检测" width="120">
            <template #default="{ row }">{{ (row.inspectionOutcomes || []).join(' / ') || '待检' }}</template>
          </el-table-column>
          <el-table-column label="原图复核" width="150">
            <template #default="{ row }">
              <el-tag :type="reviewTag(row.visualReviewDecision)">{{ reviewLabel(row.visualReviewDecision) }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="110" fixed="right">
            <template #default="{ row }">
              <el-button text type="primary" @click="useAsBaseline(row.correlationId)">设为基准</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>
    </template>

    <el-empty v-else-if="!loading" description="输入一个生产周期号，比较同产品系列历史周期" />
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { getJson } from "../api/http";

const route = useRoute();
const router = useRouter();
const baselineCycleId = ref("");
const historyLimit = ref(12);
const comparison = ref(null);
const loading = ref(false);
const error = ref("");

const comparisonRows = computed(() => comparison.value ? [
  { ...comparison.value.baseline, isBaseline: true },
  ...(comparison.value.historicalCycles || []).map((row) => ({ ...row, isBaseline: false })),
] : []);

const summaryCards = computed(() => {
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

async function loadComparison() {
  loading.value = true;
  error.value = "";
  try {
    comparison.value = await getJson(`/api/v1/cycle-comparisons/${encodeURIComponent(baselineCycleId.value.trim())}?limit=${historyLimit.value}`);
    await router.replace({ path: "/comparisons", query: { cycleId: baselineCycleId.value.trim() } });
  } catch (requestError) {
    comparison.value = null;
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

async function useAsBaseline(cycleId) {
  baselineCycleId.value = cycleId;
  await loadComparison();
}

function percent(value) { return `${((value || 0) * 100).toFixed(1)}%`; }
function number(value) { return value == null ? "-" : Number(value).toFixed(3); }
function time(value) { return value ? new Date(value).toLocaleString("zh-CN") : "-"; }
function reviewLabel(value) {
  return { CONFIRMED: "已确认", REJECTED: "已驳回", REINSPECTION_REQUIRED: "要求重检" }[value] || "待复核";
}
function reviewTag(value) {
  return { CONFIRMED: "success", REJECTED: "danger", REINSPECTION_REQUIRED: "warning" }[value] || "info";
}
onMounted(async () => {
  baselineCycleId.value = String(route.query.cycleId || "").trim();
  if (baselineCycleId.value) await loadComparison();
});
</script>

<style scoped>
.comparison-view { display: grid; gap: 18px; }
.card-heading { display: flex; align-items: center; justify-content: space-between; }
.card-heading p { margin: 5px 0 0; color: #8490a3; font-size: 12px; }
.cycle-input { width: 320px; }
.summary-row { row-gap: 16px; }
.summary-card :deep(.el-card__body) { display: grid; gap: 8px; }
.summary-card span { color: #8a95a5; font-size: 12px; }
.summary-card strong { color: #182238; font-size: 22px; }
.signal-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; padding: 14px 18px; }
.signal-item { display: grid; gap: 5px; padding: 12px; border: 1px solid #e7ebf0; border-radius: 8px; background: #fafbfd; }
.signal-item span { color: #6e7888; font-size: 12px; }
@media (max-width: 800px) { .cycle-input { width: 100%; } }
</style>
