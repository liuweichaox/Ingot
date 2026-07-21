<template>
  <div class="inspections-view">
    <el-alert
      :title="contextLocked ? '已关联生产周期，工件和周期信息由待检任务自动带入。' : '请先选择待检任务；工件和周期不允许手工填写。'"
      :type="contextLocked ? 'success' : 'info'"
      show-icon
      :closable="false"
      class="workflow-notice"
    />
    <el-card shadow="never" class="task-card">
      <template #header>
        <div class="heading">
          <div>
            <strong>待检任务</strong>
            <span class="heading-note">由已完成生产周期和检验定义自动生成</span>
          </div>
          <div class="task-actions">
            <el-button plain @click="router.push('/quality-plans')">配置质量方案</el-button>
            <el-segmented v-model="taskStatus" :options="taskStatusOptions" @change="loadTasks" />
            <el-button :icon="Refresh" :loading="loadingTasks" @click="loadTasks">刷新</el-button>
          </div>
        </div>
      </template>
      <el-table
        v-loading="loadingTasks"
        :data="tasks"
        stripe
        max-height="320"
        :row-class-name="taskRowClass"
      >
        <el-table-column prop="completedAt" label="周期完成" width="180">
          <template #default="{ row }">{{ formatTime(row.completedAt) }}</template>
        </el-table-column>
        <el-table-column prop="machineId" label="设备" width="150" />
        <el-table-column prop="productSeries" label="产品系列" width="120" />
        <el-table-column label="质量方案" min-width="170" show-overflow-tooltip>
          <template #default="{ row }">{{ row.inspectionPlanName }} · v{{ row.inspectionPlanVersion }}</template>
        </el-table-column>
        <el-table-column prop="workpieceId" label="工件" min-width="180" show-overflow-tooltip />
        <el-table-column prop="operationRunId" label="周期" min-width="210" show-overflow-tooltip />
        <el-table-column label="缺少项目" min-width="220">
          <template #default="{ row }">{{ row.missingDefinitionCodes?.join('、') || '无' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="120">
          <template #default="{ row }"><el-tag :type="taskTag(row.status)">{{ taskLabel(row.status) }}</el-tag></template>
        </el-table-column>
        <el-table-column label="操作" width="110" fixed="right">
          <template #default="{ row }">
            <el-button text type="primary" @click="selectTask(row)">处理</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loadingTasks && !tasks.length" description="当前没有符合条件的任务" />
    </el-card>
    <el-drawer v-model="entryVisible" title="检验录入" size="min(640px, 94vw)">
      <el-card shadow="never">
        <template #header>
          <div class="heading form-heading">
            <el-icon><DocumentChecked /></el-icon>
            <span>新建检验记录</span>
          </div>
        </template>

        <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="notice" />
        <el-alert v-if="success" :title="success" type="success" show-icon :closable="false" class="notice" />

        <el-form label-position="top">
          <el-form-item label="检测项目">
            <el-select v-model="selectedDefinitionKey" filterable placeholder="选择检测项目" @change="applyDefinition">
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

          <div class="actions">
            <el-button type="primary" :loading="submitting" :disabled="!canSubmit" @click="submit">
              提交检测记录
            </el-button>
            <el-button :icon="Refresh" @click="resetForm">重置</el-button>
          </div>
        </el-form>
      </el-card>
    </el-drawer>

    <div class="records-workspace">
      <el-card shadow="never">
        <template #header>
          <div class="heading">
            <span>最近检测记录</span>
            <el-button :icon="Refresh" :loading="loadingRecords" @click="loadRecords">刷新</el-button>
          </div>
        </template>
        <el-table v-loading="loadingRecords" :data="records" stripe max-height="680">
          <el-table-column type="expand" width="44">
            <template #default="{ row }">
              <div class="record-detail">
                <div>
                  <strong>测量结果</strong>
                  <p v-for="item in row.measurements || []" :key="item.characteristicCode">
                    {{ item.characteristicCode }}：{{ measurementText(item) }} · {{ outcomeLabel(item.outcome) }}
                  </p>
                  <p v-if="!row.measurements?.length" class="muted">无测量项</p>
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
      </el-card>
    </div>

    <el-dialog v-model="reviewVisible" title="视觉原图复核" width="560px">
      <el-alert
        title="复核结论会追加保存并记录当前平台身份，不会覆盖原始检测记录。"
        type="info"
        show-icon
        :closable="false"
        class="notice"
      />
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
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { DocumentChecked, Refresh } from "@element-plus/icons-vue";
import { getJson, postForm, postJson } from "../api/http";

const definitions = ref([]);
const route = useRoute();
const router = useRouter();
const selectedDefinitionKey = ref("");
const selectedDefinition = ref(null);
const measurements = reactive({});
const attachments = ref([]);
const records = ref([]);
const tasks = ref([]);
const reviews = ref([]);
const pendingFile = ref(null);
const error = ref("");
const success = ref("");
const uploading = ref(false);
const submitting = ref(false);
const loadingRecords = ref(false);
const loadingTasks = ref(false);
const contextLocked = ref(false);
const entryVisible = ref(false);
const taskStatus = ref("all");
const reviewVisible = ref(false);
const reviewTarget = ref(null);
const reviewSubmitting = ref(false);
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

const form = reactive({
  workpieceId: "",
  operationRunId: "",
  outcome: "PASS",
  notes: "",
});

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
  success.value = "";
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
    });
    success.value = `检测记录已提交：${record.recordId}`;
    await Promise.all([loadRecords(), loadTasks(), loadReviews()]);
  } catch (requestError) {
    error.value = requestError.message;
  } finally {
    submitting.value = false;
  }
}

async function loadDefinitions() {
  const result = await getJson("/api/v1/inspection-definitions");
  definitions.value = result.data || [];
  if (!selectedDefinitionKey.value && definitions.value.length) {
    selectedDefinitionKey.value = `${definitions.value[0].code}:${definitions.value[0].version}`;
    applyDefinition();
  }
}

async function loadRecords() {
  loadingRecords.value = true;
  try {
    const result = await getJson("/api/v1/inspection-records?limit=100");
    records.value = result.data || [];
  } finally {
    loadingRecords.value = false;
  }
}

async function loadTasks() {
  loadingTasks.value = true;
  try {
    const result = await getJson(`/api/v1/inspection-tasks?status=${taskStatus.value}&limit=200`);
    tasks.value = result.data || [];
  } finally {
    loadingTasks.value = false;
  }
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
  });
  attachments.value = [];
  Object.keys(measurements).forEach((key) => measurements[key] = null);
  success.value = "";
  error.value = "";
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
    success.value = "视觉复核结论已追加保存。";
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
.review-result { padding: 8px 10px; border-radius: 6px; background: #f5f8fc; }
:deep(.current-task-row td.el-table__cell) { background: #ecf6ff !important; }
.notice { margin-bottom: 16px; }
.full-width { width: 100%; }
.hint { width: 100%; margin-top: 4px; color: #909399; font-size: 12px; }
.actions { display: flex; gap: 10px; }
.attachments-table { margin-bottom: 16px; }
.workflow-notice { margin-bottom: 18px; }
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
