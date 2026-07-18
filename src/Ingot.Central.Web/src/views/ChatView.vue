<template>
  <div class="chat-view">
    <el-row :gutter="20">
      <el-col :lg="9" :md="24">
        <el-card shadow="never">
          <template #header>
            <div class="heading">
              <el-icon><ChatDotRound /></el-icon>
              <span>Ingot Chat</span>
            </div>
          </template>
          <el-alert
            title="基于可信生产事实查询数据、定位问题，并回到相关证据；不执行设备控制或数据写入。"
            type="info"
            show-icon
            :closable="false"
            class="notice"
          />
          <el-form label-position="top">
            <el-form-item label="回答方式">
              <el-radio-group v-model="form.mode">
                <el-radio-button value="standard" :disabled="!supportsMode('standard')">
                  标准分析
                </el-radio-button>
                <el-radio-button value="deep" :disabled="!supportsMode('deep')">
                  深入调查
                </el-radio-button>
              </el-radio-group>
              <div class="muted capability-note">
                {{ capabilitySummary }}
              </div>
            </el-form-item>
            <el-form-item label="想了解什么？">
              <el-input
                v-model="form.question"
                type="textarea"
                :rows="5"
                maxlength="4000"
                show-word-limit
                placeholder="例如：这个周期发生了什么，数据是否完整？"
              />
            </el-form-item>
            <el-form-item label="当前上下文（可选）">
              <el-input v-model="form.contextId" placeholder="资产 ID 或周期关联 ID">
                <template #prepend>
                  <el-select v-model="form.contextKind" style="width: 130px">
                    <el-option label="资产" value="asset" />
                    <el-option label="生产周期" value="cycle" />
                  </el-select>
                </template>
              </el-input>
            </el-form-item>
            <el-row :gutter="12">
              <el-col :span="12">
                <el-form-item label="访问身份">
                  <el-input v-model="form.actor" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="访问令牌">
                  <el-input v-model="form.token" type="password" show-password />
                </el-form-item>
              </el-col>
            </el-row>
            <div class="actions">
              <el-button
                type="primary"
                :loading="submitting"
                :disabled="running || !canStart"
                @click="start"
              >
                发送问题
              </el-button>
              <el-button v-if="running" type="danger" plain @click="cancel">
                取消回答
              </el-button>
              <el-button @click="loadChat">
                刷新
              </el-button>
            </div>
          </el-form>
        </el-card>

        <el-card shadow="never" class="history-card">
          <template #header>
            <div class="heading">
              <span>对话记录</span>
              <el-tag size="small">当前身份</el-tag>
            </div>
          </template>
          <el-empty v-if="!history.length" description="暂无可见记录" :image-size="54" />
          <button
            v-for="item in history"
            :key="item.runId"
            class="history-item"
            @click="openHistory(item.runId)"
          >
            <span>{{ item.question }}</span>
            <small>{{ modeLabel(item.mode) }} · {{ runStatusLabel(item.status) }} · {{ formatTime(item.createdAt) }}</small>
          </button>
          <el-button v-if="historyNext" text type="primary" @click="loadHistory(historyNext, true)">
            加载更多
          </el-button>
        </el-card>
      </el-col>

      <el-col :lg="15" :md="24">
        <el-card shadow="never" class="result-card">
          <template #header>
            <div class="result-header">
              <span>Ingot Chat</span>
              <el-tag :type="statusType">{{ statusLabel }}</el-tag>
            </div>
          </template>
          <el-alert
            v-if="error"
            :title="error"
            type="error"
            show-icon
            :closable="false"
            class="notice"
          />
          <el-empty
            v-if="!runId"
            description="发送问题后，这里会展示回答、相关事实和证据。"
          />
          <template v-else>
            <section v-if="participantFailures.length" class="section">
              <h3>调查说明</h3>
              <el-alert
                v-for="item in participantFailures"
                :key="`${item.data?.role}-${item.data?.round}`"
                title="部分分析步骤暂不可用，已保留可验证的结果和限制条件。"
                type="warning"
                show-icon
                :closable="false"
                class="limitation"
              />
            </section>

            <section v-if="snapshot?.plan" class="section">
              <h3>查询范围</h3>
              <p>{{ snapshot.plan.summary }}</p>
              <el-tag
                v-for="call in snapshot.plan.toolCalls"
                :key="call.tool"
                class="tool-tag"
                effect="plain"
              >
                {{ toolLabel(call.tool) }}
              </el-tag>
            </section>

            <section v-if="snapshot?.toolInvocations?.length" class="section">
              <h3>事实查询</h3>
              <el-timeline>
                <el-timeline-item
                  v-for="(item, index) in snapshot.toolInvocations"
                  :key="`${item.tool}-${index}`"
                  :type="item.status === 'completed' ? 'success' : item.status === 'failed' ? 'danger' : 'primary'"
                >
                  <strong>{{ toolLabel(item.tool) }}</strong> · {{ queryStatusLabel(item.status) }}
                  <div class="muted">{{ item.summary || item.error }}</div>
                </el-timeline-item>
              </el-timeline>
            </section>

            <section v-if="snapshot?.answer?.investigation" class="section investigation">
              <h3>深入调查</h3>
              <el-alert
                :title="snapshot.answer.investigation.summary"
                :type="snapshot.answer.investigation.status === 'candidate' ? 'warning' : 'info'"
                show-icon
                :closable="false"
              />
              <h4>候选假设</h4>
              <el-card
                v-for="item in snapshot.answer.investigation.hypotheses"
                :key="item.hypothesisId"
                shadow="never"
                class="hypothesis"
              >
                <div class="hypothesis-title">
                  <strong>{{ item.statement }}</strong>
                </div>
                <div class="muted">{{ item.rationale }}</div>
              </el-card>
              <el-alert
                v-for="item in snapshot.answer.investigation.limitations"
                :key="item"
                :title="item"
                type="warning"
                show-icon
                :closable="false"
                class="limitation"
              />
            </section>

            <section v-if="snapshot?.answer?.charts?.length" class="section">
              <h3>分析图表</h3>
              <el-card
                v-for="chart in snapshot.answer.charts"
                :key="`${chart.type}-${chart.title}`"
                shadow="never"
                class="chart-card"
              >
                <template #header>
                  <div class="result-header">
                    <span>{{ chart.title }}</span>
                    <el-tag size="small" effect="plain">{{ chartTypeLabel(chart.type) }}</el-tag>
                  </div>
                </template>
                <div class="chart-table-wrap">
                  <table class="chart-table">
                    <thead>
                      <tr>
                        <th>系列</th>
                        <th v-for="label in chart.labels" :key="label">{{ label }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="series in chart.series" :key="series.name">
                        <th>{{ series.name }}</th>
                        <td v-for="(value, index) in series.values" :key="`${series.name}-${index}`">
                          {{ formatChartValue(value) }}
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </el-card>
            </section>

            <section v-if="snapshot?.answer" class="section answer">
              <h3>回答</h3>
              <p>{{ snapshot.answer.summary }}</p>
              <ul>
                <li v-for="item in snapshot.answer.findings" :key="item">{{ item }}</li>
              </ul>
              <el-alert
                v-for="item in snapshot.answer.limitations"
                :key="item"
                :title="item"
                type="warning"
                show-icon
                :closable="false"
                class="limitation"
              />
              <div v-if="snapshot.answer.followUpQuestions?.length" class="follow-ups">
                <h4>后续问题</h4>
                <ul>
                  <li v-for="item in snapshot.answer.followUpQuestions" :key="item">{{ item }}</li>
                </ul>
              </div>
              <el-collapse>
                <el-collapse-item title="事实证据抽屉" name="evidence">
                  <el-table :data="snapshot.answer.evidence" size="small" stripe>
                    <el-table-column prop="kind" label="类型" width="150" />
                    <el-table-column prop="label" label="事实" min-width="260" />
                    <el-table-column label="回到事实" width="100">
                      <template #default="{ row }">
                        <el-link v-if="row.url" type="primary" :href="row.url">查看</el-link>
                      </template>
                    </el-table-column>
                  </el-table>
                </el-collapse-item>
              </el-collapse>
            </section>
          </template>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, reactive, ref, watch } from "vue";
import { ChatDotRound } from "@element-plus/icons-vue";
import { getJson, postJson, streamSse } from "../api/http";

const form = reactive({
  question: "",
  mode: "standard",
  contextKind: "asset",
  contextId: "",
  actor: "operator",
  token: "",
});
const runId = ref("");
const snapshot = ref(null);
const events = ref([]);
const error = ref("");
const submitting = ref(false);
const capabilities = ref(null);
const history = ref([]);
const historyNext = ref(null);
let streamController;

const authHeaders = () => ({
  "X-Ingot-Actor": form.actor.trim(),
  ...(form.token ? { Authorization: `Bearer ${form.token}` } : {}),
});
const running = computed(() => ["queued", "running", "cancelling"].includes(snapshot.value?.status));
const canStart = computed(() => Boolean(capabilities.value?.enabled && supportsMode(form.mode) && form.question.trim()));
const statusLabel = computed(() => runStatusLabel(snapshot.value?.status));
const statusType = computed(() => ({
  completed: "success",
  failed: "danger",
  cancelled: "warning",
  cancelling: "warning",
  running: "primary",
})[snapshot.value?.status] || "info");
const participantFailures = computed(() => events.value.filter((item) => item.type === "discussion.participant_failed"));
const capabilitySummary = computed(() => {
  if (!capabilities.value) return "填写访问凭据后读取可用回答方式";
  const modes = capabilities.value.modes || [];
  if (!modes.length) return "当前没有可用的回答方式";
  return `当前可用：${modes.map(modeLabel).join("、")}`;
});

const supportsMode = (mode) => capabilities.value?.modes?.includes(mode) ?? false;

async function refresh() {
  snapshot.value = await getJson(`/api/v1/chat/runs/${runId.value}`, { headers: authHeaders() });
}

async function loadCapabilities() {
  capabilities.value = await getJson("/api/v1/chat/capabilities", { headers: authHeaders() });
  if (!supportsMode(form.mode)) form.mode = capabilities.value.modes?.[0] || "standard";
}

async function loadHistory(before = null, append = false) {
  const query = new URLSearchParams({ limit: "12" });
  if (before) query.set("before", before);
  const page = await getJson(`/api/v1/chat/runs?${query}`, { headers: authHeaders() });
  history.value = append ? [...history.value, ...(page.items || [])] : (page.items || []);
  historyNext.value = page.nextBefore;
}

async function loadChat() {
  error.value = "";
  try {
    await Promise.all([loadCapabilities(), loadHistory()]);
  } catch (requestError) {
    error.value = requestError.message;
  }
}

async function start() {
  streamController?.abort();
  submitting.value = true;
  error.value = "";
  events.value = [];
  snapshot.value = null;
  try {
    const pageContext = form.contextId.trim()
      ? { kind: form.contextKind, id: form.contextId.trim() }
      : null;
    const created = await postJson(
      "/api/v1/chat/runs",
      { question: form.question, pageContext, mode: form.mode },
      { headers: authHeaders() },
    );
    runId.value = created.runId;
    snapshot.value = { status: created.status };
    streamController = new AbortController();
    let lastEventId = 0;
    for (let attempt = 0; attempt < 4; attempt++) {
      try {
        lastEventId = await streamSse(created.streamUrl, {
          headers: authHeaders(),
          signal: streamController.signal,
          lastEventId,
          onEvent: async ({ id, data }) => {
            lastEventId = id;
            events.value.push(data);
            await refresh();
          },
        });
        break;
      } catch (streamError) {
        if (streamError.name === "AbortError") throw streamError;
        await refresh();
        if (!running.value || attempt === 3) throw streamError;
        await new Promise((resolve) => setTimeout(resolve, 400 * (attempt + 1)));
      }
    }
    await refresh();
    await loadHistory();
  } catch (requestError) {
    if (requestError.name !== "AbortError") error.value = requestError.message;
  } finally {
    submitting.value = false;
  }
}

async function cancel() {
  try {
    await postJson(`/api/v1/chat/runs/${runId.value}:cancel`, {}, { headers: authHeaders() });
  } catch (requestError) {
    error.value = requestError.message;
  }
}

async function openHistory(id) {
  streamController?.abort();
  runId.value = id;
  events.value = [];
  error.value = "";
  try {
    await refresh();
  } catch (requestError) {
    error.value = requestError.message;
  }
}

function toolLabel(tool) {
  return ({
    check_data_quality: "检查数据质量",
    get_cycle_trace: "查看周期事实",
  })[tool] || "查询生产事实";
}

function queryStatusLabel(status) {
  return ({
    queued: "等待中",
    running: "查询中",
    completed: "已完成",
    failed: "未完成",
    cancelled: "已取消",
  })[status] || status || "处理中";
}

function modeLabel(mode) {
  return mode === "deep" ? "深度调查" : "标准分析";
}

function runStatusLabel(status) {
  return ({
    queued: "等待回答",
    running: "正在回答",
    cancelling: "正在取消",
    completed: "已完成",
    failed: "未完成",
    cancelled: "已取消",
  })[status] || "尚未开始";
}

function chartTypeLabel(type) {
  return ({
    line: "折线图",
    bar: "柱状图",
    scatter: "散点图",
    histogram: "直方图",
    boxplot: "箱线图",
  })[type] || type;
}

function formatChartValue(value) {
  if (value == null) return "-";
  return Number.isFinite(Number(value)) ? Number(value).toLocaleString("zh-CN") : String(value);
}

function formatTime(value) {
  return value ? new Date(value).toLocaleString("zh-CN") : "";
}

watch(() => [form.actor, form.token], () => {
  capabilities.value = null;
  history.value = [];
});

onBeforeUnmount(() => streamController?.abort());
</script>

<style scoped>
.heading,
.result-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  font-weight: 600;
}

.heading { justify-content: flex-start; }
.notice { margin-bottom: 18px; }
.actions { display: flex; flex-wrap: wrap; gap: 10px; }
.result-card { min-height: 500px; }
.section { margin-top: 24px; }
.section h3 { margin: 0 0 10px; font-size: 16px; }
.section p { line-height: 1.7; }
.tool-tag { margin: 0 8px 8px 0; }
.muted { margin-top: 4px; color: #909399; }
.answer ul { padding-left: 20px; line-height: 1.8; }
.limitation { margin: 8px 0; }
.investigation h4,
.follow-ups h4 { margin: 18px 0 10px; }
.hypothesis { margin: 10px 0; }
.hypothesis-title { display: flex; align-items: flex-start; gap: 10px; line-height: 1.6; }
.capability-note { width: 100%; }
.history-card { margin-top: 18px; }
.history-item {
  display: flex;
  width: 100%;
  flex-direction: column;
  gap: 5px;
  padding: 11px 0;
  border: 0;
  border-bottom: 1px solid #ebeef5;
  background: transparent;
  color: #303133;
  text-align: left;
  cursor: pointer;
}
.history-item:hover { color: #409eff; }
.history-item small { color: #909399; }
.chart-card { margin: 10px 0; }
.chart-table-wrap { overflow-x: auto; }
.chart-table { width: 100%; border-collapse: collapse; font-size: 13px; }
.chart-table th,
.chart-table td { padding: 9px 12px; border: 1px solid #ebeef5; text-align: right; white-space: nowrap; }
.chart-table th:first-child { text-align: left; }
.chart-table thead th { background: #f7f8fa; }

@media (max-width: 1200px) {
  .result-card { margin-top: 20px; }
}
</style>
