// 展示和维护除工装总成外的生产配置记录。
import { useEffect, useState } from "react";
import { Link } from "react-router";
import { deleteJson, postJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, DataTable, Drawer, EmptyState, Pagination, Page, RequestError, StatusBadge, WorkflowGuide, notify, useConfirmDialog } from "../ui/components";
import { formatTime, LoadingCard } from "./shared";
import { ProductionRecordForm, createProductionEditor, isProductionEditorValid, parseProductionEditor } from "./ProductionRecordForm";
import { productionConditionLabels, productionResources } from "./manufacturingResources";

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

export function ProductionRecordsPage({ section, canWrite = true }) {
  const resource = productionResources[section];
  const [siteId, setSiteId] = useState("");
  const listEndpoint = ["context", "installation"].includes(section) && siteId.trim()
    ? `${resource.endpoint}?siteId=${encodeURIComponent(siteId.trim())}`
    : resource.endpoint;
  const { data, loading, error, reload } = useApi(listEndpoint);
  const rows = extractRows(data);
  const [open, setOpen] = useState(false);
  const [editor, setEditor] = useState({});
  const [editorBase, setEditorBase] = useState({});
  const [editorMode, setEditorMode] = useState("create");
  const [actionError, setActionError] = useState("");
  const [saving, setSaving] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const { confirm, confirmationDialog } = useConfirmDialog();
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
    const value = row
      ? structuredClone(row)
      : { ...structuredClone(resource.template), ...(["context", "installation"].includes(section) && siteId.trim() ? { siteId: siteId.trim() } : {}) };
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

  function openReplacement(row) {
    const value = structuredClone(row);
    setEditorMode("replace");
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
      const prepared = resource.prepare ? resource.prepare(value) : value;
      const request = section === "installation"
        ? {
          siteId: prepared.siteId,
          equipmentId: prepared.equipmentId,
          assemblyRevisionId: prepared.assemblyRevisionId,
          installedAt: prepared.installedAt,
          source: prepared.source,
          commandId: prepared.commandId,
        }
        : section === "context"
          ? {
            siteId: prepared.siteId,
            equipmentId: prepared.equipmentId,
            productFamilyCode: prepared.productFamilyCode,
            productCode: prepared.productCode,
            processSpecificationId: prepared.processSpecificationId,
            processSpecificationVersion: String(prepared.processSpecificationVersion),
            toolingInstallationId: prepared.toolingInstallationId,
            validFrom: prepared.validFrom,
            source: prepared.source,
            commandId: prepared.commandId,
            externalOrderRef: prepared.externalOrderRef || null,
            externalBatchRef: prepared.externalBatchRef || null,
            materialLotRef: prepared.materialLotRef || null,
            materialSpecification: prepared.materialSpecification || null,
            maintenanceStatus: prepared.maintenanceStatus || null,
            calibrationStatus: prepared.calibrationStatus || null,
            calibrationRef: prepared.calibrationRef || null,
            calibrationValidUntil: prepared.calibrationValidUntil,
          }
          : prepared;
      const endpoint = section === "installation"
        ? "/api/v1/tooling-installations:replace"
        : section === "context" ? "/api/v1/production-contexts:replace" : resource.endpoint;
      await postJson(endpoint, request);
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
    if (!await confirm({
      title: `${resource.lifecycle.label}${resource.title}记录`,
      description: "该记录将不再对后续运行生效，已经形成的历史引用会继续保留。",
      confirmLabel: `确认${resource.lifecycle.label}`,
      tone: "danger",
    })) return;
    try {
      await postJson(resource.lifecycle.url(row), resource.lifecycle.body(row));
      await reload();
      notify(`${resource.lifecycle.label}操作已完成。`);
    } catch (requestError) {
      setActionError(requestError.message);
    }
  }

  async function remove(row) {
    if (!await confirm({
      title: `删除${resource.title}记录`,
      description: "只有尚未形成历史引用的记录才能删除。删除后无法恢复。",
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    try {
      await deleteJson(resource.deleteUrl(row));
      await reload();
      notify(`${resource.title}记录已删除。`);
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
                : key === "toolingInstallationId" ? (value, row) => row.toolingAssemblyId || value || "未绑定"
                : key === "productCode" ? (value, row) => <div><p className="font-medium text-slate-800">{value}</p>{row.productFamilyCode && <p className="mt-0.5 text-xs text-slate-500">{row.productFamilyCode}</p>}</div>
                  : key === "roles" ? value => value?.length ? value.map(role => role.name).join("、") : "—"
                    : key === "attributes" ? value => <ProductionAttributeSummary value={value} />
              : undefined,
    })),
    ...(canWrite ? [{
      key: "_actions",
      label: "操作",
      align: "right",
      render: (_value, row) => (
        <div className="flex min-w-max justify-end gap-1">
          {section === "context" && !row.validTo && <Button variant="ghost" className="px-2 text-trajectory-700" onClick={() => openReplacement(row)}>基于此配置切换</Button>}
          {section === "installation" && !row.removedAt && <Button variant="ghost" className="px-2 text-trajectory-700" onClick={() => openReplacement(row)}>替换工装</Button>}
          {!["context", "installation"].includes(section) && <Button variant="ghost" className="px-2" onClick={() => openEditor(row)}>{section === "type" ? "新版本维护" : "编辑"}</Button>}
          {resource.lifecycle?.visible(row) && <Button variant="ghost" className="px-2 text-amber-700" onClick={() => lifecycle(row)}>{resource.lifecycle.label}</Button>}
          {resource.deleteUrl && <Button variant="ghost" className="px-2 text-rose-700" onClick={() => remove(row)}>删除</Button>}
        </div>
      ),
    }] : []),
  ];

  return (
    <Page className="mx-auto max-w-7xl" title={resource.title} actions={canWrite && section !== "context" ? <Button variant="primary" onClick={() => openEditor()}>{resource.createLabel}</Button> : undefined}>
      <RequestError error={error} onRetry={reload} />
      {!open && actionError && <Alert tone="danger">{actionError}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <>
          {["context", "installation"].includes(section) && (
            <div className="flex flex-wrap items-end gap-3">
              <div className="w-full sm:w-64">
                <label className="mb-1.5 block text-sm font-medium text-slate-700" htmlFor={`${section}-site-filter`}>站点</label>
                <input
                  id={`${section}-site-filter`}
                  className="block min-h-10 w-full rounded-lg border border-slate-300 bg-white px-3 text-sm text-slate-900 shadow-sm outline-none placeholder:text-slate-400 focus:border-trajectory-500 focus:ring-2 focus:ring-trajectory-100"
                  value={siteId}
                  placeholder="输入 SiteId 筛选记录"
                  onChange={event => setSiteId(event.target.value)}
                />
              </div>
            </div>
          )}
          {section === "context" && (
            <>
              <WorkflowGuide
                title="生产开始前"
                description="现场接入和工艺规范发布通常只需配置一次；每次换产品或换工艺规范时更新生产配置。"
                steps={[
                  { title: "设备已有数据", description: "在“现场接入”中完成数据源配置。", state: rows.length ? "done" : "current" },
                  { title: "产品与工艺规范就绪", description: "准备产品编号和已发布工艺规范。", state: rows.some(row => row.processSpecificationId) ? "done" : rows.length ? "current" : "upcoming" },
                  { title: "启用生产配置", description: "确认设备、产品、工艺规范和当前工装。", state: activeRows.length ? "done" : "current" },
                ]}
              />
              <Card
                title="当前生效配置"
                description={activeRows.length ? `${activeRows.length} 台设备已准备好开始新运行` : "目前没有正在生效的生产配置"}
                actions={(
                  <div className="flex flex-wrap items-center gap-2">
                    {activeRows.length > 0 && <Link className="inline-flex min-h-9 items-center rounded-lg px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100" to="/process-executions">查看运行记录</Link>}
                    {canWrite && <Button variant="primary" onClick={() => openEditor()}>{activeRows.length ? "切换产品或工艺规范" : "开始配置"}</Button>}
                  </div>
                )}
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
                          <div className="flex items-center gap-2">
                            <StatusBadge value="active" />
                            {canWrite && <Button variant="ghost" className="min-h-8 px-2 text-trajectory-700" onClick={() => openReplacement(row)}>切换此设备配置</Button>}
                          </div>
                        </div>
                        <dl className="mt-4 grid gap-x-4 gap-y-3 border-t border-slate-200 pt-4 text-sm sm:grid-cols-2">
                          {[
                            ["工艺规范", `${row.processSpecificationId} v${row.processSpecificationVersion}`],
                            ["当前工装", row.toolingAssemblyId || row.toolingInstallationId || "未绑定"],
                            ["生产批次", row.externalBatchRef || "未填写"],
                            ["物料批次", row.materialLotRef || "未填写"],
                            ["材料规格", row.materialSpecification || "未填写"],
                            ["维护状态", productionConditionLabels[row.maintenanceStatus] || row.maintenanceStatus || "未记录"],
                            ["校准状态", productionConditionLabels[row.calibrationStatus] || row.calibrationStatus || "未记录"],
                            ["校准有效期", row.calibrationValidUntil ? formatTime(row.calibrationValidUntil) : "未记录"],
                          ].map(([label, value]) => (
                            <div key={label}>
                              <dt className="text-xs font-medium text-slate-500">{label}</dt>
                              <dd className="mt-1 font-medium text-slate-800">{value}</dd>
                            </div>
                          ))}
                        </dl>
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
        title={editorMode === "create" ? resource.createLabel : editorMode === "version" ? "新版本维护" : editorMode === "replace" ? section === "context" ? "切换生产配置" : "替换已装工装" : `编辑${resource.title}`}
        description={editorMode === "replace"
          ? section === "context" ? "以当前记录为起点调整下一次生产配置。保存后旧记录结束，新记录只对后续运行生效。" : "以当前装卸记录为起点替换工装。保存后旧记录结束，后续运行关联新工装。"
          : resource.drawerDescription || "填写业务信息后保存，平台会校验引用并保留历史。"}
        footer={<><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving || !editorValid}>{saving ? "保存中" : editorMode === "replace" ? section === "context" ? "确认切换" : "确认替换" : section === "context" ? "确认并生效" : "保存"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <ProductionRecordForm
          resource={resource}
          editor={editor}
          editorMode={editorMode}
          onChange={(key, value) => setEditor(current => ({ ...current, [key]: value }))}
        />
      </Drawer>
      {confirmationDialog}
    </Page>
  );
}
