// 提供生产配置记录的编辑模型、校验和表单控件。
import { useMemo } from "react";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, DateTimeField, Field, Input, Select, WorkflowGuide, notify } from "../ui/components";
import { formatTime } from "./shared";
import { productionFieldLabels, productionResources } from "./manufacturingResources";

export function createProductionEditor(resource, value) {
  return Object.fromEntries(Object.entries(resource.template).map(([key, initial]) => [
    key,
    key === "attributes"
      ? Object.entries(value[key] ?? initial).map(([attribute, attributeValue]) => ({ attribute, value: attributeValue }))
      : key === "roles"
        ? (value[key] ?? initial).map(role => ({ ...role, acceptedComponentTypeCodes: role.acceptedComponentTypeCodes || [] }))
      : value[key] ?? initial,
  ]));
}

export function parseProductionEditor(resource, editor, base) {
  const value = { ...base };
  Object.entries(resource.template).forEach(([key, initial]) => {
    if (key === "attributes") {
      value[key] = Object.fromEntries((editor[key] || [])
        .filter(item => item.attribute.trim() && item.value.trim())
        .map(item => [item.attribute.trim(), item.value.trim()]));
    } else if (key === "roles") {
      value[key] = editor[key].map(role => ({
        ...role,
        code: role.code.trim(),
        name: role.name.trim(),
        maxCount: Number(role.maxCount),
        sortOrder: Number(role.sortOrder),
      }));
    } else if (typeof initial === "number") {
      value[key] = Number(editor[key]);
    } else {
      value[key] = editor[key];
    }
  });
  return value;
}

export function isProductionEditorValid(resource, editor) {
  if (resource.requiredFields?.some(key => !String(editor[key] ?? "").trim())) return false;
  return Object.entries(resource.template).every(([key, initial]) => {
    if (typeof initial === "number") return Number(editor[key]) >= 1;
    if (key === "attributes") return (editor[key] || []).every(item =>
      (!item.attribute.trim() && !item.value.trim()) || (item.attribute.trim() && item.value.trim()));
    if (key === "roles") return editor[key].length > 0 && editor[key].every(role =>
      role.code.trim() && role.name.trim() && Number(role.maxCount) >= 1 && Number(role.sortOrder) >= 0 &&
      role.acceptedComponentTypeCodes.length > 0);
    return true;
  });
}

function ProductionReferenceField({ fieldKey, value, required, editor, onChange }) {
  const settings = {
    equipmentId: {
      endpoint: "/api/v1/data-objects?limit=500",
      label: "设备",
      filter: row => row.subjectType === "equipment",
      optionValue: row => row.subjectId,
      optionLabel: row => `${row.subjectId}${row.edgeId ? ` · 由 ${row.edgeId} 采集` : ""}`,
    },
    toolingInstallationId: {
      endpoint: "/api/v1/tooling-installations?activeOnly=true",
      label: "当前已装工装",
      filter: row => !editor.equipmentId || row.equipmentId === editor.equipmentId,
      optionValue: row => row.installationId,
      optionLabel: row => `${row.equipmentId} 当前工装${row.installedAt ? ` · ${formatTime(row.installedAt)}装入` : ""}`,
    },
    assemblyRevisionId: {
      endpoint: "/api/v1/tooling-assemblies/revisions",
      label: "工装组合版本",
      optionValue: row => row.assemblyRevisionId,
      optionLabel: row => `${row.toolingAssemblyId} · 版本 ${row.revision}`,
    },
    componentTypeCode: {
      endpoint: "/api/v1/tooling-component-types",
      label: "组件类型",
      filter: row => row.status !== "inactive",
      optionValue: row => row.componentTypeCode,
      optionLabel: row => `${row.name} · ${row.componentTypeCode}`,
    },
    toolingTypeCode: {
      endpoint: "/api/v1/tooling-types",
      label: "工装类型",
      filter: row => row.status !== "inactive",
      optionValue: row => row.toolingTypeCode,
      optionLabel: row => `${row.name} · ${row.toolingTypeCode} v${row.version}`,
    },
  };
  const setting = settings[fieldKey];
  const { data, error } = useApi(setting.endpoint);
  const sourceRows = extractRows(data).filter(setting.filter || (() => true));
  const options = [...new Map(sourceRows.map(row => [setting.optionValue(row), row])).values()];
  const hasValue = options.some(row => setting.optionValue(row) === value);
  return (
    <Field label={setting.label} error={error || ""}>
      <Select required={required} value={value || ""} onChange={event => onChange(fieldKey, event.target.value)}>
        <option value="">{required ? "请选择" : "不关联"}</option>
        {value && !hasValue && <option value={value}>{value}（历史值）</option>}
        {options.map(row => <option key={setting.optionValue(row)} value={setting.optionValue(row)}>{setting.optionLabel(row)}</option>)}
      </Select>
    </Field>
  );
}
function ProcessSpecificationReferenceField({ editor, onChange, required }) {
  const { data, error } = useApi("/api/v1/process-specifications");
  const processSpecifications = extractRows(data).filter(row =>
    row.status === "published" || (row.processSpecificationId === editor.processSpecificationId && Number(row.version) === Number(editor.processSpecificationVersion)));
  const selected = editor.processSpecificationId ? `${editor.processSpecificationId}:${editor.processSpecificationVersion}` : "";
  const hasSelected = processSpecifications.some(row => `${row.processSpecificationId}:${row.version}` === selected);
  return (
    <Field label="工艺规范" error={error || ""}>
      <Select
        required={required}
        value={selected}
        onChange={event => {
          const row = processSpecifications.find(item => `${item.processSpecificationId}:${item.version}` === event.target.value);
          onChange("processSpecificationId", row?.processSpecificationId || "");
          onChange("processSpecificationVersion", row?.version || 1);
        }}
      >
        <option value="">请选择已发布工艺规范</option>
        {selected && !hasSelected && <option value={selected}>{editor.processSpecificationId} · v{editor.processSpecificationVersion}（历史值）</option>}
        {processSpecifications.map(row => <option key={`${row.processSpecificationId}:${row.version}`} value={`${row.processSpecificationId}:${row.version}`}>{row.name} · {row.processSpecificationId} v{row.version}</option>)}
      </Select>
    </Field>
  );
}
export function ProductionRecordForm({ resource, editor, editorMode, onChange }) {
  if (resource === productionResources.context) {
    const hasMachine = Boolean(editor.equipmentId);
    const hasProduct = Boolean(editor.productCode?.trim() && editor.productFamilyCode?.trim());
    const hasProcessSpecification = Boolean(editor.processSpecificationId);
    const hasToolingInstallation = Boolean(editor.toolingInstallationId);
    return (
      <div className="grid gap-5">
        <WorkflowGuide
          title="完成这 3 步即可生效"
          description="必填内容完成后，底部按钮会自动变为可用。"
          steps={[
            { title: "选择生产设备", description: "确定接下来要切换的现场设备。", state: hasMachine ? "done" : "current" },
            { title: "确认产品与工艺规范", description: "填写产品身份并选择已发布工艺规范。", state: hasProduct && hasProcessSpecification ? "done" : hasMachine ? "current" : "upcoming" },
            { title: "确认工装并生效", description: "选择当前已装工装后保存。", state: hasMachine && hasProduct && hasProcessSpecification && hasToolingInstallation ? "current" : "upcoming" },
          ]}
        />
        <Card title="1. 选择生产设备" description="只显示已经通过现场节点上报过数据的设备。">
          <ProductionReferenceField
            fieldKey="equipmentId"
            value={editor.equipmentId}
            editor={editor}
            required
            onChange={(key, value) => {
              onChange(key, value);
              onChange("toolingInstallationId", "");
            }}
          />
        </Card>
        <Card title="2. 确认产品与工艺规范" description="产品编号用于追溯实物，产品系列用于同类分析。">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="产品系列" hint="例如 LENS-A、轴类零件">
              <Input required value={editor.productFamilyCode || ""} onChange={event => onChange("productFamilyCode", event.target.value)} />
            </Field>
            <Field label="产品编号" hint="填写现场使用的产品或物料编号">
              <Input required value={editor.productCode || ""} onChange={event => onChange("productCode", event.target.value)} />
            </Field>
            <div className="sm:col-span-2">
              <ProcessSpecificationReferenceField editor={editor} onChange={onChange} required />
            </div>
          </div>
        </Card>
        <Card title="3. 补充现场信息" description="多个设备填写相同生产批次或工件编号后，可以跨设备追溯；这些字段会固化到后续运行。">
          <div className="grid gap-4 sm:grid-cols-2">
            <ProductionReferenceField fieldKey="toolingInstallationId" value={editor.toolingInstallationId} editor={editor} required onChange={onChange} />
            <Field label="外部工单" hint="来自 MES、ERP 或现场工单，可选">
              <Input value={editor.externalOrderRef || ""} onChange={event => onChange("externalOrderRef", event.target.value)} />
            </Field>
            <Field label="生产批次" hint="同一批产品经过多台设备时，各设备填写相同批次号">
              <Input value={editor.externalBatchRef || ""} onChange={event => onChange("externalBatchRef", event.target.value)} />
            </Field>
            <Field label="物料批次" hint="没有批次管理时可以留空">
              <Input value={editor.materialLotRef || ""} onChange={event => onChange("materialLotRef", event.target.value)} />
            </Field>
            <Field label="材料规格" hint="例如牌号、等级或供应规格，可选">
              <Input value={editor.materialSpecification || ""} onChange={event => onChange("materialSpecification", event.target.value)} />
            </Field>
            <Field label="设备维护状态" hint="例如 available、due 或 maintenance">
              <Input value={editor.maintenanceStatus || ""} onChange={event => onChange("maintenanceStatus", event.target.value)} />
            </Field>
            <Field label="校准状态" hint="例如 valid、due；到期后运行快照会强制标记 expired">
              <Input value={editor.calibrationStatus || ""} onChange={event => onChange("calibrationStatus", event.target.value)} />
            </Field>
            <Field label="校准记录"><Input value={editor.calibrationRef || ""} onChange={event => onChange("calibrationRef", event.target.value)} /></Field>
            <Field label="校准有效期"><DateTimeField value={editor.calibrationValidUntil || ""} onChange={value => onChange("calibrationValidUntil", value)} /></Field>
          </div>
        </Card>
        {hasMachine && hasProduct && hasProcessSpecification && hasToolingInstallation && (
          <Alert tone="success" title="可以生效">
            保存后，设备 {editor.equipmentId} 新开始的运行将使用产品 {editor.productCode} 和工艺规范 {editor.processSpecificationId} v{editor.processSpecificationVersion}。
          </Alert>
        )}
      </div>
    );
  }
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {Object.entries(resource.template).map(([key, initial]) => {
        const required = resource.requiredFields?.includes(key);
        const label = productionFieldLabels[key] ?? key;
        if (key === "processSpecificationVersion") return null;
        if (key === "processSpecificationId") return <ProcessSpecificationReferenceField key={key} editor={editor} onChange={onChange} required={required} />;
        if (key === "toolingTypeCode" && resource === productionResources.type) {
          return (
            <Field key={key} label="工装结构代码">
              <Input
                required={required}
                value={editor[key] ?? ""}
                disabled={editorMode === "version"}
                placeholder="例如 optical-mold"
                onChange={event => onChange(key, event.target.value)}
              />
            </Field>
          );
        }
        if ((resource.referenceFields || []).includes(key)) {
          return <ProductionReferenceField key={key} fieldKey={key} value={editor[key]} editor={editor} onChange={onChange} required={required} />;
        }
        if (key === "attributes") return <AttributeFields key={key} value={editor[key] || []} onChange={value => onChange(key, value)} />;
        if (key === "roles") return <ToolingRoleFields key={key} value={editor[key] || []} onChange={value => onChange(key, value)} />;
        if (key === "status" && resource.statusOptions) {
          return (
            <Field key={key} label={label}>
              <Select required={required} value={editor[key] ?? ""} onChange={event => onChange(key, event.target.value)}>
                {resource.statusOptions.map(([value, optionLabel]) => <option key={value} value={value}>{optionLabel}</option>)}
              </Select>
            </Field>
          );
        }
        if (key === "source") {
          return (
            <Field key={key} label={label}>
              <Select value={editor[key] ?? "manual"} onChange={event => onChange(key, event.target.value)}>
                <option value="manual">手动操作</option>
              </Select>
            </Field>
          );
        }
        return (
          <Field key={key} label={label}>
            <Input
              aria-label={label}
              required={required}
              type={typeof initial === "number" ? "number" : "text"}
              min={typeof initial === "number" ? 1 : undefined}
              value={editor[key] ?? ""}
              onChange={event => onChange(key, event.target.value)}
            />
          </Field>
        );
      })}
    </div>
  );
}

function AttributeFields({ value, onChange }) {
  const rows = value.length ? value : [{ attribute: "", value: "" }];
  function update(index, field, nextValue) {
    const source = value.length ? value : [{ attribute: "", value: "" }];
    onChange(source.map((item, rowIndex) => rowIndex === index ? { ...item, [field]: nextValue } : item));
  }
  return (
    <Card
      className="sm:col-span-2"
      title="扩展属性"
      description="登记需要在台账中查询的业务属性。"
      actions={<Button onClick={() => onChange([...value, { attribute: "", value: "" }])}>添加属性</Button>}
    >
      <div className="grid gap-2">
        {rows.map((item, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <Input aria-label={`属性名称 ${index + 1}`} value={item.attribute} placeholder="属性名称" onChange={event => update(index, "attribute", event.target.value)} />
            <Input aria-label={`属性内容 ${index + 1}`} value={item.value} placeholder="属性内容" onChange={event => update(index, "value", event.target.value)} />
            {value.length > 0 && <Button variant="ghost" className="text-rose-700" onClick={() => onChange(value.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>}
          </div>
        ))}
      </div>
    </Card>
  );
}

function ToolingRoleFields({ value, onChange }) {
  const { data, error } = useApi("/api/v1/tooling-component-types");
  const componentTypes = useMemo(
    () => [...new Map(extractRows(data).map(item => [item.componentTypeCode, item])).values()],
    [data],
  );
  function update(index, patch) {
    onChange(value.map((role, rowIndex) => rowIndex === index ? { ...role, ...patch } : role));
  }
  function add() {
    onChange([...value, { code: "", name: "", required: true, maxCount: 1, sortOrder: value.length + 1, acceptedComponentTypeCodes: [] }]);
  }
  function applyMoldingTemplate() {
    const insertType = componentTypes.find(type => /模芯|insert/i.test(`${type.name} ${type.componentTypeCode}`));
    const frameType = componentTypes.find(type => /模架|frame/i.test(`${type.name} ${type.componentTypeCode}`));
    if (!insertType || !frameType) {
      notify("请先建立“模芯”和“模架”组件分类。", "danger");
      return;
    }
    onChange([
      { code: "upper-insert", name: "上模芯", required: true, maxCount: 1, sortOrder: 1, acceptedComponentTypeCodes: [insertType.componentTypeCode] },
      { code: "lower-insert", name: "下模芯", required: true, maxCount: 1, sortOrder: 2, acceptedComponentTypeCodes: [insertType.componentTypeCode] },
      { code: "mold-frame", name: "模架", required: true, maxCount: 1, sortOrder: 3, acceptedComponentTypeCodes: [frameType.componentTypeCode] },
    ]);
  }
  return (
    <Card
      className="sm:col-span-2"
      title="装配位置"
      description="定义工装由哪些组件位置组成。"
      actions={(
        <div className="flex flex-wrap gap-2">
          {value.length === 0 && <Button variant="ghost" onClick={applyMoldingTemplate}>套用精密模压示例</Button>}
          <Button onClick={add}>添加装配位置</Button>
        </div>
      )}
    >
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-4">
        {value.length === 0 && <p className="text-sm text-slate-500">请至少添加一个装配位置。</p>}
        {value.map((role, index) => (
          <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 sm:grid-cols-2">
            <Field label="位置代码"><Input value={role.code} onChange={event => update(index, { code: event.target.value })} /></Field>
            <Field label="位置名称"><Input value={role.name} onChange={event => update(index, { name: event.target.value })} /></Field>
            <Field label="最大组件数"><Input type="number" min="1" value={role.maxCount} onChange={event => update(index, { maxCount: event.target.value })} /></Field>
            <Field label="显示顺序"><Input type="number" min="0" value={role.sortOrder} onChange={event => update(index, { sortOrder: event.target.value })} /></Field>
            <div className="sm:col-span-2">
              <p className="mb-2 text-sm font-medium text-slate-700">允许的组件类型</p>
              <div className="flex flex-wrap gap-3">
                {componentTypes.map(type => (
                  <label key={type.componentTypeCode} className="flex items-center gap-1.5 text-sm">
                    <input
                      type="checkbox"
                      checked={role.acceptedComponentTypeCodes.includes(type.componentTypeCode)}
                      onChange={event => update(index, {
                        acceptedComponentTypeCodes: event.target.checked
                          ? [...role.acceptedComponentTypeCodes, type.componentTypeCode]
                          : role.acceptedComponentTypeCodes.filter(code => code !== type.componentTypeCode),
                      })}
                    />
                    {type.name}
                  </label>
                ))}
              </div>
            </div>
            <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={role.required} onChange={event => update(index, { required: event.target.checked })} />必须装配</label>
            <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => onChange(value.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>
          </div>
        ))}
      </div>
    </Card>
  );
}
