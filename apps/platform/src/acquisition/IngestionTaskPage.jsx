// 编排采集任务创建、探查、发布和站点可见性反馈。
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import {
  Alert, Badge, Button, Card, Field, Input, LinkButton, Page, Select, StatusBadge, notify, useConfirmDialog,
} from "../ui/components";
import { deleteJson, downloadFile, getJson, postForm, postJson } from "../api/http";
import { useApi, extractRows } from "../hooks/useApi";
import {
  mergeServerCapabilities, normalizeSnmpOid, protocolDescriptor, protocolOptionGroups,
} from "./protocolRegistry";
import {
  applyProtocolChange, createIngestionTaskForm, modelValue, parseModelValue,
  patchFromProbePoint, summarizeValueMappings, toPayload, validateIngestionTask,
} from "./ingestionTaskForm";
import { classifyIngestionDraftWriteOutcome } from "./ingestionBatchSave";
import { ConnectionPanel } from "./panels/ConnectionPanel";
import { PointMappingPanel, blankRow } from "./panels/PointMappingPanel";
import { DevicePointsPanel } from "./panels/DevicePointsPanel";
import { ReusableBindingPanel } from "./ReusableBindingPanel";
import { IngestionDeviceGroupCanvas } from "./IngestionDeviceGroupCanvas";
import { deviceProfileOption, deviceProfileOptions } from "./deviceProfileRegistry";
import { edgeConnectionStatus, formatDuration, formatInteger, formatTime } from "../pages/shared";

const ENDPOINT = "/api/v1/ingestion-tasks";

function deviceGroupPayload(form) {
  const taskForm = { ...form, status: "draft" };
  const payload = toPayload(taskForm);
  if (!String(taskForm.source || "").trim())
    payload.source = `connector/${taskForm.protocol}/${taskForm.taskId.trim()}`;
  return payload;
}

function storedDeviceFingerprint(task) {
  try { return JSON.stringify(deviceGroupPayload(createIngestionTaskForm(task))); }
  catch { return ""; }
}

function inspectorTabForErrors(errors, connectionSection) {
  const paths = Object.keys(errors || {});
  if (paths.some(path => path === "valueMappings" || path.startsWith("valueMappings[") || path.startsWith("valueMappings.")))
    return "mappings";
  if (paths.some(path => path.startsWith(`${connectionSection}.`))) return "connection";
  if (paths.some(path => [
    "contextMappings", "staticContextPairs", "processSpecification.", "lifecycle.", "execution.",
    "timestamp", "sequencePath", "sampleEventType",
  ].some(prefix => path.startsWith(prefix)))) return "advanced";
  return "device";
}

// Keep the setup guide conservative: node-level health is never proof that this task is linked to a production run.
function assessPublishedTaskObservation(task, edge, acquisition, loading, error) {
  if (task?.status !== "published")
    return { state: "unknown", detail: "发布后到现场节点确认配置应用、有效采样、上行和运行关联。" };
  if (loading && !acquisition)
    return { state: "unknown", detail: "正在读取节点配置、采样和上行证据。" };
  if (error)
    return { state: "unknown", detail: `现场证据读取不完整：${error}。刷新后再判断当前状态。` };
  if (!acquisition)
    return { state: "unknown", detail: "尚未收到该节点的采集状态报告；打开节点诊断或刷新现场证据。" };

  const taskKey = `${task.taskId}@${task.version}`.toLowerCase();
  const runtime = (acquisition.tasks || []).find(item => String(item.configurationKey || "").toLowerCase() === taskKey);
  const deployment = (acquisition.deployments || []).find(item => String(item.taskId || "").toLowerCase() === String(task.taskId || "").toLowerCase());
  const deploymentMatches = Boolean(deployment && Number(deployment.desiredVersion) === Number(task.version));
  const deploymentConverged = Boolean(deploymentMatches && deployment.desiredConfigurationHash &&
    deployment.desiredConfigurationHash === deployment.appliedConfigurationHash &&
    Number(deployment.appliedVersion) === Number(task.version) && deployment.state === "applied");
  const reportObservedMs = acquisition.platformObservedAt ? new Date(acquisition.platformObservedAt).getTime() : null;
  const reportReceivedMs = acquisition.platformReceivedAt ? new Date(acquisition.platformReceivedAt).getTime() : null;
  const reportAgeMs = reportObservedMs - reportReceivedMs;
  if (!Number.isFinite(reportObservedMs) || !Number.isFinite(reportReceivedMs) || !Number.isFinite(reportAgeMs) || reportAgeMs < 0)
    return { state: "unknown", detail: "节点状态报告时间无法核实；当前采样时效与部署状态未知。" };
  if (reportAgeMs > 30000)
    return { state: "unknown", detail: "节点状态报告已过期；刷新或打开节点诊断确认当前状态。" };

  if (deploymentMatches && ["failed", "rollback"].includes(deployment.state))
    return { state: "needs-attention", detail: "配置应用失败或仍保留旧版本；打开节点诊断查看错误并处理。" };
  if (!deploymentConverged)
    return { state: "unknown", detail: "尚未确认该任务版本已应用到节点；查看节点诊断并刷新部署证据。" };

  const connection = edgeConnectionStatus(edge);
  if (connection === "offline")
    return { state: "needs-attention", detail: "节点心跳已过期；先恢复节点连接，再核对采集状态。" };
  if (connection !== "online")
    return { state: "unknown", detail: "节点连接状态未知；查看节点诊断确认心跳。" };
  if (["failed", "degraded", "error"].includes(String(runtime?.state || "").toLowerCase()) || runtime?.lastError)
    return { state: "needs-attention", detail: `任务采集状态异常${runtime?.lastError ? `：${runtime.lastError}` : ""}；先处理采集错误。` };
  const validSnapshotMs = runtime?.lastValidSnapshotAt ? new Date(runtime.lastValidSnapshotAt).getTime() : null;
  if (!Number.isFinite(validSnapshotMs))
    return { state: "unknown", detail: "尚无该任务的有效采样；检查任务读取状态和数据质量缺口。" };

  const snapshotFreshness = assessTaskSnapshotFreshness(task, runtime, acquisition);
  if (snapshotFreshness.label === "超时")
    return { state: "needs-attention", detail: `有效采样已超出静默时限；${snapshotFreshness.detail}` };
  if (["时效未知", "未知", "待核实"].includes(snapshotFreshness.label))
    return { state: "unknown", detail: `${snapshotFreshness.detail}；等待新状态报告核实。` };
  if (["blocked", "degraded"].includes(String(edge?.delivery?.state || "").toLowerCase()))
    return { state: "needs-attention", detail: "节点数据上行阻塞或异常；查看节点诊断并处理待补传。" };
  const lastSuccessfulShipmentMs = edge?.delivery?.lastSuccessfulShipmentAt
    ? new Date(edge.delivery.lastSuccessfulShipmentAt).getTime()
    : null;
  if (edge?.backlogStatisticsFresh !== true || !Number.isFinite(lastSuccessfulShipmentMs))
    return { state: "unknown", detail: "任务已有有效采样；节点上行证据尚未核实，运行关联仍待确认。" };

  const freshnessNote = snapshotFreshness.label === "未监控" ? "未配置采样静默时限" : "有效采样时效已核对";
  return {
    state: "evidence-present",
    detail: `已确认配置应用和有效采样，且节点曾成功上行（${freshnessNote}）；上行时间不证明本任务最新采样已送达，还需核对运行关联。`,
  };
}

export function IngestionTaskPage({ canWrite = true }) {
  const { taskId } = useParams();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const isNew = !taskId || taskId === "new";
  const requestedVersion = searchParams.get("version");
  const mode = searchParams.get("mode") || (isNew ? "create" : "maintain");
  const [groupActivated, setGroupActivated] = useState(false);
  const multiDeviceSetup = isNew && canWrite && groupActivated;
  const groupEnabled = isNew && canWrite && groupActivated;
  const [deviceGroupEntries, setDeviceGroupEntries] = useState([]);
  const [deviceProbeValidationFingerprints, setDeviceProbeValidationFingerprints] = useState({});
  const [activeDeviceKey, setActiveDeviceKey] = useState("");
  const [groupSaveError, setGroupSaveError] = useState("");
  const groupDeviceOrdinal = useRef(0);
  const { confirm, confirmationDialog } = useConfirmDialog();

  const { data: modelData, error: modelLoadError, reload: reloadModels } = useApi("/api/v1/process-data-models");
  const { data: edgeData, error: edgeLoadError, reload: reloadEdges } = useApi("/api/edges");
  const { data: groupTaskData, error: groupTasksError, reload: reloadGroupTasks } = useApi(ENDPOINT, { enabled: groupEnabled });
  const models = extractRows(modelData);
  const edges = extractRows(edgeData);
  const groupTasks = extractRows(groupTaskData);
  const deviceGroupLoadError = edgeLoadError || modelLoadError || groupTasksError;

  const [form, setForm] = useState(() => createIngestionTaskForm({}, undefined));
  const [loading, setLoading] = useState(!isNew);
  const [saving, setSaving] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [saveError, setSaveError] = useState("");
  const [probe, setProbe] = useState(null);
  const [probeError, setProbeError] = useState("");
  const [probeErrorFingerprint, setProbeErrorFingerprint] = useState("");
  const [probing, setProbing] = useState(false);
  const probeRevision = useRef(0);
  const probeValidationRevision = useRef(0);
  const [validatedProbeRevision, setValidatedProbeRevision] = useState(null);
  const [showErrors, setShowErrors] = useState(false);
  const [activeInspectorTab, setActiveInspectorTab] = useState("device");
  const [protocolCapabilitiesError, setProtocolCapabilitiesError] = useState("");
  const [protocolCapabilitiesRevision, setProtocolCapabilitiesRevision] = useState(0);
  const [mappingFocusRevision, setMappingFocusRevision] = useState(0);
  const [mappingFocusRequest, setMappingFocusRequest] = useState(null);
  const [probePointFocusRequest, setProbePointFocusRequest] = useState(null);
  const observationEnabled = form.status === "published" && Boolean(form.edgeId);
  const { data: observedEdgeData, error: observedEdgeError, loading: observedEdgeLoading, reload: reloadObservedEdges } = useApi(
    observationEnabled ? "/api/edges" : "",
    { enabled: observationEnabled, interval: observationEnabled ? 10000 : 0 },
  );
  const { data: acquisitionObservation, error: acquisitionObservationError, loading: acquisitionObservationLoading, reload: reloadAcquisitionObservation } = useApi(
    observationEnabled ? `/api/edges/${encodeURIComponent(form.edgeId)}/acquisition/status` : "",
    { enabled: observationEnabled, interval: observationEnabled ? 10000 : 0 },
  );
  const observedEdge = extractRows(observedEdgeData).find(item => item.edgeId === form.edgeId);
  const currentTaskKey = multiDeviceSetup ? activeDeviceKey : "single-device";
  const completeMappingFocus = useCallback(revision => {
    setMappingFocusRequest(current => current?.revision === revision ? null : current);
  }, []);
  const completeProbePointFocus = useCallback(revision => {
    setProbePointFocusRequest(current => current?.revision === revision ? null : current);
  }, []);

  const deviceGroupCards = useMemo(() => {
    if (!multiDeviceSetup) return [];
    return deviceGroupEntries.map(entry => {
      const currentForm = entry.key === activeDeviceKey ? form : entry.form;
      let fingerprint = "";
      try { fingerprint = JSON.stringify(deviceGroupPayload(currentForm)); }
      catch { /* The task editor will expose incomplete values through its normal validation. */ }
      return {
        ...entry,
        form: currentForm,
        saved: Boolean(entry.savedFingerprint && entry.savedFingerprint === fingerprint),
        probeVerified: deviceProbeValidationFingerprints[entry.key] ===
          JSON.stringify({ ...toPayload(currentForm), status: "draft" }),
      };
    });
  }, [multiDeviceSetup, deviceGroupEntries, activeDeviceKey, form, deviceProbeValidationFingerprints]);
  const firstUnverifiedDevice = deviceGroupCards.find(item => item.verificationRequired);
  const firstRetryableDevice = deviceGroupCards.find(item => item.error && item.failureKind === "request");
  const firstConflictDevice = deviceGroupCards.find(item => item.error && item.failureKind === "conflict");
  const firstInvalidDevice = deviceGroupCards.find(item => item.error && item.validationFailed);
  const firstRejectedDevice = deviceGroupCards.find(item => item.error && item.failureKind === "rejected");
  const groupResultAction = firstUnverifiedDevice
    ? { label: "核实首个提交结果", run: () => verifyDeviceGroupDraft(firstUnverifiedDevice.key) }
    : firstRetryableDevice
      ? { label: "重试首个接口失败项", run: () => saveDeviceGroupDrafts(firstRetryableDevice.key) }
      : firstConflictDevice || firstInvalidDevice || firstRejectedDevice
        ? { label: "查看首个待处理项", run: () => selectDeviceGroupTask((firstConflictDevice || firstInvalidDevice || firstRejectedDevice).key) }
        : null;
  const hasUnsavedDeviceChanges = deviceGroupCards.some(item => {
    if (!item.savedFingerprint) return true;
    try { return item.savedFingerprint !== JSON.stringify(deviceGroupPayload(item.form)); }
    catch { return true; }
  });
  const canvasDevices = multiDeviceSetup
    ? deviceGroupCards
    : [{ key: "single-device", form, saved: false, error: "" }];

  useEffect(() => {
    let cancelled = false;
    setProtocolCapabilitiesError("");
    getJson("/api/v1/acquisition-protocols")
      .then(result => { if (!cancelled) { mergeServerCapabilities(result?.protocols || result); setForm(value => ({ ...value })); } })
      .catch(error => { if (!cancelled) setProtocolCapabilitiesError(error.message || "驱动能力列表暂不可用。"); });
    return () => { cancelled = true; };
  }, [protocolCapabilitiesRevision]);

  useEffect(() => {
    if (isNew) { setLoading(false); return; }
    let cancelled = false;
    setLoading(true);
    getJson(`${ENDPOINT}?taskId=${encodeURIComponent(taskId)}`)
      .then(result => {
        if (cancelled) return;
        const rows = extractRows(result);
        // The collection endpoint returns all tasks, so select the route task before its version.
        const taskVersions = rows.filter(item => String(item.taskId || "").toLowerCase() === taskId.toLowerCase());
        const match = requestedVersion
          ? taskVersions.find(item => String(item.version) === String(requestedVersion))
          : taskVersions.slice().sort((left, right) => right.version - left.version)[0];
        if (!match) { setLoadError("没有找到该接入配置。"); return; }
        setForm(createIngestionTaskForm(match, mode === "version" ? Number(match.version) + 1 : undefined));
      })
      .catch(error => { if (!cancelled) setLoadError(error.message); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [taskId, requestedVersion, mode, isNew]);

  useEffect(() => {
    if (!multiDeviceSetup || !activeDeviceKey) return;
    setDeviceGroupEntries(current => {
      const active = current.find(item => item.key === activeDeviceKey);
      if (!active || active.form === form) return current;
      return current.map(item => item.key === activeDeviceKey
        ? {
          ...item,
          form,
          error: item.verificationRequired ? item.error : "",
          failureKind: item.verificationRequired ? item.failureKind : "",
          validationFailed: item.verificationRequired ? item.validationFailed : false,
        }
        : item);
    });
  }, [form, multiDeviceSetup, activeDeviceKey]);

  useEffect(() => {
    if (!multiDeviceSetup || !hasUnsavedDeviceChanges) return undefined;
    const protectUnload = event => {
      event.preventDefault();
      event.returnValue = "";
    };
    const protectInternalNavigation = event => {
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      const link = event.target instanceof Element ? event.target.closest("a[href]") : null;
      if (!link || (link.target && link.target !== "_self") || link.hasAttribute("download")) return;
      const destination = new URL(link.href, window.location.href);
      const currentUrl = `${location.pathname}${location.search}${location.hash}`;
      const destinationUrl = `${destination.pathname}${destination.search}${destination.hash}`;
      if (destination.origin !== window.location.origin || destinationUrl === currentUrl) return;
      if (!window.confirm("当前画布还有未保存的设备配置。离开后这些更改将丢失；是否继续离开？")) {
        event.preventDefault();
        event.stopPropagation();
      }
    };
    window.addEventListener("beforeunload", protectUnload);
    document.addEventListener("click", protectInternalNavigation, true);
    return () => {
      window.removeEventListener("beforeunload", protectUnload);
      document.removeEventListener("click", protectInternalNavigation, true);
    };
  }, [hasUnsavedDeviceChanges, location.hash, location.pathname, location.search, multiDeviceSetup]);

  const descriptor = protocolDescriptor(form.protocol);
  const selectedModel = parseModelValue(form.dataModel);
  const model = models.find(item => item.modelId === selectedModel.id && item.version === selectedModel.version);
  const dataItems = model?.acquisition?.dataItems || [];
  const controlParameters = model?.controlParameters || [];
  const managedByBinding = Boolean(form.templateId && form.dataSourceId);
  const activeOutcomeUnverified = multiDeviceSetup && Boolean(
    deviceGroupCards.find(item => item.key === activeDeviceKey)?.verificationRequired,
  );
  const readOnly = !canWrite || managedByBinding || (mode === "maintain" && form.status !== "draft") ||
    (multiDeviceSetup && (saving || activeOutcomeUnverified));
  const allowProbe = canWrite && form.status === "draft" && !saving;

  const validation = useMemo(
    () => validateIngestionTask(form, { dataItems }),
    [form, dataItems],
  );
  const errors = showErrors ? validation.errors : {};
  const requiredDataItems = dataItems.filter(item => !item.nullable);
  const mappingSummary = summarizeValueMappings(form, dataItems, validation.errors);
  const mappedRequiredCount = mappingSummary.mappedRequiredCount;
  const connectionValidation = descriptor.validateConnection(form[descriptor.section] || {});
  const lifecycleValidationErrors = Object.keys(validation.errors).some(path => path.startsWith("lifecycle."));

  const probeTask = useMemo(() => ({ ...toPayload(form), status: "draft" }), [form]);
  const probeFingerprint = JSON.stringify(probeTask);
  const probeSourceFingerprint = JSON.stringify({
    edgeId: probeTask.edgeId,
    protocol: probeTask.protocol,
    connection: probeTask[descriptor.section],
  });
  const configuredReadPoints = descriptor.addressing === "modbus-register"
    ? probeTask.valueMappings.map(({ modbusArea, modbusAddress, bitIndex, sourceDataType, sourceByteLength }) =>
        [modbusArea, modbusAddress, bitIndex, sourceDataType, sourceByteLength])
    : descriptor.id === "melsec-a1e"
      ? probeTask.valueMappings.map(({ melsecDevice, melsecAddress, bitIndex, sourceDataType, sourceByteLength }) =>
          [melsecDevice, melsecAddress, bitIndex, sourceDataType, sourceByteLength])
      : descriptor.id === "ethernet-ip"
        ? probeTask.valueMappings.map(({ sourcePath, sourceDataType }) => [sourcePath, sourceDataType])
        : ["omron-fins-tcp", "omron-fins-udp", "melsec-mc-3e"].includes(descriptor.id)
          ? probeTask.valueMappings.map(({ sourcePath, sourceDataType, wordOrder }) => [sourcePath, sourceDataType, wordOrder])
        : null;
  const probeCatalogFingerprint = JSON.stringify({ source: probeSourceFingerprint, configuredReadPoints });
  useLayoutEffect(() => {
    // Any mapping edit invalidates publication evidence without discarding discovered points.
    probeValidationRevision.current += 1;
    setValidatedProbeRevision(null);
  }, [probeFingerprint]);
  useLayoutEffect(() => {
    // Points from a different Edge, protocol, or connection cannot be reused.
    probeRevision.current += 1;
    setProbe(null);
    setProbeError("");
    setProbing(false);
    setValidatedProbeRevision(null);
  }, [probeSourceFingerprint]);
  useEffect(() => () => { probeRevision.current += 1; }, []);

  const probeValidationCurrent = Boolean(probe && validatedProbeRevision === probeValidationRevision.current);
  const probeView = probe ? {
    ...probe,
    success: probeValidationCurrent && probe.success,
    mappingsValidated: probeValidationCurrent && probe.mappingsValidated,
    mappings: probeValidationCurrent ? probe.mappings : [],
    validationCurrent: probeValidationCurrent,
    catalogCurrent: probe.catalogFingerprint === probeCatalogFingerprint,
    nextCursor: probe.catalogFingerprint === probeCatalogFingerprint ? probe.nextCursor : null,
    scanLimitReached: probe.catalogFingerprint === probeCatalogFingerprint && probe.scanLimitReached,
    message: probeValidationCurrent
      ? probe.message
      : "配置已变化；点位清单可继续辅助映射，重新探查后才能发布。",
  } : null;

  const advisories = descriptor.advisories ? descriptor.advisories(form) : [];
  const probeValid = Boolean(!probing && probeView?.validationCurrent && probeView.success && probeView.mappingsValidated);
  useEffect(() => {
    if (!multiDeviceSetup || !activeDeviceKey) return;
    setDeviceProbeValidationFingerprints(current => {
      const previousFingerprint = current[activeDeviceKey];
      if (probeValid && previousFingerprint !== probeFingerprint)
        return { ...current, [activeDeviceKey]: probeFingerprint };
      if (!probeValid && previousFingerprint && previousFingerprint !== probeFingerprint) {
        const next = { ...current };
        delete next[activeDeviceKey];
        return next;
      }
      return current;
    });
  }, [multiDeviceSetup, activeDeviceKey, probeValid, probeFingerprint]);

  const deviceGroupProgress = useMemo(() => {
    if (!multiDeviceSetup) return [];
    return deviceGroupCards.map(device => {
      const currentForm = device.form;
      const selected = parseModelValue(currentForm.dataModel);
      const selectedModel = models.find(item => item.modelId === selected.id && Number(item.version) === Number(selected.version));
      const dataItems = selectedModel?.acquisition?.dataItems || [];
      const validation = validateIngestionTask(currentForm, { dataItems });
      const descriptor = protocolDescriptor(currentForm.protocol);
      const connectionErrors = descriptor.validateConnection(currentForm[descriptor.section] || {});
      const mapping = summarizeValueMappings(currentForm, dataItems, validation.errors);
      const edgeAvailable = Boolean(currentForm.edgeId && edges.some(item => item.edgeId === currentForm.edgeId));
      const subjectId = String(currentForm.subjectId || "").trim();
      const contextReady = edgeAvailable && selectedModel?.status === "published" && Boolean(subjectId);
      const connectionReady = Object.keys(connectionErrors).length === 0;
      const probeVerified = device.probeVerified;
      const runBoundaryErrors = Object.keys(validation.errors).some(path => path.startsWith("lifecycle."));
      const runBoundaryReady = Boolean(currentForm.lifecycle.enabled && !runBoundaryErrors);
      const nextTab = !contextReady ? "device"
        : !connectionReady ? "connection"
          : !mapping.mappingReady ? "mappings"
            : !probeVerified ? "connection"
              : !runBoundaryReady ? "advanced" : "device";
      const contextLabel = !currentForm.edgeId ? "待分配节点"
        : !edgeAvailable ? "节点不可用"
          : selectedModel?.status !== "published" ? "需选已发布模型"
            : !subjectId ? "待填写对象" : "已配置";
      const mappingLabel = mapping.requiredCount > 0
        ? `必需变量 ${mapping.mappedRequiredCount}/${mapping.requiredCount}`
        : mapping.completeRowCount > 0 ? `已配置 ${mapping.completeRowCount} 行` : "待配置变量";
      const saveLabel = device.verificationRequired ? "提交结果待核实"
        : device.error ? device.failureKind === "conflict" ? "任务编号冲突"
          : device.failureKind === "request" ? "保存失败，可重试"
            : device.failureKind === "rejected" ? "请求被拒绝"
              : device.validationFailed ? "配置需修正" : "保存失败"
          : device.saved ? "草稿已保存"
            : device.savedFingerprint ? "有未保存修改" : "待保存";
      return {
        ...device,
        contextReady,
        contextLabel,
        connectionReady,
        connectionLabel: connectionReady ? "已配置" : `${Object.keys(connectionErrors).length} 项待修正`,
        mappingReady: mapping.mappingReady,
        mappingLabel,
        probeVerified,
        runBoundaryErrors,
        runBoundaryReady,
        runBoundaryLabel: runBoundaryErrors ? "边界需修正" : runBoundaryReady ? "已配置" : "建议核对",
        saved: device.saved,
        saveLabel,
        published: currentForm.status === "published",
        nextTab,
      };
    });
  }, [multiDeviceSetup, deviceGroupCards, models, edges]);

  const deviceGroupProgressSummary = useMemo(() => ({
    total: deviceGroupProgress.length,
    contextReady: deviceGroupProgress.filter(item => item.contextReady && item.connectionReady).length,
    mappingReady: deviceGroupProgress.filter(item => item.mappingReady).length,
    probeVerified: deviceGroupProgress.filter(item => item.probeVerified).length,
    draftsSaved: deviceGroupProgress.filter(item => item.saved).length,
    published: deviceGroupProgress.filter(item => item.published).length,
  }), [deviceGroupProgress]);
  const supportsPointDiscovery = descriptor.probeMode === "discover";
  const probeDiscoveryCurrent = Boolean(
    probe?.sourceFingerprint === probeSourceFingerprint && probe.success,
  );
  const probeCatalogCurrent = Boolean(probeDiscoveryCurrent && probe.catalogFingerprint === probeCatalogFingerprint);
  const discoveredPointCount = Array.isArray(probe?.points) ? probe.points.length : 0;
  const probeErrorDetail = probeError
    ? probeErrorFingerprint === probeFingerprint
      ? `最近探查失败：${probeError}`
      : `上次探查失败：${probeError}；配置已变化，当前点位映射尚未复验`
    : "";
  const probeValidationDetail = form.status === "published"
    ? "发布前真实探查和点位换算校验已通过；现场应用与持续采样仍需单独核验"
    : probeErrorDetail
    || (probing ? "正在连接设备并校验点位换算"
      : probeValid ? "当前连接、点位回读和换算校验均已通过"
        : probeView?.validationCurrent && probe?.success
          ? probeView.message
          : probeDiscoveryCurrent
            ? "配置或点位地址已变化；需要重新探查，验证当前映射和换算结果"
            : "完成连接参数和变量映射后，使用真实设备回读验证当前配置");
  const observationStep = assessPublishedTaskObservation(
    form,
    observedEdge,
    acquisitionObservation,
    observedEdgeLoading || acquisitionObservationLoading,
    observedEdgeError || acquisitionObservationError,
  );
  const setupSteps = [
    { id: "device", label: "选择节点、模型与设备对象", tab: "device", done: Boolean(form.edgeId && model?.status === "published" && form.subjectId.trim()),
      detail: form.edgeId && model?.status === "published" && form.subjectId.trim()
        ? "Edge、已发布模型和设备归属已选择" : "需要选择 Edge、已发布工艺模型并填写设备对象编号" },
    { id: "connection", label: "填写连接参数", tab: "connection", done: Object.keys(connectionValidation).length === 0,
      detail: Object.keys(connectionValidation).length ? `${Object.keys(connectionValidation).length} 项连接参数待修正` : "连接参数格式完整" },
    ...(supportsPointDiscovery ? [{
      id: "discovery",
      label: "连接设备并读取点位",
      tab: "connection",
      done: form.status === "published" || (probeDiscoveryCurrent && discoveredPointCount > 0),
      detail: form.status === "published" ? "发布前已读取现场点位；运行时采样与上行需单独核验"
        : probing ? "正在连接设备并读取现场点位"
          : !probeDiscoveryCurrent ? probeErrorDetail || "需要连接当前 Edge 和设备，读取真实点位目录"
          : discoveredPointCount === 0 ? "连接已成功，但没有读取到点位；请检查设备范围或订阅配置"
            : probeCatalogCurrent ? `连接成功，已读取 ${discoveredPointCount} 个点位`
              : `已读取 ${discoveredPointCount} 个点位；映射或读取地址已变化，最终验证时需要重新探查`,
    }] : []),
    { id: "mappings", label: supportsPointDiscovery ? "映射工艺变量" : "配置点位并映射工艺变量", tab: "mappings", done: mappingSummary.mappingReady,
      detail: dataItems.length === 0 ? "模型没有可用采集变量"
        : requiredDataItems.length > 0 ? `必需变量 ${mappedRequiredCount}/${requiredDataItems.length} 项已配置`
          : mappingSummary.completeRowCount > 0 ? "必需项为可选；已配置至少一个可用变量" : "模型变量均为可选，至少配置一个变量" },
    { id: "probe-validation", label: supportsPointDiscovery ? "复探并验证变量映射" : "连接并回读已配置点位", tab: "connection", done: form.status === "published" || probeValid,
      detail: probeValidationDetail },
    { id: "lifecycle", label: "设置运行边界（建议）", tab: "advanced", done: Boolean(form.lifecycle.enabled && !lifecycleValidationErrors), recommended: true,
      detail: form.lifecycle.enabled && !lifecycleValidationErrors
        ? "运行边界已配置；发布后仍需核对实际运行关联"
        : "未配置时连续样本不会自动切分为单次运行；可配置自动识别或核对人工关联路径" },
    { id: "publish", label: "发布到现场节点", tab: null, done: form.status === "published",
      detail: form.status === "published" ? "期望配置已发布，仍需在 Edge 侧确认应用" : "完成前述配置与探查后发布" },
    { id: "observe", label: "现场核验（离开本页）", tab: null, done: false,
      state: observationStep.state, detail: observationStep.detail },
  ];
  const firstIncompleteSetupStep = setupSteps.find(step => !step.done && !step.recommended);
  const recommendedRunBoundary = setupSteps.find(step => step.id === "lifecycle" && step.recommended && !step.done);
  const activeSetupStep = form.status === "published" ? "observe"
    : firstIncompleteSetupStep?.id === "publish" && recommendedRunBoundary ? recommendedRunBoundary.id
      : firstIncompleteSetupStep?.id || "probe-validation";
  const publishChecklist = [
    { label: "选择现场节点、已发布模型与设备对象", done: Boolean(form.edgeId && model?.status === "published" && form.subjectId.trim()) },
    { label: "填写连接参数", done: !Object.keys(descriptor.validateConnection(form[descriptor.section] || {}) || {}).length },
    {
      label: "映射过程执行必需的工艺变量",
      done: requiredDataItems.length > 0 && mappedRequiredCount === requiredDataItems.length,
      detail: dataItems.length ? `必需 ${requiredDataItems.length} 项，已映射 ${mappedRequiredCount} 项` : "模型未包含可验证的采集变量",
    },
    { label: "验证连接并通过点位换算校验", done: probeValid },
  ];
  const siteId = searchParams.get("siteId");
  const qualityQuery = new URLSearchParams();
  if (siteId) qualityQuery.set("siteId", siteId);
  if (form.edgeId) qualityQuery.set("edgeId", form.edgeId);
  if (form.subjectType) qualityQuery.set("subjectType", form.subjectType);
  if (form.subjectId) qualityQuery.set("subjectId", form.subjectId);
  const dataQualityHref = `/data-quality?${qualityQuery.toString()}#run-impacts`;
  const executionQuery = new URLSearchParams();
  if (siteId) executionQuery.set("siteId", siteId);
  if (form.edgeId) executionQuery.set("edgeId", form.edgeId);
  if (form.subjectType === "equipment" && form.subjectId) executionQuery.set("equipmentId", form.subjectId);
  const processExecutionsHref = `/process-executions?${executionQuery.toString()}`;
  const edgeDiagnosticHref = form.edgeId
    ? `/edges/${encodeURIComponent(form.edgeId)}${siteId ? `?siteId=${encodeURIComponent(siteId)}` : ""}`
    : "";

  function update(patch) { setForm(value => ({ ...value, ...patch })); }
  function updateSection(section, value) { setForm(current => ({ ...current, [section]: value })); }
  function updateDeviceKind(kind) {
    const profile = deviceProfileOption(kind);
    setForm(current => {
      const deviceProfile = { ...current.deviceProfile, kind };
      const next = { ...current, deviceProfile };
      return profile.drivers.includes(current.protocol) || profile.drivers.length === 0
        ? next
        : { ...applyProtocolChange(next, profile.drivers[0]), deviceProfile };
    });
  }

  function resetActiveProbe() {
    probeRevision.current += 1;
    probeValidationRevision.current += 1;
    setProbe(null);
    setProbeError("");
    setProbeErrorFingerprint("");
    setProbing(false);
    setValidatedProbeRevision(null);
  }

  function focusMappingIssues(key) {
    if (multiDeviceSetup && key) selectDeviceGroupTask(key);
    setActiveInspectorTab("mappings");
    setShowErrors(true);
    setMappingFocusRevision(value => value + 1);
    document.getElementById("acq-workbench")?.scrollIntoView({ block: "start", behavior: "smooth" });
  }

  function focusMappingRow(key, index) {
    if (multiDeviceSetup && key) selectDeviceGroupTask(key);
    setActiveInspectorTab("mappings");
    setShowErrors(true);
    setMappingFocusRequest(current => ({ taskKey: key, index, revision: (current?.revision || 0) + 1 }));
    document.getElementById("acq-workbench")?.scrollIntoView({ block: "start", behavior: "smooth" });
  }

  function focusProbePoint(key, point) {
    if (multiDeviceSetup && key) selectDeviceGroupTask(key);
    setActiveInspectorTab("connection");
    setShowErrors(false);
    setProbePointFocusRequest(current => ({
      taskKey: key,
      topic: String(point?.topic || ""),
      path: String(point?.path || ""),
      revision: (current?.revision || 0) + 1,
    }));
    document.getElementById("acq-workbench")?.scrollIntoView({ block: "start", behavior: "smooth" });
  }

  function nextDeviceGroupKey() {
    groupDeviceOrdinal.current += 1;
    return `device-${groupDeviceOrdinal.current}`;
  }

  function nextDeviceTaskId(index, entries = deviceGroupEntries) {
    const used = new Set([
      ...groupTasks.map(item => String(item.taskId || "").trim().toLowerCase()),
      ...entries.map(item => String(item.form?.taskId || "").trim().toLowerCase()),
      String(form.taskId || "").trim().toLowerCase(),
    ].filter(Boolean));
    const stamp = Date.now().toString(36);
    let candidate = `device-${stamp}-${index}`;
    let suffix = 1;
    while (used.has(candidate.toLowerCase())) {
      candidate = `device-${stamp}-${index}-${suffix}`;
      suffix += 1;
    }
    return candidate;
  }

  function selectDeviceGroupTask(key) {
    const target = deviceGroupCards.find(item => item.key === key);
    if (!target) return;
    if (target.verificationRequired) {
      setGroupSaveError("请先核实该设备上一次保存结果，再从画布移除。未核实前会保留此设备和草稿状态。");
      return;
    }
    if (target.key === activeDeviceKey) {
      if (target.validationFailed) {
        setActiveInspectorTab(target.validationTab || "device");
        setShowErrors(true);
      }
      setGroupSaveError("");
      return;
    }
    if (target.key !== activeDeviceKey) {
      setActiveDeviceKey(key);
      setForm(target.form);
      resetActiveProbe();
    }
    setActiveInspectorTab(target.validationTab || "device");
    setShowErrors(Boolean(target.validationFailed));
    setGroupSaveError("");
  }

  function continueDeviceGroupTask(progress) {
    if (!progress || progress.verificationRequired) return;
    selectDeviceGroupTask(progress.key);
    setActiveInspectorTab(progress.nextTab);
    setShowErrors(true);
    document.getElementById("acq-workbench")?.scrollIntoView({ block: "start", behavior: "smooth" });
  }

  function addDeviceGroupTask(kindOverride, edgeIdOverride) {
    const initialKey = multiDeviceSetup ? "" : nextDeviceGroupKey();
    const initialForm = multiDeviceSetup ? null : {
      ...form,
      taskId: form.taskId || nextDeviceTaskId(1, []),
      name: form.name || "采集设备 1",
    };
    const initialEntries = multiDeviceSetup
      ? deviceGroupCards.map(({ key: entryKey, form: entryForm, savedFingerprint, error, validationFailed, validationTab, failureKind, verificationRequired }) => ({
        key: entryKey, form: entryForm, savedFingerprint, error, validationFailed, validationTab, failureKind, verificationRequired,
      }))
      : [{ key: initialKey, form: initialForm, savedFingerprint: "", error: "", validationFailed: false, validationTab: "device", failureKind: "", verificationRequired: false }];
    const baseForm = multiDeviceSetup ? form : initialForm;
    const index = initialEntries.length + 1;
    const key = nextDeviceGroupKey();
    const kind = kindOverride || baseForm.deviceProfile?.kind || "other";
    const assignedEdgeId = edgeIdOverride === undefined ? baseForm.edgeId : edgeIdOverride || "";
    if (assignedEdgeId && !edges.some(edge => edge.edgeId === assignedEdgeId)) {
      const message = `现场节点 ${assignedEdgeId} 当前不可用；请选择清单中的 Edge，或先保存为未分配草稿。`;
      if (multiDeviceSetup) setGroupSaveError(message);
      else setSaveError(message);
      return;
    }
    const profile = deviceProfileOption(kind);
    const protocol = profile.drivers[0] || "http-polling";
    const selected = parseModelValue(baseForm.dataModel);
    const newTask = applyProtocolChange(createIngestionTaskForm({
      taskId: nextDeviceTaskId(index, initialEntries),
      name: `${profile.label}采集 ${index}`,
      status: "draft",
      edgeId: assignedEdgeId,
      dataModelId: selected.id,
      dataModelVersion: selected.version,
      subjectType: baseForm.subjectType || "equipment",
      deviceProfile: { kind, manufacturer: "", model: "" },
    }), protocol);
    const nextForm = {
      ...newTask,
      edgeId: assignedEdgeId,
      dataModel: baseForm.dataModel,
      deviceProfile: { kind, manufacturer: "", model: "" },
    };
    const nextEntries = [
      ...initialEntries,
      { key, form: nextForm, savedFingerprint: "", error: "", validationFailed: false, validationTab: "device", failureKind: "", verificationRequired: false },
    ];
    setGroupActivated(true);
    setDeviceGroupEntries(nextEntries);
    setActiveDeviceKey(key);
    setForm(nextForm);
    setActiveInspectorTab("device");
    setShowErrors(false);
    setSaveError("");
    setGroupSaveError("");
    resetActiveProbe();
    return key;
  }

  function assignDeviceGroupEdge(key, edgeId) {
    if (!multiDeviceSetup && key === "single-device") {
      const normalizedEdgeId = edgeId || "";
      if (form.edgeId !== normalizedEdgeId) {
        setForm(current => ({ ...current, edgeId: normalizedEdgeId }));
        resetActiveProbe();
      }
      return;
    }
    const target = deviceGroupCards.find(item => item.key === key);
    const normalizedEdgeId = edgeId || "";
    if (!target || target.form.edgeId === normalizedEdgeId) return;
    if (normalizedEdgeId && !edges.some(edge => edge.edgeId === normalizedEdgeId)) {
      setGroupSaveError(`现场节点 ${normalizedEdgeId} 当前不可用；请刷新 Edge 清单后重新分配。`);
      return;
    }

    const nextForm = { ...target.form, edgeId: normalizedEdgeId };
    setDeviceGroupEntries(deviceGroupCards.map(({ key: entryKey, form: entryForm, savedFingerprint, error, validationFailed, validationTab, failureKind, verificationRequired }) => ({
      key: entryKey,
      form: entryKey === key ? nextForm : entryForm,
      savedFingerprint,
      error: entryKey === key && !verificationRequired ? "" : error,
      validationFailed: entryKey === key && !verificationRequired ? false : validationFailed,
      validationTab,
      failureKind: entryKey === key && !verificationRequired ? "" : failureKind,
      verificationRequired,
    })));
    setActiveDeviceKey(key);
    setForm(nextForm);
    setActiveInspectorTab(target.validationTab || "device");
    setShowErrors(Boolean(target.validationFailed));
    setGroupSaveError("");
    resetActiveProbe();
  }

  function assignDeviceGroupModel(key, dataModel) {
    const target = multiDeviceSetup
      ? deviceGroupCards.find(item => item.key === key)
      : key === "single-device" ? { form } : null;
    if (!target || target.form.dataModel === dataModel) return;
    const nextForm = { ...target.form, dataModel };
    if (multiDeviceSetup) {
      setDeviceGroupEntries(deviceGroupCards.map(({ key: entryKey, form: entryForm, savedFingerprint, error, validationFailed, validationTab, failureKind, verificationRequired }) => ({
        key: entryKey,
        form: entryKey === key ? nextForm : entryForm,
        savedFingerprint,
        error: entryKey === key && !verificationRequired ? "" : error,
        validationFailed: entryKey === key && !verificationRequired ? false : validationFailed,
        validationTab,
        failureKind: entryKey === key && !verificationRequired ? "" : failureKind,
        verificationRequired,
      })));
      setActiveDeviceKey(key);
    }
    setForm(nextForm);
    setActiveInspectorTab("device");
    setShowErrors(false);
    setGroupSaveError("");
    resetActiveProbe();
  }

  async function removeDeviceGroupTask(key) {
    if (deviceGroupCards.length <= 1) return;
    const target = deviceGroupCards.find(item => item.key === key);
    if (!target) return;
    if (target.savedFingerprint && !await confirm({
      title: `移出设备 ${target.form.name || target.form.taskId}`,
      description: "该设备已保存的独立草稿会保留在采集配置列表中；继续后只从当前画布移除。",
      confirmLabel: "移出画布",
      tone: "warning",
    })) return;
    const remaining = deviceGroupCards.filter(item => item.key !== key).map(({ key: entryKey, form: entryForm, savedFingerprint, error, validationFailed, validationTab, failureKind, verificationRequired }) => ({
      key: entryKey, form: entryForm, savedFingerprint, error, validationFailed, validationTab, failureKind, verificationRequired,
    }));
    setDeviceGroupEntries(remaining);
    if (key === activeDeviceKey) {
      const next = remaining[0];
      setActiveDeviceKey(next.key);
      setForm(next.form);
      setActiveInspectorTab(next.validationTab || "device");
      setShowErrors(Boolean(next.validationFailed));
      resetActiveProbe();
    }
    setGroupSaveError("");
  }

  async function runProbe(discovery = {}) {
    const { append = false, ...query } = discovery;
    const revision = ++probeRevision.current;
    const validationRevision = probeValidationRevision.current;
    const catalogFingerprint = probeCatalogFingerprint;
    setProbing(true);
    setProbeError("");
    setProbeErrorFingerprint("");
    setValidatedProbeRevision(null);
    try {
      const result = await postJson(`${ENDPOINT}/probe`, {
        task: probeTask,
        discovery: { pageSize: 300, ...query },
      });
      if (revision !== probeRevision.current) return;
      setProbe(current => {
        const sameSource = current?.sourceFingerprint === probeSourceFingerprint;
        return append && sameSource
          ? {
            ...result,
            sourceFingerprint: probeSourceFingerprint,
            catalogFingerprint,
            points: [...(current.points || []), ...(result.points || [])],
          }
          : { ...result, sourceFingerprint: probeSourceFingerprint, catalogFingerprint };
      });
      if (validationRevision === probeValidationRevision.current)
        setValidatedProbeRevision(validationRevision);
    } catch (error) {
      if (revision !== probeRevision.current) return;
      // Keep same-connection discovery evidence available after a failed remapping probe; it is still not publishable.
      setProbe(current => current?.sourceFingerprint === probeSourceFingerprint ? current : null);
      setProbeError(error.message);
      setProbeErrorFingerprint(probeFingerprint);
    } finally {
      if (revision === probeRevision.current) setProbing(false);
    }
  }

  function mapProbePoint(point, dataItemCode) {
    const configuredMappings = [
      ...form.valueMappings,
      ...(form.processSpecification?.parameterMappings || []),
    ];
    const patch = patchFromProbePoint(point, dataItemCode, dataItems, descriptor, configuredMappings);
    setForm(current => {
      const rows = current.valueMappings;
      const existing = rows.findIndex(item => item.dataItemCode === dataItemCode);
      const blank = rows.findIndex(item => !item.dataItemCode);
      const target = existing >= 0 ? existing : blank;
      if (target >= 0)
        return { ...current, valueMappings: rows.map((item, index) => index === target ? { ...item, ...patch } : item) };
      return { ...current, valueMappings: [...rows, { ...blankRow(descriptor), ...patch }] };
    });
  }

  async function save(targetStatus) {
    const next = { ...form, status: targetStatus };
    const check = validateIngestionTask(next, { dataItems });
    if (check.count > 0) {
      setShowErrors(true);
      setActiveInspectorTab(inspectorTabForErrors(check.errors, descriptor.section));
      setSaveError(`还有 ${check.count} 处需要修正，已在对应字段标出。`);
      return;
    }
    if (targetStatus === "published" && !probeValid) {
      setSaveError("发布前必须先验证连接并通过点位换算校验。");
      return;
    }
    setSaving(true);
    setSaveError("");
    try {
      if (targetStatus === "published" && form.templateId && form.dataSourceId)
        await postJson(`/api/v1/ingestion-configuration/bindings/${encodeURIComponent(form.taskId)}/${form.version}:publish`);
      else
        await postJson(ENDPOINT, toPayload(next));
      notify(targetStatus === "published" ? "接入配置已发布" : "接入配置已保存");
      navigate("/configuration/ingestion-tasks");
    } catch (error) {
      setSaveError(error.message);
    } finally {
      setSaving(false);
    }
  }

  async function saveDeviceGroupDrafts(targetKey = null) {
    if (!groupTaskData || deviceGroupLoadError) {
      setGroupSaveError("任务编号清单尚未完整载入；请等待载入或重试后再保存。未完整载入时不会写入草稿。");
      return;
    }
    if (deviceGroupCards.length === 0) {
      setGroupSaveError("请先添加至少一个采集设备。");
      return;
    }

    const seenIds = new Set();
    const prepared = deviceGroupCards.map(device => {
      const taskForm = { ...device.form, status: "draft" };
      const selected = parseModelValue(taskForm.dataModel);
      const model = models.find(item => item.modelId === selected.id && Number(item.version) === Number(selected.version));
      const dataItems = model?.acquisition?.dataItems || [];
      const check = validateIngestionTask(taskForm, { dataItems });
      const errors = [];
      const taskId = String(taskForm.taskId || "").trim().toLowerCase();
      const payload = deviceGroupPayload(taskForm);
      const fingerprint = JSON.stringify(payload);
      const matchingTasks = groupTasks.filter(item => String(item.taskId || "").trim().toLowerCase() === taskId);
      const alreadyPersisted = matchingTasks.some(item => storedDeviceFingerprint(item) === fingerprint);
      if (!model || model.status !== "published") errors.push("请选择当前可用的已发布工艺模型。");
      if (matchingTasks.length > 0 && !alreadyPersisted)
        errors.push("任务编号已存在且内容不同；请换一个编号，避免覆盖现有任务版本。");
      if (seenIds.has(taskId)) errors.push("画布中有重复的任务编号。");
      seenIds.add(taskId);
      if (device.savedFingerprint && device.savedFingerprint !== fingerprint)
        errors.push("此任务草稿已保存；如需修改，请从采集配置列表创建新版本。当前批次不会覆盖已保存版本。");
      if (check.count > 0) errors.push(`还有 ${check.count} 处配置需要修正。`);
      return {
        ...device,
        form: taskForm,
        payload,
        fingerprint,
        alreadyPersisted,
        errors,
        validationFailed: errors.length > 0,
        validationTab: inspectorTabForErrors(check.errors, protocolDescriptor(taskForm.protocol).section),
        verificationRequired: Boolean(device.verificationRequired),
        failureKind: device.failureKind || "",
      };
    });
    const candidates = targetKey ? prepared.filter(item => item.key === targetKey) : prepared;
    const invalid = candidates.filter(item => item.errors.length > 0);
    if (invalid.length > 0 && targetKey) {
      const firstInvalid = invalid[0];
      setDeviceGroupEntries(current => current.map(entry => {
        const item = prepared.find(candidate => candidate.key === entry.key);
        if (!item || (targetKey && item.key !== targetKey)) return entry;
        return {
          key: item.key,
          form: item.form,
          savedFingerprint: item.alreadyPersisted ? item.fingerprint : item.savedFingerprint,
          error: item.errors.join(" "),
          validationFailed: item.validationFailed,
          validationTab: item.validationTab,
          failureKind: "validation",
          verificationRequired: false,
        };
      }));
      setActiveDeviceKey(firstInvalid.key);
      setForm(firstInvalid.form);
      setActiveInspectorTab(firstInvalid.validationTab || "device");
      setShowErrors(Boolean(firstInvalid.validationFailed));
      resetActiveProbe();
      setGroupSaveError(`${invalid.length} 个设备配置未通过检查。已切换到第一个待修正设备；修正后可重试该项或再次保存全部草稿。`);
      return;
    }

    setSaving(true);
    setGroupSaveError("");
    const results = prepared.map(item => {
      const isCandidate = !targetKey || item.key === targetKey;
      return {
        key: item.key,
        form: item.form,
        savedFingerprint: item.alreadyPersisted ? item.fingerprint : item.savedFingerprint,
        error: isCandidate && !item.verificationRequired ? item.errors.join(" ") : item.error || "",
        validationFailed: item.validationFailed,
        validationTab: item.validationTab || "device",
        failureKind: item.verificationRequired ? item.failureKind || "unknown" : isCandidate && item.errors.length ? "validation" : item.failureKind || "",
        verificationRequired: item.verificationRequired,
      };
    });
    setDeviceGroupEntries(results.map(result => ({ ...result })));
    let savedCount = 0;
    try {
      for (const item of candidates) {
        const current = results.find(result => result.key === item.key);
        if (item.verificationRequired) continue;
        if (item.errors.length > 0) continue;
        if (current.savedFingerprint === item.fingerprint || item.alreadyPersisted) {
          current.savedFingerprint = item.fingerprint;
          current.error = "";
          current.failureKind = "";
          current.verificationRequired = false;
          savedCount += 1;
          continue;
        }
        try {
          await postJson(ENDPOINT, item.payload);
          current.savedFingerprint = item.fingerprint;
          current.error = "";
          current.failureKind = "";
          current.verificationRequired = false;
          savedCount += 1;
        } catch (saveError) {
          let outcome;
          try {
            const stored = await getJson(`${ENDPOINT}/${encodeURIComponent(item.form.taskId)}/${item.form.version}`);
            outcome = classifyIngestionDraftWriteOutcome({
              expectedFingerprint: item.fingerprint,
              storedFingerprint: storedDeviceFingerprint(stored),
              saveError,
            });
          } catch (verificationError) {
            outcome = classifyIngestionDraftWriteOutcome({
              expectedFingerprint: item.fingerprint,
              saveError,
              verificationError,
            });
          }
          if (outcome.kind === "saved") {
            current.savedFingerprint = item.fingerprint;
            savedCount += 1;
          }
          current.error = outcome.error;
          current.failureKind = outcome.kind;
          current.validationFailed = outcome.kind === "validation";
          current.verificationRequired = outcome.verificationRequired;
        }
        setDeviceGroupEntries(results.map(result => ({ ...result })));
      }
      const validationFailureCount = candidates.filter(item => item.errors.length > 0 || results.find(result => result.key === item.key)?.failureKind === "validation").length;
      const requestFailureCount = candidates.filter(item => {
        const result = results.find(candidate => candidate.key === item.key);
        return !item.errors.length && !result?.verificationRequired && result?.failureKind === "request";
      }).length;
      const conflictCount = candidates.filter(item => results.find(result => result.key === item.key)?.failureKind === "conflict").length;
      const rejectedCount = candidates.filter(item => results.find(result => result.key === item.key)?.failureKind === "rejected").length;
      const verificationRequiredCount = candidates.filter(item => results.find(result => result.key === item.key)?.verificationRequired).length;
      if (validationFailureCount > 0 || requestFailureCount > 0 || conflictCount > 0 || rejectedCount > 0 || verificationRequiredCount > 0) {
        const firstValidationIssue = candidates.find(item => item.errors.length > 0 || results.find(result => result.key === item.key)?.failureKind === "validation");
        if (firstValidationIssue) {
          setActiveDeviceKey(firstValidationIssue.key);
          setForm(firstValidationIssue.form);
          setActiveInspectorTab(firstValidationIssue.validationTab || "device");
          setShowErrors(Boolean(firstValidationIssue.validationFailed || results.find(result => result.key === firstValidationIssue.key)?.validationFailed));
          resetActiveProbe();
        }
        setGroupSaveError(`本次${targetKey ? "重试" : "保存"}已完成：${savedCount} 项已保存或保持成功，${validationFailureCount} 项配置校验未通过，${requestFailureCount} 项确认未保存，${conflictCount} 项任务编号冲突，${rejectedCount} 项请求被服务器拒绝，${verificationRequiredCount} 项提交结果待核实。下方列出各项原因和后续操作。`);
        return;
      }
      if (targetKey) {
        setGroupSaveError("");
        notify("该设备草稿已保存");
      } else {
        notify(`已保存 ${savedCount} 个独立采集草稿任务`);
        navigate("/configuration/ingestion-tasks");
      }
    } finally {
      setSaving(false);
    }
  }

  async function verifyDeviceGroupDraft(targetKey) {
    const target = deviceGroupCards.find(item => item.key === targetKey);
    if (!target) return;
    const fingerprint = JSON.stringify(deviceGroupPayload(target.form));
    setSaving(true);
    setGroupSaveError("");
    let outcome;
    try {
      const stored = await getJson(`${ENDPOINT}/${encodeURIComponent(target.form.taskId)}/${target.form.version}`);
      outcome = classifyIngestionDraftWriteOutcome({
        expectedFingerprint: fingerprint,
        storedFingerprint: storedDeviceFingerprint(stored),
      });
    } catch (error) {
      outcome = classifyIngestionDraftWriteOutcome({
        expectedFingerprint: fingerprint,
        verificationError: error,
      });
    } finally {
      setSaving(false);
    }
    setDeviceGroupEntries(current => current.map(entry => entry.key === targetKey
      ? {
        ...entry,
        savedFingerprint: outcome.kind === "saved" ? fingerprint : entry.savedFingerprint,
        error: outcome.error,
        failureKind: outcome.kind,
        validationFailed: outcome.kind === "validation",
        verificationRequired: outcome.verificationRequired,
      }
      : entry));
    setGroupSaveError(outcome.kind === "saved"
      ? `${target.form.taskId} v${target.form.version} 已在服务器保存，结果已核实。`
      : outcome.kind === "conflict"
        ? "服务器中的任务版本与当前草稿不同；请核对编号并修改后再保存。"
        : outcome.kind === "request"
          ? "服务器未找到该任务版本；已确认可以重试保存。"
          : "暂时无法核实服务器状态。请保持此设备未修改，稍后重新核实。其他设备可以继续处理。");
  }

  const inspectorPanels = {
    device: (
      <Card title="设备与数据归属" description="选择设备类别、工艺变量模型和数据所归属的对象。">
        <div className="grid items-start gap-4 sm:grid-cols-2">
          <Field label="接入配置代码" hint="创建后不可修改。" error={errors.taskId}>
            <Input value={form.taskId} disabled={readOnly || (!isNew && mode !== "create")}
              placeholder="press01-fx3u" onChange={event => update({ taskId: event.target.value })} />
          </Field>
          <Field label="配置名称" error={errors.name}>
            <Input value={form.name} disabled={readOnly} onChange={event => update({ name: event.target.value })} />
          </Field>
          <Field label="设备类型">
            <Select value={form.deviceProfile?.kind || "other"} disabled={readOnly}
              onChange={event => updateDeviceKind(event.target.value)}>
              {deviceProfileOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
            </Select>
          </Field>
          <Field label="设备厂商（可选）">
            <Input value={form.deviceProfile?.manufacturer || ""} disabled={readOnly} placeholder="例如 Mitsubishi、Siemens、Fanuc"
              onChange={event => update({ deviceProfile: { ...form.deviceProfile, manufacturer: event.target.value } })} />
          </Field>
          <Field label="设备型号（可选）">
            <Input value={form.deviceProfile?.model || ""} disabled={readOnly} placeholder="填写现场铭牌型号或系列"
              onChange={event => update({ deviceProfile: { ...form.deviceProfile, model: event.target.value } })} />
          </Field>
          <Field label="工艺变量" hint="规定过程量和控制参数的代码与单位，不包含来源地址。" error={errors.dataModel}>
            <Select value={form.dataModel} disabled={readOnly} onChange={event => update({ dataModel: event.target.value })}>
              <option value="">请选择工艺变量</option>
              {models.map(item => <option key={`${item.modelId}::${item.version}`} value={modelValue(item.modelId, item.version)}>
                {item.name || item.modelId} · v{item.version}
              </option>)}
            </Select>
          </Field>
          <Field label="数据归属类型">
            <Select value={form.subjectType} disabled={readOnly} onChange={event => update({ subjectType: event.target.value })}>
              <option value="equipment">生产设备</option>
              <option value="quality_instrument">检测仪器</option>
              <option value="line">生产线</option>
              <option value="utility_meter">公用工程仪表</option>
              <option value="system">业务系统</option>
              {!["equipment", "quality_instrument", "line", "utility_meter", "system"].includes(form.subjectType) &&
                <option value={form.subjectType}>{form.subjectType}（历史值）</option>}
            </Select>
          </Field>
          <Field label="归属对象编号" hint="例如 PRESS-01。" error={errors.subjectId}>
            <Input value={form.subjectId} disabled={readOnly} onChange={event => update({ subjectId: event.target.value })} />
          </Field>
          <Field label="生产设备单元编号（可选）" hint="填写生产准备、工装安装或生产上下文所关联的设备编号。">
            <Input value={form.equipmentId} disabled={readOnly} onChange={event => update({ equipmentId: event.target.value })} />
          </Field>
        </div>
      </Card>
    ),
    connection: (
      <div className="grid min-w-0 gap-4">
        <Card title="通信驱动" description={`${descriptor.summary} 驱动选项按设备类型过滤。`}>
          <Field label="协议">
            <Select value={form.protocol} disabled={readOnly}
              onChange={event => setForm(current => applyProtocolChange(current, event.target.value))}>
              {protocolOptionGroups(deviceProfileOption(form.deviceProfile?.kind).drivers).map(group => (
                <optgroup key={group.label} label={group.label}>
                  {group.options.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                </optgroup>
              ))}
            </Select>
          </Field>
        </Card>
        <ConnectionPanel
          descriptor={descriptor}
          connection={form[descriptor.section]}
          errors={errors}
          readOnly={readOnly}
          allowProbe={allowProbe}
          onChange={value => updateSection(descriptor.section, value)}
        />
        <DevicePointsPanel
          descriptor={descriptor}
          form={form}
          dataItems={dataItems}
          probe={probeView}
          probeError={probeErrorDetail}
          probing={probing}
          readOnly={readOnly}
          allowProbe={allowProbe}
          advisories={advisories}
          publishChecklist={publishChecklist}
          focusPointRequest={probePointFocusRequest?.taskKey === currentTaskKey ? probePointFocusRequest : null}
          onFocusPointComplete={completeProbePointFocus}
          onProbe={runProbe}
          onMapPoint={mapProbePoint}
        />
      </div>
    ),
    mappings: (
      <PointMappingPanel
        title="工艺变量映射"
        description="把设备点位对应到平台工艺变量，并配置换算、质量和缺失值处理。"
        descriptor={descriptor}
        rows={form.valueMappings}
        options={dataItems}
        errors={errors}
        errorPrefix="valueMappings"
        probe={probeView}
        form={form}
        readOnly={readOnly}
        focusIncompleteRevision={mappingFocusRevision}
        focusRowRequest={mappingFocusRequest?.taskKey === currentTaskKey ? mappingFocusRequest : null}
        onFocusRowComplete={completeMappingFocus}
        onChange={value => update({ valueMappings: value })}
      />
    ),
    advanced: (
      <div className="grid gap-4">
        <ContextPanel descriptor={descriptor} form={form} errors={errors} readOnly={readOnly} onChange={update} />
        <ProcessSpecificationPanel
          descriptor={descriptor}
          form={form}
          parameters={controlParameters}
          errors={errors}
          probe={probe}
          readOnly={readOnly}
          onChange={update}
        />
        <LifecyclePanel form={form} errors={errors} readOnly={readOnly} onChange={update} />
        <StrategyPanel descriptor={descriptor} form={form} errors={errors} readOnly={readOnly} onChange={update} />
      </div>
    ),
  };

  if (loading) return <Page title="采集配置"><Card><p className="text-sm text-slate-500">正在载入配置…</p></Card></Page>;
  if (loadError) return <Page title="采集配置"><Alert tone="danger">{loadError}</Alert></Page>;

  return (
    <Page
      className="ingestion-task-editor-page"
      title={isNew ? "新建采集配置" : form.name || form.taskId}
      actions={
        <div className="flex items-center gap-2">
          <Link className="inline-flex min-h-9 items-center rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
            to="/configuration/ingestion-tasks">返回列表</Link>
          {multiDeviceSetup && (
            <Button variant="primary" disabled={saving || !groupTaskData || Boolean(deviceGroupLoadError)}
              onClick={() => void saveDeviceGroupDrafts()}>
              {saving ? "逐任务保存中…" : `逐任务保存 ${deviceGroupCards.length} 个草稿`}
            </Button>
          )}
          {!readOnly && !multiDeviceSetup && (
            <>
              <Button disabled={saving} onClick={() => save("draft")}>{saving ? "保存中…" : "保存草稿"}</Button>
              <Button data-acq-publish="true" variant="primary" disabled={saving || !probeValid} onClick={() => save("published")}>
                发布到现场节点
              </Button>
            </>
          )}
          {!multiDeviceSetup && canWrite && managedByBinding && form.status === "draft" && (
            <Button data-acq-publish="true" variant="primary" disabled={saving || !probeValid} onClick={() => save("published")}>
              发布任务绑定
            </Button>
          )}
        </div>
      }
    >
      {confirmationDialog}
      {protocolCapabilitiesError && <Alert tone="warning">
        <p>驱动能力列表读取失败，当前表单使用内置能力描述；服务端仍会在探查和发布时校验配置。{protocolCapabilitiesError}</p>
        <Button className="mt-2" onClick={() => setProtocolCapabilitiesRevision(value => value + 1)}>重试读取驱动能力</Button>
      </Alert>}
      {multiDeviceSetup && deviceGroupLoadError && <Alert tone="danger">
        <p>读取可访问任务清单失败，无法安全检查多设备配置中的编号冲突：{deviceGroupLoadError}</p>
        <Button className="mt-2" onClick={() => void Promise.all([reloadEdges(), reloadModels(), reloadGroupTasks()])}>重试读取</Button>
      </Alert>}
      {multiDeviceSetup && groupSaveError && <Alert tone="warning"><p className="whitespace-pre-line">{groupSaveError}</p></Alert>}
      {!managedByBinding && form.status !== "retired" && <AcquisitionSetupGuide
        steps={setupSteps}
        activeStep={activeSetupStep}
        onSelectStep={step => {
          if (step.tab) {
            setActiveInspectorTab(step.tab);
            setShowErrors(true);
            document.getElementById("acq-workbench")?.scrollIntoView({ block: "start", behavior: "smooth" });
          } else {
            (step.id === "publish" ? document.querySelector("[data-acq-publish]") : document.querySelector("[data-acq-edges-link]"))
              ?.scrollIntoView({ block: "center", behavior: "smooth" });
          }
        }}
        selectedEdgeId={form.edgeId}
        published={form.status === "published"}
        currentDeviceLabel={multiDeviceSetup ? form.name || form.taskId : ""}
        requiredCount={requiredDataItems.length}
        mappedRequiredCount={mappedRequiredCount}
        edgeDiagnosticHref={edgeDiagnosticHref}
        dataQualityHref={dataQualityHref}
        processExecutionsHref={processExecutionsHref}
      />}
      {observationEnabled && <PublishedTaskObservationCard
        task={form}
        edge={observedEdge}
        acquisition={acquisitionObservation}
        loading={observedEdgeLoading || acquisitionObservationLoading}
        error={observedEdgeError || acquisitionObservationError}
        onRetry={() => Promise.all([reloadObservedEdges(), reloadAcquisitionObservation()])}
        edgeDiagnosticHref={edgeDiagnosticHref}
        dataQualityHref={dataQualityHref}
        processExecutionsHref={processExecutionsHref}
      />}
      {multiDeviceSetup && deviceGroupCards.length > 0 && <section className="rounded-xl border border-slate-200 bg-white p-4" aria-label="多设备接入进度与草稿保存结果">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <div><h2 className="text-sm font-semibold text-slate-900">多设备接入进度与草稿结果</h2><p className="mt-1 text-xs text-slate-500">步骤引导跟随当前设备；下方逐台显示配置、映射、探查和保存状态。保存草稿不代表探查通过或已发布。</p></div>
          <Button disabled={saving || !groupResultAction} onClick={() => groupResultAction?.run()}>
            {saving ? "处理中…" : groupResultAction?.label || "无需处理"}
          </Button>
        </div>
        <dl className="mb-3 grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-5">
          {[
            ["对象与连接", deviceGroupProgressSummary.contextReady],
            ["映射就绪", deviceGroupProgressSummary.mappingReady],
            ["探查验证", deviceGroupProgressSummary.probeVerified],
            ["草稿保存", deviceGroupProgressSummary.draftsSaved],
            ["已发布", deviceGroupProgressSummary.published],
          ].map(([label, count]) => <div key={label} className="rounded-lg bg-slate-50 px-3 py-2">
            <dt className="text-xs text-slate-500">{label}</dt>
            <dd className="mt-1 text-sm font-semibold text-slate-800">{count}/{deviceGroupProgressSummary.total} 台</dd>
          </div>)}
        </dl>
        <ul className="divide-y divide-slate-100">
          {deviceGroupCards.map(item => {
            const progress = deviceGroupProgress.find(candidate => candidate.key === item.key);
            const fingerprint = JSON.stringify(deviceGroupPayload(item.form));
            const saved = Boolean(item.savedFingerprint && item.savedFingerprint === fingerprint);
            const editedAfterSave = Boolean(item.savedFingerprint && !saved);
            return <li key={item.key} aria-current={item.key === activeDeviceKey ? "step" : undefined} className="py-3 text-sm">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <strong className="text-slate-800">{item.form.name || item.form.taskId || "新采集任务"}</strong>
                  <span className="ml-2 text-xs text-slate-500">{item.form.taskId || "编号待填写"} · {item.error
                    ? item.verificationRequired ? "提交结果待核实" : item.failureKind === "conflict" ? "任务编号冲突" : item.failureKind === "rejected" ? "请求被拒绝" : item.validationFailed ? "配置校验未通过" : "接口保存失败"
                    : saved ? "草稿已保存" : editedAfterSave ? "已保存草稿有未保存修改" : "待保存"}</span>
                  {item.error && <p className="mt-1 text-xs text-rose-700">{item.error}</p>}
                  {editedAfterSave && <p className="mt-1 text-xs text-amber-700">请从采集配置列表创建新版本，避免覆盖已保存草稿。</p>}
                </div>
                <div className="flex flex-wrap gap-2">
                  {!item.verificationRequired && <Button disabled={saving} onClick={() => continueDeviceGroupTask(progress)}>
                    {item.key === activeDeviceKey ? "定位当前步骤" : "继续此设备"}
                  </Button>}
                  {item.error && <Button disabled={saving} onClick={() => item.verificationRequired
                    ? void verifyDeviceGroupDraft(item.key)
                    : item.validationFailed || item.failureKind === "conflict" || item.failureKind === "rejected"
                      ? selectDeviceGroupTask(item.key)
                      : void saveDeviceGroupDrafts(item.key)}>
                    {item.verificationRequired ? "核实结果" : item.failureKind === "rejected" ? "查看原因" : item.validationFailed || item.failureKind === "conflict" ? "查看并修正" : "重试此项"}
                  </Button>}
                </div>
              </div>
              {progress && <div className="mt-2 flex flex-wrap gap-1.5" aria-label={`${item.form.name || item.form.taskId} 接入阶段`}>
                <Badge tone={progress.contextReady ? "success" : "warning"}>对象 {progress.contextLabel}</Badge>
                <Badge tone={progress.connectionReady ? "success" : "warning"}>连接 {progress.connectionLabel}</Badge>
                <Badge tone={progress.mappingReady ? "success" : "warning"}>映射 {progress.mappingLabel}</Badge>
                <Badge tone={progress.probeVerified ? "success" : "warning"}>探查 {progress.probeVerified ? "本会话已通过" : "待逐台验证"}</Badge>
                <Badge tone={progress.runBoundaryReady ? "success" : progress.runBoundaryErrors ? "danger" : "neutral"}>
                  运行边界 {progress.runBoundaryLabel}
                </Badge>
                <Badge tone={saved ? "success" : item.error || editedAfterSave ? "warning" : "neutral"}>草稿 {progress.saveLabel}</Badge>
                <Badge tone={progress.published ? "success" : "neutral"}>发布 {progress.published ? "已发布" : "尚未发布"}</Badge>
              </div>}
            </li>;
          })}
        </ul>
        <p className="mt-3 border-t border-slate-100 pt-3 text-xs leading-5 text-slate-500">
          画布只逐任务保存草稿。探查和映射校验需要对每台设备分别完成；发布后再单独核对现场采样、上行和运行关联。
        </p>
      </section>}
      {readOnly && <Alert tone="info">{!canWrite
        ? "当前为只读视图；你可以核对驱动、点位映射和上下文来源，修改与发布由工艺工程师或平台管理员完成。"
        : managedByBinding
          ? "该任务由不可变模板与数据源版本生成；需要调整时请创建新的资产版本和任务绑定。"
          : "已发布或已停用的配置不可修改。要调整参数请创建新版本。"}</Alert>}
      {saveError && <Alert tone="danger">{saveError}</Alert>}
      {!multiDeviceSetup && showErrors && validation.count > 0 && (
        <Alert tone="warning">还有 {validation.count} 处配置需要修正，已在对应字段下方标出。</Alert>
      )}

      <IngestionDeviceGroupCanvas
        id="acq-workbench"
        canvasInstanceKey={`${taskId || "new"}:${requestedVersion || mode}`}
        devices={canvasDevices}
        edges={edges}
        models={models}
        activeEdgeId={form.edgeId || null}
        activeDeviceKey={multiDeviceSetup ? activeDeviceKey : "single-device"}
        activeInspectorTab={activeInspectorTab}
        onInspectorTabChange={setActiveInspectorTab}
        onSelect={multiDeviceSetup ? selectDeviceGroupTask : undefined}
        onAddDevice={isNew && canWrite && !managedByBinding ? addDeviceGroupTask : undefined}
        onAssignEdge={assignDeviceGroupEdge}
        onAssignModel={assignDeviceGroupModel}
        onRemove={multiDeviceSetup ? removeDeviceGroupTask : undefined}
        canAddDevice={isNew && canWrite && !managedByBinding}
        probe={probeView}
        edgeError={errors.edgeId}
        disabled={saving || readOnly}
        inspectorPanels={inspectorPanels}
        onFocusMappingIssues={focusMappingIssues}
        onFocusMappingRow={focusMappingRow}
        onFocusProbePoint={focusProbePoint}
      />
    </Page>
  );
}

function PublishedTaskObservationCard({ task, edge, acquisition, loading, error, onRetry, edgeDiagnosticHref, dataQualityHref, processExecutionsHref }) {
  const taskKey = `${task.taskId}@${task.version}`.toLowerCase();
  const runtime = (acquisition?.tasks || []).find(item => String(item.configurationKey || "").toLowerCase() === taskKey);
  const deployment = (acquisition?.deployments || []).find(item => String(item.taskId || "").toLowerCase() === String(task.taskId || "").toLowerCase());
  const deploymentMatches = Boolean(deployment && Number(deployment.desiredVersion) === Number(task.version));
  const deploymentConverged = Boolean(deploymentMatches && deployment.desiredConfigurationHash &&
    deployment.desiredConfigurationHash === deployment.appliedConfigurationHash &&
    Number(deployment.appliedVersion) === Number(task.version) && deployment.state === "applied");
  const applicationState = !deployment ? "unknown"
    : !deploymentMatches ? "pending"
      : deploymentConverged ? "applied"
        : deployment.state || "pending";
  const applicationLabel = !deployment ? "等待节点上报"
    : !deploymentMatches ? `节点期望 v${deployment.desiredVersion}`
      : deploymentConverged ? `v${task.version} 已应用`
        : deployment.desiredConfigurationHash && deployment.appliedConfigurationHash && deployment.desiredConfigurationHash !== deployment.appliedConfigurationHash
          ? "配置指纹不一致"
          : deployment.state === "failed" ? "应用失败"
            : deployment.state === "rollback" ? "保留旧版本"
              : deployment.state === "applied" ? "尚未确认当前配置指纹"
                : undefined;
  const connectionState = edgeConnectionStatus(edge);
  const delivery = edge?.delivery;
  const backlogFresh = edge?.backlogStatisticsFresh === true;
  const backlogCount = backlogFresh && delivery?.pendingEventCount != null && Number.isFinite(Number(delivery.pendingEventCount))
    ? Number(delivery.pendingEventCount)
    : null;
  const deliveryLabel = delivery?.state === "blocked" ? "上行阻塞"
    : delivery?.state === "degraded" ? "上行异常"
      : backlogFresh && backlogCount === 0 ? "队列已核实为空"
        : backlogFresh ? "队列已核实"
          : "当前状态未知";
  const deliveryBadgeState = delivery?.state === "blocked" || delivery?.state === "degraded"
    ? delivery.state
    : backlogFresh && backlogCount === 0 ? "synchronized"
      : backlogFresh ? "pending" : "unknown";
  const backlogDetail = backlogFresh
    ? `待补传 ${backlogCount == null ? "未知" : formatInteger(backlogCount)} · 统计于 ${formatTime(delivery?.backlogObservedAt)}`
    : `当前待补传数量未知${delivery?.backlogObservedAt ? ` · 上次核实 ${formatTime(delivery.backlogObservedAt)}` : " · 尚无有效队列统计"}`;
  const deliveryObservedAtMs = edge?.serverObservedAt ? new Date(edge.serverObservedAt).getTime() : null;
  const oldestPendingAtMs = delivery?.oldestPendingEventAt ? new Date(delivery.oldestPendingEventAt).getTime() : null;
  const oldestPendingAgeMs = backlogFresh && Number.isFinite(deliveryObservedAtMs) && Number.isFinite(oldestPendingAtMs)
    && oldestPendingAtMs <= deliveryObservedAtMs ? deliveryObservedAtMs - oldestPendingAtMs : null;
  const backlogCapacityPercent = backlogFresh && delivery?.backlogCapacityUsedPercent != null
    && Number.isFinite(Number(delivery.backlogCapacityUsedPercent))
    ? Number(delivery.backlogCapacityUsedPercent) : null;
  const estimatedDrainMs = backlogFresh && delivery?.estimatedDrainSeconds != null
    && Number.isFinite(Number(delivery.estimatedDrainSeconds))
    ? Number(delivery.estimatedDrainSeconds) * 1000 : null;
  const reportObservedAt = acquisition?.reportedAt;
  const platformObservedMs = acquisition?.platformObservedAt ? new Date(acquisition.platformObservedAt).getTime() : null;
  const platformReceivedMs = acquisition?.platformReceivedAt ? new Date(acquisition.platformReceivedAt).getTime() : null;
  const reportAgeMs = platformObservedMs - platformReceivedMs;
  const reportTimeIsVerifiable = Number.isFinite(platformObservedMs) && Number.isFinite(platformReceivedMs) && reportAgeMs >= 0;
  const reportIsStale = reportTimeIsVerifiable && reportAgeMs > 30000;
  const snapshotFreshness = assessTaskSnapshotFreshness(task, runtime, acquisition);

  return (
    <Card
      title="发布后现场证据"
      description="按任务配置、节点连接、有效采样和节点级上行分别核对。最近成功上行是历史节点级证据，不能证明本任务最新采样已送达或已关联到运行。"
      actions={<Button disabled={loading} onClick={() => void onRetry()}>{loading ? "正在刷新…" : "刷新现场证据"}</Button>}
    >
      {error && <Alert tone="warning">
        <p>部分现场证据读取失败：{error}{acquisition || edge ? " 以下内容来自最近一次成功响应。" : " 当前无法判断现场状态。"}</p>
      </Alert>}
      {reportIsStale && <Alert tone="warning">节点状态报告已超过 30 秒，下面的运行与采样信息可能不是当前状态；请刷新或查看节点诊断。</Alert>}
      {acquisition && !reportTimeIsVerifiable && <Alert tone="warning">Platform 接收时间或观测时间缺失/无效，无法确认节点状态报告是否过期；采样时效显示为未知。</Alert>}
      {!acquisition && loading && <p className="py-2 text-sm text-slate-500" role="status">正在读取节点任务状态…</p>}
      {!acquisition && !loading && !error && <p className="py-2 text-sm text-slate-500">尚未收到该节点的采集状态报告。</p>}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
          <p className="text-xs font-medium text-slate-500">节点连接</p>
          <div className="mt-2"><StatusBadge value={connectionState} /></div>
          <p className="mt-2 text-xs text-slate-600">最后心跳：{formatTime(edge?.lastSeen)}</p>
        </div>
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
          <p className="text-xs font-medium text-slate-500">当前配置版本</p>
          <div className="mt-2"><StatusBadge value={applicationState} label={applicationLabel} /></div>
          <p className="mt-2 text-xs text-slate-600">应用时间：{formatTime(deployment?.appliedAt)}</p>
          {deployment?.lastError && <p className="mt-1 break-words text-xs text-rose-700">{deployment.lastError}</p>}
        </div>
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
          <p className="text-xs font-medium text-slate-500">任务采集与有效采样</p>
          <div className="mt-2"><StatusBadge value={runtime?.state || "unknown"} /></div>
          <p className="mt-2 text-xs text-slate-600">最近读取成功：{formatTime(runtime?.lastReadSuccessAt)}</p>
          <p className="mt-1 text-xs text-slate-600">最近有效采样：{formatTime(runtime?.lastValidSnapshotAt)}</p>
          <div className="mt-2"><Badge tone={snapshotFreshness.tone}>{snapshotFreshness.label}</Badge></div>
          <p className="mt-1 text-xs text-slate-600">{snapshotFreshness.detail}</p>
          {runtime?.lastError && <p className="mt-1 break-words text-xs text-rose-700">{runtime.lastError}</p>}
        </div>
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
          <p className="text-xs font-medium text-slate-500">节点数据上行</p>
          <div className="mt-2"><StatusBadge value={deliveryBadgeState} label={deliveryLabel} /></div>
          <p className="mt-2 text-xs text-slate-600">{backlogDetail}</p>
          <p className="mt-1 text-xs text-slate-600">最近成功上行：{formatTime(delivery?.lastSuccessfulShipmentAt)}</p>
          {oldestPendingAgeMs != null && backlogCount > 0 && <p className="mt-1 text-xs text-slate-600">最老待补传已等待 {formatDuration(oldestPendingAgeMs)}</p>}
          {(backlogCapacityPercent != null || estimatedDrainMs != null) && <p className="mt-1 text-xs text-slate-600">
            {backlogCapacityPercent != null ? `队列容量使用 ${backlogCapacityPercent.toFixed(1)}%` : "队列容量未知"}
            {estimatedDrainMs != null ? ` · 按近期速率预计 ${formatDuration(estimatedDrainMs)} 排空` : ""}
          </p>}
          {delivery?.lastFailureAt && <p className="mt-1 text-xs text-amber-800">最近一次上行失败：{formatTime(delivery.lastFailureAt)} · 当前连续失败 {formatInteger(delivery.consecutiveFailures || 0)} 次</p>}
          {delivery?.lastError && <p className="mt-1 break-words text-xs text-rose-700">{delivery.lastError}</p>}
        </div>
      </div>
      <p className="mt-3 text-xs text-slate-500">
        最近节点状态报告：{formatTime(reportObservedAt)} · Platform 接收：{formatTime(acquisition?.platformReceivedAt)}。在线只表示心跳有效；采样时效还需结合任务运行状态和配置的静默时限判断。
      </p>
      <p className="mt-2 text-xs leading-5 text-slate-600">
        采样超时、过程数据不完整或运行边界缺失可能影响运行完整性和分析准入。数据质量页按 Edge、对象和运行时间列出相关运行，并显示已保存样本报告的任务版本、观测区间和值质量。
        来源核对只确认样本报告的版本与当前站点及 Edge 记录相符，不证明该版本在采样时已生效或造成基线缺口；没有样本来源引用时，原因仍未知。工艺分析和配方建议仍需按各自证据与准入结果复核。
      </p>
      <nav className="mt-3 flex flex-wrap gap-x-4 gap-y-2 border-t border-slate-100 pt-3 text-sm" aria-label="发布后核验入口">
        {edgeDiagnosticHref && <Link className="font-medium text-blue-700 hover:text-blue-800" to={edgeDiagnosticHref}>打开完整节点诊断</Link>}
        <Link className="font-medium text-blue-700 hover:text-blue-800" to={dataQualityHref}>复核相关运行与基线缺口</Link>
        <Link className="font-medium text-blue-700 hover:text-blue-800" to={processExecutionsHref}>核对相关运行记录</Link>
      </nav>
    </Card>
  );
}

function assessTaskSnapshotFreshness(task, runtime, acquisition) {
  const thresholdMs = Number(task?.execution?.maximumValidSnapshotSilenceMs ?? 0);
  const reportedMs = acquisition?.reportedAt ? new Date(acquisition.reportedAt).getTime() : null;
  const receivedMs = acquisition?.platformReceivedAt ? new Date(acquisition.platformReceivedAt).getTime() : null;
  const nowMs = acquisition?.platformObservedAt ? new Date(acquisition.platformObservedAt).getTime() : null;
  const snapshotMs = runtime?.lastValidSnapshotAt ? new Date(runtime.lastValidSnapshotAt).getTime() : null;
  const durationText = thresholdMs > 0 ? formatDuration(thresholdMs) : "未配置时限";
  if (!Number.isInteger(thresholdMs) || thresholdMs < 0 || thresholdMs > 86400000)
    return { label: "时效未知", tone: "warning", detail: "任务配置的有效快照静默时限无效。" };
  if (thresholdMs === 0)
    return { label: "未监控", tone: "neutral", detail: "未配置静默时限；这里只显示最近一次有效采样时间。" };
  if (snapshotMs == null)
    return { label: "尚无有效采样", tone: "warning", detail: `没有有效采样记录 · 监控时限 ${durationText}` };
  const ageAtReportMs = reportedMs - snapshotMs;
  const timeSinceStatusMs = nowMs - receivedMs;
  if (![snapshotMs, reportedMs, receivedMs, nowMs, ageAtReportMs, timeSinceStatusMs].every(Number.isFinite) ||
      ageAtReportMs < 0 || timeSinceStatusMs < 0)
    return { label: "时效未知", tone: "warning", detail: `采样时间或节点状态报告时间无法核实 · 监控时限 ${durationText}` };
  if (ageAtReportMs > thresholdMs)
    return { label: "超时", tone: "danger", detail: `节点状态报告已确认采样静默 ${formatDuration(ageAtReportMs)} · 时限 ${durationText}` };
  if (timeSinceStatusMs > 30000)
    return { label: "未知", tone: "warning", detail: `最近状态报告超过 30 秒，未确认当前是否超时 · 等待新报告核实 · 时限 ${durationText}` };
  const estimatedAgeMs = ageAtReportMs + timeSinceStatusMs;
  if (estimatedAgeMs > thresholdMs)
    return { label: "待核实", tone: "warning", detail: `按最后报告估算已静默 ${formatDuration(estimatedAgeMs)} · 等待下次报告确认 · 时限 ${durationText}` };
  return { label: "未超时", tone: "success", detail: `按最近节点报告估算静默 ${formatDuration(estimatedAgeMs)} · 时限 ${durationText}` };
}

function AcquisitionSetupGuide({ steps, activeStep, onSelectStep, selectedEdgeId, published, currentDeviceLabel, requiredCount, mappedRequiredCount, edgeDiagnosticHref, dataQualityHref, processExecutionsHref }) {
  const readyToPublish = steps.filter(step => !["lifecycle", "publish", "observe"].includes(step.id)).every(step => step.done);
  const runIdentificationConfigured = steps.find(step => step.id === "lifecycle")?.done;
  const phases = [
    { id: "device", label: "选择节点与对象", stepIds: ["device"] },
    { id: "connection", label: "连接并探查", stepIds: ["connection", "discovery"] },
    { id: "mappings", label: "映射变量并校验", stepIds: ["mappings", "probe-validation"] },
    { id: "lifecycle", label: "设置运行边界", stepIds: ["lifecycle"] },
    { id: "publish", label: "发布并观察", stepIds: ["publish", "observe"] },
  ].map(phase => ({ ...phase, steps: phase.stepIds.map(id => steps.find(step => step.id === id)).filter(Boolean) }));
  return (
    <section className="rounded-xl border border-slate-200 bg-white p-4" aria-label="采集接入步骤">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold text-slate-900">{published ? "采集配置已发布" : currentDeviceLabel ? `当前设备：${currentDeviceLabel}` : "按步骤完成设备接入"}</h2>
          <p className="mt-1 text-xs leading-5 text-slate-600">
            {published
              ? "到现场节点确认配置已应用，再检查最近有效快照和运行记录。"
              : "逐步配置并验证；点击步骤切换编辑区，高亮项表示当前最需要完成的一步。发布后仍需到现场节点确认采样、上行与运行关联。"}
          </p>
        </div>
        {published && selectedEdgeId && <Link data-acq-edges-link="true" className="text-sm font-medium text-blue-700 hover:text-blue-800" to={edgeDiagnosticHref}>查看节点采集与上行状态</Link>}
      </div>
      <ol className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-5">
        {phases.map((phase, index) => {
          const phaseSteps = phase.steps;
          const isActive = phaseSteps.some(step => step.id === activeStep);
          const phaseDone = phaseSteps.length > 0 && phaseSteps.every(step => step.done);
          const phaseRecommended = phaseSteps.some(step => step.recommended && !step.done);
          const nextPhase = phases.slice(index + 1).find(candidate =>
            candidate.steps.some(step => !step.done && !step.recommended));
          const selectedStep = phaseSteps.find(step => !step.done && !step.recommended)
            || phaseSteps.find(step => !step.done)
            || phaseSteps[0];
          const statusStep = phaseSteps.find(step => !step.done && !step.recommended)
            || phaseSteps.find(step => !step.done)
            || phaseSteps[phaseSteps.length - 1];
          const stateLabel = statusStep?.state === "needs-attention" ? "需要处理"
            : statusStep?.state === "evidence-present" ? "有现场证据，最新采样与运行关联待核验"
              : statusStep?.state === "unknown" ? "等待核验" : "";
          const statusText = `${phaseRecommended && !phaseDone ? "建议：" : ""}${stateLabel ? `${stateLabel} · ` : ""}${statusStep?.detail || (phaseDone ? "已完成" : "待完成")}`;
          const stateToneClass = statusStep?.state === "needs-attention" ? "border-amber-300 bg-amber-50"
            : statusStep?.state === "evidence-present" ? "border-blue-200 bg-blue-50/70" : "";
          const nextText = isActive && !phaseDone && nextPhase ? ` · 然后${nextPhase.label}` : "";
          return <li key={phase.id}>
            <button type="button" onClick={() => onSelectStep(selectedStep)} aria-current={isActive ? "step" : undefined}
              className={`flex h-full w-full items-start gap-2 rounded-lg border p-3 text-left ${isActive ? "border-blue-300 bg-blue-50" : phaseDone ? "border-emerald-200 bg-emerald-50/50" : stateToneClass || "border-slate-200 bg-slate-50"}`}>
              <span className={`grid size-6 shrink-0 place-items-center rounded-full text-xs font-semibold ${phaseDone ? "bg-emerald-100 text-emerald-800" : isActive ? "bg-blue-100 text-blue-800" : "bg-slate-200 text-slate-600"}`}>{phaseDone ? "✓" : phaseRecommended ? "!" : index + 1}</span>
              <span><strong className="block text-xs text-slate-800">{phase.label}</strong><small className="mt-1 block text-xs leading-4 text-slate-500">{statusText}{nextText}</small></span>
            </button>
          </li>;
        })}
      </ol>
      <div className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-3 text-xs text-slate-600">
        <span>{steps.find(step => step.id === "mappings")?.detail
          || (requiredCount > 0 ? `运行必需变量 ${mappedRequiredCount}/${requiredCount} 项已配置` : "请选择包含采集变量的已发布工艺模型")}</span>
        {published
          ? <nav className="flex flex-wrap gap-3" aria-label="发布后验证入口">
            <Link className="font-medium text-blue-700 hover:text-blue-800" to={dataQualityHref}>复核相关运行与基线缺口</Link>
            <Link className="font-medium text-blue-700 hover:text-blue-800" to={processExecutionsHref}>查看本站点运行记录</Link>
          </nav>
          : readyToPublish && <span className="font-medium text-emerald-700">必需配置与探查验证已完成，可发布{runIdentificationConfigured ? "" : "；建议先核对自动识别或人工关联运行的方式"}</span>}
      </div>
    </section>
  );
}

function ContextPanel({ descriptor, form, errors, readOnly, onChange }) {
  const rows = form.contextMappings;
  const supportsDynamicContexts = descriptor.id !== "ethernet-ip";
  const update = (index, patch) =>
    onChange({ contextMappings: rows.map((item, rowIndex) => rowIndex === index ? { ...item, ...patch } : item) });
  return (
    <Card
      title="运行上下文"
      description={supportsDynamicContexts
        ? "从设备读取生产状态、产品、批次等上下文。阶段号由工艺模型中用途为「阶段号」的变量自动识别。"
        : "EtherNet/IP 当前只接受带明确数据类型的采集点和时间标签；固定上下文仍可使用。"}
      actions={!readOnly && supportsDynamicContexts ? (
        <Button onClick={() => onChange({ contextMappings: [...rows, { contextKey: "", sourcePath: "", required: false, topic: "" }] })}>
          添加上下文
        </Button>
      ) : undefined}
    >
      <div className="grid gap-3">
        {rows.length === 0 && <p className="text-sm text-slate-500">没有动态生产上下文。</p>}
        {rows.map((item, index) => (
          <div key={index} className="grid gap-2 md:grid-cols-[1fr_1fr_auto_auto]">
            <Field label={index === 0 ? "上下文键" : undefined} error={errors[`contextMappings[${index}].contextKey`]}>
              <Input value={item.contextKey} disabled={readOnly} placeholder="product_family_code"
                onChange={event => update(index, { contextKey: event.target.value })} />
            </Field>
            <Field
              label={index === 0 ? "设备来源" : undefined}
              hint={index === 0 ? descriptorSourceHint(descriptor) : undefined}
              error={errors[`contextMappings[${index}].sourcePath`]}
            >
              <Input value={item.sourcePath} disabled={readOnly} placeholder={descriptorSourcePlaceholder(descriptor)}
                onChange={event => update(index, { sourcePath: event.target.value })} />
            </Field>
            <label className="flex items-center gap-1.5 self-end pb-2 text-sm">
              <input type="checkbox" checked={item.required} disabled={readOnly}
                onChange={event => update(index, { required: event.target.checked })} />必需
            </label>
            {!readOnly && (
              <Button variant="ghost" className="self-end text-rose-700"
                onClick={() => onChange({ contextMappings: rows.filter((_row, rowIndex) => rowIndex !== index) })}>
                移除
              </Button>
            )}
          </div>
        ))}
      </div>
    </Card>
  );
}

function descriptorSourceHint(descriptor) {
  switch (descriptor.addressing) {
    case "modbus-register": return "使用寄存器选择器，例如 holding-register:120:uint16。";
    case "melsec-device": return "使用软元件选择器，例如 D:120:uint16 或 M:30:boolean。";
    case "siemens-s7-address": return "使用 S7 绝对地址，例如 DB1.DBL0（Unix 毫秒）或 DB1.DBD0（Unix 秒）。";
    case "ethernet-ip-tag": return "使用控制器符号标签，例如 Program:MainProgram.ProductCode 或 Motor.Speed。";
    case "bacnet-object": return "从已指定 Device Instance 的对象目录选择 object-type:instance，例如 analog-input:1。";
    case "omron-fins-address": return "填写已配置内存区地址，例如 DM:120:int16、CIO:12.3:boolean；不支持地址扫描。";
    case "melsec-mc-3e-device": return "填写二进制 MC 3E 软元件选择器并带类型，例如 D:120:int16、D:120.3:boolean、X:1A:boolean。X/Y/B/W 按十六进制，M/L/D 按十进制。";
    case "snmp-oid": return "填写数字 OID，例如 1.3.6.1.2.1.1.5.0；可带前导点，不支持 MIB 符号名称。";
    case "mtconnect-data-item": return "使用设备 /probe 返回的 MTConnect DataItem ID，例如 xact 或 avail。";
    case "node-id": return "使用 OPC UA 节点编号。";
    default: return "使用报文中的 JSON 字段路径。";
  }
}

function descriptorSourcePlaceholder(descriptor) {
  switch (descriptor.addressing) {
    case "modbus-register": return "holding-register:120:uint16";
    case "melsec-device": return "D:120:uint16";
    case "siemens-s7-address": return "DB1.DBD0";
    case "ethernet-ip-tag": return "Program:MainProgram.ProductCode";
    case "bacnet-object": return "analog-input:1";
    case "omron-fins-address": return "DM:120:int16";
    case "melsec-mc-3e-device": return "D:120:int16";
    case "snmp-oid": return "1.3.6.1.2.1.1.5.0";
    case "mtconnect-data-item": return "xact";
    case "node-id": return "ns=2;s=Machine.Product";
    default: return "productFamilyCode";
  }
}

function ProcessSpecificationPanel({ descriptor, form, parameters, errors, probe, readOnly, onChange }) {
  const processSpecification = form.processSpecification;
  const supportsProcessSpecification = !["ethernet-ip", "mtconnect", "bacnet-ip", "bacnet-mstp"].includes(descriptor.id);
  const update = patch => onChange({ processSpecification: { ...processSpecification, ...patch } });
  return (
    <Card
      title="设备配方版本识别"
      description={supportsProcessSpecification
        ? "从设备读取当前生效的配方版本标识，让每次运行都能关联到实际配方版本。"
        : descriptor.id === "mtconnect"
          ? "MTConnect 首期不把多个 DataItem 组合成设备配方参数对象；已有配置可关闭后移除。"
          : descriptor.id.startsWith("bacnet-")
            ? "BACnet 首期只采集标量 present-value；已有配方版本映射可关闭后移除。"
            : "EtherNet/IP 当前不支持未声明数据类型的配方版本辅助标签；已有配置可关闭后移除。"}
      actions={
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={processSpecification.enabled} disabled={readOnly || (!supportsProcessSpecification && !processSpecification.enabled)}
            onChange={event => update({ enabled: event.target.checked })} />启用
        </label>
      }
    >
      {!processSpecification.enabled ? (
        <p className="text-sm text-slate-500">当前采集任务不从设备数据识别配方版本。</p>
      ) : (
        <div className="grid gap-4">
          {!supportsProcessSpecification && (
            <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
              {descriptor.id === "mtconnect"
                ? "MTConnect 首期不支持把多个 DataItem 组合成设备配方参数对象；关闭上方开关后即可继续。"
                : "EtherNet/IP 当前不支持未声明数据类型的配方版本辅助标签；关闭上方开关后即可继续。"}
            </p>
          )}
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="配方版本编号来源" error={errors["processSpecification.idPath"]}>
              <Input value={processSpecification.idPath} disabled={readOnly || !supportsProcessSpecification} placeholder={descriptorSourcePlaceholder(descriptor)}
                onChange={event => update({ idPath: event.target.value })} />
            </Field>
            <Field label="配方版本号来源" error={errors["processSpecification.versionPath"]}>
              <Input value={processSpecification.versionPath} disabled={readOnly || !supportsProcessSpecification}
                onChange={event => update({ versionPath: event.target.value })} />
            </Field>
            <Field label="配方版本名称来源（可选）">
              <Input value={processSpecification.namePath} disabled={readOnly || !supportsProcessSpecification}
                onChange={event => update({ namePath: event.target.value })} />
            </Field>
            {descriptor.capabilities.parameterObjectPath && (
              <Field label="参数集合路径" hint="参数映射的路径相对于它；「.」表示报文根。">
                <Input value={processSpecification.parametersPath} disabled={readOnly}
                  onChange={event => update({ parametersPath: event.target.value })} />
              </Field>
            )}
          </div>
          <PointMappingPanel
            title="控制参数映射"
            descriptor={descriptor}
            rows={processSpecification.parameterMappings}
            options={parameters}
            errors={errors}
            errorPrefix="processSpecification.parameterMappings"
            probe={probe}
            form={form}
            readOnly={readOnly || !supportsProcessSpecification}
            onChange={value => update({ parameterMappings: value })}
          />
        </div>
      )}
    </Card>
  );
}

function LifecyclePanel({ form, errors, readOnly, onChange }) {
  const lifecycle = form.lifecycle;
  const update = patch => onChange({ lifecycle: { ...lifecycle, ...patch } });
  return (
    <Card
      title="过程执行边界识别"
      description="设备只需提供生产状态；现场节点在生产开始时生成过程执行关联号，结束时关闭过程执行。"
      actions={
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={lifecycle.enabled} disabled={readOnly}
            onChange={event => update({ enabled: event.target.checked })} />启用
        </label>
      }
    >
      {!lifecycle.enabled ? (
        <p className="text-sm text-slate-500">适用于连续设备，或不需要自动识别过程执行边界的场景。</p>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="生产状态上下文键" hint="填写上面配置过的上下文键。" error={errors["lifecycle.activeContextKey"]}>
            <Input value={lifecycle.activeContextKey || ""} disabled={readOnly}
              onChange={event => update({ activeContextKey: event.target.value })} />
          </Field>
          <Field label="运行激活条件" hint="选择如何根据生产状态判断运行开始和结束。" error={errors["lifecycle.activeCondition"]}>
            <Select value={lifecycle.activeCondition ?? "equals"} disabled={readOnly}
              onChange={event => update({ activeCondition: event.target.value })}>
              <option value="equals">等于指定值</option>
              <option value="nonzero">非零生产序号</option>
            </Select>
          </Field>
          {(lifecycle.activeCondition ?? "equals") === "equals" ? (
            <Field label="生产中的取值" hint="该上下文等于此值时视为生产中。" error={errors["lifecycle.activeValue"]}>
              <Input value={lifecycle.activeValue || ""} disabled={readOnly}
                onChange={event => update({ activeValue: event.target.value })} />
            </Field>
          ) : lifecycle.activeCondition === "nonzero" && (
            <p className="text-sm text-slate-600">生产状态上下文为非零数值时视为生产中，为零时结束运行。</p>
          )}
        </div>
      )}
    </Card>
  );
}

function StrategyPanel({ descriptor, form, errors, readOnly, onChange }) {
  const capabilities = descriptor.capabilities;
  const hidden = [
    !capabilities.connectTimeout && "连接超时",
    !capabilities.sequencePath && "序号字段",
    !capabilities.sourceTimestamp && "设备时间",
  ].filter(Boolean);
  return (
    <Card title="采集策略" description="只列出当前驱动真正生效的参数。">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {capabilities.connectTimeout && (
          <Field label="连接超时（ms）" error={errors["execution.timeoutMs"]}>
            <Input type="number" min="1000" max="300000" value={form.execution.timeoutMs} disabled={readOnly}
              onChange={event => onChange({ execution: { ...form.execution, timeoutMs: event.target.value } })} />
          </Field>
        )}
        {capabilities.reconnectDelay && (
          <Field label="重连间隔（ms）" error={errors["execution.reconnectDelayMs"]}>
            <Input type="number" min="100" value={form.execution.reconnectDelayMs} disabled={readOnly}
              onChange={event => onChange({ execution: { ...form.execution, reconnectDelayMs: event.target.value } })} />
          </Field>
        )}
        <Field label="源序号/时间戳停滞阈值（ms）" hint="0 表示关闭；配置了设备序号或设备时间时用于识别设备侧卡死。" error={errors["execution.sourceIdentityStaleAfterMs"]}>
          <Input type="number" min="0" max="86400000" value={form.execution.sourceIdentityStaleAfterMs} disabled={readOnly}
            onChange={event => onChange({ execution: { ...form.execution, sourceIdentityStaleAfterMs: event.target.value } })} />
        </Field>
        <Field label="有效快照静默时限（ms）" hint="0 表示不监控；超过该时限且节点状态报告已确认时，诊断标为超时。小于采样或状态上报周期的时限，在新报告确认前会显示待核实。此设置只影响状态提示，不会拒绝样本或停止采集。" error={errors["execution.maximumValidSnapshotSilenceMs"]}>
          <Input type="number" min="0" max="86400000" step="1" value={form.execution.maximumValidSnapshotSilenceMs} disabled={readOnly}
            onChange={event => onChange({ execution: { ...form.execution, maximumValidSnapshotSilenceMs: event.target.value } })} />
        </Field>
        {(capabilities.intrinsicSourceTimestamp ||
          (capabilities.sourceTimestamp && form.timestampMode === "source")) && (
          <Field label="设备时间戳最大超前量（ms）" hint="0 表示关闭；超过此值拒绝样本，避免错误设备时钟污染事件时间线。" error={errors["execution.maximumFutureTimestampSkewMs"]}>
            <Input type="number" min="0" max="86400000" value={form.execution.maximumFutureTimestampSkewMs} disabled={readOnly}
              onChange={event => onChange({ execution: { ...form.execution, maximumFutureTimestampSkewMs: event.target.value } })} />
          </Field>
        )}
        <Field label="采样时间来源">
          {capabilities.intrinsicSourceTimestamp ? (
            <Input value="协议服务器提供的源时间" disabled />
          ) : capabilities.sourceTimestamp ? (
            <Select value={form.timestampMode} disabled={readOnly}
              onChange={event => onChange({ timestampMode: event.target.value })}>
              <option value="source">使用设备时间</option>
              <option value="edge-received">使用现场节点接收时间</option>
            </Select>
          ) : (
            <Input value={descriptor.id === "opc-ua" ? "OPC UA 服务器 SourceTimestamp" : "现场节点接收时间"} disabled />
          )}
        </Field>
        {capabilities.sourceTimestamp && !capabilities.intrinsicSourceTimestamp && form.timestampMode === "source" && (
          <>
            <Field label="时间来源" hint={descriptorSourceHint(descriptor)} error={errors.timestampPath}>
              <Input value={form.timestampPath} disabled={readOnly}
                placeholder={descriptorSourcePlaceholder(descriptor)}
                onChange={event => {
                  const timestampPath = descriptor.id === "snmp"
                    ? normalizeSnmpOid(event.target.value) || event.target.value
                    : event.target.value;
                  onChange({ timestampPath });
                }} />
            </Field>
            <Field label="时间戳编码" error={errors.timestampEncoding}>
              <Select value={form.timestampEncoding} disabled={readOnly}
                onChange={event => onChange({ timestampEncoding: event.target.value })}>
                <option value="auto">按协议自动选择</option>
                {!(["siemens-s7", "ethernet-ip"].includes(descriptor.id)) && <option value="iso-8601">ISO 8601</option>}
                <option value="unix-s">Unix 秒</option>
                <option value="unix-ms">Unix 毫秒</option>
              </Select>
            </Field>
          </>
        )}
        {capabilities.sequencePath && (
          <Field label={descriptor.id === "snmp" ? "序号 OID（可选）" : "序号字段（可选）"}
            hint={descriptor.id === "snmp" ? "填写数字 OID，例如 1.3.6.1.2.1.1.5.0；可带前导点，保存时规范化。" : undefined}
            error={errors.sequencePath}>
            <Input value={form.sequencePath} disabled={readOnly}
              placeholder={descriptor.id === "snmp" ? "1.3.6.1.2.1.1.5.0" : "没有可留空"}
              onChange={event => {
                const sequencePath = descriptor.id === "snmp"
                  ? normalizeSnmpOid(event.target.value) || event.target.value
                  : event.target.value;
                onChange({ sequencePath });
              }} />
          </Field>
        )}
        <Field label="采样事件类型" error={errors.sampleEventType}>
          <Input value={form.sampleEventType} disabled={readOnly}
            onChange={event => onChange({ sampleEventType: event.target.value })} />
        </Field>
      </div>
      {hidden.length > 0 && (
        <p className="mt-3 text-xs text-slate-500">
          {descriptor.label} 不使用：{hidden.join("、")}。填写这些参数不会生效，因此不再显示。
        </p>
      )}
    </Card>
  );
}

export function IngestionTasksPage({ canWrite = true }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const { data, error, loading, reload } = useApi(ENDPOINT);
  const { data: templateData, error: templateError, reload: reloadTemplates } = useApi("/api/v1/ingestion-configuration/templates");
  const { data: sourceData, error: sourceError, reload: reloadSources } = useApi("/api/v1/ingestion-configuration/data-sources");
  const { data: bindingData, error: bindingError, reload: reloadBindings } = useApi("/api/v1/ingestion-configuration/bindings");
  const { data: capabilityData, error: capabilityError } = useApi("/api/v1/acquisition-protocols");
  const rows = extractRows(data);
  const templates = extractRows(templateData);
  const sources = extractRows(sourceData);
  const bindings = extractRows(bindingData);
  const capabilities = capabilityData?.protocols || [];
  const requestedEdgeId = searchParams.get("edgeId")?.trim() || "";
  const visibleRows = useMemo(
    () => requestedEdgeId ? rows.filter(row => row.edgeId === requestedEdgeId) : rows,
    [requestedEdgeId, rows],
  );
  const navigate = useNavigate();
  const { confirm, confirmationDialog } = useConfirmDialog();

  function clearEdgeFilter() {
    const next = new URLSearchParams(searchParams);
    next.delete("edgeId");
    setSearchParams(next, { replace: true });
  }

  async function removeReusableDraft(kind, row) {
    const isTemplate = kind === "template";
    const code = isTemplate ? row.templateId : row.dataSourceId;
    if (!await confirm({
      title: `删除${isTemplate ? "任务模板" : "数据源"}草稿 ${code} v${row.version}`,
      description: "只有草稿可以删除；已发布版本和仍受保护的数据不会被移除。",
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    try {
      await deleteJson(`/api/v1/ingestion-configuration/${isTemplate ? "templates" : "data-sources"}/${encodeURIComponent(code)}/${row.version}`);
      await Promise.all([reloadTemplates?.(), reloadSources?.(), reloadBindings?.()]);
      notify(`${isTemplate ? "任务模板" : "数据源"}草稿已删除。`);
    } catch (requestError) {
      notify(requestError.message || "草稿删除失败。", "danger");
    }
  }

  async function retire(row) {
    if (!await confirm({
      title: `停用 ${row.name || row.taskId}`,
      description: "现场节点会在下一次同步时停止该采集任务；历史版本和已采集数据仍会保留。",
      confirmLabel: "确认停用",
      tone: "danger",
    })) return;
    try {
      await postJson(ENDPOINT, { ...row, status: "retired" });
      notify("接入配置已停用");
      reload?.();
    } catch (requestError) {
      notify(requestError.message);
    }
  }

  async function removeDraft(row) {
    if (!await confirm({
      title: `删除草稿 ${row.name || row.taskId} v${row.version}`,
      description: "草稿删除后无法恢复；已发布任务只能停用，历史采集数据不会被删除。",
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    try {
      await deleteJson(`${ENDPOINT}/${encodeURIComponent(row.taskId)}/${row.version}`);
      await reload?.();
      notify("采集配置草稿已删除。");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  return (
    <Page
      title="采集配置"
      actions={
        <div className="flex items-center gap-2">
          <Link className="inline-flex min-h-9 items-center rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
            to="/edges">查看现场节点</Link>
          {canWrite && <LinkButton
            to="/configuration/ingestion-tasks/new">新建采集配置</LinkButton>}
        </div>
      }
    >
      <div className="grid gap-4">
        {error && <Alert tone="danger" title="采集配置读取失败">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span>{error}</span>
            <Button onClick={() => reload?.()}>重试</Button>
          </div>
        </Alert>}
        {templateError && <Alert tone="danger">任务模板读取失败：{templateError}</Alert>}
        {sourceError && <Alert tone="danger">数据源读取失败：{sourceError}</Alert>}
        {bindingError && <Alert tone="danger">任务绑定读取失败：{bindingError}</Alert>}
        {capabilityError && <Alert tone="warning">驱动能力读取失败，暂无法确认连接复用方式：{capabilityError}</Alert>}
        {requestedEdgeId && <Alert tone="info" title={`已筛选现场节点 ${requestedEdgeId}`}>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span>下方采集配置仅显示此节点可见的任务。</span>
            <Button onClick={clearEdgeFilter}>显示全部节点</Button>
          </div>
        </Alert>}
        {canWrite ? (
          <details className="group rounded-lg border border-slate-200 bg-white">
            <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4 marker:content-none">
              <div>
                <p className="font-semibold text-slate-950">复用数据源与采集模板</p>
                <p className="mt-1 text-sm text-slate-600">首条任务发布后可提取模板和数据源，为不同采集对象建立绑定；已有 {templates.filter(item => item.status === "published").length} 个模板。</p>
              </div>
              <span className="text-sm font-medium text-blue-700 group-open:hidden">展开配置</span>
              <span className="hidden text-sm font-medium text-blue-700 group-open:inline">收起</span>
            </summary>
            <div className="border-t border-slate-200 p-5">
              <ReusableConfigurationPanel
                embedded
                tasks={rows}
                templates={templates}
                sources={sources}
                bindings={bindings}
                capabilities={capabilities}
                onDeleteDraft={removeReusableDraft}
                onChanged={() => Promise.all([reload?.(), reloadTemplates?.(), reloadSources?.(), reloadBindings?.()])}
              />
            </div>
          </details>
        ) : (
          <Card title="可复用接入资产" description="用于核对批量接入是否已经形成版本化模板、数据源与绑定。">
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="rounded-lg border border-slate-200 p-3"><p className="text-xs text-slate-500">已发布模板</p><strong className="text-xl">{templates.filter(item => item.status === "published").length}</strong></div>
              <div className="rounded-lg border border-slate-200 p-3"><p className="text-xs text-slate-500">已发布数据源</p><strong className="text-xl">{sources.filter(item => item.status === "published").length}</strong></div>
              <div className="rounded-lg border border-slate-200 p-3"><p className="text-xs text-slate-500">任务绑定</p><strong className="text-xl">{bindings.length}</strong></div>
            </div>
            <ReusableBindingPanel tasks={rows} templates={templates} sources={sources} bindings={bindings} capabilities={capabilities} canWrite={false} />
          </Card>
        )}
        <div id="task-configurations" className="scroll-mt-5">
        <Card>
          <div className="overflow-auto">
            <table className="w-full min-w-[52rem] text-left text-sm">
              <thead className="text-slate-600">
                <tr>
                  <th className="px-3 py-2">设备</th>
                  <th className="px-3 py-2">现场节点</th>
                  <th className="px-3 py-2">配置名称</th>
                  <th className="px-3 py-2">通信驱动</th>
                  <th className="px-3 py-2">点位</th>
                  <th className="px-3 py-2">状态</th>
                  <th className="px-3 py-2">操作</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {visibleRows.map(row => (
                  <tr key={`${row.taskId}@${row.version}`} className="hover:bg-slate-50">
                    <td className="px-3 py-2 font-medium text-slate-800">{row.subjectId || row.equipmentId || "未绑定"}</td>
                    <td className="px-3 py-2 text-slate-600">{row.edgeId}</td>
                    <td className="px-3 py-2 text-slate-600">{row.name}<span className="ml-1 text-xs text-slate-400">v{row.version}</span></td>
                    <td className="px-3 py-2"><Badge tone="neutral">{protocolDescriptor(row.protocol).label}</Badge></td>
                    <td className="px-3 py-2 text-slate-600">{row.valueMappings?.length || 0}</td>
                    <td className="px-3 py-2"><StatusBadge value={row.status} /></td>
                    <td className="px-3 py-2">
                      <div className="flex min-w-max gap-1">
                        <Button variant="ghost"
                          onClick={() => navigate(`/configuration/ingestion-tasks/${encodeURIComponent(row.taskId)}?version=${row.version}`)}>
                          查看
                        </Button>
                        {canWrite && !row.templateId && (
                          <Button variant="ghost"
                            onClick={() => navigate(`/configuration/ingestion-tasks/${encodeURIComponent(row.taskId)}?version=${row.version}&mode=version`)}>
                            新版本
                          </Button>
                        )}
                        {canWrite && row.status === "published" && (
                          <Button variant="ghost" className="text-rose-700" onClick={() => retire(row)}>停用</Button>
                        )}
                        {canWrite && row.status === "draft" && (
                          <Button variant="ghost" className="text-rose-700" onClick={() => removeDraft(row)}>删除草稿</Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {visibleRows.length === 0 && (
                  <tr><td colSpan="7" className="px-3 py-8 text-center text-slate-500">
                    {loading && !data ? "正在读取采集配置…" : error && !data ? "采集配置读取失败，暂时无法确认此节点是否有任务。" : !data ? "尚无成功读取的配置，暂时无法判断是否存在任务。" : requestedEdgeId ? "该现场节点没有可见采集配置。" : "还没有采集配置。"}
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
        </div>
      </div>
      {confirmationDialog}
    </Page>
  );
}

function ReusableConfigurationPanel({ tasks, templates, sources, bindings, capabilities, onDeleteDraft, onChanged, embedded = false }) {
  const firstPublished = tasks.find(item => item.status === "published" && !item.templateId && !item.dataSourceId);
  const [taskKey, setTaskKey] = useState(firstPublished ? `${firstPublished.taskId}@${firstPublished.version}` : "");
  const selected = tasks.find(item => `${item.taskId}@${item.version}` === taskKey);
  const [templateId, setTemplateId] = useState("");
  const [dataSourceId, setDataSourceId] = useState("");
  const nextTemplateVersion = Math.max(0, ...templates.filter(item => item.templateId === templateId.trim().toLowerCase()).map(item => Number(item.version) || 0)) + 1;
  const nextDataSourceVersion = Math.max(0, ...sources.filter(item => item.dataSourceId === dataSourceId.trim().toLowerCase()).map(item => Number(item.version) || 0)) + 1;
  const [busy, setBusy] = useState("");
  const [message, setMessage] = useState("");
  const sourceFile = useRef(null);
  const bindingFile = useRef(null);
  const draftTemplates = templates.filter(item => item.status === "draft");
  const draftSources = sources.filter(item => item.status === "draft");

  useEffect(() => {
    if (!taskKey && firstPublished) setTaskKey(`${firstPublished.taskId}@${firstPublished.version}`);
  }, [firstPublished, taskKey]);

  async function extract() {
    if (!selected || !templateId.trim() || !dataSourceId.trim()) {
      setMessage("请选择已发布任务，并填写模板代码和首台数据源代码。");
      return;
    }
    setBusy("extract"); setMessage("");
    try {
      await postJson("/api/v1/ingestion-configuration/extract-reusable", {
        taskId: selected.taskId,
        version: selected.version,
        templateId,
        dataSourceId,
        templateVersion: nextTemplateVersion,
        dataSourceVersion: nextDataSourceVersion,
      });
      setMessage("已生成并发布任务模板、首台数据源和任务绑定。现在可导出 CSV，批量增加同类设备。");
      await onChanged();
    } catch (requestError) {
      setMessage(requestError.message);
    } finally { setBusy(""); }
  }

  async function importCsv(kind, file) {
    if (!file) return;
    setBusy(kind); setMessage("");
    try {
      const body = new FormData(); body.append("file", file);
      const endpoint = kind === "sources" ? "data-sources:import" : "bindings:import";
      const result = await postForm(`/api/v1/ingestion-configuration/${endpoint}`, body);
      setMessage(`已导入 ${result?.count ?? result?.data?.length ?? 0} 项；批量任务均为草稿，发布前仍需逐台真实探查。`);
      await onChanged();
    } catch (requestError) {
      setMessage(requestError.message);
    } finally { setBusy(""); }
  }

  const content = (
    <div className="grid gap-4">
        <div className="grid gap-3 md:grid-cols-3">
          <div className="rounded-lg border border-slate-200 p-3"><p className="text-xs text-slate-500">已发布模板</p><strong className="text-xl">{templates.filter(item => item.status === "published").length}</strong></div>
          <div className="rounded-lg border border-slate-200 p-3"><p className="text-xs text-slate-500">数据源实例</p><strong className="text-xl">{sources.length}</strong></div>
          <div className="rounded-lg border border-slate-200 p-3"><p className="text-xs text-slate-500">任务绑定</p><strong className="text-xl">{bindings.length}</strong></div>
        </div>
        <div className="grid gap-3 lg:grid-cols-[1.4fr_1fr_1fr_auto]">
          <Field label="已验证并发布的首台任务">
            <Select value={taskKey} onChange={event => setTaskKey(event.target.value)}>
              <option value="">请选择</option>
              {tasks.filter(item => item.status === "published" && !item.templateId && !item.dataSourceId).map(item => (
                <option key={`${item.taskId}@${item.version}`} value={`${item.taskId}@${item.version}`}>{item.name} · v{item.version}</option>
              ))}
            </Select>
          </Field>
          <Field label="任务模板代码" hint={`将创建 v${nextTemplateVersion}`}><Input value={templateId} placeholder="press-fx3u" onChange={event => setTemplateId(event.target.value)} /></Field>
          <Field label="首台数据源代码" hint={`将创建 v${nextDataSourceVersion}`}><Input value={dataSourceId} placeholder="press-01-source" onChange={event => setDataSourceId(event.target.value)} /></Field>
          <Button className="self-end" disabled={busy === "extract"} onClick={extract}>{busy === "extract" ? "提取中…" : "提取复用资产"}</Button>
        </div>
        <ReusableBindingPanel tasks={tasks} templates={templates} sources={sources} bindings={bindings} capabilities={capabilities} onChanged={onChanged} />
        <div className="flex flex-wrap gap-2 border-t border-slate-100 pt-4">
          <Button variant="ghost" onClick={() => downloadFile("/api/v1/ingestion-configuration/data-sources.csv", "data-sources.csv")}>导出数据源 CSV</Button>
          <Button variant="ghost" onClick={() => sourceFile.current?.click()} disabled={Boolean(busy)}>导入数据源 CSV</Button>
          <input ref={sourceFile} className="hidden" type="file" accept=".csv,text/csv" onChange={event => importCsv("sources", event.target.files?.[0])} />
          <Button variant="ghost" onClick={() => downloadFile("/api/v1/ingestion-configuration/bindings.csv", "ingestion-task-bindings.csv")}>导出任务绑定 CSV</Button>
          <Button variant="ghost" onClick={() => bindingFile.current?.click()} disabled={Boolean(busy)}>导入任务绑定 CSV</Button>
          <input ref={bindingFile} className="hidden" type="file" accept=".csv,text/csv" onChange={event => importCsv("bindings", event.target.files?.[0])} />
        </div>
        {(draftTemplates.length > 0 || draftSources.length > 0) && (
          <div className="grid gap-3 rounded-xl border border-amber-200 bg-amber-50/60 p-4">
            <div><p className="text-sm font-semibold text-slate-900">待完成或清理的复用草稿</p><p className="mt-1 text-xs text-slate-600">这里仅显示未发布模板和数据源；确认不再使用后可以直接删除。</p></div>
            {[...draftTemplates.map(item => ({ kind: "template", code: item.templateId, version: item.version, name: item.name })), ...draftSources.map(item => ({ kind: "source", code: item.dataSourceId, version: item.version, name: item.name }))].map(item => (
              <div key={`${item.kind}:${item.code}:${item.version}`} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-amber-200 bg-white px-3 py-2">
                <div><p className="text-sm font-medium text-slate-900">{item.name || item.code}</p><p className="font-mono text-xs text-slate-500">{item.kind === "template" ? "任务模板" : "数据源"} · {item.code} · v{item.version}</p></div>
                <Button variant="ghost" className="text-rose-700" onClick={() => onDeleteDraft(item.kind, item.kind === "template" ? draftTemplates.find(value => value.templateId === item.code && value.version === item.version) : draftSources.find(value => value.dataSourceId === item.code && value.version === item.version))}>删除草稿</Button>
              </div>
            ))}
          </div>
        )}
        {message && <Alert tone={message.includes("已") ? "info" : "warning"}>{message}</Alert>}
        <p className="text-xs text-slate-500">安全边界：凭据字段只填写现场节点密钥引用；固定请求体和普通请求头会随 CSV 导出，不要写入凭据。批量导入只生成草稿，每个任务必须单独连接真实设备并通过映射校验后才能发布。</p>
    </div>
  );

  if (embedded) return content;
  return (
    <Card title="复用数据源与采集模板" description="首条任务通过真实探查后提取版本化模板；数据源与任务绑定可用 CSV 原子导入。">
      {content}
    </Card>
  );
}
