<template>
  <div class="edges-view">
    <el-card shadow="never" class="page-header">
      <template #header>
        <div class="card-header">
          <span>
            <el-icon><Connection /></el-icon>
            <span style="margin-left: 8px">数据接入节点</span>
          </span>
          <el-button type="primary" :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        </div>
      </template>

      <el-alert
        title="显示用户自行部署的数据适配器接入状态。"
        type="info"
        :closable="false"
        show-icon
        style="margin-bottom: 16px"
      />

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
        :data="edges"
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
                @click="$router.push({ path: '/metrics', query: { edgeId: row.edgeId } })"
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
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from "vue";
import { Connection, Refresh, DataAnalysis, Document, CircleCheck, List } from "@element-plus/icons-vue";
import { getJson } from "../api/http";
import { ElMessage } from "element-plus";

const edges = ref([]);
const loading = ref(false);
const error = ref("");

const formatTime = (timeStr) => {
  if (!timeStr) return "-";
  try {
    const date = new Date(timeStr);
    return date.toLocaleString("zh-CN");
  } catch {
    return timeStr;
  }
};

const load = async () => {
  loading.value = true;
  error.value = "";
  try {
    edges.value = await getJson("/api/edges");
  } catch (e) {
    error.value = e?.message || String(e);
    ElMessage.error("加载边缘节点列表失败");
  } finally {
    loading.value = false;
  }
};

onMounted(() => {
  load();
});
</script>

<style scoped>
.edges-view {
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
</style>
