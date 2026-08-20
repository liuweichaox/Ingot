// 组装 ResearchAssetsPage 的页面状态与用户交互，业务判定由服务端完成。

import { useCallback, useEffect, useState } from "react";
import { getJson } from "../api/http";
import { MechanismKnowledgeWorkbench } from "../components/MechanismKnowledgeWorkbench";
import {
  Alert,
  Button,
  Card,
  DataTable,
  EmptyState,
  Field,
  Page,
  Select,
  StatusBadge,
} from "../ui/components";

const assetDefinitions = [
  {
    key: "datasets",
    title: "数据集快照",
    description: "用于复算实验结果和模型训练的版本化输入。",
    endpoint: "/api/v1/training-datasets",
    rowKey: row => `${row.datasetId}:${row.version}`,
    columns: [
      { key: "name", label: "数据集" },
      { key: "version", label: "版本" },
      { key: "rowCount", label: "样本量" },
      { key: "createdAt", label: "创建时间", render: formatTime },
    ],
  },
  {
    key: "models",
    title: "数据模型",
    description: "由研发数据训练、评估并保留适用范围的模型版本。",
    endpoint: "/api/v1/process-models",
    rowKey: row => `${row.modelId}:${row.version}`,
    columns: [
      { key: "name", label: "模型", render: (value, row) => value || row.modelId },
      { key: "version", label: "版本" },
      { key: "status", label: "状态", render: status },
      { key: "outputCode", label: "目标输出" },
    ],
  },
  {
    key: "mechanisms",
    title: "机理模型",
    description: "表达物理规律、边界和工程约束的版本化模型。",
    endpoint: "/api/v1/mechanism-models",
    rowKey: row => `${row.modelId}:${row.version}`,
    columns: [
      { key: "name", label: "机理模型", render: (value, row) => value || row.modelId },
      { key: "version", label: "版本" },
      { key: "status", label: "状态", render: status },
      { key: "outputCode", label: "输出" },
    ],
  },
  {
    key: "fusions",
    title: "融合定义",
    description: "说明机理模型如何参与数据分析和模型输出。",
    endpoint: "/api/v1/mechanism-fusions",
    rowKey: row => `${row.fusionId}:${row.version}`,
    columns: [
      { key: "name", label: "融合定义", render: (value, row) => value || row.fusionId },
      { key: "version", label: "版本" },
      { key: "mode", label: "融合方式" },
      { key: "status", label: "状态", render: status },
    ],
  },
  {
    key: "knowledge",
    title: "知识来源",
    description: "绑定研发项目范围的文档、表格、现场图片和专家记录。",
    endpoint: "/api/v1/process-knowledge",
    rowKey: row => row.sourceId,
    columns: [
      { key: "title", label: "来源" },
      { key: "sourceKind", label: "类型" },
      { key: "status", label: "状态", render: status },
      { key: "updatedAt", label: "最近更新", render: formatTime },
    ],
  },
  {
    key: "quality",
    title: "数据集质量",
    description: "检查数据快照的完整性、单位、范围和复算条件。",
    endpoint: "/api/v1/dataset-quality-validations",
    rowKey: row => row.reportId,
    columns: [
      { key: "datasetId", label: "数据集" },
      { key: "datasetVersion", label: "版本" },
      { key: "status", label: "结果", render: status },
      { key: "createdAt", label: "检查时间", render: formatTime },
    ],
  },
];

export function ResearchAssetsPage() {
  const [assets, setAssets] = useState({});
  const [cursors, setCursors] = useState({});
  const [loadingMore, setLoadingMore] = useState("");
  const [projects, setProjects] = useState([]);
  const [projectId, setProjectId] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    getJson("/api/v1/research-projects?limit=100")
      .then(response => {
        const values = response?.data || [];
        setProjects(values);
        setProjectId(current => current || values[0]?.projectId || "");
      })
      .catch(requestError => setError(requestError.message));
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const results = await Promise.all(assetDefinitions.map(async definition => {
        if (definition.key === "knowledge" && !projectId)
          return [definition.key, { data: [], nextCursor: null }];
        const endpoint = definition.key === "knowledge"
          ? `${definition.endpoint}?projectId=${encodeURIComponent(projectId)}`
          : definition.endpoint;
        const response = await getJson(endpoint);
        return [definition.key, { data: response?.data || [], nextCursor: response?.nextCursor || null }];
      }));
      setAssets(Object.fromEntries(results.map(([key, value]) => [key, value.data])));
      setCursors(Object.fromEntries(results.map(([key, value]) => [key, value.nextCursor])));
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  async function loadMore(definition) {
    const cursor = cursors[definition.key];
    if (!cursor) return;
    setLoadingMore(definition.key);
    setError("");
    try {
      const query = new URLSearchParams({ cursor, limit: "200" });
      if (definition.key === "knowledge") query.set("projectId", projectId);
      const response = await getJson(`${definition.endpoint}?${query}`);
      setAssets(current => {
        const existing = current[definition.key] || [];
        const byKey = new Map(existing.map(row => [definition.rowKey(row), row]));
        (response?.data || []).forEach(row => byKey.set(definition.rowKey(row), row));
        return { ...current, [definition.key]: [...byKey.values()] };
      });
      setCursors(current => ({ ...current, [definition.key]: response?.nextCursor || null }));
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setLoadingMore("");
    }
  }

  return (
    <Page
      title="研发资产"
      description="集中查看项目可复用的数据集、模型、机理和知识；正式研发结论仍在研发项目中形成。"
    >
      {error && <Alert tone="danger">{error}</Alert>}
      <Card title="项目范围" description="知识来源严格按研发项目隔离；其他版本化资产可被授权项目复用。">
        <Field label="当前研发项目">
          <Select value={projectId} onChange={event => setProjectId(event.target.value)}>
            {projects.length === 0 && <option value="">暂无可访问项目</option>}
            {projects.map(project => (
              <option key={project.projectId} value={project.projectId}>{project.name}</option>
            ))}
          </Select>
        </Field>
      </Card>
      {loading ? (
        <Card><p className="py-8 text-center text-sm text-slate-500">正在读取研发资产…</p></Card>
      ) : (
        <div className="space-y-5">
          <MechanismKnowledgeWorkbench projectId={projectId} sources={assets.knowledge || []} reloadAssets={load} />
          {cursors.knowledge && (
            <div className="flex justify-center">
              <Button disabled={loadingMore === "knowledge"} onClick={() => loadMore(assetDefinitions.find(item => item.key === "knowledge"))}>
                {loadingMore === "knowledge" ? "正在加载…" : "加载更多知识来源"}
              </Button>
            </div>
          )}
          <div className="grid gap-5 xl:grid-cols-2">
          {assetDefinitions.filter(definition => definition.key !== "knowledge").map(definition => (
            <Card
              key={definition.key}
              title={definition.title}
              description={definition.description}
            >
              {(assets[definition.key] || []).length === 0 ? (
                <EmptyState title={`暂无${definition.title}`} description="资产会在对应研发任务完成后进入这里。" />
              ) : (
                <DataTable
                  rows={assets[definition.key]}
                  getRowKey={definition.rowKey}
                  columns={definition.columns}
                />
              )}
              {cursors[definition.key] && (
                <div className="mt-4 flex justify-center">
                  <Button disabled={loadingMore === definition.key} onClick={() => loadMore(definition)}>
                    {loadingMore === definition.key ? "正在加载…" : "加载更多"}
                  </Button>
                </div>
              )}
            </Card>
          ))}
          </div>
        </div>
      )}
    </Page>
  );
}

function formatTime(value) {
  return value ? new Date(value).toLocaleString("zh-CN") : "—";
}

function status(value) {
  return <StatusBadge value={value || "unknown"} />;
}
