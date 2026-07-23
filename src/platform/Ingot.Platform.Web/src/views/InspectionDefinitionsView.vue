<template>
  <div class="definition-view">
    <el-card shadow="never">
      <template #header>
        <div class="heading">
          <strong>检测定义</strong>
          <el-button type="primary" @click="createDefinition">新建定义</el-button>
        </div>
      </template>
      <el-input v-model="keyword" clearable placeholder="按名称或代码查找" class="filter" />
      <el-table v-loading="loading" :data="pagedDefinitions" stripe>
        <el-table-column prop="name" label="名称" min-width="150" />
        <el-table-column prop="code" label="代码" min-width="150" show-overflow-tooltip />
        <el-table-column label="版本" width="72">
          <template #default="{ row }">v{{ row.version }}</template>
        </el-table-column>
        <el-table-column label="特性" width="68">
          <template #default="{ row }">{{ row.characteristics?.length || 0 }}</template>
        </el-table-column>
        <el-table-column label="操作" width="100" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="selectDefinition(row)">查看</el-button></template></el-table-column>
      </el-table>
      <el-empty v-if="!loading && !filteredDefinitions.length" description="尚未配置检测定义" />
      <TablePagination v-model:page="definitionPage" v-model:page-size="definitionPageSize" :total="definitionTotal" />
    </el-card>

    <el-drawer v-model="editorVisible" :title="editorTitle" size="82%" destroy-on-close>
      <el-card shadow="never">
        <template #header>
          <div class="heading">
            <div>
              <strong>{{ editorTitle }}</strong>
              <span v-if="persisted" class="immutable-note">已保存版本不可覆盖</span>
            </div>
            <div>
              <el-button v-if="persisted" @click="createNextVersion">基于此版本新建</el-button>
              <el-button v-if="persisted" type="danger" plain :disabled="references.length > 0" @click="removeDefinition">删除未引用版本</el-button>
              <el-button @click="$router.push('/configuration/quality-plans')">查看质量方案</el-button>
            </div>
          </div>
        </template>

        <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="notice" />

        <el-form label-position="top" :disabled="persisted">
          <div class="definition-meta">
            <el-form-item label="定义代码">
              <el-input v-model="form.code" placeholder="surface.appearance" />
            </el-form-item>
            <el-form-item label="版本">
              <el-input-number v-model="form.version" :min="1" controls-position="right" />
            </el-form-item>
            <el-form-item label="名称">
              <el-input v-model="form.name" placeholder="外观检查" />
            </el-form-item>
          </div>
          <el-form-item label="说明">
            <el-input v-model="form.description" type="textarea" :rows="2" maxlength="1000" show-word-limit />
          </el-form-item>

          <div class="section-title">
            <div>
              <strong>检测特性</strong>
              <span>一行就是一个结构化结果；数值型可配置限值，选择型必须配置选项。</span>
            </div>
            <el-button @click="addCharacteristic">添加特性</el-button>
          </div>
          <el-table :data="form.characteristics" stripe>
            <el-table-column label="代码" min-width="135">
              <template #default="{ row }"><el-input v-model="row.code" placeholder="diameter" /></template>
            </el-table-column>
            <el-table-column label="名称" min-width="135">
              <template #default="{ row }"><el-input v-model="row.name" placeholder="直径" /></template>
            </el-table-column>
            <el-table-column label="录入类型" width="116">
              <template #default="{ row }">
                <el-select v-model="row.inputType" @change="normalizeCharacteristic(row)">
                  <el-option label="数值" value="numeric" />
                  <el-option label="文本" value="text" />
                  <el-option label="选择" value="select" />
                  <el-option label="是/否" value="boolean" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="单位/选项" min-width="145">
              <template #default="{ row }">
                <el-input v-if="row.inputType === 'numeric'" v-model="row.unit" placeholder="mm" />
                <el-input
                  v-else-if="row.inputType === 'select'"
                  v-model="row.allowedValuesText"
                  placeholder="合格,划伤,缺口"
                />
                <span v-else class="muted">不适用</span>
              </template>
            </el-table-column>
            <el-table-column label="下限" width="104">
              <template #default="{ row }">
                <el-input-number v-model="row.lowerLimit" :disabled="row.inputType !== 'numeric'" controls-position="right" />
              </template>
            </el-table-column>
            <el-table-column label="上限" width="104">
              <template #default="{ row }">
                <el-input-number v-model="row.upperLimit" :disabled="row.inputType !== 'numeric'" controls-position="right" />
              </template>
            </el-table-column>
            <el-table-column label="必填" width="66" align="center">
              <template #default="{ row }"><el-switch v-model="row.required" /></template>
            </el-table-column>
            <el-table-column v-if="!persisted" label="操作" width="66">
              <template #default="{ $index }">
                <el-button link type="danger" @click="form.characteristics.splice($index, 1)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-form>

        <div v-if="persisted" class="references">
          <strong>被质量方案引用</strong>
          <div v-if="references.length" class="reference-tags">
            <el-tag v-for="item in references" :key="`${item.planId}:${item.version}`" effect="plain">
              {{ item.name }} · v{{ item.version }} · {{ statusLabel(item.status) }}
            </el-tag>
          </div>
          <span v-else class="muted">尚未被任何质量方案引用</span>
        </div>

        <div class="actions">
          <el-button v-if="!persisted" type="primary" :loading="saving" @click="saveDefinition">保存定义版本</el-button>
        </div>
      </el-card>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { deleteJson, getJson, postJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const definitions = ref([]);
const plans = ref([]);
const keyword = ref("");
const loading = ref(false);
const saving = ref(false);
const editorVisible = ref(false);
const persisted = ref(false);
const error = ref("");

const emptyDefinition = () => ({ code: "", version: 1, name: "", description: "", characteristics: [] });
const form = reactive(emptyDefinition());
const editorTitle = computed(() => form.code ? `${form.name || form.code} · v${form.version}` : "新建检测定义");
const filteredDefinitions = computed(() => {
  const term = keyword.value.trim().toLowerCase();
  return definitions.value.filter((item) => !term || item.code.includes(term) || item.name.toLowerCase().includes(term));
});
const { page: definitionPage, pageSize: definitionPageSize, total: definitionTotal, pagedItems: pagedDefinitions, resetPage: resetDefinitionPage } = useClientPagination(filteredDefinitions);
const references = computed(() => plans.value.filter((plan) =>
  (plan.items || []).some((item) => item.definitionCode === form.code && item.definitionVersion === form.version)
));

function mapDefinition(value) {
  return {
    ...JSON.parse(JSON.stringify(value)),
    characteristics: (value.characteristics || []).map((item) => ({
      ...item,
      allowedValuesText: (item.allowedValues || []).join(","),
    })),
  };
}

function replaceForm(value, isPersisted) {
  Object.assign(form, emptyDefinition(), mapDefinition(value));
  persisted.value = isPersisted;
  error.value = "";
}

function createDefinition() {
  replaceForm(emptyDefinition(), false);
  addCharacteristic();
  editorVisible.value = true;
}

function selectDefinition(row) { replaceForm(row, true); editorVisible.value = true; }

function createNextVersion() {
  const versions = definitions.value.filter((item) => item.code === form.code).map((item) => item.version);
  const next = mapDefinition(form);
  next.version = Math.max(form.version, ...versions) + 1;
  replaceForm(next, false);
}

function addCharacteristic() {
  form.characteristics.push({
    code: "", name: "", inputType: "numeric", unit: "", lowerLimit: null,
    upperLimit: null, allowedValuesText: "", required: true,
  });
}

function normalizeCharacteristic(row) {
  if (row.inputType !== "numeric") {
    row.unit = "";
    row.lowerLimit = null;
    row.upperLimit = null;
  }
  if (row.inputType !== "select") row.allowedValuesText = "";
}

async function load() {
  loading.value = true;
  error.value = "";
  try {
    const [definitionResult, planResult] = await Promise.all([
      getJson("/api/v1/inspection-definitions"),
      getJson("/api/v1/inspection-plans"),
    ]);
    definitions.value = definitionResult.data || [];
    plans.value = planResult.data || [];
    if (!form.code && definitions.value.length) replaceForm(definitions.value[0], true);
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    loading.value = false;
  }
}

async function saveDefinition() {
  saving.value = true;
  error.value = "";
  try {
    const payload = JSON.parse(JSON.stringify(form));
    payload.characteristics = payload.characteristics.map((item) => {
      const normalized = {
        ...item,
        unit: item.inputType === "numeric" ? (item.unit || null) : null,
        lowerLimit: item.inputType === "numeric" ? item.lowerLimit : null,
        upperLimit: item.inputType === "numeric" ? item.upperLimit : null,
        allowedValues: item.inputType === "select"
          ? item.allowedValuesText.split(/[,，\n]/).map((value) => value.trim()).filter(Boolean)
          : [],
      };
      delete normalized.allowedValuesText;
      return normalized;
    });
    const saved = await postJson("/api/v1/inspection-definitions", payload);
    await load();
    replaceForm(saved, true);
    editorVisible.value = false;
    ElMessage.success("检测定义版本已保存");
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    saving.value = false;
  }
}

async function removeDefinition() {
  await ElMessageBox.confirm("删除后不能再被质量方案选择。", "删除检测定义版本", { type: "warning" });
  saving.value = true;
  error.value = "";
  try {
    await deleteJson(`/api/v1/inspection-definitions/${encodeURIComponent(form.code)}/${form.version}`);
    replaceForm(emptyDefinition(), false);
    editorVisible.value = false;
    await load();
    ElMessage.success("检测定义版本已删除");
  } catch (requestError) { error.value = requestError.message; }
  finally { saving.value = false; }
}

function statusLabel(status) { return { draft: "草稿", published: "已发布", retired: "已停用" }[status] || status; }

watch(keyword, resetDefinitionPage);
onMounted(load);
</script>

<style scoped>
.definition-view { display: grid; gap: 18px; }
.heading, .section-title, .actions { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.heading > div { display: flex; align-items: center; gap: 10px; }
.filter, .notice { margin-bottom: 14px; }
.definition-meta { display: grid; grid-template-columns: 1.5fr .6fr 1.8fr; gap: 12px; }
.section-title { margin: 8px 0 14px; }
.section-title div { display: grid; gap: 4px; }
.section-title span, .immutable-note, .muted { color: #8490a3; font-size: 12px; }
.references { display: grid; gap: 10px; margin-top: 18px; padding: 14px; border-radius: 8px; background: #f7f9fc; }
.reference-tags { display: flex; flex-wrap: wrap; gap: 8px; }
.actions { justify-content: flex-end; margin-top: 18px; }
@media (max-width: 700px) { .definition-meta { grid-template-columns: 1fr; } }
</style>
