<template>
  <div class="metrics-view">
    <el-card shadow="never" class="page-header">
      <template #header>
        <div class="card-header">
          <span>
            <el-icon><DataAnalysis /></el-icon>
            <span style="margin-left: 8px">指标监控</span>
          </span>
          <el-button type="primary" :icon="Refresh" :loading="loading" :disabled="!edgeId" @click="loadMetrics">
            刷新
          </el-button>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        style="margin-bottom: 16px"
      >
        <template #default>
          通过 <code>/api/edges/&lt;edgeId&gt;/metrics/json</code> 代理读取边缘节点的 <code>/metrics</code>
        </template>
      </el-alert>

      <el-card shadow="never" style="margin-bottom: 16px">
        <el-form :inline="true">
          <el-form-item label="选择节点">
            <el-select
              v-model="edgeId"
              placeholder="请选择边缘节点"
              :loading="edgesLoading"
              style="width: 300px"
              @change="onEdgeChange"
            >
              <el-option
                v-for="e in edges"
                :key="e.edgeId"
                :label="e.edgeId + (e.hostname ? ' (' + e.hostname + ')' : '')"
                :value="e.edgeId"
              />
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-text v-if="lastUpdate" type="info">最后更新：{{ lastUpdate }}</el-text>
          </el-form-item>
        </el-form>
      </el-card>

      <el-alert
        v-if="error"
        :title="error"
        type="error"
        :closable="false"
        show-icon
        style="margin-bottom: 16px"
      />

      <div v-if="edgeId && metrics" class="kpi-grid">
        <el-card shadow="hover" class="kpi-card">
          <div class="kpi-label">
            <el-icon><Timer /></el-icon>
            <span>采集延迟</span>
          </div>
          <div class="kpi-value">{{ fmtNum(getOne("ingot_telemetry_collection_latency_ms")) }} ms</div>
        </el-card>
        <el-card shadow="hover" class="kpi-card">
          <div class="kpi-label">
            <el-icon><TrendCharts /></el-icon>
            <span>采集频率</span>
          </div>
          <div class="kpi-value">{{ fmtNum(getOne("ingot_telemetry_collection_rate")) }} points/s</div>
        </el-card>
        <el-card shadow="hover" class="kpi-card">
          <div class="kpi-label">
            <el-icon><Box /></el-icon>
            <span>队列深度</span>
          </div>
          <div class="kpi-value">{{ fmtNum(getOne("ingot_telemetry_queue_depth")) }}</div>
        </el-card>
        <el-card shadow="hover" class="kpi-card">
          <div class="kpi-label">
            <el-icon><Warning /></el-icon>
            <span>错误总数</span>
          </div>
          <div class="kpi-value error-value">{{ fmtNum(getOne("ingot_telemetry_errors_total")) }}</div>
        </el-card>
      </div>

      <el-card v-if="edgeId && metrics" shadow="never" style="margin-top: 16px">
        <template #header>
          <div class="card-header">
            <span>指标详情</span>
            <el-input
              v-model="keyword"
              placeholder="搜索指标名称..."
              style="width: 300px"
              clearable
              :prefix-icon="Search"
            />
          </div>
        </template>

        <el-table
          :data="filteredMetricNames.map(name => ({ name, ...metrics[name] }))"
          stripe
          style="width: 100%"
          max-height="600"
        >
          <el-table-column prop="name" label="指标名称" min-width="300">
            <template #default="{ row }">
              <el-tag type="info" effect="plain">{{ row.name }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="type" label="类型" width="120">
            <template #default="{ row }">
              <el-tag v-if="row.type" type="success" size="small">{{ row.type }}</el-tag>
              <span v-else style="color: #909399">-</span>
            </template>
          </el-table-column>
          <el-table-column label="最新值" width="150">
            <template #default="{ row }">
              <el-text type="primary" style="font-weight: 600">{{ fmtLatest(row.data) }}</el-text>
            </template>
          </el-table-column>
          <el-table-column label="标签" min-width="300">
            <template #default="{ row }">
              <el-popover
                v-if="latestLabels(row.data)"
                placement="top"
                :width="400"
                trigger="hover"
              >
                <template #reference>
                  <el-tag type="warning" effect="plain" size="small">
                    {{ Object.keys(latestLabels(row.data) || {}).length }} 个标签
                  </el-tag>
                </template>
                <pre style="white-space: pre-wrap; word-break: break-word; margin: 0">{{ JSON.stringify(latestLabels(row.data), null, 2) }}</pre>
              </el-popover>
              <span v-else style="color: #909399">-</span>
            </template>
          </el-table-column>
        </el-table>

        <el-empty v-if="filteredMetricNames.length === 0" description="暂无匹配的指标" />
      </el-card>

      <el-empty v-else-if="!loading && !error && edgeId" description="暂无指标数据（或该节点不可达）" />
    </el-card>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  DataAnalysis,
  Refresh,
  Timer,
  TrendCharts,
  Box,
  Warning,
  Search,
} from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import { ElMessage } from "element-plus";

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

const filteredMetricNames = computed(() => {
  const keys = metrics.value ? Object.keys(metrics.value) : [];
  const kw = (keyword.value || "").trim().toLowerCase();
      const filtered = kw ? keys.filter((k) => k.toLowerCase().includes(kw)) : keys;
      return filtered.sort();
});

const loadEdges = async () => {
  edgesLoading.value = true;
      try {
    edges.value = await getJson("/api/edges");
      } catch {
    ElMessage.error("加载边缘节点列表失败");
      } finally {
    edgesLoading.value = false;
      }
};

const loadMetrics = async () => {
  if (!edgeId.value) return;
  loading.value = true;
  error.value = "";
      try {
    const data = await getJson(`/api/edges/${encodeURIComponent(edgeId.value)}/metrics/json`);
    metrics.value = data.metrics || {};
    lastUpdate.value = new Date(data.timestamp || Date.now()).toLocaleString("zh-CN");
      } catch (e) {
    metrics.value = null;
    error.value = e?.message || String(e);
    ElMessage.error("加载指标数据失败");
      } finally {
    loading.value = false;
      }
};

const onEdgeChange = async () => {
  router.replace({ path: "/metrics", query: { edgeId: edgeId.value } }).catch(() => {});
  await loadMetrics();
};

const latestPoint = (data) => {
      if (!Array.isArray(data) || data.length === 0) return null;
      return data[data.length - 1];
};

const latestLabels = (data) => {
  const p = latestPoint(data);
      return p && p.labels ? p.labels : null;
};

const fmtLatest = (data) => {
  const p = latestPoint(data);
      if (!p) return "-";
      const v = typeof p.value === "number" ? p.value : Number(p.value);
  return Number.isFinite(v) ? fmtNum(v) : String(p.value);
};

const fmtNum = (v) => {
      const n = typeof v === "number" ? v : Number(v);
      if (!Number.isFinite(n)) return "-";
      return n.toFixed(2);
};

const getOne = (metricName) => {
  const m = metrics.value ? metrics.value[metricName] : null;
  const p = m ? latestPoint(m.data) : null;
      return p ? p.value : null;
};

onMounted(async () => {
  await loadEdges();
  const fromQuery = route?.query?.edgeId;
  if (typeof fromQuery === "string" && fromQuery) {
    edgeId.value = fromQuery;
  } else if (edges.value.length) {
    edgeId.value = edges.value[0].edgeId;
  }
  if (edgeId.value) await loadMetrics();
});
</script>

<style scoped>
.metrics-view {
  width: 100%;
}

.page-header {
  border-radius: 8px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 600;
  font-size: 16px;
}

.kpi-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 16px;
  margin-bottom: 16px;
}

.kpi-card {
  text-align: center;
  border-radius: 8px;
}

.kpi-label {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  color: #909399;
  font-size: 14px;
  margin-bottom: 12px;
}

.kpi-value {
  font-size: 28px;
  font-weight: 700;
  color: #409eff;
}

.kpi-value.error-value {
  color: #f56c6c;
}

@media (max-width: 768px) {
  .kpi-grid {
    grid-template-columns: 1fr;
  }
}
</style>
