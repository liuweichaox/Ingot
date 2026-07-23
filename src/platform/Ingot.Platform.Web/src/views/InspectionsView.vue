<template>
  <div class="inspections-view">
    <el-card shadow="never" class="task-card">
      <template #header>
        <div class="heading">
          <div>
            <strong>待检任务</strong>
            <span class="heading-note">周期自动生成；连续生产可建立时间窗口、运行段或物料批次</span>
          </div>
          <div class="task-actions">
            <el-button type="primary" plain @click="openScopeDrawer">新建质量范围</el-button>
            <el-button plain @click="router.push('/configuration/quality-plans')">配置质量方案</el-button>
            <el-segmented v-model="taskStatus" :options="taskStatusOptions" @change="onTaskStatusChange" />
          </div>
        </div>
      </template>
      <el-table
        v-loading="loadingTasks"
        :data="pagedTasks"
        stripe
        max-height="320"
        :row-class-name="taskRowClass"
      >
        <el-table-column prop="completedAt" label="范围截止" width="180">
          <template #default="{ row }">{{ formatTime(row.completedAt) }}</template>
        </el-table-column>
        <el-table-column label="设备 / 产品" min-width="190">
          <template #default="{ row }"><div class="cell-stack"><strong>{{ row.machineId }}</strong><span>{{ row.productSeries || '-' }}</span></div></template>
        </el-table-column>
        <el-table-column label="质量方案" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">{{ row.inspectionPlanName }} · v{{ row.inspectionPlanVersion }}</template>
        </el-table-column>
        <el-table-column label="质量对象 / 范围" min-width="250">
          <template #default="{ row }"><div class="cell-stack"><strong>{{ row.workpieceId }}</strong><span>{{ scopeTypeLabel(row.scopeType) }} · {{ row.operationRunId }}</span></div></template>
        </el-table-column>
        <el-table-column label="待检项" width="90">
          <template #default="{ row }">{{ row.missingDefinitionCodes?.length || 0 }}</template>
        </el-table-column>
        <el-table-column label="状态" width="120">
          <template #default="{ row }"><el-tag :type="taskTag(row.status)">{{ taskLabel(row.status) }}</el-tag></template>
        </el-table-column>
        <el-table-column label="操作" width="160" fixed="right">
          <template #default="{ row }">
            <el-button text type="primary" @click="selectTask(row)">处理</el-button>
            <el-button v-if="row.scopeType !== 'production-cycle' && row.status === 'pending'" text type="danger" @click="deleteScope(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loadingTasks && !tasks.length" description="当前没有符合条件的任务" />
      <TablePagination v-model:page="taskPage" v-model:page-size="taskPageSize" :total="taskTotal" />
    </el-card>
    <el-drawer v-model="scopeDrawerVisible" title="新建质量范围" size="min(600px, 94vw)">
      <el-form label-position="top">
        <el-form-item label="范围类型">
          <el-select v-model="scopeForm.scopeType">
            <el-option label="时间窗口" value="analysis-window" />
            <el-option label="生产运行段" value="production-run" />
            <el-option label="物料批次" value="material-lot" />
          </el-select>
        </el-form-item>
        <el-form-item label="质量方案">
          <el-select v-model="scopeForm.planKey" filterable placeholder="选择已发布质量方案">
            <el-option v-for="item in publishedPlans" :key="`${item.planId}@${item.version}`" :label="`${item.name} · v${item.version}`" :value="`${item.planId}@${item.version}`" />
          </el-select>
        </el-form-item>
        <el-form-item label="时间范围">
          <el-date-picker v-model="scopeForm.timeRange" type="datetimerange" start-placeholder="开始时间" end-placeholder="结束时间" class="full-width" />
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item label="对象类型"><el-input v-model="scopeForm.subjectType" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="设备或对象编号"><el-input v-model="scopeForm.subjectId" /></el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item label="质量对象标识"><el-input v-model="scopeForm.workpieceId" placeholder="样件、批次或窗口标识" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="产品系列"><el-input v-model="scopeForm.productSeries" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer><el-button @click="scopeDrawerVisible = false">取消</el-button><el-button type="primary" :loading="scopeSaving" @click="saveScope">创建</el-button></template>
    </el-drawer>
    <el-drawer v-model="entryVisible" title="检验录入" size="min(640px, 94vw)">
      <el-card shadow="never">
        <template #header>
          <div class="heading form-heading">
            <el-icon><DocumentChecked /></el-icon>
            <span>新建检验记录</span>
          </div>
        </template>

        <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="notice" />
        <el-alert
          v-if="correctionTarget"
          :title="`正在更正记录 ${correctionTarget.recordId}`"
          description="原记录不会被修改；提交后系统将保留完整更正链。"
          type="warning"
          show-icon
          :closable="false"
          class="notice"
        />

        <el-form label-position="top">
          <el-form-item label="检测项目">
            <el-select v-model="selectedDefinitionKey" :disabled="Boolean(correctionTarget)" filterable placeholder="选择检测项目" @change="applyDefinition">
              <el-option
                v-for="item in definitions"
                :key="`${item.code}:${item.version}`"
                :label="`${item.name} v${item.version}`"
                :value="`${item.code}:${item.version}`"
              />
            </el-select>
          </el-form-item>

          <el-row :gutter="12">
            <el-col :span="12">
              <el-form-item label="工件 ID">
                <el-input v-model="form.workpieceId" placeholder="从待检任务带入" readonly />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="生产周期号">
                <el-input v-model="form.operationRunId" placeholder="从待检任务带入" readonly />
              </el-form-item>
            </el-col>
          </el-row>

          <el-form-item label="结果">
            <el-segmented v-model="form.outcome" :options="outcomeOptions" />
          </el-form-item>

          <el-form-item
            v-for="characteristic in selectedDefinition?.characteristics || []"
            :key="characteristic.code"
            :label="`${characteristic.name}${characteristic.unit ? ` (${characteristic.unit})` : ''}`"
          >
            <el-input-number
              v-if="characteristic.inputType === 'numeric'"
              v-model="measurements[characteristic.code]"
              :precision="4"
              controls-position="right"
              class="full-width"
            />
            <el-select
              v-else-if="characteristic.inputType === 'select'"
              v-model="measurements[characteristic.code]"
              placeholder="请选择"
              class="full-width"
            >
              <el-option v-for="option in characteristic.allowedValues || []" :key="option" :label="option" :value="option" />
            </el-select>
            <el-segmented
              v-else-if="characteristic.inputType === 'boolean'"
              v-model="measurements[characteristic.code]"
              :options="booleanOptions"
            />
            <el-input v-else v-model="measurements[characteristic.code]" />
            <div class="hint">
              {{ limitText(characteristic) }}
            </div>
          </el-form-item>

          <el-form-item label="现场照片或附件">
            <input type="file" accept="image/*,.tif,.tiff" @change="onFileChange">
            <el-button :loading="uploading" :disabled="!pendingFile" @click="uploadAttachments">
              上传附件
            </el-button>
          </el-form-item>
          <el-table v-if="attachments.length" :data="attachments" size="small" stripe class="attachments-table">
            <el-table-column prop="fileName" label="文件" min-width="160" />
            <el-table-column prop="mediaType" label="类型" width="140" />
            <el-table-column label="大小" width="100">
              <template #default="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
            </el-table-column>
          </el-table>

          <el-form-item label="备注">
            <el-input v-model="form.notes" type="textarea" :rows="3" maxlength="2000" show-word-limit />
          </el-form-item>
          <el-form-item v-if="correctionTarget" label="更正原因">
            <el-input v-model="form.correctionReason" type="textarea" :rows="2" maxlength="500" show-word-limit />
          </el-form-item>

          <div class="actions">
            <el-button type="primary" :loading="submitting" :disabled="!canSubmit" @click="submit">
              提交检测记录
            </el-button>
            <el-button @click="resetForm">重置</el-button>
          </div>
        </el-form>
      </el-card>
    </el-drawer>

    <div class="records-workspace">
      <el-card shadow="never">
        <template #header>
          <div class="heading">
            <span>最近检测记录</span>
          </div>
        </template>
        <el-table v-loading="loadingRecords" :data="pagedRecords" stripe max-height="680">
          <el-table-column type="expand" width="44">
            <template #default="{ row }">
              <div class="record-detail">
                <div>
                  <strong>测量结果</strong>
                  <p v-for="item in row.measurements || []" :key="item.characteristicCode">
                    {{ item.characteristicCode }}：{{ measurementText(item) }} · {{ outcomeLabel(item.outcome) }}
                  </p>
                  <p v-if="!row.measurements?.length" class="muted">无测量项</p>
                  <p v-if="row.supersedesRecordId" class="correction-note">本记录更正了 {{ row.supersedesRecordId }}：{{ row.correctionReason }}</p>
                  <p v-if="correctionByRecord[row.recordId]" class="correction-note">已由 {{ correctionByRecord[row.recordId].recordId }} 更正</p>
                  <el-button v-if="!correctionByRecord[row.recordId]" size="small" plain @click="openCorrection(row)">更正记录</el-button>
                </div>
                <div>
                  <strong>原始附件与复核</strong>
                  <p v-for="item in row.attachments || []" :key="item.attachmentId">
                    <el-link
                      type="primary"
                      :href="`/api/v1/inspection-attachments/${item.attachmentId}/content`"
                      target="_blank"
                    >
                      打开原图 · {{ item.fileName }}
                    </el-link>
                    <span class="attachment-meta">SHA-256 {{ item.sha256 }}</span>
                  </p>
                  <p v-if="!row.attachments?.length" class="muted">无附件</p>
                  <template v-if="row.attachments?.length">
                    <p v-if="reviewByRecord[row.recordId]" class="review-result">
                      最近复核：{{ reviewLabel(reviewByRecord[row.recordId].decision) }} ·
                      {{ reviewByRecord[row.recordId].reviewedBy }} ·
                      {{ formatTime(reviewByRecord[row.recordId].reviewedAt) }}
                    </p>
                    <el-button size="small" type="primary" plain @click="openReview(row)">
                      {{ reviewByRecord[row.recordId] ? '追加复核' : '复核原图' }}
                    </el-button>
                  </template>
                </div>
              </div>
            </template>
          </el-table-column>
          <el-table-column prop="measuredAt" label="检测时间" width="180">
            <template #default="{ row }">{{ formatTime(row.measuredAt) }}</template>
          </el-table-column>
          <el-table-column prop="workpieceId" label="工件" min-width="130" />
          <el-table-column prop="operationRunId" label="周期" min-width="160" show-overflow-tooltip />
          <el-table-column prop="definitionCode" label="检测项目" min-width="150" />
          <el-table-column label="结果" width="110">
            <template #default="{ row }">
              <el-tag :type="row.outcome === 'PASS' ? 'success' : row.outcome === 'FAIL' ? 'danger' : 'warning'">
                {{ outcomeLabel(row.outcome) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="测量" width="100">
            <template #default="{ row }">{{ row.measurements?.length || 0 }}</template>
          </el-table-column>
          <el-table-column label="附件" width="100">
            <template #default="{ row }">{{ row.attachments?.length || 0 }}</template>
          </el-table-column>
          <el-table-column label="原图复核" width="130" fixed="right">
            <template #default="{ row }">
              <el-tag v-if="row.attachments?.length" :type="reviewTag(reviewByRecord[row.recordId]?.decision)">
                {{ reviewLabel(reviewByRecord[row.recordId]?.decision) }}
              </el-tag>
              <span v-else class="muted">不适用</span>
            </template>
          </el-table-column>
        </el-table>
        <el-empty v-if="!loadingRecords && !records.length" description="暂无检测记录" />
        <TablePagination v-model:page="recordPage" v-model:page-size="recordPageSize" :total="recordTotal" />
      </el-card>
    </div>

    <el-drawer v-model="reviewVisible" title="视觉原图复核" size="560px">
      <el-form label-position="top">
        <el-form-item label="检测记录">
          <el-input :model-value="reviewTarget?.recordId || ''" readonly />
        </el-form-item>
        <el-form-item label="复核结论">
          <el-segmented v-model="reviewForm.decision" :options="reviewOptions" />
        </el-form-item>
        <el-form-item label="复核说明">
          <el-input v-model="reviewForm.notes" type="textarea" :rows="4" maxlength="2000" show-word-limit />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewVisible = false">取消</el-button>
        <el-button type="primary" :loading="reviewSubmitting" @click="submitReview">提交复核</el-button>
      </template>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElDatePicker, ElMessage, ElMessageBox } from "element-plus";
import { useRoute, useRouter } from "vue-router";
import { DocumentChecked } from "@element-plus/icons-vue";
import { deleteJson, getJson, postForm, postJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";

const definitions = ref([]);
const route = useRoute();
const router = useRouter();
const selectedDefinitionKey = ref("");
const selectedDefinition = ref(null);
const measurements = reactive({});
const attachments = ref([]);
const records = ref([]);
const tasks = ref([]);
const qualityPlans = ref([]);
const recordPage = ref(1);
const recordPageSize = ref(20);
const recordTotal = ref(0);
const pagedRecords = computed(() => records.value);
const taskPage = ref(1);
const taskPageSize = ref(20);
const taskTotal = ref(0);
const pagedTasks = computed(() => tasks.value);
const reviews = ref([]);
const pendingFile = ref(null);
const error = ref("");
const uploading = ref(false);
const submitting = ref(false);
const loadingRecords = ref(false);
const loadingTasks = ref(false);
const contextLocked = ref(false);
const entryVisible = ref(false);
const taskStatus = ref("all");
const reviewVisible = ref(false);
const reviewTarget = ref(null);
const correctionTarget = ref(null);
const reviewSubmitting = ref(false);
const scopeDrawerVisible = ref(false);
const scopeSaving = ref(false);
const scopeForm = reactive({ scopeType: "analysis-window", planKey: "", timeRange: [], subjectType: "equipment", subjectId: "", workpieceId: "", productSeries: "" });
const reviewForm = reactive({ decision: "CONFIRMED", notes: "" });
const outcomeOptions = [
  { label: "合格", value: "PASS" },
  { label: "不合格", value: "FAIL" },
  { label: "待确认", value: "INCONCLUSIVE" },
];
const taskStatusOptions = [
  { label: "全部", value: "all" },
  { label: "待检", value: "pending" },
  { label: "处理中", value: "in_progress" },
  { label: "待复核", value: "review_pending" },
  { label: "已完成", value: "completed" },
];
const reviewOptions = [
  { label: "确认", value: "CONFIRMED" },
  { label: "驳回", value: "REJECTED" },
  { label: "要求重检", value: "REINSPECTION_REQUIRED" },
];
const booleanOptions = [
  { label: "是", value: "true" },
  { label: "否", value: "false" },
];
const publishedPlans = computed(() => qualityPlans.value.filter(item => item.status === "published"));

const form = reactive({
  workpieceId: "",
  operationRunId: "",
  outcome: "PASS",
  notes: "",
  correctionReason: "",
});

const correctionByRecord = computed(() => Object.fromEntries(
  records.value.filter(item => item.supersedesRecordId).map(item => [item.supersedesRecordId, item])
));

const reviewByRecord = computed(() => {
  const result = {};
  for (const review of reviews.value) {
    if (!result[review.inspectionRecordId]) result[review.inspectionRecordId] = review;
  }
  return result;
});

const canSubmit = computed(() =>
  selectedDefinition.value &&
  form.workpieceId.trim() &&
  form.operationRunId.trim() &&
  contextLocked.value &&
  (!correctionTarget.value || form.correctionReason.trim()) &&
  (selectedDefinition.value.characteristics || []).every((item) =>
    !item.required || (measurements[item.code] !== null && measurements[item.code] !== undefined && measurements[item.code] !== "")
  ) &&
  (Object.values(measurements).some((value) => value !== null && value !== undefined && value !== "") || attachments.value.length)
);

function applyDefinition() {
  const [code, version] = selectedDefinitionKey.value.split(":");
  selectedDefinition.value = definitions.value.find((item) => item.code === code && String(item.version) === version) || null;
  Object.keys(measurements).forEach((key) => delete measurements[key]);
  for (const characteristic of selectedDefinition.value?.characteristics || []) measurements[characteristic.code] = null;
}

function onFileChange(event) {
  pendingFile.value = event.target.files?.[0] || null;
}

async function uploadAttachments() {
  if (!pendingFile.value) return;
  uploading.value = true;
  error.value = "";
  try {
    const data = new FormData();
    data.append("file", pendingFile.value);
    const uploaded = await postForm("/api/v1/inspection-attachments", data);
    if (!attachments.value.some((item) => item.attachmentId === uploaded.attachmentId)) {
      attachments.value = [...attachments.value, uploaded];
    }
    pendingFile.value = null;
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    uploading.value = false;
  }
}

async function submit() {
  submitting.value = true;
  error.value = "";
  try {
    const definition = selectedDefinition.value;
    const measurementRows = (definition.characteristics || [])
      .map((characteristic) => {
        const raw = measurements[characteristic.code];
        if (raw === null || raw === undefined || raw === "") return null;
        const numeric = characteristic.inputType === "numeric";
        const numericValue = numeric ? Number(raw) : null;
        const outcome = numeric && Number.isFinite(numericValue)
          ? evaluateOutcome(numericValue, characteristic)
          : form.outcome;
        return {
          characteristicCode: characteristic.code,
          outcome,
          numericValue: numeric ? numericValue : null,
          textValue: numeric ? null : String(raw),
          unit: numeric ? (characteristic.unit || "1") : null,
          lowerLimit: characteristic.lowerLimit ?? null,
          upperLimit: characteristic.upperLimit ?? null,
        };
      })
      .filter(Boolean);
    const now = new Date().toISOString();
    const record = await postJson("/api/v1/inspection-records", {
      recordId: uuidv7(),
      workpieceId: form.workpieceId,
      operationRunId: form.operationRunId,
      definitionCode: definition.code,
      definitionVersion: definition.version,
      measuredAt: now,
      recordedAt: now,
      outcome: form.outcome,
      measurements: measurementRows,
      attachments: attachments.value,
      notes: form.notes || null,
      supersedesRecordId: correctionTarget.value?.recordId || null,
      correctionReason: correctionTarget.value ? form.correctionReason : null,
    });
    ElMessage.success(`检测记录已提交：${record.recordId}`);
    correctionTarget.value = null;
    await Promise.all([loadRecords(), loadTasks(), loadReviews()]);
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    submitting.value = false;
  }
}

async function loadDefinitions() {
  const [result, planResult] = await Promise.all([getJson("/api/v1/inspection-definitions"), getJson("/api/v1/inspection-plans")]);
  definitions.value = result.data || [];
  qualityPlans.value = planResult.data || [];
  if (!selectedDefinitionKey.value && definitions.value.length) {
    selectedDefinitionKey.value = `${definitions.value[0].code}:${definitions.value[0].version}`;
    applyDefinition();
  }
}

function openScopeDrawer() {
  Object.assign(scopeForm, { scopeType: "analysis-window", planKey: "", timeRange: [], subjectType: "equipment", subjectId: "", workpieceId: "", productSeries: "" });
  scopeDrawerVisible.value = true;
}

async function saveScope() {
  const [planId, version] = scopeForm.planKey.split("@");
  if (!planId || !scopeForm.timeRange?.[0] || !scopeForm.timeRange?.[1]) {
    ElMessage.warning("请选择质量方案和时间范围"); return;
  }
  scopeSaving.value = true;
  try {
    await postJson("/api/v1/inspection-scopes", {
      scopeId: `quality-${uuidv7()}`,
      scopeType: scopeForm.scopeType,
      workpieceId: scopeForm.workpieceId,
      subjectType: scopeForm.subjectType,
      subjectId: scopeForm.subjectId,
      productSeries: scopeForm.productSeries,
      inspectionPlanId: planId,
      inspectionPlanVersion: Number(version),
      from: new Date(scopeForm.timeRange[0]).toISOString(),
      to: new Date(scopeForm.timeRange[1]).toISOString(),
      context: {},
    });
    scopeDrawerVisible.value = false;
    ElMessage.success("质量范围已创建");
    taskPage.value = 1; await loadTasks();
  } catch (requestError) { ElMessage.error(requestError.message); }
  finally { scopeSaving.value = false; }
}

async function deleteScope(task) {
  await ElMessageBox.confirm("只允许删除尚未产生检测记录的质量范围。", "删除质量范围", { type: "warning" });
  await deleteJson(`/api/v1/inspection-scopes/${encodeURIComponent(task.operationRunId)}`);
  ElMessage.success("质量范围已删除"); await loadTasks();
}

async function loadRecords() {
  loadingRecords.value = true;
  try {
    const offset = (recordPage.value - 1) * recordPageSize.value;
    const result = await getJson(`/api/v1/inspection-records?limit=${recordPageSize.value}&offset=${offset}`);
    records.value = result.data || [];
    recordTotal.value = result.total || 0;
  } finally {
    loadingRecords.value = false;
  }
}

async function loadTasks() {
  loadingTasks.value = true;
  try {
    const offset = (taskPage.value - 1) * taskPageSize.value;
    const result = await getJson(`/api/v1/inspection-tasks?status=${taskStatus.value}&limit=${taskPageSize.value}&offset=${offset}`);
    tasks.value = result.data || [];
    taskTotal.value = result.total || 0;
  } finally {
    loadingTasks.value = false;
  }
}

function onTaskStatusChange() {
  if (taskPage.value === 1) loadTasks();
  else taskPage.value = 1;
}

async function loadReviews() {
  const result = await getJson("/api/v1/inspection-reviews?limit=500");
  reviews.value = result.data || [];
}

function resetForm() {
  Object.assign(form, {
    workpieceId: contextLocked.value ? String(route.query.workpieceId || "") : "",
    operationRunId: contextLocked.value ? String(route.query.operationRunId || "") : "",
    outcome: "PASS",
    notes: "",
    correctionReason: "",
  });
  attachments.value = [];
  Object.keys(measurements).forEach((key) => measurements[key] = null);
  error.value = "";
}

function openCorrection(record) {
  correctionTarget.value = record;
  contextLocked.value = true;
  entryVisible.value = true;
  form.workpieceId = record.workpieceId;
  form.operationRunId = record.operationRunId;
  form.outcome = record.outcome;
  form.notes = record.notes || "";
  form.correctionReason = "";
  selectedDefinitionKey.value = `${record.definitionCode}:${record.definitionVersion}`;
  applyDefinition();
  for (const item of record.measurements || []) {
    measurements[item.characteristicCode] = item.numericValue ?? item.textValue ?? null;
  }
  attachments.value = [...(record.attachments || [])];
}

function applyRouteContext() {
  const workpieceId = String(route.query.workpieceId || "").trim();
  const operationRunId = String(route.query.operationRunId || "").trim();
  contextLocked.value = Boolean(workpieceId && operationRunId);
  if (contextLocked.value) {
    entryVisible.value = true;
    form.workpieceId = workpieceId;
    form.operationRunId = operationRunId;
    const definitionCode = String(route.query.definitionCode || "").trim();
    const definitionVersion = String(route.query.definitionVersion || "").trim();
    if (definitionCode) {
      const definition = definitions.value.find((item) => item.code === definitionCode &&
        (!definitionVersion || String(item.version) === definitionVersion));
      if (definition) {
        selectedDefinitionKey.value = `${definition.code}:${definition.version}`;
        applyDefinition();
      }
    }
  }
}

async function selectTask(task) {
  contextLocked.value = true;
  entryVisible.value = true;
  form.workpieceId = task.workpieceId;
  form.operationRunId = task.operationRunId;
  const missingCode = task.missingDefinitionCodes?.[0];
  const requiredItem = task.requiredInspections?.find((item) => item.definitionCode === missingCode);
  if (missingCode) {
    const definition = definitions.value.find((item) => item.code === missingCode &&
      (!requiredItem || item.version === requiredItem.definitionVersion));
    if (definition) {
      selectedDefinitionKey.value = `${definition.code}:${definition.version}`;
      applyDefinition();
    }
  }
  await router.replace({
    path: "/inspections",
    query: {
      workpieceId: task.workpieceId,
      operationRunId: task.operationRunId,
      ...(missingCode ? { definitionCode: missingCode, definitionVersion: requiredItem?.definitionVersion } : {}),
    },
  });
}

function openReview(record) {
  reviewTarget.value = record;
  reviewForm.decision = "CONFIRMED";
  reviewForm.notes = "";
  reviewVisible.value = true;
}

async function submitReview() {
  if (!reviewTarget.value) return;
  reviewSubmitting.value = true;
  error.value = "";
  try {
    await postJson("/api/v1/inspection-reviews", {
      reviewId: uuidv7(),
      inspectionRecordId: reviewTarget.value.recordId,
      decision: reviewForm.decision,
      notes: reviewForm.notes || null,
    });
    reviewVisible.value = false;
    ElMessage.success("视觉复核结论已保存");
    await Promise.all([loadReviews(), loadTasks()]);
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    reviewSubmitting.value = false;
  }
}

function measurementText(item) {
  if (item.numericValue != null) return `${item.numericValue} ${item.unit || ""}`.trim();
  return item.textValue || "-";
}

function evaluateOutcome(value, characteristic) {
  if (characteristic.lowerLimit != null && value < Number(characteristic.lowerLimit)) return "FAIL";
  if (characteristic.upperLimit != null && value > Number(characteristic.upperLimit)) return "FAIL";
  return "PASS";
}

function outcomeLabel(value) {
  return outcomeOptions.find((item) => item.value === value)?.label || value;
}

function taskLabel(value) {
  return { pending: "待检", in_progress: "处理中", review_pending: "待复核", completed: "已完成" }[value] || value;
}
function scopeTypeLabel(value) {
  return { "production-cycle": "生产周期", "analysis-window": "时间窗口", "production-run": "生产运行段", "material-lot": "物料批次" }[value] || value;
}
function taskTag(value) {
  return { pending: "danger", in_progress: "warning", review_pending: "primary", completed: "success" }[value] || "info";
}
function reviewLabel(value) {
  return { CONFIRMED: "已确认", REJECTED: "已驳回", REINSPECTION_REQUIRED: "要求重检" }[value] || "待复核";
}
function reviewTag(value) {
  return { CONFIRMED: "success", REJECTED: "danger", REINSPECTION_REQUIRED: "warning" }[value] || "info";
}
function taskRowClass({ row }) {
  return row.operationRunId === form.operationRunId ? "current-task-row" : "";
}

function limitText(characteristic) {
  const lower = characteristic.lowerLimit ?? "-";
  const upper = characteristic.upperLimit ?? "-";
  return characteristic.inputType === "numeric" ? `下限 ${lower} / 上限 ${upper}` : "";
}

function formatBytes(value) {
  if (!value) return "-";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}

function formatTime(value) {
  return value ? new Date(value).toLocaleString("zh-CN") : "-";
}

function uuidv7() {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  const timestamp = BigInt(Date.now());
  bytes[0] = Number((timestamp >> 40n) & 0xffn);
  bytes[1] = Number((timestamp >> 32n) & 0xffn);
  bytes[2] = Number((timestamp >> 24n) & 0xffn);
  bytes[3] = Number((timestamp >> 16n) & 0xffn);
  bytes[4] = Number((timestamp >> 8n) & 0xffn);
  bytes[5] = Number(timestamp & 0xffn);
  bytes[6] = (bytes[6] & 0x0f) | 0x70;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

onMounted(async () => {
  try {
    await Promise.all([loadDefinitions(), loadRecords(), loadTasks(), loadReviews()]);
    applyRouteContext();
  } catch (requestError) {
    error.value = requestError.message;
  }
});

watch([recordPage, recordPageSize], loadRecords);
watch([taskPage, taskPageSize], loadTasks);
</script>

<style scoped>
.heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  font-weight: 600;
}
.form-heading { justify-content: flex-start; }
.heading-note { margin-left: 10px; color: #9099a8; font-size: 12px; font-weight: 400; }
.task-card { margin-bottom: 18px; }
.records-workspace { width: 100%; }
.task-actions { display: flex; align-items: center; gap: 10px; }
.cell-stack { display: grid; min-width: 0; gap: 3px; }
.cell-stack strong, .cell-stack span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.cell-stack strong { color: #334056; font-size: 13px; }
.cell-stack span { color: #8b95a4; font-size: 11px; }
.review-result { padding: 8px 10px; border-radius: 6px; background: #f5f8fc; }
.correction-note { padding: 8px 10px; border-radius: 6px; background: #fff7e6; color: #8a5a16 !important; }
:deep(.current-task-row td.el-table__cell) { background: #ecf6ff !important; }
.notice { margin-bottom: 16px; }
.full-width { width: 100%; }
.hint { width: 100%; margin-top: 4px; color: #909399; font-size: 12px; }
.actions { display: flex; gap: 10px; }
.attachments-table { margin-bottom: 16px; }
.record-detail { display: grid; grid-template-columns: 1fr 1fr; gap: 28px; padding: 12px 46px; }
.record-detail p { margin: 8px 0 0; color: #5f6b7c; line-height: 1.55; }
.attachment-meta { display: block; margin-top: 3px; color: #9aa3b1; font-family: ui-monospace, monospace; font-size: 11px; word-break: break-all; }
.muted { color: #9aa3b1 !important; }
@media (max-width: 1200px) {
  .inspections-view :deep(.el-card) { margin-bottom: 18px; }
}
@media (max-width: 760px) {
  .record-detail { grid-template-columns: 1fr; padding: 8px 14px; }
}
</style>
