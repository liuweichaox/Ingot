<template>
  <div class="logs-view page-stack">
    <div class="logs-shell">
      <section class="filter-panel">
        <div class="filter-heading">
          <div>
            <strong>节点日志</strong>
            <span>查询现场采集服务的结构化运行记录</span>
          </div>
          <div class="tracking-control">
            <span>{{ autoRefresh ? "实时追踪" : "已暂停" }}</span>
            <el-switch
              v-model="autoRefresh"
              aria-label="实时追踪"
              :disabled="!edgeId"
              @change="toggleAutoRefresh"
            />
          </div>
        </div>
        <el-form :inline="true" label-width="80px" class="filter-form">
          <el-form-item label="采集节点">
            <el-select
              v-model="edgeId"
              placeholder="请选择采集节点"
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
          <el-form-item label="日志级别">
            <el-select
              v-model="level"
              placeholder="全部"
              :disabled="!edgeId"
              style="width: 150px"
              clearable
              @change="applyFilters"
            >
              <el-option
                v-for="l in levels"
                :key="l"
                :label="l"
                :value="l"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="关键字">
            <el-input
              v-model="keyword"
              placeholder="搜索消息内容..."
              :disabled="!edgeId"
              style="width: 300px"
              clearable
              :prefix-icon="Search"
              @keyup.enter="applyFilters"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :icon="Search" :disabled="loading || !edgeId" @click="applyFilters">
              搜索
            </el-button>
            <el-button :disabled="loading || !edgeId" @click="clearFilters">
              清空
            </el-button>
          </el-form-item>
        </el-form>
      </section>

      <el-alert
        v-if="error"
        :title="error"
        type="error"
        :closable="false"
        show-icon
        style="margin-bottom: 16px"
      />

      <el-card v-if="!edgesLoading && edges.length === 0" shadow="never" class="empty-card">
        <el-empty description="尚无可查询日志的数据接入节点">
          <el-button type="primary" plain @click="$router.push('/edges')">查看数据接入</el-button>
        </el-empty>
      </el-card>

      <el-row v-if="edgeId && total > 0" :gutter="16" class="log-stats">
        <el-col :span="8">
          <el-statistic title="本页 Information" :value="stats.information">
            <template #prefix>
              <el-icon style="color: #409eff"><InfoFilled /></el-icon>
            </template>
          </el-statistic>
        </el-col>
        <el-col :span="8">
          <el-statistic title="本页 Warning" :value="stats.warning">
            <template #prefix>
              <el-icon style="color: #e6a23c"><WarningFilled /></el-icon>
            </template>
          </el-statistic>
        </el-col>
        <el-col :span="8">
          <el-statistic title="本页 Error/Fatal" :value="stats.error">
            <template #prefix>
              <el-icon style="color: #f56c6c"><CircleCloseFilled /></el-icon>
            </template>
          </el-statistic>
        </el-col>
      </el-row>

      <el-card v-if="edgeId" shadow="never">
        <template #header>
          <div class="card-header">
            <div>
              <strong>日志记录</strong>
              <span v-if="lastUpdate">更新于 {{ lastUpdate }}</span>
            </div>
            <span class="result-count">共 {{ total }} 条</span>
          </div>
        </template>

        <el-empty v-if="!loading && logs.length === 0" description="暂无日志数据" />

        <div v-loading="loading && logs.length === 0" element-loading-text="加载日志中...">
          <div v-for="(log, idx) in logs" :key="idx" class="log-item" :class="`log-${(log.level || '').toLowerCase()}`">
            <div class="log-header">
              <span class="log-time">{{ fmtTs(log.timestamp) }}</span>
              <el-tag
                :type="getLevelType(log.level)"
                size="small"
                effect="dark"
              >
                {{ log.level }}
              </el-tag>
              <el-tag v-if="log.source" type="info" size="small" effect="plain">
                {{ log.source }}
              </el-tag>
            </div>
            <div class="log-message">{{ log.message }}</div>
            <div v-if="log.exception" class="log-exception">
              <el-collapse>
                <el-collapse-item :name="idx">
                  <template #title>
                    <el-button text type="primary" size="small">异常详情</el-button>
                  </template>
                  <pre class="exception-content">{{ log.exception }}</pre>
                </el-collapse-item>
              </el-collapse>
            </div>
          </div>
        </div>

        <div v-if="logs.length > 0" style="margin-top: 16px; text-align: center">
          <el-pagination
            v-model:current-page="page"
            v-model:page-size="pageSize"
            :page-sizes="[50, 100, 200, 500]"
            :total="total"
            layout="total, sizes, prev, pager, next, jumper"
            @size-change="onPageSizeChange"
            @current-change="onPageChange"
          />
        </div>
      </el-card>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  Search,
  InfoFilled,
  WarningFilled,
  CircleCloseFilled,
} from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import { ElMessage } from "element-plus";

const route = useRoute();
const router = useRouter();

const edgesLoading = ref(false);
const edges = ref([]);
const edgeId = ref("");
const levels = ref([]);
const level = ref("");
const keyword = ref("");
const logs = ref([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(100);
const loading = ref(false);
const error = ref("");
const lastUpdate = ref("");
const autoRefresh = ref(true);
const timer = ref(null);

const stats = computed(() => {
  const out = { information: 0, warning: 0, error: 0 };
  for (const l of logs.value) {
    const lv = (l.level || "").toLowerCase();
    if (lv === "information" || lv === "info") out.information += 1;
    else if (lv === "warning" || lv === "warn") out.warning += 1;
    else if (lv === "error" || lv === "fatal") out.error += 1;
  }
  return out;
});

const getLevelType = (level) => {
  const lv = (level || "").toLowerCase();
  if (lv === "information" || lv === "info") return "info";
  if (lv === "warning" || lv === "warn") return "warning";
  if (lv === "error" || lv === "fatal") return "danger";
  return "";
};

const loadEdges = async () => {
  edgesLoading.value = true;
  try {
    const payload = await getJson("/api/edges");
    edges.value = Array.isArray(payload) ? payload : payload.data || [];
  } catch {
    ElMessage.error("加载采集节点失败");
  } finally {
    edgesLoading.value = false;
  }
};

const loadLevels = async () => {
  if (!edgeId.value) return;
  try {
    levels.value = await getJson(`/api/edges/${encodeURIComponent(edgeId.value)}/logs/levels`);
  } catch {
    levels.value = [];
  }
};

const loadLogs = async () => {
  if (!edgeId.value) return;
  loading.value = true;
  error.value = "";
  try {
    const params = new URLSearchParams({
      page: String(page.value),
      pageSize: String(pageSize.value),
    });
    if (level.value) params.set("level", level.value);
    if (keyword.value) params.set("keyword", keyword.value);

    const data = await getJson(`/api/edges/${encodeURIComponent(edgeId.value)}/logs?${params.toString()}`);
    logs.value = data.data || [];
    total.value = data.total || 0;
    lastUpdate.value = new Date().toLocaleTimeString("zh-CN", { hour12: false });
  } catch (e) {
    logs.value = [];
    total.value = 0;
    error.value = e?.message || String(e);
    ElMessage.error("加载日志失败");
  } finally {
    loading.value = false;
  }
};

const onEdgeChange = async () => {
  router.replace({ path: "/logs", query: { edgeId: edgeId.value } }).catch(() => {});
  page.value = 1;
  level.value = "";
  keyword.value = "";
  await loadLevels();
  await loadLogs();
};

const applyFilters = async () => {
  page.value = 1;
  await loadLogs();
};

const onPageSizeChange = async () => {
  page.value = 1;
  await loadLogs();
};

const onPageChange = async () => {
  if (page.value > 1 && autoRefresh.value) {
    autoRefresh.value = false;
    toggleAutoRefresh();
  }
  await loadLogs();
};

const clearFilters = async () => {
  level.value = "";
  keyword.value = "";
  page.value = 1;
  await loadLogs();
};

const toggleAutoRefresh = () => {
  if (autoRefresh.value) {
    if (timer.value) clearInterval(timer.value);
    timer.value = setInterval(() => loadLogs(), 5000);
  } else {
    if (timer.value) clearInterval(timer.value);
    timer.value = null;
  }
};

const fmtTs = (ts) => {
  if (!ts) return "";
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return String(ts);
  const pad = (n, w = 2) => String(n).padStart(w, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(
    d.getMinutes()
  )}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`;
};

onMounted(async () => {
  await loadEdges();
  const fromQuery = route?.query?.edgeId;
  edgeId.value = typeof fromQuery === "string" && edges.value.some(edge => edge.edgeId === fromQuery)
    ? fromQuery
    : edges.value[0]?.edgeId || "";
  if (edgeId.value) {
    await loadLevels();
    await loadLogs();
  }
  toggleAutoRefresh();
});

onUnmounted(() => {
  if (timer.value) clearInterval(timer.value);
});
</script>

<style scoped>
.logs-view {
  width: 100%;
}

.filter-heading,
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
}

.logs-shell { display: grid; gap: 16px; }
.filter-heading > div:first-child, .card-header > div { display: grid; gap: 3px; }
.filter-heading strong, .card-header strong { color: var(--ingot-ink); font-size: 16px; }
.filter-heading span, .card-header span { color: #8a95a5; font-size: 12px; }
.tracking-control { display: flex; align-items: center; gap: 9px; }
.tracking-control span { color: #667286; font-size: 12px; }
.filter-panel { padding: 18px 20px 4px; border: 1px solid var(--ingot-border); border-radius: 14px; background: #fff; }
.filter-form { margin-top: 16px; }
.empty-card { min-height: 320px; }
.log-stats { margin-bottom: 0; padding: 16px 20px; border: 1px solid var(--ingot-border); border-radius: 14px; background: #fff; }
.result-count { padding: 4px 9px; border-radius: 99px; background: #f3f6f9; }

.log-item {
  margin-bottom: 10px;
  padding: 12px 16px;
  border: 1px solid var(--ingot-border);
  border-left: 4px solid #dcdfe6;
  border-radius: 9px;
  background: #fff;
  transition: box-shadow .2s ease;
}

.log-item:hover {
  box-shadow: 0 4px 14px rgba(43, 55, 75, .08);
}

.log-item.log-information {
  border-left-color: #409eff;
}

.log-item.log-warning {
  border-left-color: #e6a23c;
}

.log-item.log-error,
.log-item.log-fatal {
  border-left-color: #f56c6c;
  background: #fef0f0;
}

.log-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
  flex-wrap: wrap;
}

.log-time {
  color: #909399;
  font-size: 12px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
}

.log-message {
  margin-bottom: 8px;
  color: #4e596a;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
}

.log-exception {
  margin-top: 8px;
}

.exception-content {
  max-height: 400px;
  overflow: auto;
  margin: 8px 0 0;
  padding: 12px;
  border-radius: 4px;
  background: #f5f7fa;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}

@media (max-width: 760px) {
  .filter-heading, .card-header { align-items: flex-start; flex-direction: column; }
}
</style>
