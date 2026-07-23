<template>
  <div class="edges-view">
    <el-card shadow="never" class="page-header">
      <el-alert
        v-if="error"
        :title="error"
        type="error"
        :closable="false"
        show-icon
        style="margin-bottom: 16px"
      />

      <el-empty v-if="!loading && !error && edges.length === 0" description="暂无数据接入节点" />

      <el-table
        v-if="edges.length"
        v-loading="loading"
        :data="pagedEdges"
        stripe
        style="width: 100%"
        element-loading-text="加载中..."
      >
        <el-table-column prop="edgeId" label="节点ID" width="180">
          <template #default="{ row }">
            <el-tag type="primary" effect="plain">{{ row.edgeId }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="hostBaseUrl" label="数据适配器地址" min-width="200">
          <template #default="{ row }">
            <el-link v-if="row.hostBaseUrl" :href="row.hostBaseUrl" target="_blank" type="primary">
              {{ row.hostBaseUrl }}
            </el-link>
            <span v-else style="color: #909399">-</span>
          </template>
        </el-table-column>
        <el-table-column prop="hostname" label="主机名" width="150">
          <template #default="{ row }">
            {{ row.hostname || "-" }}
          </template>
        </el-table-column>
        <el-table-column prop="lastSeen" label="最后在线" width="180">
          <template #default="{ row }">
            {{ formatTime(row.lastSeen) }}
          </template>
        </el-table-column>
        <el-table-column prop="lastError" label="最后错误" min-width="250">
          <template #default="{ row }">
            <el-popover v-if="row.lastError" placement="top" :width="400" trigger="hover">
              <template #reference>
                <el-text type="danger" truncated style="max-width: 200px">
                  {{ row.lastError }}
                </el-text>
              </template>
              <pre style="white-space: pre-wrap; word-break: break-word; margin: 0">{{ row.lastError }}</pre>
            </el-popover>
            <span v-else style="color: #67c23a">
              <el-icon><CircleCheck /></el-icon>
              <span style="margin-left: 4px">正常</span>
            </span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="260" fixed="right">
          <template #default="{ row }">
            <el-button-group>
              <el-button
                type="warning"
                size="small"
                :icon="List"
                @click="$router.push({ path: '/events', query: { edgeId: row.edgeId } })"
              >
                生产记录
              </el-button>
              <el-button
                type="primary"
                size="small"
                :icon="DataAnalysis"
                @click="$router.push({ path: '/platform-metrics', query: { edgeId: row.edgeId } })"
              >
                指标
              </el-button>
              <el-button
                type="success"
                size="small"
                :icon="Document"
                @click="$router.push({ path: '/logs', query: { edgeId: row.edgeId } })"
              >
                日志
              </el-button>
            </el-button-group>
          </template>
        </el-table-column>
      </el-table>
      <TablePagination v-model:page="edgePage" v-model:page-size="edgePageSize" :total="edgeTotal" />
    </el-card>
  </div>
</template>

<script setup>
import { ref, onBeforeUnmount, onMounted } from "vue";
import { DataAnalysis, Document, CircleCheck, List } from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import { ElMessage } from "element-plus";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const edges = ref([]);
const { page: edgePage, pageSize: edgePageSize, total: edgeTotal, pagedItems: pagedEdges } = useClientPagination(edges);
const loading = ref(false);
const error = ref("");
let pollTimer;

const formatTime = (timeStr) => {
  if (!timeStr) return "-";
  try {
    const date = new Date(timeStr);
    return date.toLocaleString("zh-CN");
  } catch {
    return timeStr;
  }
};

const load = async ({ silent = false } = {}) => {
  if (!silent) loading.value = true;
  error.value = "";
  try {
    edges.value = await getJson("/api/edges");
  } catch (e) {
    error.value = e?.message || String(e);
    if (!silent) ElMessage.error("加载边缘节点列表失败");
  } finally {
    if (!silent) loading.value = false;
  }
};

onMounted(() => {
  load();
  pollTimer = window.setInterval(() => load({ silent: true }), 15000);
});
onBeforeUnmount(() => window.clearInterval(pollTimer));
</script>

<style scoped>
.edges-view {
  width: 100%;
}

.page-header { border-radius: 14px; }
</style>
