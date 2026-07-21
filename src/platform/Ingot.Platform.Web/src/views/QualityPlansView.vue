<template>
  <div class="quality-plans-view">
    <el-alert
      title="质检内容由已发布质量方案决定"
      description="产品系列、型号、配方或设备均可作为适用条件；已发布版本不可覆盖，修改必须创建新版本。"
      type="info"
      show-icon
      :closable="false"
    />

    <el-row :gutter="18">
      <el-col :lg="9" :md="24">
        <el-card shadow="never">
          <template #header>
            <div class="heading">
              <strong>质量方案</strong>
              <el-button type="primary" @click="createPlan">新建方案</el-button>
            </div>
          </template>
          <el-table v-loading="loading" :data="plans" stripe @row-click="selectPlan">
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
          </el-table>
          <el-empty v-if="!loading && !plans.length" description="尚未配置质量方案" />
        </el-card>
      </el-col>

      <el-col :lg="15" :md="24">
        <el-card shadow="never">
          <template #header>
            <div class="heading">
              <strong>{{ editorTitle }}</strong>
              <div>
                <el-button @click="openDefinitionEditor">新建检测定义</el-button>
                <el-button v-if="form.status !== 'draft'" @click="createNextVersion">创建新版本</el-button>
              </div>
            </div>
          </template>

          <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="notice" />
          <el-alert v-if="success" :title="success" type="success" show-icon :closable="false" class="notice" />

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

            <div class="section-title">
              <div><strong>必检项目</strong><span>附件和复核要求由方案配置，不由行业代码判断。</span></div>
              <el-button @click="addItem">添加项目</el-button>
            </div>
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
      </el-col>
    </el-row>

    <el-dialog v-model="definitionVisible" title="新建检测定义版本" width="860px">
      <el-form label-position="top">
        <div class="definition-meta">
          <el-form-item label="定义代码"><el-input v-model="definitionForm.code" placeholder="appearance.visual" /></el-form-item>
          <el-form-item label="版本"><el-input-number v-model="definitionForm.version" :min="1" controls-position="right" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="definitionForm.name" placeholder="外观视觉检查" /></el-form-item>
        </div>
        <el-form-item label="说明"><el-input v-model="definitionForm.description" type="textarea" :rows="2" /></el-form-item>
        <div class="section-title">
          <div><strong>检测特性</strong><span>限值、单位和录入类型随定义版本保存。</span></div>
          <el-button @click="addCharacteristic">添加特性</el-button>
        </div>
        <el-table :data="definitionForm.characteristics" stripe>
          <el-table-column label="代码" min-width="150"><template #default="{ row }"><el-input v-model="row.code" /></template></el-table-column>
          <el-table-column label="名称" min-width="150"><template #default="{ row }"><el-input v-model="row.name" /></template></el-table-column>
          <el-table-column label="类型" width="120">
            <template #default="{ row }">
              <el-select v-model="row.inputType">
                <el-option label="数值" value="numeric" />
                <el-option label="文本" value="text" />
                <el-option label="选择" value="select" />
                <el-option label="布尔" value="boolean" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="单位" width="100"><template #default="{ row }"><el-input v-model="row.unit" :disabled="row.inputType !== 'numeric'" /></template></el-table-column>
          <el-table-column label="下限" width="120"><template #default="{ row }"><el-input-number v-model="row.lowerLimit" :disabled="row.inputType !== 'numeric'" controls-position="right" /></template></el-table-column>
          <el-table-column label="上限" width="120"><template #default="{ row }"><el-input-number v-model="row.upperLimit" :disabled="row.inputType !== 'numeric'" controls-position="right" /></template></el-table-column>
          <el-table-column label="必填" width="72" align="center"><template #default="{ row }"><el-switch v-model="row.required" /></template></el-table-column>
          <el-table-column label="操作" width="70"><template #default="{ $index }"><el-button link type="danger" @click="definitionForm.characteristics.splice($index, 1)">删除</el-button></template></el-table-column>
        </el-table>
      </el-form>
      <template #footer>
        <el-button @click="definitionVisible = false">取消</el-button>
        <el-button type="primary" :loading="definitionSaving" @click="saveDefinition">保存定义版本</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from "vue";
import { getJson, postJson } from "../api/http";

const plans = ref([]);
const definitions = ref([]);
const loading = ref(false);
const saving = ref(false);
const error = ref("");
const success = ref("");
const definitionVisible = ref(false);
const definitionSaving = ref(false);

const emptyPlan = () => ({
  planId: "",
  version: 1,
  name: "",
  description: "",
  status: "draft",
  priority: 0,
  effectiveFrom: new Date().toISOString(),
  effectiveTo: null,
  scope: { productSeries: "", productCode: "", recipeId: "", machineId: "" },
  items: [],
});
const form = reactive(emptyPlan());
const emptyDefinition = () => ({ code: "", version: 1, name: "", description: "", characteristics: [] });
const definitionForm = reactive(emptyDefinition());
const editorTitle = computed(() => form.planId ? `${form.name || form.planId} · v${form.version}` : "新建质量方案");

function replaceForm(value) {
  Object.assign(form, emptyPlan(), JSON.parse(JSON.stringify(value)));
  form.scope ||= emptyPlan().scope;
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
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

function createPlan() { replaceForm(emptyPlan()); success.value = ""; error.value = ""; }
function selectPlan(row) { replaceForm(row); success.value = ""; error.value = ""; }
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
function openDefinitionEditor() {
  Object.assign(definitionForm, emptyDefinition());
  addCharacteristic();
  definitionVisible.value = true;
}
function addCharacteristic() {
  definitionForm.characteristics.push({
    code: "",
    name: "",
    inputType: "numeric",
    unit: "",
    lowerLimit: null,
    upperLimit: null,
    required: true,
  });
}
async function saveDefinition() {
  definitionSaving.value = true;
  error.value = "";
  try {
    const payload = JSON.parse(JSON.stringify(definitionForm));
    payload.characteristics = payload.characteristics.map((item) => ({
      ...item,
      unit: item.inputType === "numeric" ? (item.unit || null) : null,
      lowerLimit: item.inputType === "numeric" ? item.lowerLimit : null,
      upperLimit: item.inputType === "numeric" ? item.upperLimit : null,
    }));
    await postJson("/api/v1/inspection-definitions", payload);
    definitionVisible.value = false;
    success.value = "检测定义版本已创建，可以加入质量方案。";
    await load();
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    definitionSaving.value = false;
  }
}

async function save(status) {
  saving.value = true;
  error.value = "";
  success.value = "";
  try {
    const payload = JSON.parse(JSON.stringify(form));
    payload.status = status;
    if (status === "retired" && !payload.effectiveTo) payload.effectiveTo = new Date().toISOString();
    payload.scope = Object.fromEntries(Object.entries(payload.scope).map(([key, value]) => [key, value?.trim() || null]));
    payload.items = payload.items.map((item) => {
      const normalized = { ...item, requiresAttachment: item.requiresAttachment || item.requiresReview };
      delete normalized.definitionKey;
      return normalized;
    });
    const saved = await postJson("/api/v1/inspection-plans", payload);
    replaceForm(saved);
    success.value = status === "published" ? "质量方案已发布，后续周期将按此版本生成待检任务。" : "质量方案草稿已保存。";
    await load();
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    saving.value = false;
  }
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
.effective-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; max-width: 680px; }
.definition-meta { display: grid; grid-template-columns: 1.5fr .6fr 1.8fr; gap: 12px; }
.section-title { margin: 8px 0 14px; }
.section-title div { display: grid; gap: 4px; }
.section-title span { color: #8490a3; font-size: 12px; }
.actions { justify-content: flex-end; margin-top: 18px; }
@media (max-width: 900px) { .meta-grid, .scope-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 560px) { .meta-grid, .scope-grid, .effective-grid, .definition-meta { grid-template-columns: 1fr; } }
</style>
