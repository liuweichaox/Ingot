<template>
  <div class="config-page">
    <el-card shadow="never" class="registry-card">
      <template #header><div class="catalog-heading"><div><strong>配方版本</strong><span>{{ recipes.length }} 个版本</span></div><el-button type="primary" :icon="Plus" @click="createNew">新建配方</el-button></div></template>
      <el-table v-loading="loading" :data="pagedRecipes" stripe>
        <el-table-column prop="recipeId" label="配方编码" min-width="180" />
        <el-table-column prop="name" label="名称" min-width="180" />
        <el-table-column label="版本" width="90"><template #default="{ row }">v{{ row.version }}</template></el-table-column>
        <el-table-column label="工艺数据模型" min-width="180"><template #default="{ row }">{{ row.dataModelId }} · v{{ row.dataModelVersion }}</template></el-table-column>
        <el-table-column label="有效参数" width="110"><template #default="{ row }">{{ row.values?.length || 0 }}</template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }">{{ statusLabel(row.status) }}</template></el-table-column>
        <el-table-column label="操作" width="110" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="selectExisting(keyOf(row))">{{ row.status === 'draft' ? '编辑' : '查看' }}</el-button></template></el-table-column>
      </el-table>
      <el-empty v-if="!loading && !recipes.length" description="尚未创建配方" :image-size="72" />
      <TablePagination v-model:page="recipePage" v-model:page-size="recipePageSize" :total="recipeTotal" />
    </el-card>
    <el-drawer v-model="editorVisible" :title="editor.name || '新建配方版本'" size="88%" destroy-on-close>
      <div class="detail-column">
        <div class="summary-grid">
          <div class="summary-card"><span>有效参数</span><strong>{{ filledCount }}/{{ parameterDefinitions.length }}</strong><small>按引用模型生成</small></div>
          <div class="summary-card"><span>沿用关系</span><strong class="compact">{{ editor.basedOnVersion ? `v${editor.basedOnVersion}` : "无" }}</strong><small>只记录版本来源</small></div>
          <div class="summary-card"><span>适用条件</span><strong>{{ contextRows.length }}</strong><small>通用上下文键值</small></div>
          <div class="summary-card"><span>版本状态</span><strong class="compact">{{ statusLabel(editor.status) }}</strong><small>{{ editor.recipeId }} · v{{ editor.version }}</small></div>
        </div>

        <el-card shadow="never" class="workspace-card">
          <template #header>
            <div class="editor-toolbar">
              <div class="editor-title"><strong>{{ editor.name || "新建配方版本" }}</strong><span>{{ editor.recipeId || "尚未填写编码" }} · v{{ editor.version }} · {{ statusLabel(editor.status) }}</span></div>
              <div class="editor-actions">
                <el-button :icon="CopyDocument" :disabled="!editor.recipeId" @click="createNextVersion">沿用为新版本</el-button>
                <el-button :disabled="!editable" @click="save('draft')">保存草稿</el-button>
                <el-button type="primary" :disabled="!editable" @click="save('published')">发布版本</el-button>
                <el-button v-if="editor.status === 'published'" type="warning" plain @click="save('retired')">停用版本</el-button>
                <el-button v-if="editor.status === 'draft' && selectedKey" type="danger" plain @click="removeDraft">删除草稿</el-button>
              </div>
            </div>
          </template>

          <el-form label-position="top" class="meta-form">
            <el-form-item label="配方编码"><el-input v-model="editor.recipeId" :disabled="!editable" /></el-form-item>
            <el-form-item label="配方名称"><el-input v-model="editor.name" :disabled="!editable" /></el-form-item>
            <el-form-item label="版本"><el-input-number v-model="editor.version" :min="1" :disabled="!editable" /></el-form-item>
            <el-form-item label="沿用版本"><el-input-number v-model="editor.basedOnVersion" :min="1" clearable :disabled="!editable" /></el-form-item>
            <el-form-item label="工艺数据模型">
              <el-select :model-value="modelKey" :disabled="!editable" @change="selectModel">
                <el-option v-for="item in models" :key="modelKeyOf(item)" :label="`${item.name} · v${item.version}`" :value="modelKeyOf(item)" />
              </el-select>
            </el-form-item>
          </el-form>

          <div class="section-heading"><div><h2>适用条件</h2><p>例如 product.series=LENS-A；其他行业可以配置材料、产线或设备类型，不使用固定列。</p></div><el-button :icon="Plus" :disabled="!editable" @click="contextRows.push({ key: '', value: '' })">新增条件</el-button></div>
          <div class="context-list">
            <div v-for="(item, index) in contextRows" :key="index" class="context-row">
              <el-input v-model="item.key" placeholder="上下文键，例如 product.series" :disabled="!editable" />
              <el-input v-model="item.value" placeholder="匹配值" :disabled="!editable" />
              <el-button link type="danger" :icon="Delete" :disabled="!editable" @click="contextRows.splice(index, 1)" />
            </div>
          </div>

          <div class="section-heading"><div><h2>完整有效参数</h2><p>参数结构来自 {{ selectedModel?.name || "尚未选择模型" }}；发布后不可原地修改。</p></div></div>
          <el-table :data="parameterDefinitions" row-key="code" stripe>
            <el-table-column type="index" label="#" width="52" />
            <el-table-column prop="sourceField" label="参数名称" min-width="210" />
            <el-table-column prop="code" label="标准参数编码" min-width="270" />
            <el-table-column label="类型" width="100"><template #default="{ row }">{{ typeLabel(row.dataType) }}</template></el-table-column>
            <el-table-column label="有效值" min-width="220">
              <template #default="{ row }">
                <el-switch v-if="row.dataType === 'boolean'" v-model="valueMap[row.code]" :disabled="!editable" />
                <el-input v-else-if="row.dataType === 'string'" v-model="valueMap[row.code]" :disabled="!editable" />
                <el-input-number v-else v-model="valueMap[row.code]" :precision="row.dataType === 'integer' ? 0 : 4" controls-position="right" :disabled="!editable" />
              </template>
            </el-table-column>
            <el-table-column prop="unit" label="单位" width="120" />
          </el-table>
        </el-card>
      </div>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { CopyDocument, Delete, Plus } from "@element-plus/icons-vue";
import { deleteJson, getJson, postJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

function emptyRecipeVersion() {
  return {
    recipeId: "", version: 1, name: "", basedOnVersion: null,
    dataModelId: "", dataModelVersion: 1, status: "draft",
    contextSelector: {}, values: [], updatedAt: new Date().toISOString(),
  };
}

const recipes = ref([]);
const { page: recipePage, pageSize: recipePageSize, total: recipeTotal, pagedItems: pagedRecipes } = useClientPagination(recipes);
const models = ref([]);
const loading = ref(false);
const editorVisible = ref(false);
const selectedKey = ref("");
const editor = reactive(emptyRecipeVersion());
const contextRows = ref([]);
const valueMap = reactive({});
const editable = computed(() => editor.status === "draft");
const modelKey = computed(() => `${editor.dataModelId}@${editor.dataModelVersion}`);
const selectedModel = computed(() => models.value.find((item) => modelKeyOf(item) === modelKey.value));
const parameterDefinitions = computed(() => selectedModel.value?.recipeParameters || []);
const filledCount = computed(() => parameterDefinitions.value.filter((item) => valueMap[item.code] !== undefined && valueMap[item.code] !== null && valueMap[item.code] !== "").length);

function clone(value) { return JSON.parse(JSON.stringify(value)); }
function keyOf(value) { return `${value.recipeId}@${value.version}`; }
function modelKeyOf(value) { return `${value.modelId}@${value.version}`; }
function statusLabel(value) { return { draft: "草稿", published: "已发布", retired: "已停用" }[value] || value; }
function typeLabel(value) { return { double: "数值", integer: "整数", boolean: "布尔", string: "文本" }[value] || value; }

function replace(value) {
  Object.assign(editor, clone(value));
  contextRows.value = Object.entries(value.contextSelector || {}).map(([key, itemValue]) => ({ key, value: itemValue }));
  for (const key of Object.keys(valueMap)) delete valueMap[key];
  for (const item of value.values || []) valueMap[item.code] = item.value;
}

function selectExisting(value) {
  const found = recipes.value.find((item) => keyOf(item) === value);
  if (found) {
    selectedKey.value = value;
    replace(found);
    editorVisible.value = true;
  }
}

function selectModel(value) {
  const [dataModelId, version] = value.split("@");
  editor.dataModelId = dataModelId;
  editor.dataModelVersion = Number(version);
}

function createNew() {
  const model = models.value.find((item) => item.status === "published") || models.value[0];
  replace({ ...emptyRecipeVersion(), dataModelId: model?.modelId || "", dataModelVersion: model?.version || 1 });
  selectedKey.value = "";
  editorVisible.value = true;
}

function createNextVersion() {
  const sourceVersion = editor.version;
  const versions = recipes.value.filter((item) => item.recipeId === editor.recipeId).map((item) => item.version);
  editor.basedOnVersion = sourceVersion;
  editor.version = Math.max(sourceVersion, ...versions, 0) + 1;
  editor.status = "draft";
  selectedKey.value = "";
}

async function load() {
  loading.value = true;
  try {
    const [modelResponse, recipeResponse] = await Promise.all([
      getJson("/api/v1/process-data-models"),
      getJson("/api/v1/recipe-versions"),
    ]);
    models.value = modelResponse.data || [];
    recipes.value = recipeResponse.data || [];
    if (recipes.value.length && !selectedKey.value) {
      selectedKey.value = keyOf(recipes.value[0]);
      replace(recipes.value[0]);
    }
  } catch (error) {
    ElMessage.error(error.message);
  } finally {
    loading.value = false;
  }
}

async function save(status) {
  const previousStatus = editor.status;
  try {
    if (!selectedModel.value) throw new Error("请先选择已保存的工艺数据模型版本。");
    editor.status = status;
    const contextSelector = Object.fromEntries(contextRows.value.filter((item) => item.key.trim() && item.value.trim()).map((item) => [item.key.trim(), item.value.trim()]));
    const values = parameterDefinitions.value
      .filter((item) => valueMap[item.code] !== undefined && valueMap[item.code] !== null && valueMap[item.code] !== "")
      .map((item) => ({ code: item.code, value: valueMap[item.code] }));
    const saved = await postJson("/api/v1/recipe-versions", { ...clone(editor), contextSelector, values, updatedAt: new Date().toISOString() });
    ElMessage.success({ published: "配方版本已发布", retired: "配方版本已停用" }[status] || "配方草稿已保存");
    selectedKey.value = keyOf(saved);
    await load();
    selectExisting(selectedKey.value);
    editorVisible.value = false;
  } catch (error) {
    editor.status = previousStatus;
    ElMessage.error(error.message);
  }
}

async function removeDraft() {
  await ElMessageBox.confirm("删除未发布草稿后不可恢复。", "删除配方草稿", { type: "warning" });
  try {
    await deleteJson(`/api/v1/recipe-versions/${encodeURIComponent(editor.recipeId)}/${editor.version}`);
    selectedKey.value = "";
    await load();
    ElMessage.success("草稿已删除");
  } catch (error) { ElMessage.error(error.message); }
}

onMounted(load);
</script>

<style scoped>
.config-page { display: flex; flex-direction: column; gap: 18px; }
.summary-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; }.summary-card { display: grid; gap: 6px; padding: 18px 20px; border: 1px solid #e1e7ef; border-radius: 13px; background: #fff; }.summary-card span,.summary-card small { color: #7b8797; }.summary-card strong { color: #17233d; font-size: 26px; }.summary-card .compact { font-size: 20px; }
.workspace-card { border-radius: 14px; }.meta-form { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.section-heading { display: flex; align-items: center; justify-content: space-between; gap: 20px; margin: 18px 0 12px; }.section-heading h2 { margin: 0 0 5px; color: #26344d; font-size: 18px; }.section-heading p { margin: 0; color: #8791a1; font-size: 13px; }
.context-list { display: grid; gap: 8px; max-width: 850px; }.context-row { display: grid; grid-template-columns: 1.2fr 1fr 40px; gap: 8px; }
@media (max-width: 1000px) { .summary-grid { grid-template-columns: repeat(2, 1fr); }.meta-form { grid-template-columns: repeat(2, 1fr); } }@media (max-width: 640px) { .summary-grid,.meta-form,.context-row { grid-template-columns: 1fr; } }
</style>
