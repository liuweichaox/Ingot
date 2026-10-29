// 编排采集任务创建、探查、发布和站点可见性反馈。
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
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
  patchFromProbePoint, toPayload, validateIngestionTask,
} from "./ingestionTaskForm";
import { ConnectionPanel } from "./panels/ConnectionPanel";
import { PointMappingPanel, blankRow } from "./panels/PointMappingPanel";
import { DevicePointsPanel } from "./panels/DevicePointsPanel";
import { ReusableBindingPanel } from "./ReusableBindingPanel";
import { IngestionDeviceGroupCanvas } from "./IngestionDeviceGroupCanvas";
import { deviceProfileOption, deviceProfileOptions } from "./deviceProfileRegistry";

const ENDPOINT = "/api/v1/ingestion-tasks";

function deviceGroupPayload(form) {
  const taskForm = { ...form, status: "draft" };
  const payload = toPayload(taskForm);
  if (!String(taskForm.source || "").trim())
    payload.source = `connector/${taskForm.protocol}/${taskForm.taskId.trim()}`;
  return payload;
}

export function IngestionTaskPage({ canWrite = true }) {
  const { taskId } = useParams();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const isNew = !taskId || taskId === "new";
  const requestedVersion = searchParams.get("version");
  const mode = searchParams.get("mode") || (isNew ? "create" : "maintain");
  const [groupActivated, setGroupActivated] = useState(false);
  const multiDeviceSetup = isNew && canWrite && groupActivated;
  const groupEnabled = isNew && canWrite && groupActivated;
  const [deviceGroupEntries, setDeviceGroupEntries] = useState([]);
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
  const [probing, setProbing] = useState(false);
  const probeRevision = useRef(0);
  const probeValidationRevision = useRef(0);
  const [validatedProbeRevision, setValidatedProbeRevision] = useState(null);
  const [showErrors, setShowErrors] = useState(false);

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
      };
    });
  }, [multiDeviceSetup, deviceGroupEntries, activeDeviceKey, form]);
  const canvasDevices = multiDeviceSetup
    ? deviceGroupCards
    : [{ key: "single-device", form, saved: false, error: "" }];

  useEffect(() => {
    let cancelled = false;
    getJson("/api/v1/acquisition-protocols")
      .then(result => { if (!cancelled) { mergeServerCapabilities(result?.protocols || result); setForm(value => ({ ...value })); } })
      .catch(() => {  });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (isNew) { setLoading(false); return; }
    let cancelled = false;
    setLoading(true);
    getJson(`${ENDPOINT}?taskId=${encodeURIComponent(taskId)}`)
      .then(result => {
        if (cancelled) return;
        const rows = extractRows(result);
        const match = requestedVersion
          ? rows.find(item => String(item.version) === String(requestedVersion))
          : rows.slice().sort((left, right) => right.version - left.version)[0];
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
        ? { ...item, form, error: "" }
        : item);
    });
  }, [form, multiDeviceSetup, activeDeviceKey]);

  const descriptor = protocolDescriptor(form.protocol);
  const selectedModel = parseModelValue(form.dataModel);
  const model = models.find(item => item.modelId === selectedModel.id && item.version === selectedModel.version);
  const dataItems = model?.acquisition?.dataItems || [];
  const controlParameters = model?.controlParameters || [];
  const managedByBinding = Boolean(form.templateId && form.dataSourceId);
  const readOnly = !canWrite || managedByBinding || (mode === "maintain" && form.status !== "draft") || (multiDeviceSetup && saving);
  const allowProbe = canWrite && form.status === "draft" && !saving;

  const validation = useMemo(
    () => validateIngestionTask(form, { dataItems }),
    [form, dataItems],
  );
  const errors = showErrors ? validation.errors : {};

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
  const publishChecklist = [
    { label: "选择现场节点与工艺变量", done: Boolean(form.edgeId && form.dataModel) },
    { label: "填写连接参数", done: !Object.keys(descriptor.validateConnection(form[descriptor.section] || {}) || {}).length },
    {
      label: "映射过程执行必需的工艺变量",
      done: dataItems.filter(item => !item.nullable)
        .every(item => form.valueMappings.some(row => row.dataItemCode === item.code)),
      detail: dataItems.length ? `必需 ${dataItems.filter(item => !item.nullable).length} 项，已映射 ${form.valueMappings.filter(item => item.dataItemCode).length} 项` : undefined,
    },
    { label: "验证连接并通过点位换算校验", done: probeValid },
  ];

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
    setProbing(false);
    setValidatedProbeRevision(null);
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
    if (!target || target.key === activeDeviceKey) return;
    setActiveDeviceKey(key);
    setForm(target.form);
    setShowErrors(false);
    setGroupSaveError("");
    resetActiveProbe();
  }

  function addDeviceGroupTask(kindOverride, edgeIdOverride) {
    const initialKey = multiDeviceSetup ? "" : nextDeviceGroupKey();
    const initialForm = multiDeviceSetup ? null : {
      ...form,
      taskId: form.taskId || nextDeviceTaskId(1, []),
      name: form.name || "采集设备 1",
    };
    const initialEntries = multiDeviceSetup
      ? deviceGroupCards.map(({ key, form: entryForm, savedFingerprint, error }) => ({ key, form: entryForm, savedFingerprint, error }))
      : [{ key: initialKey, form: initialForm, savedFingerprint: "", error: "" }];
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
      { key, form: nextForm, savedFingerprint: "", error: "" },
    ];
    setGroupActivated(true);
    setDeviceGroupEntries(nextEntries);
    setActiveDeviceKey(key);
    setForm(nextForm);
    setShowErrors(false);
    setSaveError("");
    setGroupSaveError("");
    resetActiveProbe();
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
    setDeviceGroupEntries(deviceGroupCards.map(({ key: entryKey, form: entryForm, savedFingerprint, error }) => ({
      key: entryKey,
      form: entryKey === key ? nextForm : entryForm,
      savedFingerprint,
      error: entryKey === key ? "" : error,
    })));
    setActiveDeviceKey(key);
    setForm(nextForm);
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
    const remaining = deviceGroupCards.filter(item => item.key !== key).map(({ key: entryKey, form: entryForm, savedFingerprint, error }) => ({
      key: entryKey, form: entryForm, savedFingerprint, error,
    }));
    setDeviceGroupEntries(remaining);
    if (key === activeDeviceKey) {
      const next = remaining[0];
      setActiveDeviceKey(next.key);
      setForm(next.form);
      setShowErrors(false);
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
      setProbe(null);
      setProbeError(error.message);
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

  async function saveDeviceGroupDrafts() {
    if (!groupTaskData || deviceGroupLoadError) {
      setGroupSaveError("任务编号清单尚未完整载入；请等待载入或重试后再保存。未完整载入时不会写入草稿。");
      return;
    }
    if (deviceGroupCards.length === 0) {
      setGroupSaveError("请先添加至少一个采集设备。");
      return;
    }

    const seenIds = new Set();
    const existingIds = new Set(groupTasks.map(item => String(item.taskId || "").trim().toLowerCase()));
    const prepared = deviceGroupCards.map(device => {
      const taskForm = { ...device.form, status: "draft" };
      const selected = parseModelValue(taskForm.dataModel);
      const model = models.find(item => item.modelId === selected.id && Number(item.version) === Number(selected.version));
      const dataItems = model?.acquisition?.dataItems || [];
      const check = validateIngestionTask(taskForm, { dataItems });
      const errors = [];
      const taskId = String(taskForm.taskId || "").trim().toLowerCase();
      if (!model || model.status !== "published") errors.push("请选择当前可用的已发布工艺模型。");
      if (existingIds.has(taskId)) errors.push("任务编号已存在；请换一个编号，避免覆盖现有任务版本。");
      if (seenIds.has(taskId)) errors.push("画布中有重复的任务编号。");
      seenIds.add(taskId);
      if (check.count > 0) errors.push(`还有 ${check.count} 处配置需要修正。`);
      const payload = deviceGroupPayload(taskForm);
      return {
        ...device,
        form: taskForm,
        payload,
        fingerprint: JSON.stringify(payload),
        errors,
      };
    });
    const invalid = prepared.filter(item => item.errors.length > 0);
    if (invalid.length > 0) {
      const firstInvalid = invalid[0];
      setDeviceGroupEntries(prepared.map(item => ({
        key: item.key,
        form: item.form,
        savedFingerprint: item.savedFingerprint,
        error: item.errors.join(" "),
      })));
      setActiveDeviceKey(firstInvalid.key);
      setForm(firstInvalid.form);
      setShowErrors(true);
      resetActiveProbe();
      setGroupSaveError(`${invalid.length} 个设备配置未通过检查。已切换到第一个待修正设备；修正后再次保存全部草稿。`);
      return;
    }

    setSaving(true);
    setGroupSaveError("");
    const results = prepared.map(item => ({
      key: item.key,
      form: item.form,
      savedFingerprint: item.savedFingerprint,
      error: "",
    }));
    let savedCount = 0;
    const failures = [];
    try {
      for (const item of prepared) {
        const current = results.find(result => result.key === item.key);
        if (current.savedFingerprint === item.fingerprint) {
          savedCount += 1;
          continue;
        }
        try {
          await postJson(ENDPOINT, item.payload);
          current.savedFingerprint = item.fingerprint;
          current.error = "";
          savedCount += 1;
        } catch (error) {
          current.error = error.message;
          failures.push(`${item.form.name || item.form.taskId}：${error.message}`);
        }
        setDeviceGroupEntries(results.map(result => ({ ...result })));
      }
      if (failures.length > 0) {
        setGroupSaveError(`已保存 ${savedCount} 个设备草稿，${failures.length} 个失败。可修正失败项后重试；已成功项不会重复写入。\n${failures.join("\n")}`);
        return;
      }
      notify(`已保存 ${savedCount} 个独立采集草稿任务`);
      navigate("/configuration/ingestion-tasks");
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <Page title="采集配置"><Card><p className="text-sm text-slate-500">正在载入配置…</p></Card></Page>;
  if (loadError) return <Page title="采集配置"><Alert tone="danger">{loadError}</Alert></Page>;

  return (
    <Page
      title={isNew ? "采集配置工作区" : form.name || form.taskId}
      actions={
        <div className="flex items-center gap-2">
          <Link className="inline-flex min-h-9 items-center rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
            to="/configuration/ingestion-tasks">返回列表</Link>
          {multiDeviceSetup && (
            <Button variant="primary" disabled={saving || !groupTaskData || Boolean(deviceGroupLoadError)}
              onClick={() => void saveDeviceGroupDrafts()}>
              {saving ? "逐设备保存中…" : `保存 ${deviceGroupCards.length} 个草稿`}
            </Button>
          )}
          {!readOnly && !multiDeviceSetup && (
            <>
              <Button disabled={saving} onClick={() => save("draft")}>{saving ? "保存中…" : "保存草稿"}</Button>
              <Button variant="primary" disabled={saving || !probeValid} onClick={() => save("published")}>
                发布到现场节点
              </Button>
            </>
          )}
          {!multiDeviceSetup && canWrite && managedByBinding && form.status === "draft" && (
            <Button variant="primary" disabled={saving || !probeValid} onClick={() => save("published")}>
              发布任务绑定
            </Button>
          )}
        </div>
      }
    >
      {confirmationDialog}
      {multiDeviceSetup && deviceGroupLoadError && <Alert tone="danger">
        <p>读取可访问任务清单失败，无法安全检查多设备配置中的编号冲突：{deviceGroupLoadError}</p>
        <Button className="mt-2" onClick={() => void Promise.all([reloadEdges(), reloadModels(), reloadGroupTasks()])}>重试读取</Button>
      </Alert>}
      {multiDeviceSetup && groupSaveError && <Alert tone="warning"><p className="whitespace-pre-line">{groupSaveError}</p></Alert>}
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
        devices={canvasDevices}
        edges={edges}
        models={models}
        activeEdgeId={form.edgeId || null}
        activeDeviceKey={multiDeviceSetup ? activeDeviceKey : "single-device"}
        onSelect={multiDeviceSetup ? selectDeviceGroupTask : undefined}
        onAddDeviceProfile={isNew && canWrite && !managedByBinding ? addDeviceGroupTask : undefined}
        onAssignEdge={assignDeviceGroupEdge}
        onRemove={multiDeviceSetup ? removeDeviceGroupTask : undefined}
        canAddDevice={isNew && canWrite && !managedByBinding}
        groupSaving={multiDeviceSetup && saving}
        probe={probeView}
        edgeError={errors.edgeId}
        disabled={saving || readOnly}
      >
        <div className="grid gap-5">
            <div id="acquisition-basic-info" className="scroll-mt-4">
              <Card title="任务与数据归属" description="识别设备、选择运行节点，并指定采集数据对应的工艺变量和对象。">
              <div className="grid items-start gap-4 md:grid-cols-2">
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
                    {models.map(item => (
                      <option key={`${item.modelId}::${item.version}`} value={modelValue(item.modelId, item.version)}>
                        {item.name || item.modelId} · v{item.version}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label="数据归属类型" hint="选择产生这组数据的设备、仪器、产线或业务系统。">
                  <Select value={form.subjectType} disabled={readOnly} onChange={event => update({ subjectType: event.target.value })}>
                    <option value="equipment">生产设备</option>
                    <option value="quality_instrument">检测仪器</option>
                    <option value="line">生产线</option>
                    <option value="utility_meter">公用工程仪表</option>
                    <option value="system">业务系统</option>
                    {!["equipment", "quality_instrument", "line", "utility_meter", "system"].includes(form.subjectType) && <option value={form.subjectType}>{form.subjectType}（历史值）</option>}
                  </Select>
                </Field>
                <Field label="归属对象编号" hint="数据所归属对象的唯一编号，例如 PRESS-01。" error={errors.subjectId}>
                  <Input id="acquisition-subject-id" value={form.subjectId} disabled={readOnly} onChange={event => update({ subjectId: event.target.value })} />
                </Field>
                <Field label="生产设备单元编号（可选）" hint="填写生产准备、工装安装或生产上下文所关联的设备编号；编号粒度按现场资产关系确定。">
                  <Input id="acquisition-equipment-id" value={form.equipmentId} disabled={readOnly} onChange={event => update({ equipmentId: event.target.value })} />
                </Field>
                <Field label="通信驱动" hint={`${descriptor.summary} 当前设备类型可用的驱动。`}>
                  <Select value={form.protocol} disabled={readOnly}
                    onChange={event => setForm(current => applyProtocolChange(current, event.target.value))}>
                    {protocolOptionGroups(deviceProfileOption(form.deviceProfile?.kind).drivers).map(group => (
                      <optgroup key={group.label} label={group.label}>
                        {group.options.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                      </optgroup>
                    ))}
                  </Select>
                </Field>
              </div>
              </Card>
            </div>

            <details id="acquisition-connection" open className="scroll-mt-4 rounded-xl border border-slate-200 bg-slate-50/50 p-2">
              <summary className="cursor-pointer px-3 py-2 text-sm font-semibold text-slate-900">连接参数 · {descriptor.label}</summary>
              <ConnectionPanel
                descriptor={descriptor}
                connection={form[descriptor.section]}
                errors={errors}
                readOnly={readOnly}
                allowProbe={allowProbe}
                onChange={value => updateSection(descriptor.section, value)}
              />
            </details>
            <details id="acquisition-probe" open className="scroll-mt-4 rounded-xl border border-slate-200 bg-slate-50/50 p-2">
              <summary className="cursor-pointer px-3 py-2 text-sm font-semibold text-slate-900">连接探查与设备点位</summary>
              <DevicePointsPanel
                descriptor={descriptor}
                form={form}
                dataItems={dataItems}
                probe={probeView}
                probeError={probeError}
                probing={probing}
                readOnly={readOnly}
                allowProbe={allowProbe}
                advisories={advisories}
                publishChecklist={publishChecklist}
                onProbe={runProbe}
                onMapPoint={mapProbePoint}
              />
            </details>

            <details id="acquisition-mappings" open className="scroll-mt-4 rounded-xl border border-slate-200 bg-slate-50/50 p-2">
              <summary className="cursor-pointer px-3 py-2 text-sm font-semibold text-slate-900">工艺变量映射</summary>
              <PointMappingPanel
                title="工艺变量映射"
                description="把设备点位对应到平台的工艺变量，并给出换算关系。"
                descriptor={descriptor}
                rows={form.valueMappings}
                options={dataItems}
                errors={errors}
                errorPrefix="valueMappings"
                probe={probeView}
                form={form}
                readOnly={readOnly}
                onChange={value => update({ valueMappings: value })}
              />
            </details>

            <details id="acquisition-advanced" className="scroll-mt-4 rounded-lg border border-slate-200 bg-white">
              <summary className="cursor-pointer px-5 py-4 text-sm font-semibold text-slate-900">高级采集与运行识别</summary>
              <div className="grid gap-5 border-t border-slate-200 p-5">
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
            </details>
        </div>
      </IngestionDeviceGroupCanvas>
    </Page>
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
  const { data, error, reload } = useApi(ENDPOINT);
  const { data: templateData, error: templateError, reload: reloadTemplates } = useApi("/api/v1/ingestion-configuration/templates");
  const { data: sourceData, error: sourceError, reload: reloadSources } = useApi("/api/v1/ingestion-configuration/data-sources");
  const { data: bindingData, error: bindingError, reload: reloadBindings } = useApi("/api/v1/ingestion-configuration/bindings");
  const { data: capabilityData, error: capabilityError } = useApi("/api/v1/acquisition-protocols");
  const rows = extractRows(data);
  const templates = extractRows(templateData);
  const sources = extractRows(sourceData);
  const bindings = extractRows(bindingData);
  const capabilities = capabilityData?.protocols || [];
  const navigate = useNavigate();
  const { confirm, confirmationDialog } = useConfirmDialog();

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
        {error && <Alert tone="danger">{error}</Alert>}
        {templateError && <Alert tone="danger">任务模板读取失败：{templateError}</Alert>}
        {sourceError && <Alert tone="danger">数据源读取失败：{sourceError}</Alert>}
        {bindingError && <Alert tone="danger">任务绑定读取失败：{bindingError}</Alert>}
        {capabilityError && <Alert tone="warning">驱动能力读取失败，暂无法确认连接复用方式：{capabilityError}</Alert>}
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
                {rows.map(row => (
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
                {rows.length === 0 && (
                  <tr><td colSpan="7" className="px-3 py-8 text-center text-slate-500">还没有采集配置。</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
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
