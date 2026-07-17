<template>
  <div class="events-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <div>
            <el-icon><List /></el-icon>
            <span class="title">生产事件流</span>
          </div>
          <div class="actions">
            <el-tag :type="live ? 'success' : 'info'" effect="plain">
              {{ live ? "实时订阅中" : "查询模式" }}
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
        <el-form-item label="事件类型">
          <el-input v-model="filters.type" clearable placeholder="cycle.completed" style="width: 190px" />
        </el-form-item>
        <el-form-item label="主体">
          <el-input v-model="filters.subjectId" clearable placeholder="POL-03" style="width: 150px" />
        </el-form-item>
        <el-form-item label="上下文键">
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
        <el-statistic title="最新中心游标" :value="latestIngestId || 0" />
        <el-statistic title="事件类型数" :value="eventTypeCount" />
      </div>

      <el-table :data="events" stripe v-loading="loading" max-height="650">
        <el-table-column label="发生时间" width="190">
          <template #default="{ row }">{{ formatTime(row.event.occurredAt) }}</template>
        </el-table-column>
        <el-table-column prop="edgeId" label="Edge" width="130">
          <template #default="{ row }"><el-tag effect="plain">{{ row.edgeId }}</el-tag></template>
        </el-table-column>
        <el-table-column label="事件类型" min-width="190">
          <template #default="{ row }">
            <el-tag :type="eventTagType(row.event.eventType)" effect="dark">{{ row.event.eventType }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="主体" min-width="180">
          <template #default="{ row }">
            <span>{{ row.event.subject.type }}/{{ row.event.subject.id }}</span>
          </template>
        </el-table-column>
        <el-table-column label="关联 ID" min-width="180" show-overflow-tooltip>
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
        <el-table-column label="上下文" width="110">
          <template #default="{ row }">
            <json-popover label="查看" :value="row.event.context" />
          </template>
        </el-table-column>
        <el-table-column label="载荷" width="110">
          <template #default="{ row }">
            <json-popover label="查看" :value="row.event.data" />
          </template>
        </el-table-column>
        <el-table-column prop="ingestId" label="游标" width="90" />
      </el-table>

      <el-empty v-if="!loading && events.length === 0" description="暂无符合条件的生产事件" />
    </el-card>

    <el-dialog v-model="cycleVisible" title="周期事实链" width="900px">
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
      <el-empty v-else description="暂无周期事件" />
    </el-dialog>
  </div>
</template>

<script>
import { defineComponent, h } from "vue";
import { ElButton, ElPopover } from "element-plus";

const JsonPopover = defineComponent({
  name: "JsonPopover",
  props: { label: String, value: Object },
  setup(props) {
    return () =>
      h(
        ElPopover,
        { width: 440, trigger: "click", placement: "left" },
        {
          reference: () => h(ElButton, { text: true, type: "primary" }, () => props.label),
          default: () => h("pre", { class: "json-preview" }, JSON.stringify(props.value || {}, null, 2)),
        }
      );
  },
});

export default { components: { JsonPopover } };
</script>

<script setup>
import { computed, onMounted, onUnmounted, reactive, ref } from "vue";
import { List, Refresh, RefreshLeft, Search } from "@element-plus/icons-vue";
import { getJson } from "../api/http";

const edges = ref([]);
const events = ref([]);
const loading = ref(false);
const error = ref("");
const live = ref(false);
const stream = ref(null);
const cycleVisible = ref(false);
const cycleEvents = ref([]);
const filters = reactive({
  edgeId: "",
  type: "",
  subjectId: "",
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
    params.set("limit", "500");
    const result = await getJson(`/api/v1/events?${params}`);
    events.value = result.data || [];
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
      events.value = [item, ...events.value].slice(0, 500);
    }
  };
  source.onerror = () => {
    error.value = "实时事件流暂时断开，浏览器正在自动重连。";
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
  Object.assign(filters, { edgeId: "", type: "", subjectId: "", contextKey: "", contextValue: "" });
  await applyFilters();
};

const openCycle = async (correlationId) => {
  const result = await getJson(`/api/v1/cycles/${encodeURIComponent(correlationId)}`);
  cycleEvents.value = result.events || [];
  cycleVisible.value = true;
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
