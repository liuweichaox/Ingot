<template>
  <div class="subscriptions-view page-stack">
    <section class="signal-strip" aria-label="事件订阅状态概览">
      <article class="signal-item">
        <small>订阅总数</small><strong>{{ summary.total }}</strong><span>外部系统接收通道</span>
      </article>
      <article class="signal-item">
        <small>已启用</small><strong>{{ summary.enabled }}</strong><span>正在匹配并投递事件</span>
      </article>
      <article class="signal-item" :class="{ warning: summary.failed }">
        <small>投递异常</small><strong>{{ summary.failed }}</strong><span>需要检查接收端</span>
      </article>
      <article class="signal-item">
        <small>签名保护</small><strong>{{ summary.signed }}</strong><span>启用 HMAC-SHA256</span>
      </article>
    </section>

    <el-card shadow="never" class="subscription-card">
      <template #header>
        <div class="card-heading">
          <div>
            <strong>订阅与投递</strong>
            <span>按事件类型和运行上下文向外部系统投递 CloudEvents</span>
          </div>
          <div class="header-actions">
            <el-button type="primary" @click="openCreate">新建订阅</el-button>
          </div>
        </div>
      </template>

      <div class="filter-bar">
        <el-input
          v-model="keyword"
          :prefix-icon="Search"
          clearable
          placeholder="搜索名称、接收地址或事件类型"
        />
        <el-select v-model="statusFilter" aria-label="订阅状态" style="width: 170px">
          <el-option label="全部状态" value="all" />
          <el-option label="已启用" value="enabled" />
          <el-option label="已停用" value="disabled" />
          <el-option label="投递异常" value="failed" />
        </el-select>
      </div>

      <el-alert
        v-if="error"
        :title="error"
        type="error"
        :closable="false"
        show-icon
        class="page-error"
      />

      <el-table v-if="pagedSubscriptions.length" v-loading="loading" :data="pagedSubscriptions">
        <el-table-column label="订阅" min-width="190">
          <template #default="{ row }">
            <div class="primary-cell">
              <strong>{{ row.name }}</strong>
              <span>创建于 {{ formatTime(row.createdAt) }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="接收地址" min-width="250">
          <template #default="{ row }">
            <div class="endpoint-cell">
              <span>{{ row.endpoint }}</span>
              <small>{{ row.hasSecret ? "请求已签名" : "未配置签名" }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="事件范围" min-width="220">
          <template #default="{ row }">
            <div class="scope-cell">
              <span>{{ eventTypeSummary(row) }}</span>
              <small>{{ subjectSummary(row) }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="投递状态" min-width="175">
          <template #default="{ row }">
            <div class="delivery-cell">
              <el-tag :type="deliveryType(row)" effect="light" round>{{ deliveryLabel(row) }}</el-tag>
              <el-popover v-if="row.lastError" placement="top" :width="400" trigger="hover">
                <template #reference>
                  <el-button link type="danger" size="small">
                    连续失败 {{ row.consecutiveFailures || 1 }} 次
                  </el-button>
                </template>
                <pre class="error-detail">{{ row.lastError }}</pre>
              </el-popover>
              <small v-else-if="row.lastSuccessAt">最近成功 {{ formatTime(row.lastSuccessAt) }}</small>
              <small v-else>游标 {{ formatInteger(row.cursor) }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="启用" width="84" align="center">
          <template #default="{ row }">
            <el-switch
              :model-value="row.enabled"
              :loading="updatingId === row.subscriptionId"
              @change="value => setEnabled(row, value)"
            />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="132" align="center" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-empty
        v-if="!loading && !error && filteredSubscriptions.length === 0"
        :description="subscriptions.length ? '没有符合筛选条件的订阅' : '尚未创建事件订阅'"
      >
        <el-button v-if="!subscriptions.length" type="primary" plain @click="openCreate">创建第一个订阅</el-button>
      </el-empty>
      <TablePagination
        v-if="filteredSubscriptions.length"
        v-model:page="subscriptionPage"
        v-model:page-size="subscriptionPageSize"
        :total="subscriptionTotal"
      />
    </el-card>

    <el-drawer
      v-model="dialogVisible"
      :title="editingId ? '编辑事件订阅' : '新建事件订阅'"
      size="620px"
      destroy-on-close
    >
      <el-form label-position="top">
        <section class="form-section">
          <div class="section-heading"><strong>接收端</strong><span>外部系统必须提供 HTTP 或 HTTPS 地址</span></div>
          <div class="form-grid">
            <el-form-item label="名称">
              <el-input v-model="form.name" placeholder="例如：质量系统事件接收" />
            </el-form-item>
            <el-form-item label="接收地址">
              <el-input v-model="form.endpoint" placeholder="https://example.com/ingot/events" />
            </el-form-item>
          </div>
          <el-form-item label="签名密钥">
            <el-input
              v-model="form.secret"
              type="password"
              show-password
              autocomplete="new-password"
              :placeholder="editingHasSecret ? '留空保留现有密钥' : '可选；用于 HMAC-SHA256 验签'"
            />
          </el-form-item>
          <el-form-item v-if="editingId && editingHasSecret">
            <el-checkbox v-model="form.clearSecret" :disabled="Boolean(form.secret)">清除现有签名密钥</el-checkbox>
          </el-form-item>
        </section>

        <section class="form-section">
          <div class="section-heading"><strong>事件范围</strong><span>条件之间为“同时满足”关系</span></div>
          <el-form-item label="事件类型">
            <el-select
              v-model="form.eventTypes"
              multiple
              filterable
              allow-create
              default-first-option
              placeholder="留空表示全部事件"
            />
          </el-form-item>
          <div class="form-grid even">
            <el-form-item label="对象类型">
              <el-input v-model="form.subjectType" clearable placeholder="留空表示全部" />
            </el-form-item>
            <el-form-item label="对象 ID">
              <el-input v-model="form.subjectId" clearable placeholder="留空表示全部" />
            </el-form-item>
          </div>
          <div class="context-heading">
            <span>上下文过滤条件</span>
            <el-button text type="primary" @click="contextRows.push({ key: '', value: '' })">添加条件</el-button>
          </div>
          <div class="context-rows">
            <div v-for="(item, index) in contextRows" :key="index" class="context-row">
              <el-input v-model="item.key" placeholder="上下文键" />
              <el-input v-model="item.value" placeholder="匹配值" />
              <el-button link type="danger" @click="contextRows.splice(index, 1)">删除</el-button>
            </div>
            <span v-if="!contextRows.length" class="empty-context">未配置上下文条件</span>
          </div>
        </section>

        <section v-if="!editingId" class="form-section">
          <div class="section-heading"><strong>起始位置</strong><span>决定首次启用时是否回放历史</span></div>
          <el-form-item label="开始投递">
            <el-select v-model="form.startMode">
              <el-option label="仅投递创建后的新事件" value="new" />
              <el-option label="从最早记录开始回放" value="history" />
            </el-select>
          </el-form-item>
        </section>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">
          {{ editingId ? "保存修改" : "创建订阅" }}
        </el-button>
      </template>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import { Search } from "@element-plus/icons-vue";
import { ElCheckbox, ElMessage, ElMessageBox } from "element-plus";
import { deleteJson, getJson, postJson, putJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const subscriptions = ref([]);
const loading = ref(false);
const saving = ref(false);
const error = ref("");
const keyword = ref("");
const statusFilter = ref("all");
const updatingId = ref(null);
const dialogVisible = ref(false);
const editingId = ref(null);
const editingHasSecret = ref(false);
const contextRows = ref([]);
let pollTimer;
const emptyForm = () => ({
  name: "",
  endpoint: "",
  eventTypes: [],
  subjectType: "",
  subjectId: "",
  secret: "",
  clearSecret: false,
  startMode: "new",
});
const form = reactive(emptyForm());

const summary = computed(() => ({
  total: subscriptions.value.length,
  enabled: subscriptions.value.filter(item => item.enabled).length,
  failed: subscriptions.value.filter(item => item.enabled && item.lastError).length,
  signed: subscriptions.value.filter(item => item.hasSecret).length,
}));
const filteredSubscriptions = computed(() => {
  const query = keyword.value.trim().toLowerCase();
  return subscriptions.value.filter(item => {
    if (statusFilter.value === "enabled" && !item.enabled) return false;
    if (statusFilter.value === "disabled" && item.enabled) return false;
    if (statusFilter.value === "failed" && !(item.enabled && item.lastError)) return false;
    const searchable = [item.name, item.endpoint, ...(item.eventTypes || [])].join(" ").toLowerCase();
    return !query || searchable.includes(query);
  });
});
const {
  page: subscriptionPage,
  pageSize: subscriptionPageSize,
  total: subscriptionTotal,
  pagedItems: pagedSubscriptions,
  resetPage,
} = useClientPagination(filteredSubscriptions);

async function load({ silent = false } = {}) {
  if (!silent) loading.value = true;
  error.value = "";
  try {
    subscriptions.value = (await getJson("/api/v1/subscriptions")).data || [];
  } catch (cause) {
    error.value = cause.message;
    if (!silent) ElMessage.error("加载事件订阅失败");
  } finally {
    if (!silent) loading.value = false;
  }
}

function openCreate() {
  editingId.value = null;
  editingHasSecret.value = false;
  Object.assign(form, emptyForm());
  contextRows.value = [];
  dialogVisible.value = true;
}

function openEdit(row) {
  editingId.value = row.subscriptionId;
  editingHasSecret.value = Boolean(row.hasSecret);
  Object.assign(form, emptyForm(), {
    name: row.name,
    endpoint: row.endpoint,
    eventTypes: [...(row.eventTypes || [])],
    subjectType: row.subjectType || "",
    subjectId: row.subjectId || "",
  });
  contextRows.value = Object.entries(row.context || {}).map(([key, value]) => ({ key, value }));
  dialogVisible.value = true;
}

async function save() {
  saving.value = true;
  try {
    const payload = {
      ...form,
      name: form.name.trim(),
      endpoint: form.endpoint.trim(),
      subjectType: form.subjectType.trim() || null,
      subjectId: form.subjectId.trim() || null,
      secret: form.secret || null,
      clearSecret: form.secret ? false : form.clearSecret,
      startAfterIngestId: form.startMode === "history" ? 0 : null,
      context: Object.fromEntries(contextRows.value
        .filter(item => item.key.trim() && item.value.trim())
        .map(item => [item.key.trim(), item.value.trim()])),
    };
    if (editingId.value) await putJson(`/api/v1/subscriptions/${editingId.value}`, payload);
    else await postJson("/api/v1/subscriptions", payload);
    dialogVisible.value = false;
    await load();
    ElMessage.success(editingId.value ? "事件订阅已更新" : "事件订阅已创建");
  } catch (cause) {
    ElMessage.error(cause.message);
  } finally {
    saving.value = false;
  }
}

async function setEnabled(row, enabled) {
  updatingId.value = row.subscriptionId;
  try {
    await putJson(`/api/v1/subscriptions/${row.subscriptionId}/enabled`, { enabled });
    row.enabled = enabled;
    ElMessage.success(enabled ? "事件订阅已启用" : "事件订阅已停用");
  } catch (cause) {
    ElMessage.error(cause.message);
  } finally {
    updatingId.value = null;
  }
}

async function remove(row) {
  await ElMessageBox.confirm(`删除订阅“${row.name}”后将停止投递。`, "删除事件订阅", { type: "warning" });
  try {
    await deleteJson(`/api/v1/subscriptions/${row.subscriptionId}`);
    await load();
    ElMessage.success("事件订阅已删除");
  } catch (cause) {
    ElMessage.error(cause.message);
  }
}

function deliveryLabel(row) {
  if (!row.enabled) return "已停用";
  if (row.lastError) return "投递异常";
  return row.lastSuccessAt ? "投递正常" : "等待事件";
}

function deliveryType(row) {
  if (!row.enabled) return "info";
  if (row.lastError) return "danger";
  return row.lastSuccessAt ? "success" : "warning";
}

function eventTypeSummary(row) {
  const eventTypes = row.eventTypes || [];
  if (!eventTypes.length) return "全部事件类型";
  if (eventTypes.length <= 2) return eventTypes.join("、");
  return `${eventTypes.slice(0, 2).join("、")} 等 ${eventTypes.length} 类`;
}

function subjectSummary(row) {
  const subject = [row.subjectType, row.subjectId].filter(Boolean).join("/");
  const contextCount = Object.keys(row.context || {}).length;
  if (subject && contextCount) return `${subject} · ${contextCount} 项上下文条件`;
  if (subject) return subject;
  if (contextCount) return `${contextCount} 项上下文条件`;
  return "全部运行对象";
}

function formatTime(value) {
  return value ? new Date(value).toLocaleString("zh-CN", { hour12: false }) : "-";
}

function formatInteger(value) {
  return new Intl.NumberFormat("zh-CN", { maximumFractionDigits: 0 }).format(Number(value) || 0);
}

watch([keyword, statusFilter], resetPage);
onMounted(() => {
  load();
  pollTimer = window.setInterval(() => load({ silent: true }), 15000);
});
onBeforeUnmount(() => window.clearInterval(pollTimer));
</script>

<style scoped>
.subscriptions-view { width: 100%; }
.signal-strip { display: grid; overflow: hidden; grid-template-columns: repeat(4, minmax(0, 1fr)); border: 1px solid var(--ingot-border); border-radius: 14px; background: var(--ingot-surface); }
.signal-item { display: grid; min-height: 94px; gap: 3px; padding: 16px 20px; border-right: 1px solid var(--ingot-border); }
.signal-item:last-child { border-right: 0; }
.signal-item.warning { background: #fffafa; }
.signal-item small { color: #778397; }
.signal-item strong { color: var(--ingot-ink); font-size: 24px; line-height: 1.15; }
.signal-item.warning strong { color: #ce5555; }
.signal-item span { color: #9aa3b1; font-size: 12px; }
.subscription-card { overflow: hidden; }
.card-heading, .header-actions, .filter-bar, .context-heading, .section-heading { display: flex; align-items: center; }
.card-heading { justify-content: space-between; gap: 16px; }
.card-heading > div:first-child, .section-heading { display: grid; gap: 3px; }
.card-heading strong { color: var(--ingot-ink); font-size: 16px; }
.card-heading span, .section-heading span { color: #8a95a5; font-size: 12px; }
.header-actions, .filter-bar { gap: 10px; }
.filter-bar { padding-bottom: 16px; }
.filter-bar .el-input { max-width: 360px; }
.page-error { margin-bottom: 16px; }
.primary-cell, .endpoint-cell, .scope-cell, .delivery-cell { display: grid; gap: 4px; }
.primary-cell strong { color: #2a3446; }
.primary-cell span, .endpoint-cell small, .scope-cell small, .delivery-cell small { color: #8a95a5; font-size: 12px; }
.endpoint-cell span { overflow: hidden; color: #37678f; text-overflow: ellipsis; white-space: nowrap; }
.scope-cell > span { color: #425066; }
.delivery-cell { justify-items: start; }
.delivery-cell .el-button { height: auto; padding: 0; }
.error-detail { margin: 0; white-space: pre-wrap; word-break: break-word; }
.form-section { margin-bottom: 16px; padding: 16px; border: 1px solid var(--ingot-border); border-radius: 11px; background: #fff; }
.section-heading { margin-bottom: 14px; }
.form-grid { display: grid; grid-template-columns: 1fr 1.6fr; gap: 0 14px; }
.form-grid.even { grid-template-columns: 1fr 1fr; }
.context-heading { justify-content: space-between; margin: 4px 0 10px; color: #697588; font-size: 13px; }
.context-rows { display: grid; gap: 8px; }
.context-row { display: grid; grid-template-columns: 1fr 1fr 64px; gap: 8px; }
.empty-context { padding: 10px 0; color: #9aa3b1; font-size: 12px; }
@media (max-width: 900px) {
  .signal-strip { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .signal-item { border-bottom: 1px solid var(--ingot-border); }
  .signal-item:nth-child(2) { border-right: 0; }
}
@media (max-width: 700px) {
  .card-heading { align-items: flex-start; flex-direction: column; }
  .filter-bar, .form-grid, .form-grid.even, .context-row { grid-template-columns: 1fr; }
  .filter-bar { align-items: stretch; flex-direction: column; }
  .filter-bar .el-input { max-width: none; }
}
</style>
