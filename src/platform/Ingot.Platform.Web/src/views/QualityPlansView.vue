<template>
  <div class="quality-plans-view">
    <el-card shadow="never">
      <template #header>
        <div class="heading">
          <strong>质量方案</strong>
          <el-button type="primary" @click="createPlan">新建方案</el-button>
        </div>
      </template>
      <el-table v-loading="loading" :data="pagedQualityPlans" stripe>
        <el-table-column prop="name" label="名称" min-width="160" />
        <el-table-column label="版本" width="76">
          <template #default="{ row }">v{{ row.version }}</template>
        </el-table-column>
        <el-table-column label="状态" width="92">
          <template #default="{ row }">
            <el-tag :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="项目" width="72">
          <template #default="{ row }">{{ row.items?.length || 0 }}</template>
        </el-table-column>
        <el-table-column label="操作" width="100" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="selectPlan(row)">{{ row.status === 'draft' ? '编辑' : '查看' }}</el-button></template></el-table-column>
      </el-table>
      <el-empty v-if="!loading && !plans.length" description="尚未配置质量方案" />
      <TablePagination v-model:page="qualityPlanPage" v-model:page-size="qualityPlanPageSize" :total="qualityPlanTotal" />
    </el-card>

    <el-drawer v-model="editorVisible" :title="editorTitle" size="84%" destroy-on-close>
      <el-card shadow="never">
        <template #header>
          <div class="heading">
            <strong>{{ editorTitle }}</strong>
            <div>
              <el-button @click="$router.push('/configuration/inspection-definitions')">管理检测定义</el-button>
              <el-button v-if="form.status !== 'draft'" @click="createNextVersion">创建新版本</el-button>
            </div>
          </div>
        </template>

        <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="notice" />

        <el-form label-position="top" :disabled="form.status !== 'draft'">
          <div class="meta-grid">
            <el-form-item label="方案 ID">
              <el-input v-model="form.planId" placeholder="quality.standard" />
            </el-form-item>
            <el-form-item label="版本">
              <el-input-number v-model="form.version" :min="1" controls-position="right" />
            </el-form-item>
            <el-form-item label="名称">
              <el-input v-model="form.name" placeholder="标准终检方案" />
            </el-form-item>
            <el-form-item label="优先级">
              <el-input-number v-model="form.priority" controls-position="right" />
            </el-form-item>
          </div>

          <div class="effective-grid">
            <el-form-item label="生效时间">
              <el-date-picker v-model="form.effectiveFrom" type="datetime" value-format="YYYY-MM-DDTHH:mm:ssZ" clearable />
            </el-form-item>
            <el-form-item label="失效时间">
              <el-date-picker v-model="form.effectiveTo" type="datetime" value-format="YYYY-MM-DDTHH:mm:ssZ" clearable />
            </el-form-item>
          </div>

          <el-form-item label="说明">
            <el-input v-model="form.description" type="textarea" :rows="2" maxlength="1000" show-word-limit />
          </el-form-item>

          <div class="section-title">
            <div><strong>适用范围</strong><span>留空表示通配；条件越具体，匹配优先级越高。</span></div>
          </div>
          <div class="scope-grid">
            <el-form-item label="产品系列"><el-input v-model="form.scope.productSeries" clearable /></el-form-item>
            <el-form-item label="产品型号"><el-input v-model="form.scope.productCode" clearable /></el-form-item>
            <el-form-item label="配方 ID"><el-input v-model="form.scope.recipeId" clearable /></el-form-item>
            <el-form-item label="设备 ID"><el-input v-model="form.scope.machineId" clearable /></el-form-item>
          </div>
          <div class="context-heading"><span>其他上下文条件</span><el-button text type="primary" @click="contextRows.push({ key: '', value: '' })">添加条件</el-button></div>
          <div class="context-rows">
            <div v-for="(item, index) in contextRows" :key="index" class="context-row">
              <el-input v-model="item.key" placeholder="上下文键，例如 material_grade" />
              <el-input v-model="item.value" placeholder="匹配值" />
              <el-button link type="danger" @click="contextRows.splice(index, 1)">删除</el-button>
            </div>
          </div>

          <div class="section-title">
            <div><strong>必检项目</strong><span>附件和复核要求由方案配置，不由行业代码判断。</span></div>
            <el-button @click="addItem">添加项目</el-button>
          </div>
          <el-alert
            v-if="!definitions.length"
            title="还没有可用的检测定义，请先进入“检测定义”配置检测特性。"
            type="warning"
            show-icon
            :closable="false"
            class="notice"
          />
          <el-table :data="form.items" stripe>
            <el-table-column label="检测定义" min-width="220">
              <template #default="{ row }">
                <el-select v-model="row.definitionKey" filterable placeholder="选择定义" @change="applyDefinition(row)">
                  <el-option
                    v-for="definition in definitions"
                    :key="`${definition.code}:${definition.version}`"
                    :label="`${definition.name} · v${definition.version}`"
                    :value="`${definition.code}:${definition.version}`"
                  />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="顺序" width="110">
              <template #default="{ row }"><el-input-number v-model="row.sequence" :min="0" controls-position="right" /></template>
            </el-table-column>
            <el-table-column label="必检" width="82" align="center">
              <template #default="{ row }"><el-switch v-model="row.required" /></template>
            </el-table-column>
            <el-table-column label="需原始附件" width="118" align="center">
              <template #default="{ row }"><el-switch v-model="row.requiresAttachment" /></template>
            </el-table-column>
            <el-table-column label="需复核" width="88" align="center">
              <template #default="{ row }"><el-switch v-model="row.requiresReview" @change="enforceAttachment(row)" /></template>
            </el-table-column>
            <el-table-column label="操作" width="72" align="center">
              <template #default="{ $index }"><el-button link type="danger" @click="form.items.splice($index, 1)">删除</el-button></template>
            </el-table-column>
          </el-table>
        </el-form>

        <div class="actions">
          <template v-if="form.status === 'draft'">
            <el-button v-if="plans.some((item) => item.planId === form.planId && item.version === form.version)" type="danger" plain :loading="saving" @click="removeDraft">删除草稿</el-button>
            <el-button :loading="saving" @click="save('draft')">保存草稿</el-button>
            <el-button type="primary" :loading="saving" @click="save('published')">发布方案</el-button>
          </template>
          <template v-else-if="form.status === 'published'">
            <el-button type="warning" plain :loading="saving" @click="save('retired')">停用方案</el-button>
            <el-tag type="success">当前版本已发布且不可覆盖</el-tag>
          </template>
          <el-tag v-else type="warning">当前版本已停用且不可覆盖</el-tag>
        </div>
      </el-card>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from "vue";
import { ElDatePicker, ElMessage, ElMessageBox } from "element-plus";
import { deleteJson, getJson, postJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const plans = ref([]);
const { page: qualityPlanPage, pageSize: qualityPlanPageSize, total: qualityPlanTotal, pagedItems: pagedQualityPlans } = useClientPagination(plans);
const definitions = ref([]);
const loading = ref(false);
const saving = ref(false);
const editorVisible = ref(false);
const error = ref("");
const contextRows = ref([]);

const emptyPlan = () => ({
  planId: "",
  version: 1,
  name: "",
  description: "",
  status: "draft",
  priority: 0,
  effectiveFrom: new Date().toISOString(),
  effectiveTo: null,
  scope: { productSeries: "", productCode: "", recipeId: "", machineId: "", contextSelector: {} },
  items: [],
});
const form = reactive(emptyPlan());
const editorTitle = computed(() => form.planId ? `${form.name || form.planId} · v${form.version}` : "新建质量方案");

function replaceForm(value) {
  Object.assign(form, emptyPlan(), JSON.parse(JSON.stringify(value)));
  form.scope ||= emptyPlan().scope;
  contextRows.value = Object.entries(form.scope.contextSelector || {}).map(([key, value]) => ({ key, value }));
  form.items = (form.items || []).map((item) => ({
    ...item,
    definitionKey: `${item.definitionCode}:${item.definitionVersion}`,
  }));
}

async function load() {
  loading.value = true;
  error.value = "";
  try {
    const [planResult, definitionResult] = await Promise.all([
      getJson("/api/v1/inspection-plans"),
      getJson("/api/v1/inspection-definitions"),
    ]);
    plans.value = planResult.data || [];
    definitions.value = definitionResult.data || [];
    if (!form.planId && plans.value.length) replaceForm(plans.value[0]);
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

function createPlan() { replaceForm(emptyPlan()); error.value = ""; editorVisible.value = true; }
function selectPlan(row) { replaceForm(row); error.value = ""; editorVisible.value = true; }
function createNextVersion() {
  const next = JSON.parse(JSON.stringify(form));
  next.version += 1;
  next.status = "draft";
  replaceForm(next);
}
function addItem() {
  form.items.push({
    definitionKey: "",
    definitionCode: "",
    definitionVersion: 1,
    sequence: form.items.length * 10 + 10,
    required: true,
    requiresAttachment: false,
    requiresReview: false,
  });
}
function applyDefinition(row) {
  const [code, version] = String(row.definitionKey || "").split(":");
  row.definitionCode = code;
  row.definitionVersion = Number(version || 1);
}
function enforceAttachment(row) { if (row.requiresReview) row.requiresAttachment = true; }

async function save(status) {
  saving.value = true;
  error.value = "";
  try {
    const payload = JSON.parse(JSON.stringify(form));
    payload.status = status;
    if (status === "retired" && !payload.effectiveTo) payload.effectiveTo = new Date().toISOString();
    payload.scope = Object.fromEntries(Object.entries(payload.scope).filter(([key]) => key !== "contextSelector").map(([key, value]) => [key, value?.trim() || null]));
    payload.scope.contextSelector = Object.fromEntries(contextRows.value.filter(item => item.key.trim() && item.value.trim()).map(item => [item.key.trim(), item.value.trim()]));
    payload.items = payload.items.map((item) => {
      const normalized = { ...item, requiresAttachment: item.requiresAttachment || item.requiresReview };
      delete normalized.definitionKey;
      return normalized;
    });
    const saved = await postJson("/api/v1/inspection-plans", payload);
    replaceForm(saved);
    ElMessage.success(status === "published" ? "质量方案已发布" : status === "retired" ? "质量方案已停用" : "质量方案草稿已保存");
    await load();
    editorVisible.value = false;
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    saving.value = false;
  }
}

async function removeDraft() {
  await ElMessageBox.confirm("删除未发布草稿后不可恢复。", "删除质量方案草稿", { type: "warning" });
  saving.value = true;
  error.value = "";
  try {
    await deleteJson(`/api/v1/inspection-plans/${encodeURIComponent(form.planId)}/${form.version}`);
    replaceForm(emptyPlan());
    editorVisible.value = false;
    await load();
    ElMessage.success("质量方案草稿已删除");
  } catch (requestError) { error.value = requestError.message; }
  finally { saving.value = false; }
}

function statusLabel(status) { return { draft: "草稿", published: "已发布", retired: "已停用" }[status] || status; }
function statusTag(status) { return { draft: "info", published: "success", retired: "warning" }[status] || "info"; }

onMounted(load);
</script>

<style scoped>
.quality-plans-view { display: grid; gap: 18px; }
.heading, .section-title, .actions { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.notice { margin-bottom: 14px; }
.meta-grid { display: grid; grid-template-columns: 1.4fr .6fr 1.6fr .7fr; gap: 12px; }
.scope-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; }
.context-heading { display: flex; align-items: center; justify-content: space-between; margin: -4px 0 8px; color: #697588; font-size: 13px; }
.context-rows { display: grid; gap: 8px; margin-bottom: 18px; max-width: 760px; }
.context-row { display: grid; grid-template-columns: 1fr 1fr 64px; gap: 8px; }
.effective-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; max-width: 680px; }
.section-title { margin: 8px 0 14px; }
.section-title div { display: grid; gap: 4px; }
.section-title span { color: #8490a3; font-size: 12px; }
.actions { justify-content: flex-end; margin-top: 18px; }
@media (max-width: 900px) { .meta-grid, .scope-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 560px) { .meta-grid, .scope-grid, .effective-grid { grid-template-columns: 1fr; } }
</style>
