<template>
  <div class="subscriptions-view">
    <el-card shadow="never">
      <template #header><div class="heading"><strong>事件订阅</strong><el-button type="primary" @click="openCreate">新建订阅</el-button></div></template>
      <el-table v-loading="loading" :data="pagedSubscriptions" stripe>
        <el-table-column prop="name" label="名称" min-width="150" />
        <el-table-column prop="endpoint" label="接收地址" min-width="300" show-overflow-tooltip />
        <el-table-column label="事件类型" min-width="180"><template #default="{ row }">{{ row.eventTypes?.join('、') || '全部事件' }}</template></el-table-column>
        <el-table-column label="投递状态" width="120"><template #default="{ row }"><el-tag :type="row.lastError ? 'danger' : row.lastSuccessAt ? 'success' : 'info'">{{ deliveryLabel(row) }}</el-tag></template></el-table-column>
        <el-table-column label="启用" width="88" align="center"><template #default="{ row }"><el-switch :model-value="row.enabled" @change="value => setEnabled(row, value)" /></template></el-table-column>
        <el-table-column label="操作" width="132" align="center"><template #default="{ row }"><el-button link type="primary" @click="openEdit(row)">编辑</el-button><el-button link type="danger" @click="remove(row)">删除</el-button></template></el-table-column>
      </el-table>
      <el-empty v-if="!loading && !subscriptions.length" description="尚未创建事件订阅" />
      <TablePagination v-model:page="subscriptionPage" v-model:page-size="subscriptionPageSize" :total="subscriptionTotal" />
    </el-card>

    <el-drawer v-model="dialogVisible" :title="editingId ? '编辑事件订阅' : '新建事件订阅'" size="620px" destroy-on-close>
      <el-form label-position="top">
        <div class="form-grid">
          <el-form-item label="名称"><el-input v-model="form.name" placeholder="质量系统事件接收" /></el-form-item>
          <el-form-item label="接收地址"><el-input v-model="form.endpoint" placeholder="https://example.com/ingot/events" /></el-form-item>
          <el-form-item label="对象类型"><el-input v-model="form.subjectType" clearable placeholder="留空表示全部" /></el-form-item>
          <el-form-item label="对象 ID"><el-input v-model="form.subjectId" clearable placeholder="留空表示全部" /></el-form-item>
        </div>
        <el-form-item label="事件类型"><el-select v-model="form.eventTypes" multiple filterable allow-create default-first-option placeholder="留空表示全部事件" /></el-form-item>
        <el-form-item label="签名密钥"><el-input v-model="form.secret" type="password" show-password autocomplete="new-password" :placeholder="editingHasSecret ? '留空保留现有密钥' : '可选；用于 HMAC-SHA256 验签'" /></el-form-item>
        <el-form-item v-if="editingId && editingHasSecret"><el-checkbox v-model="form.clearSecret" :disabled="Boolean(form.secret)">清除现有签名密钥</el-checkbox></el-form-item>
        <div class="context-heading"><span>上下文过滤条件</span><el-button text type="primary" @click="contextRows.push({ key: '', value: '' })">添加条件</el-button></div>
        <div class="context-rows">
          <div v-for="(item, index) in contextRows" :key="index" class="context-row">
            <el-input v-model="item.key" placeholder="上下文键" />
            <el-input v-model="item.value" placeholder="匹配值" />
            <el-button link type="danger" @click="contextRows.splice(index, 1)">删除</el-button>
          </div>
        </div>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="save">{{ editingId ? '保存修改' : '创建订阅' }}</el-button></template>
    </el-drawer>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from "vue";
import { ElCheckbox, ElMessage, ElMessageBox } from "element-plus";
import { deleteJson, getJson, postJson, putJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const subscriptions = ref([]);
const { page: subscriptionPage, pageSize: subscriptionPageSize, total: subscriptionTotal, pagedItems: pagedSubscriptions } = useClientPagination(subscriptions);
const loading = ref(false);
const saving = ref(false);
const dialogVisible = ref(false);
const editingId = ref(null);
const editingHasSecret = ref(false);
const contextRows = ref([]);
const emptyForm = () => ({ name: "", endpoint: "", eventTypes: [], subjectType: "", subjectId: "", secret: "", clearSecret: false, startAfterIngestId: null });
const form = reactive(emptyForm());

async function load() {
  loading.value = true;
  try { subscriptions.value = (await getJson("/api/v1/subscriptions")).data || []; }
  catch (error) { ElMessage.error(error.message); }
  finally { loading.value = false; }
}
function openCreate() { editingId.value = null; editingHasSecret.value = false; Object.assign(form, emptyForm()); contextRows.value = []; dialogVisible.value = true; }
function openEdit(row) {
  editingId.value = row.subscriptionId;
  editingHasSecret.value = Boolean(row.hasSecret);
  Object.assign(form, emptyForm(), {
    name: row.name, endpoint: row.endpoint, eventTypes: [...(row.eventTypes || [])],
    subjectType: row.subjectType || "", subjectId: row.subjectId || "",
  });
  contextRows.value = Object.entries(row.context || {}).map(([key, value]) => ({ key, value }));
  dialogVisible.value = true;
}
async function save() {
  saving.value = true;
  try {
    const payload = {
      ...form,
      name: form.name.trim(), endpoint: form.endpoint.trim(),
      subjectType: form.subjectType.trim() || null, subjectId: form.subjectId.trim() || null,
      secret: form.secret || null,
      context: Object.fromEntries(contextRows.value.filter(item => item.key.trim() && item.value.trim()).map(item => [item.key.trim(), item.value.trim()])),
    };
    if (editingId.value) await putJson(`/api/v1/subscriptions/${editingId.value}`, payload);
    else await postJson("/api/v1/subscriptions", payload);
    dialogVisible.value = false;
    await load();
    ElMessage.success(editingId.value ? "事件订阅已更新" : "事件订阅已创建");
  } catch (error) { ElMessage.error(error.message); }
  finally { saving.value = false; }
}
async function setEnabled(row, enabled) {
  try { await putJson(`/api/v1/subscriptions/${row.subscriptionId}/enabled`, { enabled }); row.enabled = enabled; }
  catch (error) { ElMessage.error(error.message); }
}
async function remove(row) {
  await ElMessageBox.confirm(`删除订阅“${row.name}”后将停止投递。`, "删除事件订阅", { type: "warning" });
  try { await deleteJson(`/api/v1/subscriptions/${row.subscriptionId}`); await load(); ElMessage.success("事件订阅已删除"); }
  catch (error) { ElMessage.error(error.message); }
}
function deliveryLabel(row) { return row.lastError ? "投递异常" : row.lastSuccessAt ? "正常" : "尚未投递"; }
onMounted(load);
</script>

<style scoped>
.subscriptions-view { display: grid; gap: 18px; }.heading,.context-heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; }.form-grid { display: grid; grid-template-columns: 1fr 1.6fr; gap: 0 14px; }.context-heading { margin: 6px 0 10px; color: #697588; font-size: 13px; }.context-rows { display: grid; gap: 8px; }.context-row { display: grid; grid-template-columns: 1fr 1fr 64px; gap: 8px; }@media (max-width: 700px) { .form-grid,.context-row { grid-template-columns: 1fr; } }
</style>
