import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import {
  Alert, Badge, Button, Card, Field, Input, Page, Select, StatusBadge, notify,
} from "../ui/components";
import { downloadFile, getJson, postForm, postJson } from "../api/http";
import { useApi, extractRows } from "../hooks/useApi";
import {
  mergeServerCapabilities, protocolDescriptor, protocolOptions,
} from "./protocolRegistry";
import {
  applyProtocolChange, createIngestionTaskForm, modelValue, parseModelValue,
  patchFromProbePoint, toPayload, validateIngestionTask,
} from "./ingestionTaskForm";
import { ConnectionPanel } from "./panels/ConnectionPanel";
import { PointMappingPanel, blankRow } from "./panels/PointMappingPanel";
import { DevicePointsPanel } from "./panels/DevicePointsPanel";

const ENDPOINT = "/api/v1/ingestion-tasks";

/**
 * 设备接入配置页。
 *
 * 从通用注册表抽屉里独立出来的原因：接入配置的工作流是"改一处 → 看设备返回什么 → 再改"，
 * 抽屉里垂直堆叠九个卡片无法支撑这个循环。左栏配置、右栏常驻设备面板，两边同时可见。
 */
export function IngestionTaskPage() {
  const { taskId } = useParams();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const isNew = !taskId || taskId === "new";
  const requestedVersion = searchParams.get("version");
  const mode = searchParams.get("mode") || (isNew ? "create" : "maintain");

  const { data: modelData } = useApi("/api/v1/process-data-models");
  const { data: edgeData } = useApi("/api/edges");
  const models = extractRows(modelData);
  const edges = extractRows(edgeData);

  const [form, setForm] = useState(() => createIngestionTaskForm({}, undefined));
  const [loading, setLoading] = useState(!isNew);
  const [saving, setSaving] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [saveError, setSaveError] = useState("");
  const [probe, setProbe] = useState(null);
  const [probeError, setProbeError] = useState("");
  const [probing, setProbing] = useState(false);
  const [showErrors, setShowErrors] = useState(false);

  // 能力矩阵以后端 Runner 的实际行为为准，界面不自行猜测字段是否生效。
  useEffect(() => {
    let cancelled = false;
    getJson("/api/v1/acquisition-protocols")
      .then(result => { if (!cancelled) { mergeServerCapabilities(result?.protocols || result); setForm(value => ({ ...value })); } })
      .catch(() => { /* 后端尚未提供时沿用本地默认能力矩阵 */ });
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

  const descriptor = protocolDescriptor(form.protocol);
  const selectedModel = parseModelValue(form.dataModel);
  const model = models.find(item => item.modelId === selectedModel.id && item.version === selectedModel.version);
  const dataItems = model?.acquisition?.dataItems || [];
  const controlParameters = model?.controlParameters || [];
  const managedByBinding = Boolean(form.templateId && form.dataSourceId);
  const readOnly = managedByBinding || (mode === "maintain" && form.status !== "draft");
  const allowProbe = form.status === "draft";

  const validation = useMemo(
    () => validateIngestionTask(form, { dataItems }),
    [form, dataItems],
  );
  const errors = showErrors ? validation.errors : {};

  // 连接参数或点位一变，之前的验证结果立即失效——设备可能已经不是刚才那台了。
  const probeFingerprint = useMemo(
    () => JSON.stringify({
      edgeId: form.edgeId, protocol: form.protocol, dataModel: form.dataModel,
      section: form[descriptor.section], valueMappings: form.valueMappings,
      contextMappings: form.contextMappings, processSpecification: form.processSpecification,
    }),
    [form, descriptor.section],
  );
  useEffect(() => { setProbe(null); setProbeError(""); }, [probeFingerprint]);

  const advisories = descriptor.advisories ? descriptor.advisories(form) : [];
  const probeValid = Boolean(probe?.success && probe?.mappingsValidated);
  const publishChecklist = [
    { label: "选择采集节点与工艺数据模型", done: Boolean(form.edgeId && form.dataModel) },
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

  async function runProbe(discovery = {}) {
    const { append = false, ...query } = discovery;
    setProbing(true);
    setProbeError("");
    try {
      const result = await postJson(`${ENDPOINT}/probe`, {
        task: { ...toPayload(form), status: "draft" },
        discovery: { pageSize: 300, ...query },
      });
      setProbe(current => append && current
        ? { ...result, points: [...(current.points || []), ...(result.points || [])] }
        : result);
    } catch (error) {
      setProbe(null);
      setProbeError(error.message);
    } finally {
      setProbing(false);
    }
  }

  function mapProbePoint(point, dataItemCode) {
    const patch = patchFromProbePoint(point, dataItemCode, dataItems, descriptor);
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

  if (loading) return <Page title="设备接入"><Card><p className="text-sm text-slate-500">正在载入配置…</p></Card></Page>;
  if (loadError) return <Page title="设备接入"><Alert tone="danger">{loadError}</Alert></Page>;

  return (
    <Page
      title={isNew ? "配置数据源" : form.name || form.taskId}
      description={`${descriptor.label} · ${descriptor.summary}`}
      actions={
        <div className="flex items-center gap-2">
          <Link className="inline-flex min-h-9 items-center rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
            to="/configuration/ingestion-tasks">返回列表</Link>
          {!readOnly && (
            <>
              <Button disabled={saving} onClick={() => save("draft")}>{saving ? "保存中…" : "保存草稿"}</Button>
              <Button variant="primary" disabled={saving || !probeValid} onClick={() => save("published")}>
                {probeValid ? "发布到现场节点" : "请先验证连接"}
              </Button>
            </>
          )}
          {managedByBinding && form.status === "draft" && (
            <Button variant="primary" disabled={saving || !probeValid} onClick={() => save("published")}>
              {probeValid ? "验证并发布绑定" : "请先验证连接"}
            </Button>
          )}
        </div>
      }
    >
      <div className="grid gap-5">
        {readOnly && <Alert tone="info">{managedByBinding
          ? "该任务由不可变模板与数据源版本生成；需要调整时请创建新的资产版本和任务绑定。"
          : "已发布或已停用的配置不可修改。要调整参数请创建新版本。"}</Alert>}
        {saveError && <Alert tone="danger">{saveError}</Alert>}
        {showErrors && validation.count > 0 && (
          <Alert tone="warning">还有 {validation.count} 处配置需要修正，已在对应字段下方标出。</Alert>
        )}

        <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1.55fr)_minmax(22rem,1fr)]">
          <div className="grid gap-5">
            <Card title="基本信息" description="采集发生在哪里、采哪台设备、结果采用哪套工艺定义。">
              <div className="grid gap-4 md:grid-cols-2">
                <Field label="接入配置代码" hint="创建后不可修改。" error={errors.taskId}>
                  <Input value={form.taskId} disabled={!isNew && mode !== "create"}
                    placeholder="press01-fx3u" onChange={event => update({ taskId: event.target.value })} />
                </Field>
                <Field label="配置名称" error={errors.name}>
                  <Input value={form.name} disabled={readOnly} onChange={event => update({ name: event.target.value })} />
                </Field>
                <Field label="采集节点" hint="运行在设备所在网络、负责执行采集的边缘节点。" error={errors.edgeId}>
                  <Select value={form.edgeId} disabled={readOnly} onChange={event => update({ edgeId: event.target.value })}>
                    <option value="">请选择采集节点</option>
                    {form.edgeId && !edges.some(edge => edge.edgeId === form.edgeId) && (
                      <option value={form.edgeId}>{form.edgeId}（历史值）</option>
                    )}
                    {edges.map(edge => (
                      <option key={edge.edgeId} value={edge.edgeId}>
                        {edge.hostname || edge.displayName || edge.edgeId} · {edge.edgeId}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label="工艺数据模型" hint="规定平台中的工艺变量和单位，不包含设备地址。" error={errors.dataModel}>
                  <Select value={form.dataModel} disabled={readOnly} onChange={event => update({ dataModel: event.target.value })}>
                    <option value="">请选择数据模型</option>
                    {models.map(item => (
                      <option key={`${item.modelId}::${item.version}`} value={modelValue(item.modelId, item.version)}>
                        {item.name || item.modelId} · v{item.version}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label="归属对象类型" hint="使用工厂对象模型中的稳定类型代码，例如 equipment、line 或 utility_meter。">
                  <Input value={form.subjectType} disabled={readOnly} onChange={event => update({ subjectType: event.target.value })} />
                </Field>
                <Field label="归属对象编号" hint="数据所归属对象的唯一编号，例如 PRESS-01。" error={errors.subjectId}>
                  <Input value={form.subjectId} disabled={readOnly} onChange={event => update({ subjectId: event.target.value })} />
                </Field>
                <Field label="通信驱动" hint={descriptor.summary}>
                  <Select value={form.protocol} disabled={readOnly}
                    onChange={event => setForm(current => applyProtocolChange(current, event.target.value))}>
                    {protocolOptions.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                  </Select>
                </Field>
              </div>
            </Card>

            <ConnectionPanel
              descriptor={descriptor}
              connection={form[descriptor.section]}
              errors={errors}
              readOnly={readOnly}
              allowProbe={allowProbe}
              onChange={value => updateSection(descriptor.section, value)}
            />

            <PointMappingPanel
              title="工艺变量映射"
              description="把设备点位对应到平台的工艺变量，并给出换算关系。"
              descriptor={descriptor}
              rows={form.valueMappings}
              options={dataItems}
              errors={errors}
              errorPrefix="valueMappings"
              probe={probe}
              form={form}
              readOnly={readOnly}
              onChange={value => update({ valueMappings: value })}
            />

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

          <div className="xl:sticky xl:top-4">
            <DevicePointsPanel
              descriptor={descriptor}
              form={form}
              dataItems={dataItems}
              probe={probe}
              probeError={probeError}
              probing={probing}
              readOnly={readOnly}
              advisories={advisories}
              publishChecklist={publishChecklist}
              onProbe={runProbe}
              onMapPoint={mapProbePoint}
            />
          </div>
        </div>
      </div>
    </Page>
  );
}

function ContextPanel({ descriptor, form, errors, readOnly, onChange }) {
  const rows = form.contextMappings;
  const update = (index, patch) =>
    onChange({ contextMappings: rows.map((item, rowIndex) => rowIndex === index ? { ...item, ...patch } : item) });
  return (
    <Card
      title="运行上下文"
      description="从设备读取生产状态、产品、批次等上下文。阶段号由工艺模型中用途为「阶段号」的变量自动识别。"
      actions={!readOnly ? (
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
    case "node-id": return "使用 OPC UA 节点编号。";
    default: return "使用报文中的 JSON 字段路径。";
  }
}

function descriptorSourcePlaceholder(descriptor) {
  switch (descriptor.addressing) {
    case "modbus-register": return "holding-register:120:uint16";
    case "melsec-device": return "D:120:uint16";
    case "node-id": return "ns=2;s=Machine.Product";
    default: return "productFamilyCode";
  }
}

function ProcessSpecificationPanel({ descriptor, form, parameters, errors, probe, readOnly, onChange }) {
  const processSpecification = form.processSpecification;
  const update = patch => onChange({ processSpecification: { ...processSpecification, ...patch } });
  return (
    <Card
      title="设备工艺规范识别"
      description="从设备读取当前生效的工艺规范标识，让每次运行都能关联到实际工艺规范。"
      actions={
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={processSpecification.enabled} disabled={readOnly}
            onChange={event => update({ enabled: event.target.checked })} />启用
        </label>
      }
    >
      {!processSpecification.enabled ? (
        <p className="text-sm text-slate-500">当前采集任务不从设备数据识别工艺规范。</p>
      ) : (
        <div className="grid gap-4">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="工艺规范编号来源" error={errors["processSpecification.idPath"]}>
              <Input value={processSpecification.idPath} disabled={readOnly} placeholder={descriptorSourcePlaceholder(descriptor)}
                onChange={event => update({ idPath: event.target.value })} />
            </Field>
            <Field label="工艺规范版本来源" error={errors["processSpecification.versionPath"]}>
              <Input value={processSpecification.versionPath} disabled={readOnly}
                onChange={event => update({ versionPath: event.target.value })} />
            </Field>
            <Field label="工艺规范名称来源（可选）">
              <Input value={processSpecification.namePath} disabled={readOnly}
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
            readOnly={readOnly}
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
      description="设备只需提供生产状态；采集节点在生产开始时生成过程执行关联号，结束时关闭过程执行。"
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
          <Field label="生产中的取值" hint="该上下文等于此值时视为生产中。">
            <Input value={lifecycle.activeValue || ""} disabled={readOnly}
              onChange={event => update({ activeValue: event.target.value })} />
          </Field>
        </div>
      )}
    </Card>
  );
}

/**
 * 采集策略。只显示当前驱动真正会读取的字段。
 */
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
              <option value="edge-received">使用采集节点接收时间</option>
            </Select>
          ) : (
            <Input value={descriptor.id === "opc-ua" ? "OPC UA 服务器 SourceTimestamp" : "采集节点接收时间"} disabled />
          )}
        </Field>
        {capabilities.sourceTimestamp && !capabilities.intrinsicSourceTimestamp && form.timestampMode === "source" && (
          <>
            <Field label="时间来源" hint={descriptorSourceHint(descriptor)} error={errors.timestampPath}>
              <Input value={form.timestampPath} disabled={readOnly}
                placeholder={descriptorSourcePlaceholder(descriptor)}
                onChange={event => onChange({ timestampPath: event.target.value })} />
            </Field>
            <Field label="时间戳编码" error={errors.timestampEncoding}>
              <Select value={form.timestampEncoding} disabled={readOnly}
                onChange={event => onChange({ timestampEncoding: event.target.value })}>
                <option value="auto">按协议自动选择</option>
                <option value="iso-8601">ISO 8601</option>
                <option value="unix-s">Unix 秒</option>
                <option value="unix-ms">Unix 毫秒</option>
              </Select>
            </Field>
          </>
        )}
        {capabilities.sequencePath && (
          <Field label="序号字段（可选）">
            <Input value={form.sequencePath} disabled={readOnly} placeholder="没有可留空"
              onChange={event => onChange({ sequencePath: event.target.value })} />
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

/** 接入配置列表。点击进入独立配置页，而不是打开抽屉。 */
export function IngestionTasksPage() {
  const { data, error, reload } = useApi(ENDPOINT);
  const { data: templateData, reload: reloadTemplates } = useApi("/api/v1/ingestion-configuration/templates");
  const { data: sourceData, reload: reloadSources } = useApi("/api/v1/ingestion-configuration/data-sources");
  const { data: bindingData, reload: reloadBindings } = useApi("/api/v1/ingestion-configuration/bindings");
  const rows = extractRows(data);
  const templates = extractRows(templateData);
  const sources = extractRows(sourceData);
  const bindings = extractRows(bindingData);
  const navigate = useNavigate();

  async function retire(row) {
    if (!window.confirm(`确认停用 ${row.name || row.taskId}？现场节点会在下一次同步时停止该采集任务。`)) return;
    try {
      await postJson(ENDPOINT, { ...row, status: "retired" });
      notify("接入配置已停用");
      reload?.();
    } catch (requestError) {
      notify(requestError.message);
    }
  }

  return (
    <Page
      title="设备接入"
      description="选择采集节点和通信驱动，把设备点位映射到工艺变量。"
      actions={
        <div className="flex items-center gap-2">
          <Link className="inline-flex min-h-9 items-center rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
            to="/edges">查看现场节点</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700"
            to="/configuration/ingestion-tasks/new">配置数据源</Link>
        </div>
      }
    >
      <div className="grid gap-4">
        {error && <Alert tone="danger">{error}</Alert>}
        <ReusableConfigurationPanel
          tasks={rows}
          templates={templates}
          sources={sources}
          bindings={bindings}
          onChanged={() => Promise.all([reload?.(), reloadTemplates?.(), reloadSources?.(), reloadBindings?.()])}
        />
        <Card>
          <div className="overflow-auto">
            <table className="w-full min-w-[52rem] text-left text-sm">
              <thead className="text-slate-600">
                <tr>
                  <th className="px-3 py-2">设备</th>
                  <th className="px-3 py-2">采集节点</th>
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
                    <td className="px-3 py-2 font-medium text-slate-800">{row.subjectId}</td>
                    <td className="px-3 py-2 text-slate-600">{row.edgeId}</td>
                    <td className="px-3 py-2 text-slate-600">{row.name}<span className="ml-1 text-xs text-slate-400">v{row.version}</span></td>
                    <td className="px-3 py-2"><Badge tone="neutral">{protocolDescriptor(row.protocol).label}</Badge></td>
                    <td className="px-3 py-2 text-slate-600">{row.valueMappings?.length || 0}</td>
                    <td className="px-3 py-2"><StatusBadge value={row.status} /></td>
                    <td className="px-3 py-2">
                      <div className="flex flex-wrap gap-1">
                        <Button variant="ghost"
                          onClick={() => navigate(`/configuration/ingestion-tasks/${encodeURIComponent(row.taskId)}?version=${row.version}`)}>
                          查看
                        </Button>
                        {!row.templateId && (
                          <Button variant="ghost"
                            onClick={() => navigate(`/configuration/ingestion-tasks/${encodeURIComponent(row.taskId)}?version=${row.version}&mode=version`)}>
                            新版本
                          </Button>
                        )}
                        {row.status !== "retired" && (
                          <Button variant="ghost" className="text-rose-700" onClick={() => retire(row)}>停用</Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr><td colSpan="7" className="px-3 py-8 text-center text-slate-500">还没有配置任何数据源。</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
      </div>
    </Page>
  );
}

function ReusableConfigurationPanel({ tasks, templates, sources, bindings, onChanged }) {
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

  return (
    <Card title="批量接入同类设备" description="首台设备通过真实探查后提取版本化模板；连接实例与任务绑定可用 CSV 原子导入，避免重复维护整份点位映射。">
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
        <div className="flex flex-wrap gap-2 border-t border-slate-100 pt-4">
          <Button variant="ghost" onClick={() => downloadFile("/api/v1/ingestion-configuration/data-sources.csv", "data-sources.csv")}>导出数据源 CSV</Button>
          <Button variant="ghost" onClick={() => sourceFile.current?.click()} disabled={Boolean(busy)}>导入数据源 CSV</Button>
          <input ref={sourceFile} className="hidden" type="file" accept=".csv,text/csv" onChange={event => importCsv("sources", event.target.files?.[0])} />
          <Button variant="ghost" onClick={() => downloadFile("/api/v1/ingestion-configuration/bindings.csv", "ingestion-task-bindings.csv")}>导出任务绑定 CSV</Button>
          <Button variant="ghost" onClick={() => bindingFile.current?.click()} disabled={Boolean(busy)}>导入任务绑定 CSV</Button>
          <input ref={bindingFile} className="hidden" type="file" accept=".csv,text/csv" onChange={event => importCsv("bindings", event.target.files?.[0])} />
        </div>
        {message && <Alert tone={message.includes("已") ? "info" : "warning"}>{message}</Alert>}
        <p className="text-xs text-slate-500">安全边界：凭据字段只填写现场节点密钥引用；固定请求体和普通请求头会随 CSV 导出，不要写入凭据。批量导入只生成草稿，每个任务必须单独连接真实设备并通过映射校验后才能发布。</p>
      </div>
    </Card>
  );
}
