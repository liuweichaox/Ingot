<template>
  <div class="logs-view">
    <el-card shadow="never" class="page-header">
      <template #header>
        <div class="card-header">
          <span>
            <el-icon><Document /></el-icon>
            <span style="margin-left: 8px">日志查看</span>
          </span>
      <div>
            <el-switch
              v-model="autoRefresh"
              active-text="自动刷新"
              inactive-text="手动刷新"
              @change="toggleAutoRefresh"
              style="margin-right: 12px"
            />
            <el-button type="primary" :icon="Refresh" @click="loadLogs" :loading="loading" :disabled="!edgeId">
              刷新
            </el-button>
      </div>
      </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        style="margin-bottom: 16px"
      >
        <template #default>
          通过 <code>/api/edges/&lt;edgeId&gt;/logs</code> 代理读取 Edge.Agent 日志
        </template>
      </el-alert>

      <el-card shadow="never" style="margin-bottom: 16px">
        <el-form :inline="true" label-width="80px">
          <el-form-item label="选择节点">
            <el-select
              v-model="edgeId"
              placeholder="请选择边缘节点"
              @change="onEdgeChange"
              :loading="edgesLoading"
              style="width: 300px"
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
              @change="applyFilters"
              :disabled="!edgeId"
              style="width: 150px"
              clearable
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
              @keyup.enter="applyFilters"
              :disabled="!edgeId"
              style="width: 300px"
              clearable
              :prefix-icon="Search"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :icon="Search" @click="applyFilters" :disabled="loading || !edgeId">
              搜索
            </el-button>
            <el-button :icon="RefreshLeft" @click="clearFilters" :disabled="loading || !edgeId">
              清空
            </el-button>
          </el-form-item>
        </el-form>
        <div style="margin-top: 12px; color: #909399; font-size: 12px">
        <span v-if="lastUpdate">最后更新：{{ lastUpdate }}</span>
          <span v-if="total > 0" style="margin-left: 16px">共 {{ total }} 条</span>
        </div>
      </el-card>

      <el-alert
        v-if="error"
        :title="error"
        type="error"
        :closable="false"
        show-icon
        style="margin-bottom: 16px"
      />

      <el-row v-if="edgeId && total > 0" :gutter="16" style="margin-bottom: 16px">
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
            <span>日志列表</span>
            <el-pagination
              v-model:current-page="page"
              v-model:page-size="pageSize"
              :page-sizes="[50, 100, 200, 500]"
              :total="total"
              layout="sizes, prev, pager, next"
              @size-change="applyFilters"
              @current-change="applyFilters"
            />
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
                    <el-button text type="primary" size="small">
                      {{ expanded[idx] ? "隐藏异常" : "显示异常" }}
                    </el-button>
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
            @size-change="applyFilters"
            @current-change="applyFilters"
          />
        </div>
      </el-card>
    </el-card>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  Document,
  Refresh,
  Search,
  RefreshLeft,
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
const totalPages = ref(1);
const loading = ref(false);
const error = ref("");
const lastUpdate = ref("");
const autoRefresh = ref(false);
const timer = ref(null);
const expanded = ref({});

const stats = computed(() => {
      const out = { information: 0, warning: 0, error: 0 };
  for (const l of logs.value) {
        const lv = (l.level || "").toLowerCase();
        if (lv === "information") out.information += 1;
        else if (lv === "warning") out.warning += 1;
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
    edges.value = await getJson("/api/edges");
      } catch (e) {
    ElMessage.error("加载边缘节点列表失败");
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
    totalPages.value = data.totalPages || 1;
    lastUpdate.value = new Date().toLocaleString("zh-CN");
    expanded.value = {};
      } catch (e) {
    logs.value = [];
    total.value = 0;
    totalPages.value = 1;
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

const clearFilters = async () => {
  level.value = "";
  keyword.value = "";
  page.value = 1;
  await loadLogs();
};

const toggleAutoRefresh = () => {
  if (autoRefresh.value) {
    if (timer.value) clearInterval(timer.value);
    timer.value = setInterval(() => loadLogs(), 1000);
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
  if (typeof fromQuery === "string" && fromQuery) {
    edgeId.value = fromQuery;
  } else if (edges.value.length) {
    edgeId.value = edges.value[0].edgeId;
  }
  if (edgeId.value) {
    await loadLevels();
    await loadLogs();
  }
});

onUnmounted(() => {
  if (timer.value) clearInterval(timer.value);
});
</script>

<style scoped>
.logs-view {
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

.log-item {
  border: 1px solid #e4e7ed;
  border-left: 4px solid #dcdfe6;
  border-radius: 6px;
  padding: 12px 16px;
  margin-bottom: 12px;
  background: #fff;
  transition: all 0.3s;
}

.log-item:hover {
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
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
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.6;
  font-size: 13px;
  color: #606266;
  margin-bottom: 8px;
}

.log-exception {
  margin-top: 8px;
}

.exception-content {
  margin: 8px 0 0;
  padding: 12px;
  background: #f5f7fa;
  border-radius: 4px;
  overflow: auto;
  max-height: 400px;
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 12px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
  line-height: 1.5;
}
</style>
