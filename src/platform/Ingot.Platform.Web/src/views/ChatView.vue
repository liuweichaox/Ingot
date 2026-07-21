<template>
  <div class="chat-view">
    <header class="chat-topbar">
      <div class="chat-brand">
        <div class="brand-mark"><el-icon><ChatDotRound /></el-icon></div>
        <div>
          <strong>Ingot Chat</strong>
          <span>生产数据只读分析</span>
        </div>
      </div>
      <div class="topbar-actions">
        <el-button :icon="Clock" @click="historyVisible = true">对话记录</el-button>
        <el-button :icon="Plus" @click="newChat">新对话</el-button>
      </div>
    </header>

    <main class="conversation-stream">
      <el-alert
        v-if="error"
        :title="error"
        type="error"
        show-icon
        :closable="false"
        class="conversation-alert"
      />

      <section v-if="!runId" class="welcome-state">
        <div class="welcome-mark"><el-icon><ChatDotRound /></el-icon></div>
        <h1>今天想分析什么？</h1>
        <p>查询生产记录、比较同系列周期、检查数据质量。Ingot Chat 不会修改设备或生产数据。</p>
        <div class="suggestion-grid">
          <button v-for="item in suggestions" :key="item" type="button" @click="useSuggestion(item)">
            <span>{{ item }}</span>
            <el-icon><Right /></el-icon>
          </button>
        </div>
      </section>

      <template v-else>
        <article class="message-row user-message">
          <div class="message-content user-bubble">
            <p>{{ currentQuestion }}</p>
            <el-tag v-if="currentContext" size="small" effect="plain" class="context-tag">
              {{ currentContext.kind === "cycle" ? "生产周期" : "设备" }} · {{ currentContext.id }}
            </el-tag>
          </div>
        </article>

        <article class="message-row assistant-message">
          <div class="assistant-avatar"><el-icon><ChatDotRound /></el-icon></div>
          <div class="message-content assistant-content">
            <div class="assistant-heading">
              <strong>Ingot Chat</strong>
              <el-tag :type="statusType" size="small" effect="plain">{{ statusLabel }}</el-tag>
            </div>

            <div v-if="!snapshot?.answer" class="thinking-state">
              <el-icon v-if="running" class="is-loading"><Loading /></el-icon>
              <span>{{ running ? "正在查询生产记录并组织回答…" : statusLabel }}</span>
            </div>

            <section v-if="snapshot?.answer" class="answer-section">
              <p class="answer-summary">{{ snapshot.answer.summary }}</p>
              <ul v-if="snapshot.answer.findings?.length" class="finding-list">
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
                <h3>你还可以继续问</h3>
                <button
                  v-for="item in snapshot.answer.followUpQuestions"
                  :key="item"
                  type="button"
                  @click="useSuggestion(item)"
                >
                  {{ item }}
                </button>
              </div>
            </section>

            <section v-if="snapshot?.answer?.combinedAnalysis" class="detail-section combined-analysis">
              <h3>综合分析</h3>
              <el-alert
                :title="snapshot.answer.combinedAnalysis.summary"
                :type="snapshot.answer.combinedAnalysis.status === 'needs-review' ? 'warning' : 'info'"
                show-icon
                :closable="false"
              />
              <div
                v-for="item in snapshot.answer.combinedAnalysis.possibleCauses"
                :key="item.causeId"
                class="cause-card"
              >
                <strong>{{ item.statement }}</strong>
                <p>{{ item.reason }}</p>
              </div>
              <el-alert
                v-for="item in snapshot.answer.combinedAnalysis.limitations"
                :key="item"
                :title="item"
                type="warning"
                show-icon
                :closable="false"
                class="limitation"
              />
            </section>

            <section v-if="snapshot?.answer?.charts?.length" class="detail-section">
              <h3>分析图表</h3>
              <el-card
                v-for="chart in snapshot.answer.charts"
                :key="`${chart.type}-${chart.title}`"
                shadow="never"
                class="chart-card"
              >
                <template #header>
                  <div class="section-header">
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

            <el-collapse v-if="snapshot?.plan || snapshot?.toolInvocations?.length" class="process-collapse">
              <el-collapse-item title="查看分析过程" name="process">
                <section v-if="participantFailures.length" class="process-section">
                  <el-alert
                    v-for="item in participantFailures"
                    :key="`${item.data?.role}-${item.data?.round}`"
                    title="部分分析步骤暂时无法完成，页面仅展示已经查到的结果。"
                    type="warning"
                    show-icon
                    :closable="false"
                    class="limitation"
                  />
                </section>
                <section v-if="snapshot?.plan" class="process-section">
                  <h3>调查说明</h3>
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
                <section v-if="snapshot?.toolInvocations?.length" class="process-section">
                  <h3>生产记录查询</h3>
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
              </el-collapse-item>
            </el-collapse>

            <el-collapse v-if="snapshot?.answer?.relatedRecords?.length" class="records-collapse">
              <el-collapse-item title="查看相关生产记录" name="relatedRecords">
                <el-table :data="snapshot.answer.relatedRecords" size="small" stripe>
                  <el-table-column prop="kind" label="类型" width="150" />
                  <el-table-column prop="label" label="生产记录" min-width="260" />
                  <el-table-column label="操作" width="100">
                    <template #default="{ row }">
                      <el-link v-if="row.url" type="primary" :href="row.url">查看</el-link>
                    </template>
                  </el-table-column>
                </el-table>
              </el-collapse-item>
            </el-collapse>
          </div>
        </article>
      </template>
    </main>

    <footer class="composer-area">
      <div class="composer-card">
        <el-input
          v-model="form.question"
          type="textarea"
          :autosize="{ minRows: 1, maxRows: 6 }"
          maxlength="4000"
          resize="none"
          placeholder="向 Ingot 询问生产数据…"
          @keydown.enter.exact.prevent="submitFromKeyboard"
        />
        <div class="composer-toolbar">
          <div class="composer-options">
            <el-button-group class="mode-switch">
              <el-button
                size="small"
                :type="form.mode === 'quick' ? 'primary' : 'default'"
                :disabled="!supportsMode('quick')"
                @click="form.mode = 'quick'"
              >
                快速查询
              </el-button>
              <el-button
                size="small"
                :type="form.mode === 'combined' ? 'primary' : 'default'"
                :disabled="!supportsMode('combined')"
                @click="form.mode = 'combined'"
              >
                综合分析
              </el-button>
            </el-button-group>
            <div class="context-control">
              <el-select v-model="form.contextKind" class="context-kind">
                <el-option label="设备" value="asset" />
                <el-option label="生产周期" value="cycle" />
              </el-select>
              <el-input v-model="form.contextId" clearable placeholder="查询对象（可选）" />
            </div>
          </div>
          <div class="send-actions">
            <el-button v-if="running" type="danger" plain @click="cancel">停止</el-button>
            <el-button
              type="primary"
              circle
              :icon="Promotion"
              :loading="submitting"
              :disabled="running || !canStart"
              aria-label="发送问题"
              @click="start"
            />
          </div>
        </div>
      </div>
      <div class="composer-note">{{ capabilitySummary }} · 回答仅基于已保存的生产记录</div>
    </footer>

    <el-drawer v-model="historyVisible" title="对话记录" size="380px">
      <div class="history-toolbar">
        <el-button :icon="Refresh" @click="loadChat">刷新</el-button>
      </div>
      <el-empty v-if="!history.length" description="暂无可见记录" :image-size="64" />
      <button
        v-for="item in history"
        :key="item.runId"
        type="button"
        class="history-item"
        @click="openHistory(item.runId)"
      >
        <span>{{ item.question }}</span>
        <small>{{ modeLabel(item.mode) }} · {{ runStatusLabel(item.status) }} · {{ formatTime(item.createdAt) }}</small>
      </button>
      <el-button v-if="historyNext" text type="primary" @click="loadHistory(historyNext, true)">加载更多</el-button>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref } from "vue";
import { ChatDotRound, Clock, Loading, Plus, Promotion, Refresh, Right } from "@element-plus/icons-vue";
import { getJson, postJson, streamSse } from "../api/http";

const suggestions = [
  "这个周期发生了什么，数据是否完整？",
  "查找同产品系列的历史周期并比较差异",
  "哪个模压阶段最可能出现异常？",
  "比较当前配方与历史合格周期的参数差异",
];
const form = reactive({ question: "", mode: "quick", contextKind: "asset", contextId: "" });
const runId = ref("");
const snapshot = ref(null);
const events = ref([]);
const error = ref("");
const submitting = ref(false);
const capabilities = ref(null);
const history = ref([]);
const historyNext = ref(null);
const historyVisible = ref(false);
let streamController;

const running = computed(() => ["queued", "running", "cancelling"].includes(snapshot.value?.status));
const canStart = computed(() => Boolean(capabilities.value?.enabled && supportsMode(form.mode) && form.question.trim()));
const currentQuestion = computed(() => snapshot.value?.question || form.question);
const currentContext = computed(() => snapshot.value?.pageContext || (form.contextId.trim()
  ? { kind: form.contextKind, id: form.contextId.trim() }
  : null));
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
  if (!capabilities.value) return "正在读取可用回答方式";
  const modes = capabilities.value.modes || [];
  if (!modes.length) return "当前没有可用的回答方式";
  return `当前可用：${modes.map(modeLabel).join("、")}`;
});

const supportsMode = (mode) => capabilities.value?.modes?.includes(mode) ?? false;

async function refresh() {
  snapshot.value = await getJson(`/api/v1/chat/runs/${runId.value}`);
}

async function loadCapabilities() {
  capabilities.value = await getJson("/api/v1/chat/capabilities");
  if (!supportsMode(form.mode)) form.mode = capabilities.value.modes?.[0] || "quick";
}

async function loadHistory(before = null, append = false) {
  const query = new URLSearchParams({ limit: "12" });
  if (before) query.set("before", before);
  const page = await getJson(`/api/v1/chat/runs?${query}`);
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
    );
    runId.value = created.runId;
    snapshot.value = { status: created.status, question: form.question, pageContext };
    streamController = new AbortController();
    let lastEventId = 0;
    for (let attempt = 0; attempt < 4; attempt++) {
      try {
        lastEventId = await streamSse(created.streamUrl, {
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
    await postJson(`/api/v1/chat/runs/${runId.value}:cancel`, {});
  } catch (requestError) {
    error.value = requestError.message;
  }
}

async function openHistory(id) {
  streamController?.abort();
  runId.value = id;
  events.value = [];
  error.value = "";
  historyVisible.value = false;
  try {
    await refresh();
  } catch (requestError) {
    error.value = requestError.message;
  }
}

function newChat() {
  streamController?.abort();
  runId.value = "";
  snapshot.value = null;
  events.value = [];
  error.value = "";
  form.question = "";
}

function useSuggestion(question) {
  form.question = question;
}

function submitFromKeyboard() {
  if (canStart.value && !running.value) start();
}

function toolLabel(tool) {
  return ({
    check_data_quality: "检查数据质量",
    get_cycle_trace: "查看生产周期过程",
    find_comparable_cycles: "查找同类周期",
    compare_cycles: "比较周期差异",
  })[tool] || "查询数据";
}

function queryStatusLabel(status) {
  return ({ queued: "等待中", running: "查询中", completed: "已完成", failed: "未完成", cancelled: "已取消" })[status]
    || status
    || "处理中";
}

function modeLabel(mode) {
  return mode === "combined" ? "综合分析" : "快速查询";
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
  return ({ line: "折线图", bar: "柱状图", scatter: "散点图", histogram: "直方图", boxplot: "箱线图" })[type] || type;
}

function formatChartValue(value) {
  if (value == null) return "-";
  return Number.isFinite(Number(value)) ? Number(value).toLocaleString("zh-CN") : String(value);
}

function formatTime(value) {
  return value ? new Date(value).toLocaleString("zh-CN") : "";
}

onMounted(loadChat);
onBeforeUnmount(() => streamController?.abort());
</script>

<style scoped>
.chat-view {
  display: flex;
  flex-direction: column;
  min-height: calc(100vh - 112px);
  color: #253047;
}
.chat-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  padding: 4px 8px 18px;
  border-bottom: 1px solid #e7ebf1;
}
.chat-brand { display: flex; align-items: center; gap: 11px; }
.chat-brand > div:last-child { display: grid; gap: 2px; }
.chat-brand strong { color: #182238; font-size: 17px; }
.chat-brand span { color: #8a94a6; font-size: 12px; }
.brand-mark,
.welcome-mark,
.assistant-avatar {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 auto;
  border-radius: 12px;
  color: #fff;
  background: linear-gradient(135deg, #409eff, #6b7cff);
}
.brand-mark { width: 36px; height: 36px; }
.topbar-actions { display: flex; gap: 8px; }
.conversation-stream {
  width: min(900px, 100%);
  flex: 1;
  margin: 0 auto;
  padding: 36px 18px 190px;
}
.conversation-alert { margin-bottom: 24px; }
.welcome-state { display: grid; justify-items: center; padding: 72px 0 30px; text-align: center; }
.welcome-mark { width: 58px; height: 58px; margin-bottom: 18px; font-size: 26px; box-shadow: 0 12px 28px rgba(64, 158, 255, .22); }
.welcome-state h1 { margin: 0 0 10px; color: #182238; font-size: 30px; letter-spacing: -.5px; }
.welcome-state > p { max-width: 620px; margin: 0; color: #6d788a; line-height: 1.75; }
.suggestion-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; width: 100%; margin-top: 34px; }
.suggestion-grid button {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 68px;
  padding: 14px 17px;
  border: 1px solid #e1e6ee;
  border-radius: 14px;
  color: #3c475a;
  background: #fff;
  text-align: left;
  cursor: pointer;
  transition: border-color .18s, box-shadow .18s, transform .18s;
}
.suggestion-grid button:hover { border-color: #a9cdf8; box-shadow: 0 8px 24px rgba(31, 67, 120, .08); transform: translateY(-1px); }
.message-row { display: flex; gap: 14px; margin-bottom: 30px; }
.user-message { justify-content: flex-end; }
.message-content { min-width: 0; }
.user-bubble { max-width: 75%; padding: 13px 17px; border-radius: 18px 18px 4px 18px; background: #eef4fb; }
.user-bubble p { margin: 0; line-height: 1.7; }
.context-tag { margin-top: 8px; }
.assistant-avatar { width: 34px; height: 34px; margin-top: 1px; border-radius: 10px; }
.assistant-content { flex: 1; padding-top: 4px; }
.assistant-heading,
.section-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.assistant-heading { margin-bottom: 16px; }
.thinking-state { display: flex; align-items: center; gap: 9px; color: #727e90; }
.answer-section { color: #303b4f; font-size: 15px; line-height: 1.8; }
.answer-summary { margin: 0 0 14px; white-space: pre-wrap; }
.finding-list { margin: 10px 0 0; padding-left: 22px; }
.finding-list li { margin: 7px 0; }
.limitation { margin: 10px 0; }
.follow-ups { margin-top: 22px; }
.follow-ups h3,
.detail-section h3,
.process-section h3 { margin: 0 0 12px; font-size: 15px; }
.follow-ups button {
  display: block;
  width: 100%;
  margin: 8px 0;
  padding: 10px 12px;
  border: 1px solid #e3e8ef;
  border-radius: 10px;
  color: #42658c;
  background: #fbfcfe;
  text-align: left;
  cursor: pointer;
}
.detail-section { margin-top: 26px; }
.cause-card { margin-top: 10px; padding: 14px 16px; border: 1px solid #e5eaf1; border-radius: 12px; background: #fbfcfe; }
.cause-card p { margin: 6px 0 0; color: #6d788a; line-height: 1.65; }
.process-collapse,
.records-collapse { margin-top: 24px; border-top: 1px solid #e8ecf2; }
.process-section { padding: 6px 0 10px; }
.process-section p { line-height: 1.7; }
.tool-tag { margin: 0 8px 8px 0; }
.muted { margin-top: 4px; color: #909399; }
.chart-card { margin: 10px 0; }
.chart-table-wrap { overflow-x: auto; }
.chart-table { width: 100%; border-collapse: collapse; font-size: 13px; }
.chart-table th,
.chart-table td { padding: 9px 12px; border: 1px solid #ebeef5; text-align: right; white-space: nowrap; }
.chart-table th:first-child { text-align: left; }
.chart-table thead th { background: #f7f8fa; }
.composer-area {
  position: fixed;
  z-index: 20;
  right: 0;
  bottom: 0;
  left: var(--app-sidebar-width, 0px);
  padding: 12px 24px 18px;
  background: linear-gradient(to bottom, rgba(245, 247, 250, 0), #f5f7fa 28%, #f5f7fa 100%);
  pointer-events: none;
}
.composer-card {
  width: min(900px, calc(100% - 36px));
  margin: 0 auto;
  padding: 11px 13px 10px;
  border: 1px solid #dfe4eb;
  border-radius: 20px;
  background: #fff;
  box-shadow: 0 12px 38px rgba(30, 50, 80, .12);
  pointer-events: auto;
}
.composer-card :deep(.el-textarea__inner) { padding: 7px 5px 10px; border: 0; box-shadow: none; font-size: 15px; line-height: 1.6; }
.composer-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 14px; }
.composer-options { display: flex; align-items: center; gap: 10px; min-width: 0; }
.context-control { display: flex; width: 330px; }
.context-kind { width: 112px; flex: 0 0 auto; }
.context-control :deep(.el-input__wrapper),
.context-control :deep(.el-select__wrapper) { box-shadow: 0 0 0 1px #e2e7ee inset; }
.send-actions { display: flex; align-items: center; gap: 8px; }
.composer-note { margin: 7px auto 0; color: #939cad; font-size: 11px; text-align: center; pointer-events: none; }
.history-toolbar { display: flex; justify-content: flex-end; margin-bottom: 10px; }
.history-item {
  display: flex;
  width: 100%;
  flex-direction: column;
  gap: 6px;
  padding: 13px 12px;
  border: 0;
  border-bottom: 1px solid #ebeef5;
  border-radius: 9px;
  background: transparent;
  color: #303b4f;
  text-align: left;
  cursor: pointer;
}
.history-item:hover { background: #f4f7fb; }
.history-item small { color: #9099a8; }
@media (max-width: 900px) {
  .conversation-stream { padding-right: 8px; padding-left: 8px; }
  .suggestion-grid { grid-template-columns: 1fr; }
  .composer-options { align-items: stretch; flex-direction: column; }
  .context-control { width: 100%; }
  .composer-toolbar { align-items: flex-end; }
}
@media (max-width: 640px) {
  .chat-topbar { align-items: flex-start; }
  .topbar-actions .el-button:first-child { display: none; }
  .welcome-state { padding-top: 44px; }
  .welcome-state h1 { font-size: 25px; }
  .user-bubble { max-width: 88%; }
  .composer-area { padding-right: 8px; padding-left: 8px; }
  .composer-card { width: 100%; }
  .composer-toolbar { align-items: stretch; flex-direction: column; }
  .send-actions { justify-content: flex-end; }
}
@media (max-width: 800px) {
  .composer-area { left: 0; }
}
</style>
