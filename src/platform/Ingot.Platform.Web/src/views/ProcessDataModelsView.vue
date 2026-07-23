<template>
  <div class="config-page">
    <el-card shadow="never" class="registry-card">
      <template #header><div class="catalog-heading"><div><strong>工艺数据模型</strong><span>{{ models.length }} 个版本</span></div><el-button type="primary" :icon="Plus" @click="createNew">新建模型</el-button></div></template>
      <el-table v-loading="loading" :data="pagedModels" stripe>
        <el-table-column prop="modelId" label="模型编码" min-width="180" />
        <el-table-column prop="name" label="名称" min-width="180" />
        <el-table-column label="版本" width="90"><template #default="{ row }">v{{ row.version }}</template></el-table-column>
        <el-table-column label="采集数据项" width="120"><template #default="{ row }">{{ row.acquisition?.dataItems?.length || 0 }}</template></el-table-column>
        <el-table-column label="配方参数" width="110"><template #default="{ row }">{{ row.recipeParameters?.length || 0 }}</template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }">{{ statusLabel(row.status) }}</template></el-table-column>
        <el-table-column label="操作" width="110" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="selectExisting(keyOf(row))">{{ row.status === 'draft' ? '编辑' : '查看' }}</el-button></template></el-table-column>
      </el-table>
      <el-empty v-if="!loading && !models.length" description="尚未创建模型" :image-size="72" />
      <TablePagination v-model:page="modelPage" v-model:page-size="modelPageSize" :total="modelTotal" />
    </el-card>

    <el-drawer v-model="editorVisible" :title="editor.name || '新建工艺数据模型'" size="88%" destroy-on-close>
      <div class="detail-column">
        <div class="summary-grid">
          <div class="summary-card"><span>采集数据项</span><strong>{{ editor.acquisition.dataItems.length }}</strong><small>同一采样时刻原子成组</small></div>
          <div class="summary-card"><span>配方参数定义</span><strong>{{ editor.recipeParameters.length }}</strong><small>这里只定义结构</small></div>
          <div class="summary-card"><span>工艺阶段</span><strong>{{ editor.stages.length }}</strong><small>预计 {{ expectedDuration }} 秒</small></div>
          <div class="summary-card"><span>版本状态</span><strong class="status-text">{{ statusLabel(editor.status) }}</strong><small>{{ editor.modelId }} · v{{ editor.version }}</small></div>
        </div>

        <el-card shadow="never" class="workspace-card">
          <template #header>
            <div class="editor-toolbar">
              <div class="editor-title"><strong>{{ editor.name || "新建工艺数据模型" }}</strong><span>{{ editor.modelId || "尚未填写编码" }} · v{{ editor.version }} · {{ statusLabel(editor.status) }}</span></div>
              <div class="editor-actions">
                <el-button :icon="CopyDocument" :disabled="!editor.modelId" @click="createNextVersion">创建新版本</el-button>
                <el-button :disabled="!editable" @click="save('draft')">保存草稿</el-button>
                <el-button type="primary" :disabled="!editable" @click="save('published')">发布版本</el-button>
                <el-button v-if="editor.status === 'published'" type="warning" plain @click="save('retired')">停用版本</el-button>
                <el-button v-if="editor.status === 'draft' && selectedKey" type="danger" plain @click="removeDraft">删除草稿</el-button>
              </div>
            </div>
          </template>

          <el-form label-position="top" class="meta-form">
            <el-form-item label="模型编码"><el-input v-model="editor.modelId" :disabled="!editable" /></el-form-item>
            <el-form-item label="模型名称"><el-input v-model="editor.name" :disabled="!editable" /></el-form-item>
            <el-form-item label="版本"><el-input-number v-model="editor.version" :min="1" :disabled="!editable" /></el-form-item>
            <el-form-item label="采样周期 (ms)"><el-input-number v-model="editor.acquisition.samplePeriodMs" :min="1" :disabled="!editable" /></el-form-item>
            <el-form-item label="步序来源字段"><el-input v-model="editor.acquisition.stepSourceKey" :disabled="!editable" /></el-form-item>
            <el-form-item label="说明"><el-input v-model="editor.description" :disabled="!editable" /></el-form-item>
          </el-form>

          <el-tabs v-model="activeTab">
            <el-tab-pane name="items" label="采集数据项">
              <div class="section-heading"><div><h2>标准数据项</h2><p>来源字段映射到稳定编码；单位和类别描述数据本身的含义。</p></div><el-button :disabled="!editable" :icon="Plus" @click="addDataItem">新增数据项</el-button></div>
              <el-table :data="editor.acquisition.dataItems" row-key="code" stripe>
                <el-table-column type="index" label="#" width="52" />
                <el-table-column label="来源字段" min-width="180"><template #default="{ row }"><el-input v-model="row.sourceField" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="标准数据项编码" min-width="230"><template #default="{ row }"><el-input v-model="row.code" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="类型" width="110"><template #default="{ row }"><el-select v-model="row.dataType" :disabled="!editable"><el-option v-for="item in dataTypes" :key="item.value" v-bind="item" /></el-select></template></el-table-column>
                <el-table-column label="单位" width="120"><template #default="{ row }"><el-input v-model="row.unit" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="数据类别" width="140"><template #default="{ row }"><el-select v-model="row.category" :disabled="!editable" filterable allow-create><el-option v-for="item in categories" :key="item.value" v-bind="item" /></el-select></template></el-table-column>
                <el-table-column label="操作" width="70" align="center"><template #default="{ $index }"><el-button link type="danger" :icon="Delete" :disabled="!editable" @click="remove(editor.acquisition.dataItems, $index)" /></template></el-table-column>
              </el-table>
            </el-tab-pane>

            <el-tab-pane name="parameters" label="配方参数定义">
              <div class="section-heading"><div><h2>参数结构</h2><p>实际有效值由“配方版本”维护，生产开始时再冻结到生产条件快照。</p></div><el-button :disabled="!editable" :icon="Plus" @click="addParameter">新增参数</el-button></div>
              <el-table :data="editor.recipeParameters" row-key="code" stripe>
                <el-table-column type="index" label="#" width="52" />
                <el-table-column label="来源字段" min-width="210"><template #default="{ row }"><el-input v-model="row.sourceField" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="标准参数编码" min-width="260"><template #default="{ row }"><el-input v-model="row.code" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="类型" width="130"><template #default="{ row }"><el-select v-model="row.dataType" :disabled="!editable"><el-option v-for="item in dataTypes" :key="item.value" v-bind="item" /></el-select></template></el-table-column>
                <el-table-column label="单位" width="140"><template #default="{ row }"><el-input v-model="row.unit" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="允许为空" width="100" align="center"><template #default="{ row }"><el-switch v-model="row.nullable" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="操作" width="70" align="center"><template #default="{ $index }"><el-button link type="danger" :icon="Delete" :disabled="!editable" @click="remove(editor.recipeParameters, $index)" /></template></el-table-column>
              </el-table>
            </el-tab-pane>

            <el-tab-pane name="stages" label="工艺阶段">
              <div class="section-heading"><div><h2>阶段映射</h2><p>把控制器步序解释为稳定的工艺阶段，供周期对齐和阶段特征计算使用。</p></div><el-button :disabled="!editable" :icon="Plus" @click="addStage">新增阶段</el-button></div>
              <el-table :data="editor.stages" row-key="code" stripe>
                <el-table-column type="index" label="顺序" width="70" />
                <el-table-column label="来源步序" width="150"><template #default="{ row }"><el-input v-model="row.sourceStep" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="阶段编码" min-width="180"><template #default="{ row }"><el-input v-model="row.code" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="显示名称" min-width="180"><template #default="{ row }"><el-input v-model="row.name" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="预计时长 (秒)" width="180"><template #default="{ row }"><el-input-number v-model="row.expectedDurationSeconds" :min="0" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="必需" width="90" align="center"><template #default="{ row }"><el-switch v-model="row.required" :disabled="!editable" /></template></el-table-column>
                <el-table-column label="操作" width="70" align="center"><template #default="{ $index }"><el-button link type="danger" :icon="Delete" :disabled="!editable" @click="remove(editor.stages, $index)" /></template></el-table-column>
              </el-table>
            </el-tab-pane>
          </el-tabs>
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

function emptyDataModel() {
  return {
    modelId: "", version: 1, name: "", description: "", status: "draft",
    acquisition: { samplePeriodMs: 1000, stepSourceKey: "", dataItems: [] },
    recipeParameters: [], stages: [], updatedAt: new Date().toISOString(),
  };
}

const models = ref([]);
const { page: modelPage, pageSize: modelPageSize, total: modelTotal, pagedItems: pagedModels } = useClientPagination(models);
const loading = ref(false);
const editorVisible = ref(false);
const selectedKey = ref("");
const activeTab = ref("items");
const editor = reactive(emptyDataModel());
const editable = computed(() => editor.status === "draft");
const expectedDuration = computed(() => editor.stages.reduce((total, item) => total + Number(item.expectedDurationSeconds || 0), 0));
const dataTypes = [{ label: "数值", value: "double" }, { label: "整数", value: "integer" }, { label: "布尔", value: "boolean" }, { label: "文本", value: "string" }];
const categories = [{ label: "过程变量", value: "process" }, { label: "控制输出", value: "control" }, { label: "设备状态", value: "state" }, { label: "环境变量", value: "environment" }];

function clone(value) { return JSON.parse(JSON.stringify(value)); }
function replace(value) { Object.assign(editor, clone(value)); }
function keyOf(value) { return `${value.modelId}@${value.version}`; }
function statusLabel(value) { return { draft: "草稿", published: "已发布", retired: "已停用" }[value] || value; }
function remove(values, index) { values.splice(index, 1); }
function addDataItem() { editor.acquisition.dataItems.push({ code: "", sourceField: "", dataType: "double", unit: "", category: "process", nullable: true }); }
function addParameter() { editor.recipeParameters.push({ code: "", sourceField: "", dataType: "double", unit: "", nullable: true }); }
function addStage() { editor.stages.push({ sourceStep: "", code: "", name: "", expectedDurationSeconds: 0, required: true }); }

function selectExisting(value) {
  const found = models.value.find((item) => keyOf(item) === value);
  if (found) {
    selectedKey.value = value;
    replace(found);
    editorVisible.value = true;
  }
}

function createNew() {
  replace(emptyDataModel());
  selectedKey.value = "";
  editorVisible.value = true;
}

function createNextVersion() {
  const versions = models.value.filter((item) => item.modelId === editor.modelId).map((item) => item.version);
  editor.version = Math.max(editor.version, ...versions, 0) + 1;
  editor.status = "draft";
  selectedKey.value = "";
}

async function load() {
  loading.value = true;
  try {
    const response = await getJson("/api/v1/process-data-models");
    models.value = response.data || [];
    if (models.value.length && !selectedKey.value) {
      selectedKey.value = keyOf(models.value[0]);
      replace(models.value[0]);
    }
    editorVisible.value = false;
  } catch (error) {
    ElMessage.error(error.message);
  } finally {
    loading.value = false;
  }
}

async function save(status) {
  const previousStatus = editor.status;
  try {
    editor.status = status;
    const saved = await postJson("/api/v1/process-data-models", { ...clone(editor), updatedAt: new Date().toISOString() });
    ElMessage.success({ published: "工艺数据模型已发布", retired: "工艺数据模型已停用" }[status] || "工艺数据模型草稿已保存");
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
  await ElMessageBox.confirm("删除未发布草稿后不可恢复。", "删除工艺数据模型草稿", { type: "warning" });
  try {
    await deleteJson(`/api/v1/process-data-models/${encodeURIComponent(editor.modelId)}/${editor.version}`);
    selectedKey.value = "";
    await load();
    ElMessage.success("草稿已删除");
  } catch (error) { ElMessage.error(error.message); }
}

onMounted(load);
</script>

<style scoped>
.config-page { display: flex; flex-direction: column; gap: 18px; }
.summary-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; }
.summary-card { display: grid; gap: 6px; padding: 18px 20px; border: 1px solid #e1e7ef; border-radius: 13px; background: #fff; }
.summary-card span, .summary-card small { color: #7b8797; }
.summary-card strong { color: #17233d; font-size: 26px; }
.summary-card .status-text { font-size: 20px; }
.workspace-card { border-radius: 14px; }
.meta-form { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.section-heading { display: flex; align-items: center; justify-content: space-between; gap: 20px; margin: 12px 0 16px; }
.section-heading h2 { margin: 0 0 5px; color: #26344d; font-size: 18px; }
.section-heading p { margin: 0; color: #8791a1; font-size: 13px; }
@media (max-width: 1000px) { .summary-grid { grid-template-columns: repeat(2, 1fr); } .meta-form { grid-template-columns: repeat(2, 1fr); } }
@media (max-width: 640px) { .summary-grid, .meta-form { grid-template-columns: 1fr; } }
</style>
