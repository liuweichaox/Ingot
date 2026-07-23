<template>
  <div class="config-page">
    <el-card shadow="never" class="registry-card">
      <template #header><div class="catalog-heading"><div><strong>分析方案</strong><span>{{ plans.length }} 个版本</span></div><el-button type="primary" :icon="Plus" @click="createNew">新建方案</el-button></div></template>
      <el-table v-loading="loading" :data="pagedPlans" stripe>
        <el-table-column prop="planId" label="方案编码" min-width="180" />
        <el-table-column prop="name" label="名称" min-width="180" />
        <el-table-column label="版本" width="90"><template #default="{ row }">v{{ row.version }}</template></el-table-column>
        <el-table-column label="分析范围" width="130"><template #default="{ row }">{{ scopeLabel(row.analysisScope) }}</template></el-table-column>
        <el-table-column label="对齐方式" min-width="160"><template #default="{ row }">{{ alignmentLabel(row.alignmentMode) }}</template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }">{{ statusLabel(row.status) }}</template></el-table-column>
        <el-table-column label="操作" width="110" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="selectExisting(keyOf(row))">{{ row.status === 'draft' ? '编辑' : '查看' }}</el-button></template></el-table-column>
      </el-table>
      <el-empty v-if="!loading && !plans.length" description="尚未创建分析方案" :image-size="72" />
      <TablePagination v-model:page="planPage" v-model:page-size="planPageSize" :total="planTotal" />
    </el-card>
    <el-drawer v-model="editorVisible" :title="editor.name || '新建分析方案'" size="88%" destroy-on-close>
      <div class="detail-column">
        <div class="summary-grid">
          <div class="summary-card"><span>分析范围</span><strong class="compact">{{ scopeLabel(editor.analysisScope) }}</strong><small>周期、运行段或分析窗口</small></div>
          <div class="summary-card"><span>对齐方式</span><strong class="compact">{{ alignmentLabel(editor.alignmentMode) }}</strong><small>决定曲线比较坐标</small></div>
          <div class="summary-card"><span>选用数据项</span><strong>{{ selectedCount }}</strong><small>来自引用模型</small></div>
          <div class="summary-card"><span>分组维度</span><strong class="compact">{{ editor.cohortDimension || "未配置" }}</strong><small>推荐使用质量结果</small></div>
        </div>

        <el-card shadow="never" class="workspace-card">
          <template #header>
            <div class="editor-toolbar">
              <div class="editor-title"><strong>{{ editor.name || "新建分析方案" }}</strong><span>{{ editor.planId || "尚未填写编码" }} · v{{ editor.version }} · {{ statusLabel(editor.status) }}</span></div>
              <div class="editor-actions">
                <el-button :icon="CopyDocument" :disabled="!editor.planId" @click="createNextVersion">创建新版本</el-button>
                <el-button :disabled="!editable" @click="save('draft')">保存草稿</el-button>
                <el-button type="primary" :disabled="!editable" @click="save('published')">发布方案</el-button>
                <el-button v-if="editor.status === 'published'" type="warning" plain @click="save('retired')">停用方案</el-button>
                <el-button v-if="editor.status === 'draft' && selectedKey" type="danger" plain @click="removeDraft">删除草稿</el-button>
              </div>
            </div>
          </template>

          <el-form label-position="top" class="meta-form">
            <el-form-item label="方案编码"><el-input v-model="editor.planId" :disabled="!editable" /></el-form-item>
            <el-form-item label="方案名称"><el-input v-model="editor.name" :disabled="!editable" /></el-form-item>
            <el-form-item label="版本"><el-input-number v-model="editor.version" :min="1" :disabled="!editable" /></el-form-item>
            <el-form-item label="工艺数据模型"><el-select :model-value="modelKey" :disabled="!editable" @change="selectModel"><el-option v-for="item in models" :key="modelKeyOf(item)" :label="`${item.name} · v${item.version}`" :value="modelKeyOf(item)" /></el-select></el-form-item>
            <el-form-item label="分析范围"><el-select v-model="editor.analysisScope" :disabled="!editable"><el-option label="生产周期" value="production-cycle" /><el-option label="生产运行段" value="production-run" /><el-option label="分析窗口" value="analysis-window" /></el-select></el-form-item>
            <el-form-item label="对齐方式"><el-select v-model="editor.alignmentMode" :disabled="!editable"><el-option label="按工艺阶段相对时间" value="stage-relative" /><el-option label="按开始后时间" value="elapsed" /><el-option label="按完成度归一化" value="normalized" /></el-select></el-form-item>
            <el-form-item label="同类比较键"><el-select v-model="editor.comparisonKeys" multiple filterable allow-create default-first-option :disabled="!editable" placeholder="选择或输入上下文键"><el-option v-for="item in comparisonKeyOptions" :key="item.value" v-bind="item" /></el-select></el-form-item>
            <el-form-item label="分组维度"><el-input v-model="editor.cohortDimension" :disabled="!editable" placeholder="例如 quality.outcome" /></el-form-item>
            <el-form-item label="说明"><el-input v-model="editor.description" :disabled="!editable" /></el-form-item>
          </el-form>

          <div class="section-heading"><div><h2>筛选条件</h2><p>限制参与分析的同类生产记录；条件来自生产条件快照。</p></div><el-button :icon="Plus" :disabled="!editable" @click="contextRows.push({ key: '', value: '' })">新增条件</el-button></div>
          <div class="context-list">
            <div v-for="(item, index) in contextRows" :key="index" class="context-row">
              <el-input v-model="item.key" placeholder="上下文键" :disabled="!editable" />
              <el-input v-model="item.value" placeholder="匹配值" :disabled="!editable" />
              <el-button link type="danger" :icon="Delete" :disabled="!editable" @click="contextRows.splice(index, 1)" />
            </div>
          </div>

          <div class="section-heading"><div><h2>分析数据项</h2><p>“是否参与比较”和派生特征属于分析方案，不写入采集数据项定义。</p></div></div>
          <el-table :data="dataItems" row-key="code" stripe>
            <el-table-column label="参与分析" width="100" align="center"><template #default="{ row }"><el-switch v-model="signalSettings[row.code].selected" :disabled="!editable" /></template></el-table-column>
            <el-table-column prop="sourceField" label="数据项名称" min-width="210" />
            <el-table-column prop="code" label="标准数据项编码" min-width="270" />
            <el-table-column prop="unit" label="单位" width="100" />
            <el-table-column label="保留完整曲线" width="130" align="center"><template #default="{ row }"><el-switch v-model="signalSettings[row.code].includeTrace" :disabled="!editable || !signalSettings[row.code].selected" /></template></el-table-column>
            <el-table-column label="派生特征" min-width="260"><template #default="{ row }"><el-select v-model="signalSettings[row.code].features" multiple collapse-tags :disabled="!editable || !signalSettings[row.code].selected"><el-option v-for="item in featureOptions" :key="item.value" v-bind="item" /></el-select></template></el-table-column>
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

function emptyAnalysisPlan() {
  return {
    planId: "", version: 1, name: "", description: "", status: "draft",
    dataModelId: "", dataModelVersion: 1, analysisScope: "production-cycle",
    alignmentMode: "stage-relative", cohortDimension: "quality.outcome",
    comparisonKeys: ["product_series"],
    contextSelector: {}, signals: [], updatedAt: new Date().toISOString(),
  };
}

const plans = ref([]);
const { page: planPage, pageSize: planPageSize, total: planTotal, pagedItems: pagedPlans } = useClientPagination(plans);
const models = ref([]);
const loading = ref(false);
const editorVisible = ref(false);
const selectedKey = ref("");
const editor = reactive(emptyAnalysisPlan());
const contextRows = ref([]);
const signalSettings = reactive({});
const editable = computed(() => editor.status === "draft");
const modelKey = computed(() => `${editor.dataModelId}@${editor.dataModelVersion}`);
const selectedModel = computed(() => models.value.find((item) => modelKeyOf(item) === modelKey.value));
const dataItems = computed(() => selectedModel.value?.acquisition?.dataItems || []);
const selectedCount = computed(() => Object.values(signalSettings).filter((item) => item.selected).length);
const comparisonKeyOptions = [
  { label: "产品系列", value: "product_series" },
  { label: "产品型号", value: "product_code" },
  { label: "工序", value: "operation_code" },
  { label: "配方", value: "recipe_id" },
  { label: "配方版本", value: "recipe_version" },
  { label: "设备", value: "machine_id" },
  { label: "工装", value: "tooling_id" },
  { label: "材料批次", value: "material_lot_ref" },
];
const featureOptions = [{ label: "平均值", value: "mean" }, { label: "最小值", value: "min" }, { label: "最大值", value: "max" }, { label: "标准差", value: "stddev" }, { label: "斜率", value: "slope" }, { label: "超限时长", value: "out-of-range-duration" }];

function clone(value) { return JSON.parse(JSON.stringify(value)); }
function keyOf(value) { return `${value.planId}@${value.version}`; }
function modelKeyOf(value) { return `${value.modelId}@${value.version}`; }
function statusLabel(value) { return { draft: "草稿", published: "已发布", retired: "已停用" }[value] || value; }
function scopeLabel(value) { return { "production-cycle": "生产周期", "production-run": "生产运行段", "analysis-window": "分析窗口" }[value] || value; }
function alignmentLabel(value) { return { "stage-relative": "阶段对齐", elapsed: "相对时间", normalized: "完成度归一化" }[value] || value; }

function ensureSettings() {
  for (const item of dataItems.value) signalSettings[item.code] ||= { selected: false, includeTrace: true, features: [] };
}

function replace(value) {
  Object.assign(editor, clone(value));
  contextRows.value = Object.entries(value.contextSelector || {}).map(([key, itemValue]) => ({ key, value: itemValue }));
  for (const key of Object.keys(signalSettings)) delete signalSettings[key];
  for (const item of value.signals || []) signalSettings[item.dataItemCode] = { selected: true, includeTrace: item.includeTrace !== false, features: [...(item.features || [])] };
  ensureSettings();
}

function selectExisting(value) {
  const found = plans.value.find((item) => keyOf(item) === value);
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
  ensureSettings();
}

function createNew() {
  const model = models.value.find((item) => item.status === "published") || models.value[0];
  replace({ ...emptyAnalysisPlan(), dataModelId: model?.modelId || "", dataModelVersion: model?.version || 1 });
  selectedKey.value = "";
  editorVisible.value = true;
}

function createNextVersion() {
  const versions = plans.value.filter((item) => item.planId === editor.planId).map((item) => item.version);
  editor.version = Math.max(editor.version, ...versions, 0) + 1;
  editor.status = "draft";
  selectedKey.value = "";
}

async function load() {
  loading.value = true;
  try {
    const [modelResponse, planResponse] = await Promise.all([
      getJson("/api/v1/process-data-models"),
      getJson("/api/v1/process-analysis-plans"),
    ]);
    models.value = modelResponse.data || [];
    plans.value = planResponse.data || [];
    ensureSettings();
    if (plans.value.length && !selectedKey.value) {
      selectedKey.value = keyOf(plans.value[0]);
      replace(plans.value[0]);
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
    if (!selectedModel.value) throw new Error("请先选择已保存的工艺数据模型版本。");
    editor.status = status;
    const contextSelector = Object.fromEntries(contextRows.value.filter((item) => item.key.trim() && item.value.trim()).map((item) => [item.key.trim(), item.value.trim()]));
    const signals = dataItems.value.filter((item) => signalSettings[item.code]?.selected).map((item) => ({ dataItemCode: item.code, includeTrace: signalSettings[item.code].includeTrace, features: signalSettings[item.code].features }));
    const saved = await postJson("/api/v1/process-analysis-plans", { ...clone(editor), contextSelector, signals, updatedAt: new Date().toISOString() });
    ElMessage.success({ published: "分析方案已发布", retired: "分析方案已停用" }[status] || "分析方案草稿已保存");
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
  await ElMessageBox.confirm("删除未发布草稿后不可恢复。", "删除分析方案草稿", { type: "warning" });
  try {
    await deleteJson(`/api/v1/process-analysis-plans/${encodeURIComponent(editor.planId)}/${editor.version}`);
    selectedKey.value = "";
    await load();
    ElMessage.success("草稿已删除");
  } catch (error) { ElMessage.error(error.message); }
}

onMounted(() => { createNew(); load(); });
</script>

<style scoped>
.config-page { display: flex; flex-direction: column; gap: 18px; }
.summary-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; }.summary-card { display: grid; gap: 6px; padding: 18px 20px; border: 1px solid #e1e7ef; border-radius: 13px; background: #fff; }.summary-card span,.summary-card small { color: #7b8797; }.summary-card strong { overflow: hidden; color: #17233d; font-size: 26px; text-overflow: ellipsis; white-space: nowrap; }.summary-card .compact { font-size: 18px; }
.workspace-card { border-radius: 14px; }.meta-form { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }.section-heading { display: flex; align-items: center; justify-content: space-between; gap: 20px; margin: 18px 0 12px; }.section-heading h2 { margin: 0 0 5px; color: #26344d; font-size: 18px; }.section-heading p { margin: 0; color: #8791a1; font-size: 13px; }.context-list { display: grid; gap: 8px; max-width: 850px; }.context-row { display: grid; grid-template-columns: 1.2fr 1fr 40px; gap: 8px; }
@media (max-width: 1000px) { .summary-grid { grid-template-columns: repeat(2, 1fr); }.meta-form { grid-template-columns: repeat(2, 1fr); } }@media (max-width: 640px) { .summary-grid,.meta-form,.context-row { grid-template-columns: 1fr; } }
</style>
