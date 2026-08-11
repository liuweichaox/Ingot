import { useEffect, useMemo, useState } from "react";
import { deleteJson, postJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, DataTable, Drawer, EmptyState, Field, Input, Pagination, Page, Select, StatusBadge, WorkflowGuide, notify } from "../ui/components";
import { formatTime, LoadingCard } from "./shared";

const productionResources = {
  context: {
    title: "运行准备", endpoint: "/api/v1/production-contexts", key: "contextId",
    description: "为设备选择接下来生产的产品、工艺规范和已装工装，保存后对新运行生效。",
    drawerDescription: "按顺序确认设备、产品、工艺规范和工装；保存后只影响新开始的生产运行。",
    columns: [["equipmentId", "设备"], ["productCode", "产品"], ["processSpecificationId", "工艺规范"], ["validFrom", "生效时间"], ["validTo", "结束时间"]],
    template: { equipmentId: "", productFamilyCode: "", productCode: "", processSpecificationId: "", processSpecificationVersion: 1, toolingInstallationId: "", source: "manual", externalOrderRef: "", externalBatchRef: "", materialLotRef: "", materialSpecification: "", maintenanceStatus: "", calibrationStatus: "", calibrationRef: "", calibrationValidUntil: "" },
    createLabel: "配置下一批生产",
    requiredFields: ["equipmentId", "productFamilyCode", "productCode", "processSpecificationId"],
    prepare: value => ({
      ...value,
      validFrom: new Date().toISOString(),
      calibrationValidUntil: value.calibrationValidUntil
        ? new Date(value.calibrationValidUntil).toISOString()
        : null,
    }),
    lifecycle: { label: "结束", visible: value => !value.validTo, url: value => `/api/v1/production-contexts/${value.contextId}:close`, body: () => ({ at: new Date().toISOString() }) },
  },
  installation: {
    title: "工装装卸", endpoint: "/api/v1/tooling-installations", key: "installationId",
    description: "记录哪个工装组合版本在何时装入设备，供后续运行自动关联。",
    drawerDescription: "选择设备和已经建立的工装组合版本，装入后会进入该设备的有效工装记录。",
    columns: [["equipmentId", "设备"], ["toolingAssemblyId", "工装"], ["installedAt", "装入"], ["removedAt", "卸下"]],
    template: { equipmentId: "", assemblyRevisionId: "", source: "manual" },
    createLabel: "装入工装",
    requiredFields: ["equipmentId", "assemblyRevisionId"],
    prepare: value => ({ ...value, installedAt: new Date().toISOString(), commandId: crypto.randomUUID() }),
    lifecycle: { label: "卸下", visible: value => !value.removedAt, url: value => `/api/v1/tooling-installations/${value.installationId}:remove`, body: () => ({ at: new Date().toISOString() }) },
  },
  componentType: {
    title: "组件分类", endpoint: "/api/v1/tooling-component-types", key: "componentTypeCode",
    description: "定义模芯、模架等物理资产类别；上模和下模属于装配位置，不在这里重复分类。",
    columns: [["componentTypeCode", "代码"], ["name", "名称"], ["status", "状态"], ["attributes", "属性"]],
    template: { componentTypeCode: "", name: "", status: "active", attributes: {} },
    createLabel: "新建组件分类",
    requiredFields: ["componentTypeCode", "name"],
    statusOptions: [["active", "启用"], ["inactive", "停用"]],
    deleteUrl: value => `/api/v1/tooling-component-types/${encodeURIComponent(value.componentTypeCode)}`,
  },
  component: {
    title: "组件资产", endpoint: "/api/v1/tooling-components", key: "componentId",
    description: "每条记录代表一件具有独立资产编号和序列号、可更换并需要追溯的物理组件。",
    columns: [["componentId", "资产编号"], ["componentTypeCode", "分类"], ["serialNo", "序列号"], ["name", "名称"], ["status", "资产状态"]],
    template: { componentId: "", componentTypeCode: "", serialNo: "", name: "", status: "available", attributes: {} },
    createLabel: "登记组件资产",
    requiredFields: ["componentId", "componentTypeCode", "serialNo", "name"],
    statusOptions: [["available", "合格可用"], ["maintenance", "维护中"], ["retired", "已退役"]],
    deleteUrl: value => `/api/v1/tooling-components/${encodeURIComponent(value.componentId)}`,
  },
  type: {
    title: "装配模板", endpoint: "/api/v1/tooling-types", key: "toolingTypeCode",
    description: "定义一类工装总成包含哪些装配位置，以及每个位置允许使用哪种组件资产。",
    columns: [["toolingTypeCode", "代码"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["roles", "装配位置"]],
    template: { toolingTypeCode: "", version: 1, name: "", status: "active", roles: [] },
    createLabel: "新建装配模板",
    requiredFields: ["toolingTypeCode", "name"],
    statusOptions: [["active", "启用"], ["inactive", "停用"]],
    deleteUrl: value => `/api/v1/tooling-types/${encodeURIComponent(value.toolingTypeCode)}/${value.version}`,
  },
  assembly: {
    title: "工装总成", endpoint: "/api/v1/tooling-assemblies", key: "toolingAssemblyId",
    description: "维护工装总成身份，并通过不可变配置版本记录每个装配位置实际使用的组件资产。",
    columns: [["toolingAssemblyId", "工装总成编号"], ["name", "名称"], ["toolingTypeCode", "装配模板"], ["status", "状态"]],
    template: { toolingAssemblyId: "", toolingTypeCode: "", name: "", status: "active" },
    createLabel: "新建工装",
    requiredFields: ["toolingAssemblyId", "toolingTypeCode", "name"],
    statusOptions: [["active", "启用"], ["inactive", "停用"]],
    deleteUrl: value => `/api/v1/tooling-assemblies/${encodeURIComponent(value.toolingAssemblyId)}`,
  },
};

const productionFieldLabels = {
  equipmentId: "设备编号",
  productFamilyCode: "产品系列",
  productCode: "产品编号",
  processSpecificationId: "工艺规范编号",
  processSpecificationVersion: "工艺规范版本",
  toolingInstallationId: "工装装卸记录",
  source: "记录来源",
  materialLotRef: "物料批次",
  externalOrderRef: "外部工单",
  externalBatchRef: "生产批次",
  materialSpecification: "材料规格",
  maintenanceStatus: "维护状态",
  calibrationStatus: "校准状态",
  calibrationRef: "校准记录",
  calibrationValidUntil: "校准有效期",
  assemblyRevisionId: "工装组合版本",
  componentTypeCode: "组件类型代码",
  name: "名称",
  status: "状态",
  attributes: "扩展属性",
  componentId: "组件编号",
  serialNo: "序列号",
  toolingTypeCode: "工装类型代码",
  version: "版本",
  roles: "装配位置",
  toolingAssemblyId: "工装编号",
};

function createProductionEditor(resource, value) {
  return Object.fromEntries(Object.entries(resource.template).map(([key, initial]) => [
    key,
    key === "attributes"
      ? Object.entries(value[key] ?? initial).map(([attribute, attributeValue]) => ({ attribute, value: attributeValue }))
      : key === "roles"
        ? (value[key] ?? initial).map(role => ({ ...role, acceptedComponentTypeCodes: role.acceptedComponentTypeCodes || [] }))
      : value[key] ?? initial,
  ]));
}

function parseProductionEditor(resource, editor, base) {
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

function isProductionEditorValid(resource, editor) {
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
function ProductionRecordForm({ resource, editor, onChange }) {
  if (resource === productionResources.context) {
    const hasMachine = Boolean(editor.equipmentId);
    const hasProduct = Boolean(editor.productCode?.trim() && editor.productFamilyCode?.trim());
    const hasProcessSpecification = Boolean(editor.processSpecificationId);
    return (
      <div className="grid gap-5">
        <WorkflowGuide
          title="完成这 3 步即可生效"
          description="必填内容完成后，底部按钮会自动变为可用。"
          steps={[
            { title: "选择生产设备", description: "确定接下来要切换的现场设备。", state: hasMachine ? "done" : "current" },
            { title: "确认产品与工艺规范", description: "填写产品身份并选择已发布工艺规范。", state: hasProduct && hasProcessSpecification ? "done" : hasMachine ? "current" : "upcoming" },
            { title: "检查并生效", description: "核对工装和物料批次后保存。", state: hasMachine && hasProduct && hasProcessSpecification ? "current" : "upcoming" },
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
            <ProductionReferenceField fieldKey="toolingInstallationId" value={editor.toolingInstallationId} editor={editor} onChange={onChange} />
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
            <Field label="校准有效期"><Input type="datetime-local" value={editor.calibrationValidUntil || ""} onChange={event => onChange("calibrationValidUntil", event.target.value)} /></Field>
          </div>
        </Card>
        {hasMachine && hasProduct && hasProcessSpecification && (
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
        if (["equipmentId", "toolingInstallationId", "assemblyRevisionId", "componentTypeCode", "toolingTypeCode"].includes(key)) {
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
  return (
    <Card className="sm:col-span-2" title="装配位置" description="定义工装由哪些组件位置组成。" actions={<Button onClick={add}>添加装配位置</Button>}>
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

function ToolingRevisionComposition({ revision, template, components, componentTypes, installation }) {
  const members = new Map((revision?.members || []).map(member => [member.roleCode, member]));
  const componentById = new Map(components.map(component => [component.componentId, component]));
  const typeByCode = new Map(componentTypes.map(type => [type.componentTypeCode, type]));
  const roles = template?.roles || [];
  if (!revision) {
    return <EmptyState title="尚未建立配置版本" description="为工装总成的每个装配位置选择具体组件资产后，才能用于设备装卸和运行追溯。" />;
  }
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2 text-xs text-slate-500">
        <StatusBadge value={`Rev.${revision.revision}`} />
        <span>配置时间 {formatTime(revision.createdAt)}</span>
        {installation && <span>当前装在 {installation.equipmentId}</span>}
      </div>
      <div className="grid gap-3 md:grid-cols-2">
        {roles.map(role => {
          const member = members.get(role.code);
          const component = componentById.get(member?.componentId);
          const type = typeByCode.get(component?.componentTypeCode);
          return (
            <article key={role.code} className="rounded-xl border border-slate-200 bg-slate-50 p-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold tracking-wide text-slate-500">{role.name}</p>
                  <p className="mt-1 font-semibold text-slate-950">{component?.name || member?.componentId || "未装配"}</p>
                </div>
                <StatusBadge value={component?.status || (member ? "unknown" : "missing")} />
              </div>
              {component ? (
                <dl className="mt-3 grid grid-cols-[5rem_1fr] gap-x-3 gap-y-1 text-xs leading-5">
                  <dt className="text-slate-400">资产编号</dt><dd className="text-slate-700">{component.componentId}</dd>
                  <dt className="text-slate-400">序列号</dt><dd className="text-slate-700">{component.serialNo}</dd>
                  <dt className="text-slate-400">组件分类</dt><dd className="text-slate-700">{type?.name || component.componentTypeCode}</dd>
                  <dt className="text-slate-400">型号/零件号</dt><dd className="text-slate-700">{component.attributes?.model || "—"} · {component.attributes?.productCode || "—"}</dd>
                </dl>
              ) : <p className="mt-3 text-xs text-rose-700">该位置缺少可追溯的组件资产。</p>}
            </article>
          );
        })}
      </div>
    </div>
  );
}

function ToolingAssembliesPage() {
  const assembliesApi = useApi("/api/v1/tooling-assemblies");
  const revisionsApi = useApi("/api/v1/tooling-assemblies/revisions");
  const templatesApi = useApi("/api/v1/tooling-types");
  const componentsApi = useApi("/api/v1/tooling-components");
  const componentTypesApi = useApi("/api/v1/tooling-component-types");
  const installationsApi = useApi("/api/v1/tooling-installations?activeOnly=true");
  const assemblies = extractRows(assembliesApi.data);
  const revisions = extractRows(revisionsApi.data);
  const templates = extractRows(templatesApi.data);
  const components = extractRows(componentsApi.data);
  const componentTypes = extractRows(componentTypesApi.data);
  const installations = extractRows(installationsApi.data);
  const errors = [assembliesApi.error, revisionsApi.error, templatesApi.error, componentsApi.error, componentTypesApi.error, installationsApi.error].filter(Boolean);
  const loading = [assembliesApi, revisionsApi, templatesApi, componentsApi, componentTypesApi].some(api => api.loading && !api.data);
  const [assetOpen, setAssetOpen] = useState(false);
  const [assetForm, setAssetForm] = useState({ toolingAssemblyId: "", toolingTypeCode: "", name: "", status: "active" });
  const [revisionTarget, setRevisionTarget] = useState(null);
  const [memberSelection, setMemberSelection] = useState({});
  const [saving, setSaving] = useState(false);
  const [actionError, setActionError] = useState("");

  const latestTemplateByCode = useMemo(() => {
    const result = new Map();
    templates.forEach(template => {
      const previous = result.get(template.toolingTypeCode);
      if (!previous || Number(template.version) > Number(previous.version)) result.set(template.toolingTypeCode, template);
    });
    return result;
  }, [templates]);
  const revisionsByMold = useMemo(() => {
    const result = new Map();
    revisions.forEach(revision => {
      const values = result.get(revision.toolingAssemblyId) || [];
      values.push(revision);
      result.set(revision.toolingAssemblyId, values);
    });
    result.forEach(values => values.sort((left, right) => Number(right.revision) - Number(left.revision)));
    return result;
  }, [revisions]);
  const activeInstallationByRevision = new Map(installations.map(value => [value.assemblyRevisionId, value]));

  async function reloadAll() {
    await Promise.all([
      assembliesApi.reload(), revisionsApi.reload(), templatesApi.reload(),
      componentsApi.reload(), componentTypesApi.reload(), installationsApi.reload(),
    ]);
  }

  async function saveAsset() {
    setSaving(true);
    setActionError("");
    try {
      await postJson("/api/v1/tooling-assemblies", assetForm);
      setAssetOpen(false);
      setAssetForm({ toolingAssemblyId: "", toolingTypeCode: "", name: "", status: "active" });
      await reloadAll();
      notify("工装总成已建立，请继续创建首个配置版本。", "success");
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setSaving(false);
    }
  }

  function openRevision(assembly) {
    const previous = (revisionsByMold.get(assembly.toolingAssemblyId) || [])[0];
    setRevisionTarget(assembly);
    setMemberSelection(Object.fromEntries((previous?.members || []).map(member => [member.roleCode, member.componentId])));
    setActionError("");
  }

  async function saveRevision() {
    if (!revisionTarget) return;
    const template = latestTemplateByCode.get(revisionTarget.toolingTypeCode);
    const previous = (revisionsByMold.get(revisionTarget.toolingAssemblyId) || [])[0];
    setSaving(true);
    setActionError("");
    try {
      await postJson(`/api/v1/tooling-assemblies/${encodeURIComponent(revisionTarget.toolingAssemblyId)}/revisions`, {
        toolingAssemblyId: revisionTarget.toolingAssemblyId,
        revision: Number(previous?.revision || 0) + 1,
        members: (template?.roles || []).map(role => ({ roleCode: role.code, componentId: memberSelection[role.code] })),
        createdBy: "operator",
        createdAt: new Date().toISOString(),
      });
      setRevisionTarget(null);
      setMemberSelection({});
      await reloadAll();
      notify("新的不可变配置版本已建立；历史版本保持不变。", "success");
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setSaving(false);
    }
  }

  const revisionTemplate = revisionTarget ? latestTemplateByCode.get(revisionTarget.toolingTypeCode) : null;
  const selectedIds = new Set(Object.values(memberSelection).filter(Boolean));
  const revisionValid = Boolean(revisionTemplate?.roles?.length) && revisionTemplate.roles.every(role => memberSelection[role.code]) &&
    selectedIds.size === revisionTemplate.roles.length;
  const activeTemplates = [...latestTemplateByCode.values()].filter(template => template.status !== "inactive");

  return (
    <Page
      title="工装总成"
      description="一个工装总成拥有稳定身份；每次组件更换形成新的不可变配置版本，生产运行自动保留当时的真实组成。"
      actions={<Button variant="primary" onClick={() => { setActionError(""); setAssetOpen(true); }}>新建工装总成</Button>}
    >
      {(errors.length > 0 || actionError) && <Alert tone="danger">{errors[0] || actionError}</Alert>}
      <WorkflowGuide
        title="工装总成数据的正确关系"
        description="组件分类说明“是什么”，装配模板说明“装在哪里”，配置版本说明“这次具体装了哪一件”。"
        steps={[
          { title: "登记组件资产", description: "每个模芯、模架使用独立资产编号和序列号。", state: components.length ? "done" : "current" },
          { title: "建立工装总成配置", description: "按装配位置选择实际组件，形成不可变版本。", state: revisions.length ? "done" : components.length ? "current" : "upcoming" },
          { title: "装入生产设备", description: "安装后新运行自动关联工装总成及全部成员。", state: installations.length ? "done" : revisions.length ? "current" : "upcoming" },
        ]}
      />
      {loading ? <LoadingCard /> : assemblies.length === 0 ? (
        <EmptyState title="还没有工装总成" description="先准备组件分类、组件资产和装配模板，再建立工装总成身份。" />
      ) : (
        <div className="grid gap-5">
          {assemblies.map(assembly => {
            const assemblyRevisions = revisionsByMold.get(assembly.toolingAssemblyId) || [];
            const latest = assemblyRevisions[0];
            const template = latestTemplateByCode.get(assembly.toolingTypeCode);
            const installation = latest ? activeInstallationByRevision.get(latest.assemblyRevisionId) : null;
            return (
              <Card
                key={assembly.toolingAssemblyId}
                title={assembly.name}
                description={`${assembly.toolingAssemblyId} · ${template?.name || assembly.toolingTypeCode}`}
                actions={<Button onClick={() => openRevision(assembly)}>{latest ? "更换组件并创建新版本" : "建立首个配置版本"}</Button>}
              >
                <ToolingRevisionComposition
                  revision={latest}
                  template={template}
                  components={components}
                  componentTypes={componentTypes}
                  installation={installation}
                />
                {assemblyRevisions.length > 1 && (
                  <details className="mt-4 rounded-xl border border-slate-200 p-4">
                    <summary className="cursor-pointer text-sm font-medium text-slate-700">查看全部 {assemblyRevisions.length} 个配置版本</summary>
                    <div className="mt-4 grid gap-5">
                      {assemblyRevisions.slice(1).map(revision => (
                        <ToolingRevisionComposition
                          key={revision.assemblyRevisionId}
                          revision={revision}
                          template={template}
                          components={components}
                          componentTypes={componentTypes}
                          installation={activeInstallationByRevision.get(revision.assemblyRevisionId)}
                        />
                      ))}
                    </div>
                  </details>
                )}
              </Card>
            );
          })}
        </div>
      )}
      <Drawer
        open={assetOpen}
        onClose={() => setAssetOpen(false)}
        title="新建工装总成"
        description="建立长期稳定的工装总成身份；具体成员在下一步配置版本中选择。"
        footer={<><Button onClick={() => setAssetOpen(false)}>取消</Button><Button variant="primary" disabled={saving || !assetForm.toolingAssemblyId.trim() || !assetForm.name.trim() || !assetForm.toolingTypeCode} onClick={saveAsset}>{saving ? "保存中" : "保存并继续"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <div className="grid gap-4">
          <Field label="工装总成编号"><Input value={assetForm.toolingAssemblyId} onChange={event => setAssetForm(current => ({ ...current, toolingAssemblyId: event.target.value }))} /></Field>
          <Field label="工装总成名称"><Input value={assetForm.name} onChange={event => setAssetForm(current => ({ ...current, name: event.target.value }))} /></Field>
          <Field label="装配模板">
            <Select value={assetForm.toolingTypeCode} onChange={event => setAssetForm(current => ({ ...current, toolingTypeCode: event.target.value }))}>
              <option value="">请选择</option>
              {activeTemplates.map(template => <option key={template.toolingTypeCode} value={template.toolingTypeCode}>{template.name} · v{template.version}</option>)}
            </Select>
          </Field>
        </div>
      </Drawer>
      <Drawer
        open={Boolean(revisionTarget)}
        onClose={() => setRevisionTarget(null)}
        title={revisionTarget ? `${revisionTarget.name} · 配置版本` : "配置版本"}
        description="每个装配位置选择一件具体组件资产。保存后该版本不可修改，更换组件时创建下一版本。"
        footer={<><Button onClick={() => setRevisionTarget(null)}>取消</Button><Button variant="primary" disabled={saving || !revisionValid} onClick={saveRevision}>{saving ? "保存中" : "创建新版本"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <div className="grid gap-4">
          {(revisionTemplate?.roles || []).map(role => {
            const options = components.filter(component => component.status !== "retired" && role.acceptedComponentTypeCodes.includes(component.componentTypeCode));
            return (
              <Field key={role.code} label={role.name} hint={`允许：${role.acceptedComponentTypeCodes.map(code => componentTypes.find(type => type.componentTypeCode === code)?.name || code).join("、")}`}>
                <Select value={memberSelection[role.code] || ""} onChange={event => setMemberSelection(current => ({ ...current, [role.code]: event.target.value }))}>
                  <option value="">请选择组件资产</option>
                  {options.map(component => (
                    <option key={component.componentId} value={component.componentId} disabled={selectedIds.has(component.componentId) && memberSelection[role.code] !== component.componentId}>
                      {component.name} · {component.serialNo}
                    </option>
                  ))}
                </Select>
              </Field>
            );
          })}
        </div>
      </Drawer>
    </Page>
  );
}

export function ProductionSetupPage({ section }) {
  return section === "assembly"
    ? <ToolingAssembliesPage />
    : <ProductionRecordsPage key={section} section={section} />;
}

const productionAttributeLabels = {
  dataClassification: "数据类型",
  model: "型号",
  productCode: "零件号",
};

const productionAttributeValueLabels = {
  simulated: "模拟数据",
};

function ProductionAttributeSummary({ value }) {
  const entries = Object.entries(value || {});
  if (!entries.length) return <span className="text-slate-400">—</span>;
  return (
    <div className="flex flex-wrap gap-1.5">
      {entries.map(([attribute, attributeValue]) => (
        <span key={attribute} className="inline-flex items-center gap-1 rounded-md bg-slate-100 px-2 py-1 text-xs text-slate-600 ring-1 ring-inset ring-slate-200">
          <span>{productionAttributeLabels[attribute] || attribute}</span>
          <strong className="font-medium text-slate-800">{productionAttributeValueLabels[attributeValue] || String(attributeValue)}</strong>
        </span>
      ))}
    </div>
  );
}

function ProductionRecordsPage({ section }) {
  const resource = productionResources[section];
  const { data, loading, error, reload } = useApi(resource.endpoint);
  const rows = extractRows(data);
  const [open, setOpen] = useState(false);
  const [editor, setEditor] = useState({});
  const [editorBase, setEditorBase] = useState({});
  const [editorMode, setEditorMode] = useState("create");
  const [actionError, setActionError] = useState("");
  const [saving, setSaving] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const pagedRows = rows.slice((page - 1) * pageSize, page * pageSize);
  const editorValid = isProductionEditorValid(resource, editor);
  const activeRows = rows.filter(row => section === "context" ? !row.validTo : section === "installation" ? !row.removedAt : false);

  useEffect(() => {
    setPage(1);
  }, [section]);

  useEffect(() => {
    const pageCount = Math.max(1, Math.ceil(rows.length / pageSize));
    if (page > pageCount) setPage(pageCount);
  }, [page, pageSize, rows.length]);

  function openEditor(row = null) {
    const value = row ? structuredClone(row) : structuredClone(resource.template);
    if (row?.version && section === "type") {
      value.version = Number(row.version) + 1;
      value.status = "active";
    }
    setEditorMode(row ? (section === "type" ? "version" : "edit") : "create");
    setEditorBase(value);
    setEditor(createProductionEditor(resource, value));
    setActionError("");
    setOpen(true);
  }

  async function save() {
    setSaving(true);
    setActionError("");
    try {
      const value = parseProductionEditor(resource, editor, editorBase);
      await postJson(resource.endpoint, resource.prepare ? resource.prepare(value) : value);
      setOpen(false);
      await reload();
      notify(section === "context" ? "生产配置已生效，新开始的运行会自动关联。" : `${resource.title}已保存。`);
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setSaving(false);
    }
  }

  async function lifecycle(row) {
    if (!window.confirm(`确认${resource.lifecycle.label}这条${resource.title}记录？历史引用会继续保留。`)) return;
    try {
      await postJson(resource.lifecycle.url(row), resource.lifecycle.body(row));
      await reload();
      notify(`${resource.lifecycle.label}操作已完成。`);
    } catch (requestError) {
      setActionError(requestError.message);
    }
  }

  async function remove(row) {
    if (!window.confirm("只能删除尚未形成历史引用的数据，是否继续？")) return;
    try {
      await deleteJson(resource.deleteUrl(row));
      await reload();
    } catch (requestError) {
      setActionError(requestError.message);
    }
  }

  const columns = [
    ...resource.columns.map(([key, label]) => ({
      key,
      label,
      primary: key === resource.key,
      render: key.endsWith("At") || ["validFrom", "validTo"].includes(key)
        ? formatTime
        : key === "status" ? value => <StatusBadge value={value} />
          : key === resource.key ? value => <span className="font-mono text-xs font-semibold tracking-tight text-slate-800">{value || "—"}</span>
            : key === "name" ? value => <span className="font-medium text-slate-900">{value || "—"}</span>
              : key === "processSpecificationId" ? (value, row) => `${value} v${row.processSpecificationVersion}`
                : key === "productCode" ? (value, row) => <div><p className="font-medium text-slate-800">{value}</p>{row.productFamilyCode && <p className="mt-0.5 text-xs text-slate-500">{row.productFamilyCode}</p>}</div>
                  : key === "roles" ? value => value?.length ? value.map(role => role.name).join("、") : "—"
                    : key === "attributes" ? value => <ProductionAttributeSummary value={value} />
              : undefined,
    })),
    {
      key: "_actions",
      label: "操作",
      align: "right",
      render: (_value, row) => (
        <div className="flex min-w-max justify-end gap-1">
          {!["context", "installation"].includes(section) && <Button variant="ghost" className="px-2" onClick={() => openEditor(row)}>{section === "type" ? "新版本维护" : "编辑"}</Button>}
          {resource.lifecycle?.visible(row) && <Button variant="ghost" className="px-2 text-amber-700" onClick={() => lifecycle(row)}>{resource.lifecycle.label}</Button>}
          {resource.deleteUrl && <Button variant="ghost" className="px-2 text-rose-700" onClick={() => remove(row)}>删除</Button>}
        </div>
      ),
    },
  ];

  return (
    <Page className="mx-auto max-w-7xl" title={resource.title} description={resource.description} actions={section === "context" ? undefined : <Button variant="primary" onClick={() => openEditor()}>{resource.createLabel}</Button>}>
      {(error || (!open && actionError)) && <Alert tone="danger">{error || actionError}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <>
          {section === "context" && (
            <>
              <WorkflowGuide
                title="生产开始前"
                description="设备接入和工艺规范发布通常只需配置一次；每次换产品或换工艺规范时更新生产配置。"
                steps={[
                  { title: "设备已有数据", description: "在“设备采集”中完成设备接入。", state: rows.length ? "done" : "current" },
                  { title: "产品与工艺规范就绪", description: "准备产品编号和已发布工艺规范。", state: rows.some(row => row.processSpecificationId) ? "done" : rows.length ? "current" : "upcoming" },
                  { title: "启用生产配置", description: "确认设备、产品、工艺规范和当前工装。", state: activeRows.length ? "done" : "current" },
                ]}
              />
              <Card
                title="当前生效配置"
                description={activeRows.length ? `${activeRows.length} 台设备已准备好开始新运行` : "目前没有正在生效的生产配置"}
                actions={<Button variant="primary" onClick={() => openEditor()}>{activeRows.length ? "切换产品或工艺规范" : "开始配置"}</Button>}
              >
                {activeRows.length ? (
                  <div className="grid gap-3 lg:grid-cols-2">
                    {activeRows.map(row => (
                      <article key={row.contextId} className="rounded-xl border border-slate-200 bg-slate-50 p-4">
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <p className="font-semibold text-slate-950">{row.equipmentId}</p>
                            <p className="mt-1 text-sm text-slate-600">{row.productCode} · {row.productFamilyCode || "未填写系列"}</p>
                          </div>
                          <StatusBadge value="active" />
                        </div>
                        <p className="mt-3 text-sm text-slate-600">工艺规范：{row.processSpecificationId} v{row.processSpecificationVersion}</p>
                        <p className="mt-1 text-xs text-slate-400">自 {formatTime(row.validFrom)} 生效</p>
                      </article>
                    ))}
                  </div>
                ) : <EmptyState title="还没有生效配置" description="点击“开始配置”，完成设备、产品和工艺规范选择。" />}
              </Card>
            </>
          )}
          {section === "installation" && (
            <WorkflowGuide
              title="工装装卸怎么用"
              steps={[
                { title: "先建立工装组合", description: "在工装管理中确定工装及其组件版本。", state: rows.length ? "done" : "current" },
                { title: "选择设备并装入", description: "一台设备可保留当前有效工装记录。", state: activeRows.length ? "done" : "current" },
                { title: "换装时先卸下", description: "卸下后历史运行仍保留原工装关联。", state: activeRows.length ? "current" : "upcoming" },
              ]}
            />
          )}
          <Card title={["context", "installation"].includes(section) ? "历史记录" : `${resource.title}列表`} description={`已登记 ${rows.length} 条`}>
            <DataTable
              key={resource.endpoint}
              rows={pagedRows}
              keyField={resource.key}
              getRowKey={section === "type" ? row => `${row[resource.key]}:${row.version ?? 1}` : undefined}
              columns={columns}
            />
            <Pagination
              page={page}
              pageSize={pageSize}
              total={rows.length}
              onPageChange={setPage}
              onPageSizeChange={value => { setPageSize(value); setPage(1); }}
            />
          </Card>
        </>
      )}
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        closeOnBackdrop={false}
        title={editorMode === "create" ? resource.createLabel : editorMode === "version" ? "新版本维护" : `编辑${resource.title}`}
        description={resource.drawerDescription || "填写业务信息后保存，平台会校验引用并保留历史。"}
        footer={<><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving || !editorValid}>{saving ? "保存中" : section === "context" ? "确认并生效" : "保存"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <ProductionRecordForm
          resource={resource}
          editor={editor}
          onChange={(key, value) => setEditor(current => ({ ...current, [key]: value }))}
        />
      </Drawer>
    </Page>
  );
}
