import { useCallback, useEffect, useMemo, useState } from "react";
import { getJson, postJson } from "../api/http";
import {
  Alert,
  Button,
  Card,
  DataTable,
  Drawer,
  EmptyState,
  Field,
  Input,
  Metric,
  Page,
  Select,
  StatusBadge,
  Textarea,
  notify,
} from "../ui/components";

const initialForm = {
  code: "",
  name: "",
  processName: "",
  productName: "",
  materialName: "",
  description: "",
  objectiveCode: "",
  objectiveName: "",
  objectiveUnit: "",
  objectiveDirection: "minimize",
  objectiveTarget: "",
  variableCode: "",
  variableName: "",
  variableUnit: "",
  variableLower: "",
  variableUpper: "",
};

const statusLabels = {
  draft: "定义中",
  active: "研发中",
  validating: "验证中",
  completed: "已完成",
  archived: "已归档",
};

export function ResearchProjectsPage() {
  const [projects, setProjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [createOpen, setCreateOpen] = useState(false);
  const [form, setForm] = useState(initialForm);
  const [saving, setSaving] = useState(false);
  const [selected, setSelected] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const response = await getJson("/api/v1/research-projects");
      setProjects(response?.data || []);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const metrics = useMemo(() => ({
    active: projects.filter(project => project.status === "active").length,
    validating: projects.filter(project => project.status === "validating").length,
    completed: projects.filter(project => project.status === "completed").length,
  }), [projects]);

  async function openProject(project) {
    setDetailLoading(true);
    setSelected({ project, hypotheses: [], experiments: [], processWindows: [], knowledgeClaims: [] });
    try {
      setSelected(await getJson(`/api/v1/research-projects/${project.projectId}`));
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setDetailLoading(false);
    }
  }

  async function createProject(event) {
    event.preventDefault();
    setSaving(true);
    try {
      const project = await postJson("/api/v1/research-projects", {
        code: form.code,
        name: form.name,
        processName: form.processName,
        productName: form.productName || null,
        materialName: form.materialName || null,
        description: form.description || null,
        objectives: [
          {
            code: form.objectiveCode,
            name: form.objectiveName,
            unit: form.objectiveUnit,
            direction: form.objectiveDirection,
            target: Number(form.objectiveTarget),
          },
        ],
        variables: [
          {
            code: form.variableCode,
            name: form.variableName,
            role: "control",
            unit: form.variableUnit,
            lowerLimit: Number(form.variableLower),
            upperLimit: Number(form.variableUpper),
          },
        ],
        constraints: [],
      });
      setProjects(current => [project, ...current]);
      setForm(initialForm);
      setCreateOpen(false);
      notify("研发项目已创建，可以继续补充变量、假设和实验。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setSaving(false);
    }
  }

  async function activateProject() {
    if (!selected?.project) return;
    try {
      const project = await postJson(
        `/api/v1/research-projects/${selected.project.projectId}/status`,
        { targetStatus: "active" },
      );
      setSelected(current => ({ ...current, project }));
      setProjects(current => current.map(item => item.projectId === project.projectId ? project : item));
      notify("研发项目已进入执行阶段。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  return (
    <Page
      title="工艺研发项目"
      description="围绕研发目标组织变量、假设、实验、工艺窗口和可复用知识。"
      actions={<Button variant="primary" onClick={() => setCreateOpen(true)}>创建研发项目</Button>}
    >
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="全部项目" value={projects.length} hint="统一研发主线" />
        <Metric label="研发中" value={metrics.active} hint="正在设计和执行实验" />
        <Metric label="验证中" value={metrics.validating} hint="正在确认工艺窗口" />
        <Metric label="已完成" value={metrics.completed} hint="已形成验证结论" />
      </div>
      <Card
        title="研发项目"
        description="从目标进入项目，继续查看当前证据和下一步实验。"
      >
        {loading ? (
          <p className="py-8 text-center text-sm text-slate-500">正在读取研发项目…</p>
        ) : projects.length === 0 ? (
          <EmptyState
            title="还没有研发项目"
            description="创建第一个项目，先定义研发目标和一个可控变量。"
          />
        ) : (
          <DataTable
            rows={projects}
            keyField="projectId"
            onRowClick={openProject}
            columns={[
              { key: "code", label: "项目代码" },
              { key: "name", label: "研发项目" },
              { key: "processName", label: "工艺" },
              { key: "productName", label: "产品", render: value => value || "—" },
              {
                key: "status",
                label: "阶段",
                render: value => <StatusBadge value={statusLabels[value] || value} />,
              },
              {
                key: "objectives",
                label: "目标",
                render: value => `${value?.length || 0} 项`,
              },
              {
                key: "updatedAt",
                label: "最近更新",
                render: value => value ? new Date(value).toLocaleString("zh-CN") : "—",
              },
            ]}
          />
        )}
      </Card>

      <Drawer
        open={createOpen}
        onClose={() => !saving && setCreateOpen(false)}
        title="创建工艺研发项目"
        description="先建立目标和首个可控变量，项目创建后继续完善实验空间。"
        size="xl"
        footer={(
          <>
            <Button disabled={saving} onClick={() => setCreateOpen(false)}>取消</Button>
            <Button variant="primary" disabled={saving} type="submit" form="research-project-form">
              {saving ? "正在创建…" : "创建项目"}
            </Button>
          </>
        )}
      >
        <form id="research-project-form" className="space-y-6" onSubmit={createProject}>
          <Card title="项目范围">
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="项目代码"><Input required value={form.code} onChange={event => setForm({ ...form, code: event.target.value })} placeholder="optical-molding-window" /></Field>
              <Field label="项目名称"><Input required value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} placeholder="光学模压工艺窗口研发" /></Field>
              <Field label="工艺名称"><Input required value={form.processName} onChange={event => setForm({ ...form, processName: event.target.value })} /></Field>
              <Field label="目标产品"><Input value={form.productName} onChange={event => setForm({ ...form, productName: event.target.value })} /></Field>
              <Field label="材料"><Input value={form.materialName} onChange={event => setForm({ ...form, materialName: event.target.value })} /></Field>
              <Field label="项目说明" className="md:col-span-2"><Textarea value={form.description} onChange={event => setForm({ ...form, description: event.target.value })} rows={3} /></Field>
            </div>
          </Card>
          <Card title="首要研发目标">
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="指标代码"><Input required value={form.objectiveCode} onChange={event => setForm({ ...form, objectiveCode: event.target.value })} placeholder="form-error" /></Field>
              <Field label="指标名称"><Input required value={form.objectiveName} onChange={event => setForm({ ...form, objectiveName: event.target.value })} placeholder="面形误差" /></Field>
              <Field label="优化方向"><Select value={form.objectiveDirection} onChange={event => setForm({ ...form, objectiveDirection: event.target.value })}><option value="minimize">越低越好</option><option value="maximize">越高越好</option><option value="target">接近目标</option><option value="range">保持范围</option></Select></Field>
              <Field label="单位"><Input required value={form.objectiveUnit} onChange={event => setForm({ ...form, objectiveUnit: event.target.value })} /></Field>
              <Field label="目标值"><Input required type="number" step="any" value={form.objectiveTarget} onChange={event => setForm({ ...form, objectiveTarget: event.target.value })} /></Field>
            </div>
          </Card>
          <Card title="首个可控变量">
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="变量代码"><Input required value={form.variableCode} onChange={event => setForm({ ...form, variableCode: event.target.value })} placeholder="holding-temperature" /></Field>
              <Field label="变量名称"><Input required value={form.variableName} onChange={event => setForm({ ...form, variableName: event.target.value })} placeholder="保压温度" /></Field>
              <Field label="单位"><Input required value={form.variableUnit} onChange={event => setForm({ ...form, variableUnit: event.target.value })} /></Field>
              <Field label="允许下限"><Input required type="number" step="any" value={form.variableLower} onChange={event => setForm({ ...form, variableLower: event.target.value })} /></Field>
              <Field label="允许上限"><Input required type="number" step="any" value={form.variableUpper} onChange={event => setForm({ ...form, variableUpper: event.target.value })} /></Field>
            </div>
          </Card>
        </form>
      </Drawer>

      <Drawer
        open={Boolean(selected)}
        onClose={() => setSelected(null)}
        title={selected?.project?.name || "研发项目"}
        description={selected?.project?.description || "查看项目目标、研发证据和闭环进度。"}
        size="xl"
        footer={selected?.project?.status === "draft" ? (
          <Button variant="primary" disabled={detailLoading} onClick={activateProject}>开始研发</Button>
        ) : <Button onClick={() => setSelected(null)}>关闭</Button>}
      >
        {selected && (
          <div className="space-y-5">
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <Metric label="研发假设" value={selected.hypotheses?.length || 0} hint="等待验证的规律" />
              <Metric label="实验" value={selected.experiments?.length || 0} hint="计划与已执行实验" />
              <Metric label="工艺窗口" value={selected.processWindows?.length || 0} hint="候选与已验证范围" />
              <Metric label="知识声明" value={selected.knowledgeClaims?.length || 0} hint="可复用工艺知识" />
            </div>
            <Card title="研发目标">
              <DataTable
                rows={selected.project.objectives || []}
                keyField="code"
                columns={[
                  { key: "name", label: "指标" },
                  { key: "direction", label: "方向" },
                  { key: "baseline", label: "基线", render: value => value ?? "—" },
                  { key: "target", label: "目标" },
                  { key: "unit", label: "单位" },
                ]}
              />
            </Card>
            <Card title="可控变量">
              <DataTable
                rows={(selected.project.variables || []).filter(variable => variable.role === "control")}
                keyField="code"
                columns={[
                  { key: "name", label: "变量" },
                  { key: "lowerLimit", label: "下限" },
                  { key: "upperLimit", label: "上限" },
                  { key: "unit", label: "单位" },
                  { key: "dataSource", label: "数据来源", render: value => value || "待关联" },
                ]}
              />
            </Card>
          </div>
        )}
      </Drawer>
    </Page>
  );
}
