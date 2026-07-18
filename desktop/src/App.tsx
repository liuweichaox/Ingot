import { listen } from "@tauri-apps/api/event";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiRequest, downloadVerifiedPackage, startAgentStream } from "./lib/native";
import {
  TERMINAL_RUN_STATUSES,
  WORKSPACE_STATUS_LABELS,
  buildConnectorQuestion,
  connectorRequirementIsComplete,
  formatBytes,
  normalizeCentralUrl,
  packageFileName,
  workspaceIdsFromRun
} from "./lib/workflow";
import type {
  AgentArtifact,
  AgentCapabilities,
  AgentRunListItem,
  AgentRunPage,
  AgentRunSnapshot,
  ApiConnection,
  ConnectorPackageDescriptor,
  ConnectorRequirement,
  ConnectorWorkspaceSnapshot,
  StreamEnvelope
} from "./types";

const defaultRequirement: ConnectorRequirement = {
  name: "",
  sourceCode: "",
  protocol: "",
  endpoint: "",
  authentication: "none",
  dataContract: "",
  samplingPolicy: "",
  successCriteria: "",
  allowedNetworkTargets: "",
  notes: ""
};

function statusTone(status?: string): string {
  if (!status) return "neutral";
  if (["completed", "built", "package-approved", "packaged"].includes(status)) return "success";
  if (["failed", "build-failed", "test-failed", "cancelled"].includes(status)) return "danger";
  if (["running", "building", "testing", "awaiting-package-approval"].includes(status)) return "active";
  return "neutral";
}

function timeLabel(value?: string): string {
  if (!value) return "—";
  return new Intl.DateTimeFormat("zh-CN", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(new Date(value));
}

function App() {
  const [centralUrl, setCentralUrl] = useState(() => localStorage.getItem("ingot.centralUrl") ?? "http://127.0.0.1:8000");
  const [actor, setActor] = useState(() => localStorage.getItem("ingot.actor") ?? "operator");
  const [token, setToken] = useState("");
  const [capabilities, setCapabilities] = useState<AgentCapabilities>();
  const [requirement, setRequirement] = useState(defaultRequirement);
  const [runIdInput, setRunIdInput] = useState("");
  const [run, setRun] = useState<AgentRunSnapshot>();
  const [runHistory, setRunHistory] = useState<AgentRunListItem[]>([]);
  const [events, setEvents] = useState<StreamEnvelope[]>([]);
  const [artifacts, setArtifacts] = useState<AgentArtifact[]>([]);
  const [selectedArtifactId, setSelectedArtifactId] = useState<string>();
  const [workspaceIdInput, setWorkspaceIdInput] = useState("");
  const [workspace, setWorkspace] = useState<ConnectorWorkspaceSnapshot>();
  const [files, setFiles] = useState<string[]>([]);
  const [selectedFile, setSelectedFile] = useState<string>();
  const [fileContent, setFileContent] = useState("");
  const [packageDescriptor, setPackageDescriptor] = useState<ConnectorPackageDescriptor>();
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const runIdRef = useRef<string | undefined>(undefined);
  const refreshTimerRef = useRef<number | undefined>(undefined);

  const connection = useMemo<ApiConnection>(
    () => ({ centralUrl: normalizeCentralUrl(centralUrl), actor: actor.trim(), token }),
    [actor, centralUrl, token]
  );

  const selectedArtifact = artifacts.find((item) => item.artifactId === selectedArtifactId);
  const connectorQuestion = useMemo(() => buildConnectorQuestion(requirement), [requirement]);
  const requirementReady = connectorRequirementIsComplete(requirement) && connectorQuestion.length <= 4000;
  const runActive = Boolean(run && !TERMINAL_RUN_STATUSES.has(run.status));

  const handleFailure = useCallback((cause: unknown) => {
    setError(cause instanceof Error ? cause.message : String(cause));
  }, []);

  const refreshArtifacts = useCallback(
    async (targetRunId: string) => {
      const response = await apiRequest<{ items: AgentArtifact[] }>(connection, "GET", "/api/v1/agent/artifacts?limit=100");
      const matching = response.items.filter((item) => item.runId === targetRunId);
      setArtifacts(matching);
      setSelectedArtifactId((current) => current ?? matching[0]?.artifactId);
    },
    [connection]
  );

  const refreshHistory = useCallback(async () => {
    const response = await apiRequest<AgentRunPage>(connection, "GET", "/api/v1/agent/runs?limit=12");
    setRunHistory(response.items);
  }, [connection]);

  const openWorkspace = useCallback(
    async (workspaceId: string) => {
      const normalized = workspaceId.trim();
      if (!normalized) return;
      const [snapshot, fileList] = await Promise.all([
        apiRequest<ConnectorWorkspaceSnapshot>(connection, "GET", `/api/v1/connector-workspaces/${encodeURIComponent(normalized)}`),
        apiRequest<{ files: string[] }>(connection, "GET", `/api/v1/connector-workspaces/${encodeURIComponent(normalized)}/files`)
      ]);
      setWorkspace(snapshot);
      setWorkspaceIdInput(snapshot.workspaceId);
      setFiles(fileList.files);
      setSelectedFile((current) => (current && fileList.files.includes(current) ? current : fileList.files[0]));
      setPackageDescriptor((current) => (current?.workspaceId === snapshot.workspaceId ? current : undefined));
    },
    [connection]
  );

  const refreshRun = useCallback(
    async (targetRunId: string) => {
      const snapshot = await apiRequest<AgentRunSnapshot>(connection, "GET", `/api/v1/agent/runs/${encodeURIComponent(targetRunId)}`);
      setRun(snapshot);
      setRunIdInput(snapshot.runId);
      runIdRef.current = snapshot.runId;
      const ids = workspaceIdsFromRun(snapshot);
      if (ids.length > 0) await openWorkspace(ids.at(-1)!);
      if (TERMINAL_RUN_STATUSES.has(snapshot.status)) {
        await Promise.all([refreshArtifacts(snapshot.runId), refreshHistory()]);
      }
      return snapshot;
    },
    [connection, openWorkspace, refreshArtifacts, refreshHistory]
  );

  const scheduleRunRefresh = useCallback(() => {
    window.clearTimeout(refreshTimerRef.current);
    refreshTimerRef.current = window.setTimeout(() => {
      if (runIdRef.current) void refreshRun(runIdRef.current).catch(handleFailure);
    }, 250);
  }, [handleFailure, refreshRun]);

  useEffect(() => {
    setCapabilities(undefined);
    setRunHistory([]);
  }, [actor, centralUrl, token]);

  useEffect(() => {
    let disposed = false;
    const removers: Array<() => void> = [];
    void Promise.all([
      listen<StreamEnvelope>("agent-stream", (event) => {
        if (disposed || event.payload.runId !== runIdRef.current) return;
        setEvents((current) => [...current.slice(-199), event.payload]);
        scheduleRunRefresh();
      }),
      listen<{ runId: string }>("agent-stream-closed", (event) => {
        if (disposed || event.payload.runId !== runIdRef.current) return;
        scheduleRunRefresh();
      })
    ]).then((unlisten) => removers.push(...unlisten));

    return () => {
      disposed = true;
      removers.forEach((remove) => remove());
      window.clearTimeout(refreshTimerRef.current);
    };
  }, [scheduleRunRefresh]);

  useEffect(() => {
    if (!runActive || !run?.runId) return;
    const timer = window.setInterval(() => void refreshRun(run.runId).catch(handleFailure), 2000);
    return () => window.clearInterval(timer);
  }, [handleFailure, refreshRun, run?.runId, runActive]);

  useEffect(() => {
    if (!workspace || !selectedFile) {
      setFileContent("");
      return;
    }
    void apiRequest<{ content: string }>(
      connection,
      "GET",
      `/api/v1/connector-workspaces/${encodeURIComponent(workspace.workspaceId)}/file?path=${encodeURIComponent(selectedFile)}`
    )
      .then((response) => setFileContent(response.content))
      .catch(handleFailure);
  }, [connection, handleFailure, selectedFile, workspace]);

  async function verifyConnection() {
    setBusy("connection");
    setError(undefined);
    setNotice(undefined);
    try {
      const result = await apiRequest<AgentCapabilities>(connection, "GET", "/api/v1/agent/capabilities");
      if (!result.enabled || !result.connectorWorkspaceWorkflow) {
        throw new Error("Central API 未启用连接器工作区 Agent 能力。");
      }
      setCapabilities(result);
      await refreshHistory();
      localStorage.setItem("ingot.centralUrl", connection.centralUrl);
      localStorage.setItem("ingot.actor", connection.actor);
      setNotice(`已连接 ${result.provider} · ${result.reasoningModel}`);
    } catch (cause) {
      handleFailure(cause);
    } finally {
      setBusy(undefined);
    }
  }

  async function createRun() {
    if (!capabilities || !requirementReady) return;
    setBusy("create");
    setError(undefined);
    setNotice(undefined);
    setEvents([]);
    setArtifacts([]);
    setWorkspace(undefined);
    setFiles([]);
    setFileContent("");
    setPackageDescriptor(undefined);
    try {
      const created = await apiRequest<{ runId: string; status: string }>(connection, "POST", "/api/v1/agent/runs", {
        question: connectorQuestion,
        pageContext: { kind: "connector-generation", id: requirement.sourceCode.trim() },
        mode: "standard"
      });
      runIdRef.current = created.runId;
      setRunIdInput(created.runId);
      await refreshRun(created.runId);
      await refreshHistory();
      void startAgentStream(connection, created.runId).catch(handleFailure);
    } catch (cause) {
      handleFailure(cause);
    } finally {
      setBusy(undefined);
    }
  }

  async function openRunById(runId: string) {
    const target = runId.trim();
    if (!target) return;
    setBusy("open-run");
    setError(undefined);
    setEvents([]);
    try {
      const snapshot = await refreshRun(target);
      if (!TERMINAL_RUN_STATUSES.has(snapshot.status)) {
        void startAgentStream(connection, target).catch(handleFailure);
      }
    } catch (cause) {
      handleFailure(cause);
    } finally {
      setBusy(undefined);
    }
  }

  async function openRun() {
    await openRunById(runIdInput);
  }

  async function cancelRun() {
    if (!run?.runId) return;
    setBusy("cancel");
    setError(undefined);
    try {
      await apiRequest(connection, "POST", `/api/v1/agent/runs/${encodeURIComponent(run.runId)}:cancel`);
      await refreshRun(run.runId);
    } catch (cause) {
      handleFailure(cause);
    } finally {
      setBusy(undefined);
    }
  }

  async function approvePackage() {
    if (!workspace) return;
    setBusy("approve");
    setError(undefined);
    try {
      const result = await apiRequest<ConnectorWorkspaceSnapshot>(
        connection,
        "POST",
        `/api/v1/connector-workspaces/${encodeURIComponent(workspace.workspaceId)}:approve-package`
      );
      setWorkspace(result);
      setNotice("打包批准已记录，可生成内容寻址制品。");
    } catch (cause) {
      handleFailure(cause);
    } finally {
      setBusy(undefined);
    }
  }

  async function packageWorkspace() {
    if (!workspace) return;
    setBusy("package");
    setError(undefined);
    try {
      const response = await apiRequest<{ workspace: ConnectorWorkspaceSnapshot; package: ConnectorPackageDescriptor }>(
        connection,
        "POST",
        `/api/v1/connector-workspaces/${encodeURIComponent(workspace.workspaceId)}:package`
      );
      setWorkspace(response.workspace);
      setPackageDescriptor(response.package);
      setNotice(`制品已生成 · SHA-256 ${response.package.sha256}`);
      if (run?.runId) await refreshArtifacts(run.runId);
    } catch (cause) {
      handleFailure(cause);
    } finally {
      setBusy(undefined);
    }
  }

  async function downloadPackage() {
    if (!workspace?.packageSha256 && !packageDescriptor?.sha256) return;
    const sha256 = packageDescriptor?.sha256 ?? workspace!.packageSha256!;
    const name = packageFileName(packageDescriptor?.relativePath ?? "", workspace!.packageName, sha256);
    setBusy("download");
    setError(undefined);
    try {
      const savedPath = await downloadVerifiedPackage(connection, workspace!.workspaceId, name, sha256);
      if (savedPath) setNotice(`ZIP 已校验并保存到 ${savedPath}`);
    } catch (cause) {
      handleFailure(cause);
    } finally {
      setBusy(undefined);
    }
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand">
          <svg viewBox="0 0 48 48" aria-hidden="true">
            <path d="M24 3 43 14v20L24 45 5 34V14Z" fill="currentColor" opacity=".16" />
            <path d="m24 9 13 8v14l-13 8-13-8V17Z" fill="none" stroke="currentColor" strokeWidth="2.5" />
            <path d="m20 18-6 6 6 6m8-12 6 6-6 6" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          <div>
            <strong>Ingot Agent</strong>
            <span>Connector Engineering Desktop</span>
          </div>
        </div>
        <div className="scope-badge"><i /> 仅用于连接器代码生成</div>
      </header>

      <main>
        <section className="connection-panel panel">
          <div className="section-heading">
            <div><span className="eyebrow">01 · CONNECTION</span><h2>Central API</h2></div>
            <span className={`status-pill ${capabilities ? "success" : "neutral"}`}>{capabilities ? "已验证" : "未连接"}</span>
          </div>
          <div className="connection-grid">
            <label><span>API 地址</span><input value={centralUrl} onChange={(event) => setCentralUrl(event.target.value)} placeholder="https://central.example.com" spellCheck={false} /></label>
            <label><span>Actor</span><input value={actor} onChange={(event) => setActor(event.target.value)} placeholder="operator" spellCheck={false} /></label>
            <label><span>访问令牌</span><input type="password" value={token} onChange={(event) => setToken(event.target.value)} placeholder="仅保存在当前进程内存" autoComplete="off" /></label>
            <button className="button secondary" onClick={verifyConnection} disabled={busy === "connection" || !centralUrl.trim() || !actor.trim() || !token.trim()}>{busy === "connection" ? "验证中…" : "验证连接"}</button>
          </div>
          {capabilities && <div className="capability-strip"><span>Provider <b>{capabilities.provider}</b></span><span>Model <b>{capabilities.reasoningModel}</b></span><span>Max tools <b>{capabilities.maxToolCalls}</b></span><span>Timeout <b>{capabilities.maxRunSeconds}s</b></span></div>}
        </section>

        {(error || notice) && <div className={`message ${error ? "error" : "notice"}`} role="status"><span>{error ? "!" : "✓"}</span>{error ?? notice}<button aria-label="关闭消息" onClick={() => { setError(undefined); setNotice(undefined); }}>×</button></div>}

        <section className="workspace-layout">
          <div className="left-column">
            <section className="panel requirement-panel">
              <div className="section-heading">
                <div><span className="eyebrow">02 · REQUIREMENT</span><h2>连接器任务</h2></div>
                <span className="field-progress">{[requirement.name, requirement.sourceCode, requirement.protocol, requirement.endpoint, requirement.dataContract, requirement.samplingPolicy, requirement.successCriteria].filter((value) => value.trim()).length}/7</span>
              </div>
              <p className="section-copy">定义数据来源与验收条件。Agent 将生成源码并在受限容器中完成固定构建和测试。</p>
              <div className="form-grid">
                <label><span>连接器名称 *</span><input value={requirement.name} onChange={(event) => setRequirement({ ...requirement, name: event.target.value })} placeholder="Line PLC Connector" /></label>
                <label><span>来源编码 *</span><input maxLength={200} value={requirement.sourceCode} onChange={(event) => setRequirement({ ...requirement, sourceCode: event.target.value })} placeholder="PLC-01" /></label>
                <label><span>协议或 SDK *</span><input value={requirement.protocol} onChange={(event) => setRequirement({ ...requirement, protocol: event.target.value })} placeholder="OPC UA / Modbus TCP / Vendor SDK" /></label>
                <label><span>数据端点 *</span><input value={requirement.endpoint} onChange={(event) => setRequirement({ ...requirement, endpoint: event.target.value })} placeholder="opc.tcp://10.0.0.5:4840" spellCheck={false} /></label>
                <label className="wide"><span>认证方式</span><input value={requirement.authentication} onChange={(event) => setRequirement({ ...requirement, authentication: event.target.value })} placeholder="none / certificate / token reference" /></label>
                <label className="wide"><span>输入数据契约 *</span><textarea value={requirement.dataContract} onChange={(event) => setRequirement({ ...requirement, dataContract: event.target.value })} placeholder="字段、类型、单位、时间戳和示例数据" rows={3} /></label>
                <label><span>采样策略 *</span><textarea value={requirement.samplingPolicy} onChange={(event) => setRequirement({ ...requirement, samplingPolicy: event.target.value })} placeholder="频率、触发条件、重试与背压" rows={3} /></label>
                <label><span>成功标准 *</span><textarea value={requirement.successCriteria} onChange={(event) => setRequirement({ ...requirement, successCriteria: event.target.value })} placeholder="构建、测试和 ProductionEvent 输出要求" rows={3} /></label>
                <label className="wide"><span>允许的运行期网络目标</span><input value={requirement.allowedNetworkTargets} onChange={(event) => setRequirement({ ...requirement, allowedNetworkTargets: event.target.value })} placeholder="逗号分隔；生成和测试仍保持禁网" /></label>
                <label className="wide"><span>补充约束</span><textarea value={requirement.notes} onChange={(event) => setRequirement({ ...requirement, notes: event.target.value })} placeholder="依赖限制、部署环境、异常处理要求" rows={3} /></label>
              </div>
              <div className="action-row">
                <span className={`safety-note ${connectorQuestion.length > 4000 ? "invalid" : ""}`}>不会连接设备、部署代码或写入生产数据 · 请求 {connectorQuestion.length}/4000</span>
                <button className="button primary" onClick={createRun} disabled={!capabilities || !requirementReady || busy === "create"}>{busy === "create" ? "创建中…" : "生成连接器代码"}</button>
              </div>
            </section>

            <section className="panel source-panel">
              <div className="section-heading">
                <div><span className="eyebrow">04 · SOURCE</span><h2>工作区源码</h2></div>
                {workspace && <span className={`status-pill ${statusTone(workspace.status)}`}>{WORKSPACE_STATUS_LABELS[workspace.status] ?? workspace.status}</span>}
              </div>
              <div className="lookup-row"><input value={workspaceIdInput} onChange={(event) => setWorkspaceIdInput(event.target.value)} placeholder="Workspace ID" spellCheck={false} /><button className="button secondary compact" onClick={() => void openWorkspace(workspaceIdInput).catch(handleFailure)} disabled={!workspaceIdInput.trim()}>打开工作区</button></div>
              {workspace ? (
                <>
                  <div className="workspace-meta"><span><small>PACKAGE</small>{workspace.packageName}</span><span><small>REVISION</small>{workspace.revision}</span><span><small>WORKSPACE</small>{workspace.workspaceId}</span></div>
                  <div className="code-browser">
                    <nav aria-label="工作区文件">{files.map((file) => <button key={file} className={selectedFile === file ? "selected" : ""} onClick={() => setSelectedFile(file)}><span>◇</span>{file}</button>)}</nav>
                    <div className="code-view"><div className="code-title"><span>{selectedFile ?? "选择文件"}</span><span>{fileContent ? `${fileContent.split("\n").length} lines` : ""}</span></div><pre><code>{fileContent || "工作区中暂无可读取的源码文件。"}</code></pre></div>
                  </div>
                </>
              ) : <div className="empty-state"><span>{"{ }"}</span><p>Agent 创建工作区后，源码将在这里以只读方式展示。</p></div>}
            </section>
          </div>

          <aside className="right-column">
            <section className="panel run-panel">
              <div className="section-heading"><div><span className="eyebrow">03 · EXECUTION</span><h2>生成运行</h2></div>{run && <span className={`status-pill ${statusTone(run.status)}`}>{run.status}</span>}</div>
              <div className="lookup-row"><input value={runIdInput} onChange={(event) => setRunIdInput(event.target.value)} placeholder="Run ID" spellCheck={false} /><button className="button secondary compact" onClick={openRun} disabled={!runIdInput.trim() || busy === "open-run"}>打开</button></div>
              {runHistory.length > 0 && <div className="history-block"><div className="history-heading"><span>当前 Actor 最近运行</span><button onClick={() => void refreshHistory().catch(handleFailure)}>刷新</button></div><div className="history-list">{runHistory.map((item) => <button key={item.runId} className={run?.runId === item.runId ? "selected" : ""} onClick={() => void openRunById(item.runId)} disabled={busy === "open-run"}><i className={statusTone(item.status)} /><span><strong>{item.summary || item.question}</strong><small>{timeLabel(item.createdAt)} · {item.status}</small></span></button>)}</div></div>}
              {run ? (
                <>
                  <div className="run-stats"><div><small>STAGE</small><strong>{run.workflowStage}</strong></div><div><small>ITERATION</small><strong>{run.iteration}</strong></div><div><small>TOOLS</small><strong>{run.usage.toolCalls}</strong></div><div><small>TOKENS</small><strong>{run.usage.totalTokens.toLocaleString()}</strong></div></div>
                  {run.answer?.summary && <p className="run-summary">{run.answer.summary}</p>}
                  {run.error && <p className="inline-error">{run.error}</p>}
                  <ol className="timeline">{run.toolInvocations.map((tool, index) => <li key={`${tool.tool}-${tool.startedAt}-${index}`} className={statusTone(tool.status)}><i /><div><strong>{tool.tool}</strong><span>{tool.summary ?? tool.error ?? tool.status}</span><small>{timeLabel(tool.completedAt ?? tool.startedAt)}</small></div></li>)}{events.slice(-4).map((event, index) => <li key={`event-${event.sequence ?? index}`} className="active"><i /><div><strong>{event.eventType}</strong><small>event #{event.sequence ?? "—"}</small></div></li>)}</ol>
                  {runActive && <button className="button danger full" onClick={cancelRun} disabled={busy === "cancel"}>取消运行</button>}
                </>
              ) : <div className="empty-state small"><span>◎</span><p>创建任务后显示模型、工具和工作流状态。</p></div>}
            </section>

            <section className="panel result-panel">
              <div className="section-heading"><div><span className="eyebrow">05 · VERIFICATION</span><h2>构建与测试</h2></div></div>
              {workspace ? <div className="verification-list"><CommandResult title="Build" result={workspace.lastBuild} /><CommandResult title="Test" result={workspace.lastTest} /></div> : <div className="empty-state small"><span>✓</span><p>固定构建与测试结果将在此显示。</p></div>}
            </section>

            <section className="panel artifacts-panel">
              <div className="section-heading"><div><span className="eyebrow">06 · ARTIFACTS</span><h2>规格与制品</h2></div><span className="field-progress">{artifacts.length}</span></div>
              {artifacts.length > 0 ? <><div className="artifact-tabs">{artifacts.map((artifact) => <button key={artifact.artifactId} className={selectedArtifactId === artifact.artifactId ? "selected" : ""} onClick={() => setSelectedArtifactId(artifact.artifactId)}><strong>{artifact.title}</strong><span>{artifact.kind} · v{artifact.version}</span></button>)}</div>{selectedArtifact && <pre className="artifact-content"><code>{selectedArtifact.content}</code></pre>}</> : <div className="empty-state small"><span>▱</span><p>连接器规格与包记录按当前运行加载。</p></div>}
            </section>

            <section className="panel package-panel">
              <div className="section-heading"><div><span className="eyebrow">07 · PACKAGE</span><h2>人工批准与下载</h2></div></div>
              {workspace ? <><div className="package-status"><span>当前状态</span><strong>{WORKSPACE_STATUS_LABELS[workspace.status] ?? workspace.status}</strong></div>{workspace.packageApprovedBy && <div className="approval-record"><span>批准人 {workspace.packageApprovedBy}</span><span>{timeLabel(workspace.packageApprovedAt)}</span></div>}{(packageDescriptor?.sha256 || workspace.packageSha256) && <div className="hash-block"><small>SHA-256</small><code>{packageDescriptor?.sha256 ?? workspace.packageSha256}</code>{packageDescriptor && <span>{formatBytes(packageDescriptor.sizeBytes)}</span>}</div>}<div className="package-actions"><button className="button primary" onClick={approvePackage} disabled={workspace.status !== "awaiting-package-approval" || busy === "approve"}>批准打包</button><button className="button secondary" onClick={packageWorkspace} disabled={workspace.status !== "package-approved" || busy === "package"}>生成 ZIP</button><button className="button secondary" onClick={downloadPackage} disabled={workspace.status !== "packaged" || busy === "download"}>校验并下载</button></div></> : <div className="empty-state small"><span>⬡</span><p>只有测试通过且人工批准的源码可以打包。</p></div>}
            </section>
          </aside>
        </section>
      </main>
    </div>
  );
}

function CommandResult({ title, result }: { title: string; result?: ConnectorWorkspaceSnapshot["lastBuild"] }) {
  return <div className={`command-result ${result ? (result.succeeded ? "success" : "danger") : "neutral"}`}><div><span>{title}</span><strong>{result ? (result.succeeded ? "PASSED" : "FAILED") : "PENDING"}</strong></div>{result && <><dl><div><dt>Exit</dt><dd>{result.exitCode}</dd></div><div><dt>Duration</dt><dd>{result.durationMilliseconds} ms</dd></div><div><dt>Completed</dt><dd>{timeLabel(result.completedAt)}</dd></div></dl><pre>{result.output || "No output"}</pre></>}</div>;
}

export default App;
