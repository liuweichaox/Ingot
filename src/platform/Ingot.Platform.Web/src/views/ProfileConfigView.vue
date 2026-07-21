<template>
  <div class="profile-view">
    <section class="hero">
      <div>
        <div class="eyebrow">PROCESS CONFIGURATION</div>
        <h1>行业 Profile 配置</h1>
        <p>把设备专有字段留在配置层，用稳定代码支撑采集、配方追溯和历史周期分析。</p>
      </div>
      <div class="hero-actions">
        <el-button :icon="Upload" @click="openImport">导入 JSON</el-button>
        <el-button :icon="Download" @click="exportWorkspace">导出 JSON</el-button>
        <el-button :icon="Check" @click="saveWorkspace">保存草稿</el-button>
        <el-button type="primary" :icon="Upload" :loading="publishing" @click="publishWorkspace">发布到平台</el-button>
      </div>
      <input ref="fileInput" class="hidden-input" type="file" accept="application/json,.json" @change="importWorkspace">
    </section>

    <el-alert
      title="配置先保存草稿，再发布到平台"
      description="浏览器只保存编辑草稿；阶段与历史比较信号发布后写入平台版本化主数据。已用于生产的 Profile 应创建新版本，不能改变历史含义。"
      type="info"
      show-icon
      :closable="false"
      class="storage-notice"
    />

    <div class="summary-grid">
      <div class="summary-card">
        <span>采集信号</span>
        <strong>{{ workspace.acquisition.fields.length }}</strong>
        <small>每 {{ workspace.acquisition.samplePeriodMs }} ms 原子成组</small>
      </div>
      <div class="summary-card">
        <span>配方参数</span>
        <strong>{{ workspace.recipe.parameters.length }}</strong>
        <small>{{ workspace.recipe.recipeId }} · v{{ workspace.recipe.recipeVersion }}</small>
      </div>
      <div class="summary-card">
        <span>模压阶段</span>
        <strong>{{ workspace.phases.mappings.length }}</strong>
        <small>预计 {{ expectedDuration }} 秒</small>
      </div>
      <div class="summary-card warning-card">
        <span>待确认语义</span>
        <strong>{{ needsConfirmation }}</strong>
        <small>单位或压力基准需工艺确认</small>
      </div>
    </div>

    <el-card shadow="never" class="workspace-card">
      <el-tabs v-model="activeTab">
        <el-tab-pane name="acquisition">
          <template #label><span class="tab-label"><DataLine />采集字段</span></template>

          <div class="section-heading">
            <div>
              <h2>采集 Profile</h2>
              <p>一条过程事件是一组同时读取的值，不拆成 13 条独立事件。</p>
            </div>
            <el-button :icon="Plus" @click="addAcquisitionField">新增信号</el-button>
          </div>

          <el-form label-position="top" class="meta-form">
            <el-form-item label="Profile ID">
              <el-input v-model="workspace.acquisition.profileId" />
            </el-form-item>
            <el-form-item label="显示名称">
              <el-input v-model="workspace.acquisition.displayName" />
            </el-form-item>
            <el-form-item label="版本">
              <el-input-number v-model="workspace.acquisition.version" :min="1" controls-position="right" />
            </el-form-item>
            <el-form-item label="采样周期 (ms)">
              <el-input-number v-model="workspace.acquisition.samplePeriodMs" :min="100" :step="100" controls-position="right" />
            </el-form-item>
            <el-form-item label="步序字段">
              <el-input v-model="workspace.acquisition.stepContextKey" />
            </el-form-item>
          </el-form>

          <el-table :data="workspace.acquisition.fields" row-key="code" stripe class="config-table">
            <el-table-column type="index" label="#" width="54" />
            <el-table-column label="设备原始字段" min-width="190">
              <template #default="{ row }"><el-input v-model="row.sourceField" /></template>
            </el-table-column>
            <el-table-column label="分析稳定代码" min-width="230">
              <template #default="{ row }"><el-input v-model="row.code" /></template>
            </el-table-column>
            <el-table-column label="单位" width="130">
              <template #default="{ row }">
                <el-select v-model="row.unit" filterable allow-create default-first-option>
                  <el-option v-for="unit in unitOptions" :key="unit" :label="unit" :value="unit" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="语义状态" width="160">
              <template #default="{ row }">
                <el-select v-model="row.unitStatus">
                  <el-option label="已确认" value="confirmed" />
                  <el-option label="待确认" value="needs-confirmation" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="历史对比" width="100" align="center">
              <template #default="{ row }"><el-switch v-model="row.useInComparison" /></template>
            </el-table-column>
            <el-table-column label="操作" width="76" align="center">
              <template #default="{ $index }">
                <el-button link type="danger" :icon="Delete" aria-label="删除信号" @click="removeRow(workspace.acquisition.fields, $index)" />
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane name="recipe">
          <template #label><span class="tab-label"><Tickets />配方参数</span></template>

          <div class="section-heading">
            <div>
              <h2>配方与本次有效值</h2>
              <p>允许沿用上一版后局部修改；生产开始时冻结全部有效参数。</p>
            </div>
            <el-button :icon="Plus" @click="addRecipeParameter">新增参数</el-button>
          </div>

          <el-form label-position="top" class="meta-form recipe-meta">
            <el-form-item label="Profile ID">
              <el-input v-model="workspace.recipe.profileId" />
            </el-form-item>
            <el-form-item label="Profile 版本">
              <el-input-number v-model="workspace.recipe.profileVersion" :min="1" controls-position="right" />
            </el-form-item>
            <el-form-item label="配方 ID">
              <el-input v-model="workspace.recipe.recipeId" />
            </el-form-item>
            <el-form-item label="配方版本">
              <el-input-number v-model="workspace.recipe.recipeVersion" :min="1" controls-position="right" />
            </el-form-item>
            <el-form-item label="沿用版本">
              <el-input-number v-model="workspace.recipe.basedOnVersion" :min="1" controls-position="right" />
            </el-form-item>
            <el-form-item label="产品系列">
              <el-input v-model="workspace.recipe.productSeries" />
            </el-form-item>
          </el-form>

          <el-alert
            title="按当前业务规则，不记录配方参数修改原因；系统仍保存 basedOn、版本号和完整有效值快照。"
            type="success"
            show-icon
            :closable="false"
            class="inline-alert"
          />

          <el-table :data="workspace.recipe.parameters" row-key="code" stripe class="config-table">
            <el-table-column type="index" label="#" width="54" />
            <el-table-column label="设备原始字段" min-width="185">
              <template #default="{ row }"><el-input v-model="row.sourceField" /></template>
            </el-table-column>
            <el-table-column label="分析稳定代码" min-width="220">
              <template #default="{ row }"><el-input v-model="row.code" /></template>
            </el-table-column>
            <el-table-column label="类型" width="110">
              <template #default="{ row }">
                <el-select v-model="row.dataType">
                  <el-option label="数值" value="double" />
                  <el-option label="文本" value="string" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="本次有效值" min-width="160">
              <template #default="{ row }">
                <el-input-number v-if="row.dataType === 'double'" v-model="row.value" :precision="4" controls-position="right" class="number-input" />
                <el-input v-else v-model="row.value" />
              </template>
            </el-table-column>
            <el-table-column label="单位" width="120">
              <template #default="{ row }">
                <el-select v-model="row.unit" filterable allow-create clearable default-first-option :disabled="row.dataType === 'string'">
                  <el-option v-for="unit in unitOptions" :key="unit" :label="unit" :value="unit" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="语义状态" width="150">
              <template #default="{ row }">
                <el-select v-model="row.unitStatus" :disabled="row.dataType === 'string'">
                  <el-option label="已确认" value="confirmed" />
                  <el-option label="待确认" value="needs-confirmation" />
                  <el-option label="不适用" value="not-applicable" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="76" align="center">
              <template #default="{ $index }">
                <el-button link type="danger" :icon="Delete" aria-label="删除参数" @click="removeRow(workspace.recipe.parameters, $index)" />
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane name="phases">
          <template #label><span class="tab-label"><Guide />阶段映射</span></template>

          <div class="section-heading">
            <div>
              <h2>控制器步序与业务阶段</h2>
              <p>边缘保留原始步序，中心按映射版本解释阶段，历史数据可以重新计算。</p>
            </div>
            <el-button :icon="Plus" @click="addPhase">新增阶段</el-button>
          </div>

          <el-form label-position="top" class="meta-form phase-meta">
            <el-form-item label="映射 ID">
              <el-input v-model="workspace.phases.mappingId" />
            </el-form-item>
            <el-form-item label="版本">
              <el-input-number v-model="workspace.phases.version" :min="1" controls-position="right" />
            </el-form-item>
            <el-form-item label="预计总时长">
              <el-input :model-value="`${expectedDuration} 秒`" disabled />
            </el-form-item>
          </el-form>

          <el-table :data="workspace.phases.mappings" row-key="phaseCode" stripe class="config-table">
            <el-table-column type="index" label="顺序" width="70" />
            <el-table-column label="控制器步序" width="160">
              <template #default="{ row }"><el-input v-model="row.sourceStep" /></template>
            </el-table-column>
            <el-table-column label="阶段代码" min-width="190">
              <template #default="{ row }"><el-input v-model="row.phaseCode" /></template>
            </el-table-column>
            <el-table-column label="显示名称" min-width="180">
              <template #default="{ row }"><el-input v-model="row.displayName" /></template>
            </el-table-column>
            <el-table-column label="预计时长 (秒)" width="190">
              <template #default="{ row }">
                <el-input-number v-model="row.expectedDurationSeconds" :min="0" controls-position="right" class="number-input" />
              </template>
            </el-table-column>
            <el-table-column label="必需阶段" width="100" align="center">
              <template #default="{ row }"><el-switch v-model="row.required" /></template>
            </el-table-column>
            <el-table-column label="操作" width="76" align="center">
              <template #default="{ $index }">
                <el-button link type="danger" :icon="Delete" aria-label="删除阶段" @click="removeRow(workspace.phases.mappings, $index)" />
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <div class="footer-actions">
      <span>{{ saveStatus }}</span>
      <el-button @click="resetSample">恢复光学镜片样例</el-button>
      <el-button :icon="Check" @click="saveWorkspace">保存草稿</el-button>
      <el-button type="primary" :icon="Upload" :loading="publishing" @click="publishWorkspace">发布到平台</el-button>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { Check, DataLine, Delete, Download, Guide, Plus, Tickets, Upload } from "@element-plus/icons-vue";
import { createOpticalMoldingSample } from "../data/profileSamples";
import { postJson } from "../api/http";

const storageKey = "ingot.production-profile.workspace.v1";
const activeTab = ref("acquisition");
const fileInput = ref(null);
const savedAt = ref("");
const publishing = ref(false);
const workspace = reactive(createOpticalMoldingSample());
const unitOptions = ["Cel", "A", "V", "W", "mm", "mm/s", "s", "kPa", "kg", "1"];

const expectedDuration = computed(() => workspace.phases.mappings.reduce(
  (total, phase) => total + Number(phase.expectedDurationSeconds || 0),
  0,
));
const needsConfirmation = computed(() => [
  ...workspace.acquisition.fields,
  ...workspace.recipe.parameters,
].filter((item) => item.unitStatus === "needs-confirmation").length);
const saveStatus = computed(() => savedAt.value
  ? `最近保存：${new Date(savedAt.value).toLocaleString("zh-CN")}`
  : "尚未保存当前修改");

function replaceWorkspace(value) {
  Object.assign(workspace, JSON.parse(JSON.stringify(value)));
}

function addAcquisitionField() {
  workspace.acquisition.fields.push({ code: "", sourceField: "", unit: "", unitStatus: "confirmed", useInComparison: false });
}

function addRecipeParameter() {
  workspace.recipe.parameters.push({
    code: "",
    sourceField: "",
    dataType: "double",
    value: 0,
    unit: "",
    unitStatus: "confirmed",
  });
}

function addPhase() {
  workspace.phases.mappings.push({ sourceStep: "", phaseCode: "", displayName: "", expectedDurationSeconds: 0, required: true });
}

function removeRow(rows, index) {
  rows.splice(index, 1);
}

function duplicate(values) {
  const normalized = values.map((value) => String(value || "").trim()).filter(Boolean);
  return normalized.find((value, index) => normalized.indexOf(value) !== index);
}

function validateWorkspace() {
  if (!workspace.acquisition.profileId.trim() || !workspace.recipe.profileId.trim()) {
    throw new Error("采集和配方 Profile ID 不能为空。");
  }
  if (workspace.acquisition.samplePeriodMs < 100) throw new Error("采样周期不能小于 100ms。");
  const collections = [
    ["采集字段", workspace.acquisition.fields],
    ["配方参数", workspace.recipe.parameters],
  ];
  for (const [label, rows] of collections) {
    if (!rows.length) throw new Error(`${label}不能为空。`);
    if (rows.some((row) => !row.code?.trim() || !row.sourceField?.trim())) {
      throw new Error(`${label}存在未填写的原始字段或稳定代码。`);
    }
    const repeatedCode = duplicate(rows.map((row) => row.code));
    if (repeatedCode) throw new Error(`${label}存在重复代码：${repeatedCode}`);
    const repeatedSource = duplicate(rows.map((row) => row.sourceField));
    if (repeatedSource) throw new Error(`${label}存在重复原始字段：${repeatedSource}`);
  }
  if (!workspace.phases.mappings.length) throw new Error("至少需要一个阶段映射。");
  if (workspace.phases.mappings.some((row) => !row.sourceStep?.trim() || !row.phaseCode?.trim() || !row.displayName?.trim())) {
    throw new Error("阶段映射存在未填写项。");
  }
  const repeatedStep = duplicate(workspace.phases.mappings.map((row) => row.sourceStep));
  if (repeatedStep) throw new Error(`控制器步序重复：${repeatedStep}`);
}

function saveWorkspace() {
  try {
    validateWorkspace();
    savedAt.value = new Date().toISOString();
    localStorage.setItem(storageKey, JSON.stringify({ ...workspace, savedAt: savedAt.value }));
    ElMessage.success("配置已保存到当前浏览器");
  } catch (error) {
    ElMessage.error(error.message);
  }
}

async function publishWorkspace() {
  publishing.value = true;
  try {
    validateWorkspace();
    const updatedAt = new Date().toISOString();
    for (let index = 0; index < workspace.phases.mappings.length; index += 1) {
      const phase = workspace.phases.mappings[index];
      await postJson("/api/v1/phase-definitions", {
        code: phase.phaseCode,
        name: phase.displayName,
        sortOrder: (index + 1) * 10,
        required: phase.required !== false,
        updatedAt,
      });
      await postJson("/api/v1/phase-mappings", {
        mappingId: "",
        recipeId: workspace.recipe.recipeId,
        recipeVersion: String(workspace.recipe.recipeVersion),
        recipeTemplate: workspace.recipe.profileId,
        recipeStep: phase.sourceStep,
        recipeStepName: phase.displayName,
        phaseCode: phase.phaseCode,
        required: phase.required !== false,
        phaseSource: "recipe",
        updatedAt,
      });
    }
    for (const field of workspace.acquisition.fields) {
      await postJson("/api/v1/feature-definitions", {
        code: `comparison.${workspace.acquisition.profileId}.v${workspace.acquisition.version}.${field.code}.mean`,
        name: field.sourceField,
        phaseCode: "cycle",
        signal: field.code,
        aggregation: "mean",
        boundaryMode: "strict",
        unit: field.unit,
        productSeries: workspace.recipe.productSeries || null,
        productCode: null,
        recipeId: workspace.recipe.recipeId || null,
        machineId: null,
        enabled: true,
        useInComparison: Boolean(field.useInComparison),
        updatedAt,
      });
    }
    saveWorkspace();
    ElMessage.success("工艺配置已发布到平台");
  } catch (error) {
    ElMessage.error(`发布失败：${error.message}`);
  } finally {
    publishing.value = false;
  }
}

function exportWorkspace() {
  try {
    validateWorkspace();
    const content = JSON.stringify({ ...workspace, exportedAt: new Date().toISOString() }, null, 2);
    const url = URL.createObjectURL(new Blob([content], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `${workspace.recipe.productSeries || "production"}-profiles.json`;
    link.click();
    URL.revokeObjectURL(url);
    ElMessage.success("配置 JSON 已导出");
  } catch (error) {
    ElMessage.error(error.message);
  }
}

function openImport() {
  fileInput.value?.click();
}

async function importWorkspace(event) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  try {
    const value = JSON.parse(await file.text());
    if (!value.acquisition || !value.recipe || !value.phases) {
      throw new Error("JSON 必须包含 acquisition、recipe 和 phases。");
    }
    replaceWorkspace(value);
    validateWorkspace();
    savedAt.value = "";
    ElMessage.success("配置已导入，请检查后保存");
  } catch (error) {
    ElMessage.error(`导入失败：${error.message}`);
  }
}

async function resetSample() {
  try {
    await ElMessageBox.confirm("将放弃当前未导出的修改，恢复内置光学镜片样例。", "恢复样例", {
      confirmButtonText: "恢复",
      cancelButtonText: "取消",
      type: "warning",
    });
    replaceWorkspace(createOpticalMoldingSample());
    savedAt.value = "";
    ElMessage.success("已恢复样例");
  } catch {
    // 用户取消。
  }
}

onMounted(() => {
  const stored = localStorage.getItem(storageKey);
  if (!stored) return;
  try {
    const value = JSON.parse(stored);
    replaceWorkspace(value);
    savedAt.value = value.savedAt || "";
  } catch {
    localStorage.removeItem(storageKey);
    ElMessage.warning("本地配置无法解析，已载入内置样例");
  }
});
</script>

<style scoped>
.profile-view { display: flex; flex-direction: column; gap: 18px; }
.hero {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  padding: 28px 30px;
  border: 1px solid #dfe7f1;
  border-radius: 16px;
  background: linear-gradient(125deg, #fff 0%, #f4f8ff 58%, #edf5ff 100%);
}
.hero h1 { margin: 4px 0 8px; color: #17233d; font-size: 28px; }
.hero p { margin: 0; color: #637083; line-height: 1.7; }
.eyebrow { color: #337ecc; font-size: 12px; font-weight: 700; letter-spacing: 1.8px; }
.hero-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 10px; }
.hidden-input { display: none; }
.storage-notice { border-radius: 10px; }
.summary-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; }
.summary-card {
  display: grid;
  gap: 6px;
  padding: 18px 20px;
  border: 1px solid #e2e8f0;
  border-radius: 12px;
  background: #fff;
}
.summary-card span { color: #6b778c; font-size: 13px; }
.summary-card strong { color: #17233d; font-size: 28px; line-height: 1; }
.summary-card small { color: #8a94a6; }
.warning-card { border-color: #f3d19e; background: #fffaf2; }
.workspace-card { border-radius: 14px; }
.tab-label { display: inline-flex; align-items: center; gap: 7px; }
.tab-label svg { width: 16px; }
.section-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; margin: 10px 0 18px; }
.section-heading h2 { margin: 0 0 6px; color: #26334d; font-size: 20px; }
.section-heading p { margin: 0; color: #7a8598; }
.meta-form { display: grid; grid-template-columns: 1.5fr 1.5fr .7fr 1fr 1fr; gap: 12px; margin-bottom: 8px; }
.recipe-meta { grid-template-columns: 1.5fr .75fr 1fr .75fr .75fr 1fr; }
.phase-meta { grid-template-columns: 2fr .7fr 1fr; max-width: 760px; }
.meta-form :deep(.el-input-number) { width: 100%; }
.config-table { width: 100%; }
.number-input { width: 100%; }
.inline-alert { margin: 4px 0 18px; }
.footer-actions { display: flex; align-items: center; justify-content: flex-end; gap: 12px; color: #7a8598; font-size: 13px; }
@media (max-width: 1200px) {
  .hero { align-items: flex-start; flex-direction: column; }
  .hero-actions { justify-content: flex-start; }
  .summary-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .meta-form, .recipe-meta { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
@media (max-width: 720px) {
  .summary-grid, .meta-form, .recipe-meta, .phase-meta { grid-template-columns: 1fr; }
  .footer-actions { align-items: stretch; flex-direction: column; }
}
</style>
