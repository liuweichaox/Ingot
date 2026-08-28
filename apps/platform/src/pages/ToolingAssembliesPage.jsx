// 展示工装总成及其不可变配置版本。
import { useMemo, useState } from "react";
import { postJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, Drawer, EmptyState, Field, Input, Page, RequestError, Select, StatusBadge, WorkflowGuide, notify } from "../ui/components";
import { formatTime, LoadingCard } from "./shared";

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

export function ToolingAssembliesPage({ canWrite = true }) {
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
      title="实际工装总成"
      actions={canWrite ? <Button variant="primary" onClick={() => { setActionError(""); setAssetOpen(true); }}>新建工装总成</Button> : undefined}
    >
      <RequestError
        error={errors[0]}
        onRetry={() => Promise.all([assembliesApi.reload(), revisionsApi.reload(), templatesApi.reload(), componentsApi.reload(), componentTypesApi.reload(), installationsApi.reload()])}
      />
      {actionError && <Alert tone="danger">{actionError}</Alert>}
      <WorkflowGuide
        title="工装总成数据的正确关系"
        description="组件分类说明“是什么”，工装结构定义说明“装在哪里”，配置版本说明“这次具体装了哪一件”。"
        steps={[
          { title: "登记组件资产", description: "每个模芯、模架使用独立资产编号和序列号。", state: components.length ? "done" : "current" },
          { title: "建立工装总成配置", description: "按装配位置选择实际组件，形成不可变版本。", state: revisions.length ? "done" : components.length ? "current" : "upcoming" },
          { title: "装入生产设备", description: "安装后新运行自动关联工装总成及全部成员。", state: installations.length ? "done" : revisions.length ? "current" : "upcoming" },
        ]}
      />
      {loading ? <LoadingCard /> : assemblies.length === 0 ? (
        <EmptyState title="还没有实际工装总成" description="先准备组件分类、组件资产和工装结构定义，再建立工装总成身份。" />
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
                actions={canWrite ? <Button onClick={() => openRevision(assembly)}>{latest ? "更换组件并创建新版本" : "建立首个配置版本"}</Button> : undefined}
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
          <Field label="工装结构">
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
