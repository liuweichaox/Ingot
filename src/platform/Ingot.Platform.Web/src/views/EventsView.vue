<template>
  <div class="events-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <div>
            <el-icon><List /></el-icon>
            <span class="title">事件查询</span>
          </div>
          <div class="actions">
            <el-tag :type="live ? 'success' : 'info'" effect="plain">
              {{ live ? "实时更新中" : "按条件查询" }}
            </el-tag>
            <el-switch v-model="live" active-text="实时" @change="toggleLive" />
            <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
          </div>
        </div>
      </template>

      <el-form :inline="true" class="filters">
        <el-form-item label="Edge">
          <el-select v-model="filters.edgeId" clearable filterable placeholder="全部节点" style="width: 180px">
            <el-option v-for="edge in edges" :key="edge.edgeId" :label="edge.edgeId" :value="edge.edgeId" />
          </el-select>
        </el-form-item>
        <el-form-item label="记录类型">
          <el-input v-model="filters.type" clearable placeholder="cycle.completed" style="width: 190px" />
        </el-form-item>
        <el-form-item label="设备或工件">
          <el-input v-model="filters.subjectId" clearable placeholder="POL-03" style="width: 150px" />
        </el-form-item>
        <el-form-item label="生产周期号">
          <el-input v-model="filters.correlationId" clearable placeholder="周期 ID" style="width: 190px" />
        </el-form-item>
        <el-form-item label="生产信息项">
          <el-input v-model="filters.contextKey" clearable placeholder="material_lot" style="width: 150px" />
        </el-form-item>
        <el-form-item label="值">
          <el-input v-model="filters.contextValue" clearable placeholder="LOT-001" style="width: 150px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="applyFilters">查询</el-button>
          <el-button :icon="RefreshLeft" @click="resetFilters">重置</el-button>
        </el-form-item>
      </el-form>

      <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="alert" />

      <div class="summary">
        <el-statistic title="当前结果" :value="events.length" />
        <el-statistic title="最新记录序号" :value="latestIngestId || 0" />
        <el-statistic title="记录类型数" :value="eventTypeCount" />
      </div>

      <el-table v-loading="loading" :data="events" stripe max-height="650">
        <el-table-column label="发生时间" width="190">
          <template #default="{ row }">{{ formatTime(row.event.occurredAt) }}</template>
        </el-table-column>
        <el-table-column prop="edgeId" label="Edge" width="130">
          <template #default="{ row }"><el-tag effect="plain">{{ row.edgeId }}</el-tag></template>
        </el-table-column>
        <el-table-column label="记录类型" min-width="190">
          <template #default="{ row }">
            <el-tag :type="eventTagType(row.event.eventType)" effect="dark">{{ row.event.eventType }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="设备或工件" min-width="180">
          <template #default="{ row }">
            <span>{{ row.event.subject.type }}/{{ row.event.subject.id }}</span>
          </template>
        </el-table-column>
        <el-table-column label="生产周期号" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            <el-link
              v-if="row.event.correlationId"
              type="primary"
              @click="openCycle(row.event.correlationId)"
            >
              {{ row.event.correlationId }}
            </el-link>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column label="生产信息" width="110">
          <template #default="{ row }">
            <json-popover label="查看" :value="row.event.context" />
          </template>
        </el-table-column>
        <el-table-column label="记录内容" width="110">
          <template #default="{ row }">
            <json-popover label="查看" :value="row.event.data" />
          </template>
        </el-table-column>
        <el-table-column prop="ingestId" label="序号" width="90" />
      </el-table>

      <el-empty v-if="!loading && events.length === 0" description="暂无符合条件的生产记录" />
    </el-card>

    <el-dialog v-model="cycleVisible" title="生产周期过程" width="900px">
      <el-timeline v-if="cycleEvents.length">
        <el-timeline-item
          v-for="item in cycleEvents"
          :key="item.ingestId"
          :timestamp="formatTime(item.event.occurredAt)"
          placement="top"
        >
          <el-card shadow="never">
            <strong>{{ item.event.eventType }}</strong>
            <span class="cycle-source">{{ item.edgeId }} · {{ item.event.subject.id }}</span>
            <pre>{{ JSON.stringify({ context: item.event.context, data: item.event.data }, null, 2) }}</pre>
          </el-card>
        </el-timeline-item>
      </el-timeline>
      <el-empty v-else description="暂无该周期的生产记录" />
      <template #footer>
        <el-button @click="cycleVisible = false">关闭</el-button>
        <el-button :disabled="!cycleContext" @click="openComparison">历史对比</el-button>
        <el-button type="primary" :icon="DocumentChecked" :disabled="!cycleContext" @click="openInspection">
          进入质量检验
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, reactive, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { DocumentChecked, List, Refresh, RefreshLeft, Search } from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import JsonPopover from "../components/JsonPopover.js";

const edges = ref([]);
const route = useRoute();
const router = useRouter();
const events = ref([]);
const loading = ref(false);
const error = ref("");
const live = ref(false);
const stream = ref(null);
const cycleVisible = ref(false);
const cycleEvents = ref([]);
const cycleContext = ref(null);
const filters = reactive({
  edgeId: "",
  type: "",
  subjectId: "",
  correlationId: String(route.query.cycleId || ""),
  contextKey: "",
  contextValue: "",
});

const latestIngestId = computed(() =>
  events.value.reduce((max, item) => Math.max(max, item.ingestId || 0), 0)
);
const eventTypeCount = computed(() =>
  new Set(events.value.map((item) => item.event.eventType)).size
);

const buildParams = () => {
  const params = new URLSearchParams();
  if (filters.edgeId) params.set("edgeId", filters.edgeId);
  if (filters.type) params.set("type", filters.type);
  if (filters.subjectId) params.set("subjectId", filters.subjectId);
  if (filters.correlationId) params.set("correlationId", filters.correlationId);
  if (filters.contextKey && filters.contextValue) {
    params.set(`ctx.${filters.contextKey}`, filters.contextValue);
  }
  return params;
};

const load = async () => {
  loading.value = true;
  error.value = "";
  try {
    const params = buildParams();
    const allEvents = [];
    let cursor = 0;
    while (true) {
      params.set("afterIngestId", String(cursor));
      params.set("limit", "500");
      const result = await getJson(`/api/v1/events?${params}`);
      const page = result.data || [];
      if (!page.length) break;
      allEvents.push(...page);
      const nextCursor = Math.max(...page.map((item) => Number(item.ingestId || 0)));
      if (nextCursor <= cursor) throw new Error("生产记录查询游标没有前进。");
      cursor = nextCursor;
      if (page.length < 500) break;
    }
    events.value = allEvents.sort((left, right) => Number(right.ingestId) - Number(left.ingestId));
  } catch (e) {
    error.value = e?.message || String(e);
  } finally {
    loading.value = false;
  }
};

const startLive = () => {
  stopLive();
  const params = buildParams();
  const source = new EventSource(`/api/v1/events/stream?${params}`);
  source.onmessage = (message) => {
    const item = JSON.parse(message.data);
    if (!events.value.some((existing) => existing.ingestId === item.ingestId)) {
      events.value = [item, ...events.value];
    }
  };
  source.onerror = () => {
    error.value = "生产记录的实时更新暂时断开，浏览器正在自动重连。";
  };
  source.onopen = () => {
    error.value = "";
  };
  stream.value = source;
};

const stopLive = () => {
  stream.value?.close();
  stream.value = null;
};

const toggleLive = async () => {
  await load();
  if (live.value) startLive();
  else stopLive();
};

const applyFilters = async () => {
  await load();
  if (live.value) startLive();
};

const resetFilters = async () => {
  Object.assign(filters, { edgeId: "", type: "", subjectId: "", correlationId: "", contextKey: "", contextValue: "" });
  await applyFilters();
};

const openCycle = async (correlationId) => {
  const result = await getJson(`/api/v1/cycles/${encodeURIComponent(correlationId)}`);
  cycleEvents.value = result.events || [];
  const workpieceId = cycleEvents.value
    .map((item) => item.event?.context?.workpiece_id)
    .find(Boolean) || "";
  cycleContext.value = {
    operationRunId: result.correlationId,
    workpieceId,
  };
  cycleVisible.value = true;
};

const openComparison = async () => {
  if (!cycleContext.value) return;
  cycleVisible.value = false;
  await router.push({ path: "/comparisons", query: { cycleId: cycleContext.value.operationRunId } });
};

const openInspection = async () => {
  if (!cycleContext.value) return;
  cycleVisible.value = false;
  await router.push({ path: "/inspections", query: cycleContext.value });
};

const formatTime = (value) => (value ? new Date(value).toLocaleString("zh-CN") : "-");
const eventTagType = (type) => {
  if (type?.startsWith("alarm.")) return "danger";
  if (type?.startsWith("diagnostic.")) return "warning";
  if (type?.endsWith(".completed") || type?.endsWith(".cleared")) return "success";
  return "primary";
};

onMounted(async () => {
  edges.value = await getJson("/api/edges").catch(() => []);
  await load();
});
onUnmounted(stopLive);
</script>

<style scoped>
.card-header,
.actions,
.summary {
  display: flex;
  align-items: center;
}
.card-header {
  justify-content: space-between;
}
.title {
  margin-left: 8px;
  font-weight: 600;
}
.actions {
  gap: 12px;
}
.filters {
  margin-bottom: 8px;
}
.alert {
  margin-bottom: 16px;
}
.summary {
  gap: 72px;
  padding: 8px 16px 20px;
}
.cycle-source {
  margin-left: 12px;
  color: #909399;
}
pre {
  white-space: pre-wrap;
  word-break: break-word;
}
:global(.json-preview) {
  margin: 0;
  max-height: 420px;
  overflow: auto;
  white-space: pre-wrap;
}
</style>
