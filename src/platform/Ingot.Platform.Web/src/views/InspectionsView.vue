<template>
  <div class="inspections-view">
    <el-row :gutter="20">
      <el-col :lg="9" :md="24">
        <el-card shadow="never">
          <template #header>
            <div class="heading">
              <el-icon><DocumentChecked /></el-icon>
              <span>检测录入</span>
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
                  <el-input v-model="form.workpieceId" placeholder="WP-001" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="生产周期号">
                  <el-input v-model="form.operationRunId" placeholder="CYCLE-001" />
                </el-form-item>
              </el-col>
            </el-row>

            <el-row :gutter="12">
              <el-col :span="12">
                <el-form-item label="检测员或工位">
                  <el-input v-model="form.submittedBy" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="结果">
                  <el-segmented v-model="form.outcome" :options="outcomeOptions" />
                </el-form-item>
              </el-col>
            </el-row>

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
              <input type="file" @change="onFileChange">
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
      </el-col>

      <el-col :lg="15" :md="24">
        <el-card shadow="never">
          <template #header>
            <div class="heading">
              <span>最近检测记录</span>
              <el-button :icon="Refresh" :loading="loadingRecords" @click="loadRecords">刷新</el-button>
            </div>
          </template>
          <el-table v-loading="loadingRecords" :data="records" stripe max-height="680">
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
          </el-table>
          <el-empty v-if="!loadingRecords && !records.length" description="暂无检测记录" />
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from "vue";
import { DocumentChecked, Refresh } from "@element-plus/icons-vue";
import { getJson, postForm, postJson } from "../api/http";

const definitions = ref([]);
const selectedDefinitionKey = ref("");
const selectedDefinition = ref(null);
const measurements = reactive({});
const attachments = ref([]);
const records = ref([]);
const pendingFile = ref(null);
const error = ref("");
const success = ref("");
const uploading = ref(false);
const submitting = ref(false);
const loadingRecords = ref(false);
const outcomeOptions = [
  { label: "合格", value: "PASS" },
  { label: "不合格", value: "FAIL" },
  { label: "待确认", value: "INCONCLUSIVE" },
];

const form = reactive({
  workpieceId: "",
  operationRunId: "",
  submittedBy: "operator",
  outcome: "PASS",
  notes: "",
});

const canSubmit = computed(() =>
  selectedDefinition.value &&
  form.workpieceId.trim() &&
  form.operationRunId.trim() &&
  form.submittedBy.trim() &&
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
      submittedBy: form.submittedBy,
      measurements: measurementRows,
      attachments: attachments.value,
      notes: form.notes || null,
    });
    success.value = `检测记录已提交：${record.recordId}`;
    await loadRecords();
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

function resetForm() {
  Object.assign(form, { workpieceId: "", operationRunId: "", submittedBy: "operator", outcome: "PASS", notes: "" });
  attachments.value = [];
  Object.keys(measurements).forEach((key) => measurements[key] = null);
  success.value = "";
  error.value = "";
}

function evaluateOutcome(value, characteristic) {
  if (characteristic.lowerLimit != null && value < Number(characteristic.lowerLimit)) return "FAIL";
  if (characteristic.upperLimit != null && value > Number(characteristic.upperLimit)) return "FAIL";
  return "PASS";
}

function outcomeLabel(value) {
  return outcomeOptions.find((item) => item.value === value)?.label || value;
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
    await Promise.all([loadDefinitions(), loadRecords()]);
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
.heading:first-child { justify-content: flex-start; }
.notice { margin-bottom: 16px; }
.full-width { width: 100%; }
.hint { width: 100%; margin-top: 4px; color: #909399; font-size: 12px; }
.actions { display: flex; gap: 10px; }
.attachments-table { margin-bottom: 16px; }
@media (max-width: 1200px) {
  .inspections-view :deep(.el-card) { margin-bottom: 18px; }
}
</style>
