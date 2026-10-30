// 提供工艺资料与机理知识的业务入口，明确知识范围、证据边界和 AI 助手的使用关系。
import { useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import { getJson, postForm, postJson } from "../api/http";
import { extractRows, registeredSiteIds, useApi } from "../hooks/useApi";
import { Alert, Button, LinkButton, Page, Card, EmptyState, Field, Input, Select, StatusBadge, Textarea, notify } from "../ui/components";

const knowledgePages = {
  materials: {
    title: "工艺资料",
    description: "管理经过复核、带来源和适用范围的工艺资料，供工艺分析和 AI 助手按授权引用。",
    cards: [
      ["资料来源", "工艺文件、作业指导书、检验规范和工程记录应保留来源、内容哈希与适用范围；只有完成复核的片段才能被助手引用。"],
      ["使用边界", "工艺资料用于解释和约束判断，不替代实际运行、质量结果或工程师决定。"],
    ],
  },
  mechanism: {
    title: "机理知识",
    description: "管理按站点与配方归属、带证据和适用范围的可复核机理声明。",
    cards: [
      ["知识范围", "机理声明可以记录变量作用、方向、约束、冲突和反证条件，但不能脱离所属站点、配方和真实运行证据。"],
      ["验证边界", "知识检索命中不等于机理成立；支持、验证和激活必须继续引用冻结的真实运行结果，并保留工程师决定。"],
    ],
  },
};

function KnowledgePage({ kind }) {
  const content = knowledgePages[kind];
  const [params] = useSearchParams();
  const sourceId = params.get("sourceId");
  const recordId = params.get("recordId");
  const siteId = params.get("siteId") || "";
  const processSpecificationId = params.get("processSpecificationId") || "";
  const sourceParams = new URLSearchParams({ section: "sources" });
  if (siteId) sourceParams.set("siteId", siteId);
  if (processSpecificationId) sourceParams.set("processSpecificationId", processSpecificationId);
  const chatContext = new URLSearchParams({
    contextKind: processSpecificationId ? "process-specification" : siteId ? "site" : "page",
    contextId: processSpecificationId || siteId || "knowledge.materials",
  });
  if (siteId) chatContext.set("contextSiteId", siteId);
  return (
    <Page title={content.title} description={content.description}>
      <KnowledgeCitationCard sourceId={sourceId} recordId={recordId} />
      {kind === "materials" && <Card title="管理资料来源" description="资料按站点管理；上传时可选站点通用或限定到一个已发布配方。抽取片段逐条复核后，审核通过的资料才能被助手引用。">
        <LinkButton to={`/knowledge/mechanism?${sourceParams.toString()}`}>打开资料来源</LinkButton>
      </Card>}
      <div className="grid gap-5 lg:grid-cols-2">
        {content.cards.map(([title, description]) => (
          <Card key={title} title={title}>
            <p className="text-sm leading-6 text-slate-600">{description}</p>
          </Card>
        ))}
      </div>
      <Card title="进入 AI 助手" description="AI 助手只在授权站点和适用范围内检索已复核资料，并返回来源引用。">
        <LinkButton to={`/chat?${chatContext.toString()}`}>打开 AI 助手</LinkButton>
      </Card>
    </Page>
  );
}

function KnowledgeCitationCard({ sourceId, recordId }) {
  const [citation, setCitation] = useState(null);
  const [citationError, setCitationError] = useState("");

  useEffect(() => {
    if (!sourceId || !recordId) {
      setCitation(null);
      setCitationError("");
      return undefined;
    }
    let active = true;
    setCitation(null);
    setCitationError("");
    getJson(`/api/v1/knowledge/sources/${encodeURIComponent(sourceId)}/records/${encodeURIComponent(recordId)}`)
      .then(value => { if (active) setCitation(value); })
      .catch(error => { if (active) setCitationError(error.message || "来源暂时无法读取。"); });
    return () => { active = false; };
  }, [recordId, sourceId]);

  if (!sourceId || !recordId) return null;
  return <Card title="引用来源" description="仅展示当前账号有权访问且已复核的知识片段。">
    {citationError ? <Alert tone="warning">{citationError}</Alert> : citation ? (
      <div className="space-y-4 text-sm text-slate-700">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-500"><span>{citation.source.title}</span><span>{citation.source.fileName}</span>{citation.record.pageOrSheet && <span>{citation.record.pageOrSheet}</span>}{citation.record.region && <span>{citation.record.region}</span>}</div>
        <div className="border-l-2 border-trajectory-500 pl-4 leading-7 text-slate-800">{citation.record.content}</div>
        <p className="text-xs text-slate-400">内容哈希：{citation.record.citation?.contentHash || citation.source.sha256}</p>
      </div>
    ) : <p className="text-sm text-slate-500">正在读取引用来源…</p>}
  </Card>;
}

export function ProcessMaterialsPage() {
  return <KnowledgePage kind="materials" />;
}

export function MechanismKnowledgePage() {
  return <MechanismKnowledgeWorkspace />;
}

function sourceStatusLabel(value) {
  return ({ uploaded: "已上传", indexed: "已抽取", reviewed: "已审核", retired: "已停用" })[value] || value || "未知";
}

function extractionStatusLabel(value) {
  return ({ pending: "待抽取", running: "抽取中", completed: "已完成", failed: "失败" })[value] || value || "未知";
}

function sourceKindLabel(value) {
  return value || "未标注类型";
}

function sourceScopeLabel(source, specifications) {
  const selector = source?.contextSelector || {};
  const scopedValues = Object.entries(selector)
    .filter(([key]) => key.trim().toLowerCase() === "process-specification")
    .map(([, value]) => value);
  if (!scopedValues.length) return "站点通用";
  if (scopedValues.length !== 1 || typeof scopedValues[0] !== "string" || !scopedValues[0].trim())
    return "范围异常 · 不参与检索";
  const processSpecificationId = scopedValues[0].trim();
  const specification = specifications.find(item => item.processSpecificationId === processSpecificationId);
  return `配方限定 · ${specification?.name || processSpecificationId}`;
}

function formatKnowledgeTime(value) {
  if (!value) return "时间未记录";
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? String(value) : date.toLocaleString();
}

function isBlankValue(value) {
  return value === undefined || value === null || value === "";
}

const MECHANISM_API = "/api/v1/mechanism-knowledge";
const mechanismTypes = [
  ["qualitative", "定性关系"], ["monotonic", "单调关系"], ["threshold", "阈值关系"],
  ["interaction", "交互关系"], ["temporal", "时序关系"], ["constraint", "机理约束"],
  ["failure-mode", "失效模式"], ["executable-model", "可执行模型"],
];
const claimStatusLabels = {
  draft: "草稿", reviewed: "已审核", supported: "已支持", validated: "已验证",
  active: "生效中", rejected: "已驳回", falsified: "已证伪", retired: "已退役",
};
const variableRoles = [["cause", "原因"], ["mediator", "中介"], ["outcome", "结果"], ["moderator", "调节因素"]];

function asRows(payload) {
  return extractRows(payload);
}

function emptyClaimForm(scope = {}) {
  return {
    claimId: "", siteCode: scope.siteCode || "", processSpecificationId: scope.processSpecificationId || "",
    name: "", mechanismType: "qualitative", statement: "", expectedSignature: "",
    falsificationCondition: "", evidenceLevel: "engineering-observation",
    variables: [{ variableCode: "", variableRole: "cause", direction: "", delayMilliseconds: "", unit: "" }],
    applicability: [], constraints: [], forbiddenCombinations: [], evidence: [],
  };
}

function claimFormFrom(value, scope) {
  const claimId = String(value?.claimId || "");
  return {
    ...emptyClaimForm(scope), ...value,
    claimId: claimId && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(claimId) ? claimId : "",
    siteCode: scope.siteCode, processSpecificationId: scope.processSpecificationId,
    variables: (value?.variables || []).map(item => ({ ...item, delayMilliseconds: item.delayMilliseconds ?? "" })),
    applicability: (value?.applicability || []).filter(item => !["site", "process-specification"].includes(item.dimensionCode)),
    constraints: (value?.constraints || []).map(item => ({ ...item, minimum: item.minimum ?? "", maximum: item.maximum ?? "" })),
    forbiddenCombinations: (value?.forbiddenCombinations || []).map(item => ({
      ...item,
      factors: (item.factors || []).map(factor => ({ ...factor, minimum: factor.minimum ?? "", maximum: factor.maximum ?? "" })),
    })),
    evidence: value?.evidence || [],
  };
}

function scopedUrl(path, values) {
  const query = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => { if (value) query.set(key, value); });
  return `${MECHANISM_API}/${path}${query.size ? `?${query.toString()}` : ""}`;
}

const knowledgeSourcePageSize = 50;

function mergeKnowledgeSourceRows(existing, incoming) {
  const merged = new Map((existing || []).map(item => [String(item.sourceId), item]));
  (incoming || []).forEach(item => merged.set(String(item.sourceId), item));
  return [...merged.values()];
}

function sourcePageState(scopeKey, cursor = "", items = [], nextCursor = "") {
  return { scopeKey, cursor, items, nextCursor };
}

function MechanismKnowledgeWorkspace() {
  const [searchParams, setSearchParams] = useSearchParams();
  const citationSourceId = searchParams.get("sourceId");
  const citationRecordId = searchParams.get("recordId");
  const identityResponse = useApi("/api/v1/auth/me");
  const edgeResponse = useApi("/api/edges");
  const specificationResponse = useApi("/api/v1/process-specifications");
  const modelResponse = useApi("/api/v1/process-data-models");
  const currentIdentity = identityResponse.data;
  const canWrite = (currentIdentity?.roles || []).some(role => role === "process.engineer" || role === "platform.admin");
  const [activeSection, setActiveSection] = useState(() => {
    const requested = searchParams.get("section");
    return ["claims", "conflicts", "sources"].includes(requested) ? requested : "claims";
  });
  const requestedSection = searchParams.get("section");
  const siteChoices = useMemo(() => {
    const assigned = Array.isArray(currentIdentity?.siteIds) ? currentIdentity.siteIds : [];
    if (assigned.length) return [...new Set(assigned)];
    if ((currentIdentity?.roles || []).includes("platform.admin")) return registeredSiteIds(edgeResponse.data);
    return [];
  }, [currentIdentity, edgeResponse.data]);
  const requestedSiteCode = searchParams.get("siteId") || "";
  const invalidRequestedSite = Boolean(requestedSiteCode && siteChoices.length && !siteChoices.includes(requestedSiteCode));
  const [siteCode, setSiteCode] = useState(() => searchParams.get("siteId") || "");
  const siteSelectionRequested = useRef(false);
  const specifications = useMemo(() => {
    const byId = new Map();
    asRows(specificationResponse.data).filter(item => item.status !== "draft").forEach(item => {
      const existing = byId.get(item.processSpecificationId);
      if (!existing || Number(item.version || 0) > Number(existing.version || 0)) byId.set(item.processSpecificationId, item);
    });
    return [...byId.values()].sort((a, b) => String(a.name || a.processSpecificationId).localeCompare(String(b.name || b.processSpecificationId), "zh-CN"));
  }, [specificationResponse.data]);
  const publishedSpecifications = specifications.filter(item => item.status === "published");
  const [processSpecificationId, setProcessSpecificationId] = useState(() => searchParams.get("processSpecificationId") || "");
  const scopedSpecification = specifications.find(item => item.processSpecificationId === processSpecificationId);
  const scopeReady = Boolean(siteCode && processSpecificationId);
  const claimsResponse = useApi(scopedUrl("claims", { siteCode, processSpecificationId }), { enabled: scopeReady });
  const conflictsResponse = useApi(scopedUrl("conflicts", { siteCode, processSpecificationId }), { enabled: scopeReady });
  const sourceScopeKey = JSON.stringify([siteCode, processSpecificationId]);
  const managedSourceScopeKey = siteCode;
  const [sourcePage, setSourcePage] = useState(() => sourcePageState(""));
  const [managedSourcePage, setManagedSourcePage] = useState(() => sourcePageState(""));
  const visibleSourcePage = sourcePage.scopeKey === sourceScopeKey ? sourcePage : sourcePageState(sourceScopeKey);
  const visibleManagedSourcePage = managedSourcePage.scopeKey === managedSourceScopeKey
    ? managedSourcePage
    : sourcePageState(managedSourceScopeKey);
  const sourcesCursor = visibleSourcePage.cursor;
  const managedSourcesCursor = visibleManagedSourcePage.cursor;
  const sourcesResponse = useApi(scopedUrl("sources", {
    siteCode, processSpecificationId, limit: String(knowledgeSourcePageSize), cursor: sourcesCursor,
  }), { enabled: Boolean(siteCode) });
  const managedSourcesResponse = useApi(scopedUrl("sources/manage", {
    siteCode, limit: String(knowledgeSourcePageSize), cursor: managedSourcesCursor,
  }), { enabled: Boolean(siteCode && canWrite) });
  const [sourceId, setSourceId] = useState("");
  const recordsResponse = useApi(sourceId ? `${MECHANISM_API}/sources/${encodeURIComponent(sourceId)}/records` : "", { enabled: Boolean(sourceId) });
  const [managedSourceId, setManagedSourceId] = useState("");
  const managedSourcePreview = (canWrite ? visibleManagedSourcePage.items : visibleSourcePage.items)
    .find(item => String(item.sourceId) === managedSourceId);
  const managedSourceStatusForPolling = managedSourcePreview?.status;
  const managedSourceExtractionForPolling = managedSourcePreview?.extractionStatus;
  const managedSourceResponse = useApi(managedSourceId ? `${MECHANISM_API}/sources/${encodeURIComponent(managedSourceId)}` : "", {
    enabled: Boolean(managedSourceId && canWrite),
    interval: canWrite && activeSection === "sources" && ["pending", "running"].includes(managedSourceExtractionForPolling)
      && !["reviewed", "retired"].includes(managedSourceStatusForPolling) ? 5000 : 0,
  });
  const claims = asRows(claimsResponse.data);
  const conflicts = asRows(conflictsResponse.data);
  const sources = visibleSourcePage.items;
  const managedSources = canWrite ? visibleManagedSourcePage.items : sources;
  const displayedManagedSourceResponse = canWrite ? managedSourcesResponse : sourcesResponse;
  const selectedManagedSource = managedSourceResponse.data?.data || managedSourcePreview;
  const managedRecordsResponse = useApi(managedSourceId ? `${MECHANISM_API}/sources/${encodeURIComponent(managedSourceId)}/records` : "", {
    enabled: Boolean(managedSourceId && (canWrite || selectedManagedSource?.status === "reviewed")),
    interval: canWrite && activeSection === "sources" && ["pending", "running"].includes(selectedManagedSource?.extractionStatus) && selectedManagedSource.status !== "reviewed" && selectedManagedSource.status !== "retired" ? 5000 : 0,
  });
  const records = asRows(recordsResponse.data).filter(item => String(item.sourceId) === sourceId);
  const managedRecords = asRows(managedRecordsResponse.data).filter(item => String(item.sourceId) === managedSourceId);
  const selectedSource = sources.find(item => String(item.sourceId) === sourceId);
  const selectedModel = asRows(modelResponse.data).find(item =>
    item.modelId === scopedSpecification?.dataModelId && Number(item.version) === Number(scopedSpecification?.dataModelVersion || 1));
  const variableOptions = [
    ...(selectedModel?.controlParameters || []).map(item => ({ ...item, kind: "parameter" })),
    ...(selectedModel?.acquisition?.dataItems || []).map(item => ({ ...item, kind: "dataItem", changeAllowed: false })),
  ];
  const adjustableVariables = variableOptions.filter(item => item.kind === "parameter" && item.changeAllowed);
  const [selectedClaimId, setSelectedClaimId] = useState("");
  const selectedClaim = claims.find(item => String(item.claimId) === selectedClaimId);
  const [editorOpen, setEditorOpen] = useState(false);
  const [claimForm, setClaimForm] = useState(() => emptyClaimForm());
  const [formError, setFormError] = useState("");
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState("");
  const [reviewComment, setReviewComment] = useState("");
  const [conflictDraft, setConflictDraft] = useState({ leftClaimId: "", rightClaimId: "", leftClaimVersion: "", rightClaimVersion: "", conflictKind: "", rationale: "" });
  const [conflictResolution, setConflictResolution] = useState("");
  const [selectedConflictId, setSelectedConflictId] = useState("");
  const selectedConflict = conflicts.find(item => String(item.conflictId) === selectedConflictId);
  const [suggestionFocus, setSuggestionFocus] = useState("");
  const [lifecycleTarget, setLifecycleTarget] = useState("");
  const [lifecycleEvidence, setLifecycleEvidence] = useState({ referenceId: "", contentHash: "", evaluationSummary: "", comment: "" });
  const [uploadForm, setUploadForm] = useState({ title: "", sourceKind: "document", processSpecificationId: "", file: null });
  const [extractSubmittedId, setExtractSubmittedId] = useState("");

  useEffect(() => {
    if (!siteChoices.length) return;
    if (requestedSiteCode) {
      setSiteCode(siteChoices.includes(requestedSiteCode) ? requestedSiteCode : "");
      return;
    }
    setSiteCode(current => siteChoices.includes(current) ? current : siteChoices.length === 1 ? siteChoices[0] : "");
  }, [requestedSiteCode, siteChoices]);
  useEffect(() => {
    setSourceId("");
    setManagedSourceId("");
  }, [siteCode]);
  useEffect(() => {
    if (!specifications.length) return;
    const requested = searchParams.get("processSpecificationId");
    const demoSpecification = specifications.find(item =>
      item.processSpecificationId === "wafer-etch-demo-v1" || item.dataModelId === "semiconductor-wafer-etch");
    setProcessSpecificationId(current => specifications.some(item => item.processSpecificationId === requested)
      ? requested
      : specifications.some(item => item.processSpecificationId === current)
        ? current
        : (demoSpecification || specifications[0]).processSpecificationId);
  }, [specifications, searchParams]);
  useEffect(() => {
    if (invalidRequestedSite && !siteSelectionRequested.current) return;
    setSearchParams(current => {
      const next = new URLSearchParams(current);
      if (siteCode) next.set("siteId", siteCode); else next.delete("siteId");
      if (processSpecificationId) next.set("processSpecificationId", processSpecificationId); else next.delete("processSpecificationId");
      return next;
    }, { replace: true });
    siteSelectionRequested.current = false;
  }, [siteCode, processSpecificationId, invalidRequestedSite, setSearchParams]);
  useEffect(() => {
    const nextSection = ["claims", "conflicts", "sources"].includes(requestedSection) ? requestedSection : "claims";
    setActiveSection(current => current === nextSection ? current : nextSection);
  }, [requestedSection]);
  useEffect(() => {
    const payload = sourcesResponse.data;
    if (!payload || sourcesResponse.loading) return;
    setSourcePage(current => {
      const nextItems = current.scopeKey === sourceScopeKey && current.cursor === sourcesCursor
        ? mergeKnowledgeSourceRows(current.items, asRows(payload))
        : asRows(payload);
      return sourcePageState(sourceScopeKey, sourcesCursor, nextItems, payload.nextCursor || "");
    });
  }, [sourceScopeKey, sourcesCursor, sourcesResponse.data, sourcesResponse.loading]);
  useEffect(() => {
    const payload = managedSourcesResponse.data;
    if (!payload || managedSourcesResponse.loading) return;
    setManagedSourcePage(current => {
      const nextItems = current.scopeKey === managedSourceScopeKey && current.cursor === managedSourcesCursor
        ? mergeKnowledgeSourceRows(current.items, asRows(payload))
        : asRows(payload);
      return sourcePageState(managedSourceScopeKey, managedSourcesCursor, nextItems, payload.nextCursor || "");
    });
  }, [managedSourceScopeKey, managedSourcesCursor, managedSourcesResponse.data, managedSourcesResponse.loading]);
  useEffect(() => {
    const refreshedSource = managedSourceResponse.data?.data;
    if (!refreshedSource) return;
    setManagedSourcePage(current => current.scopeKey === managedSourceScopeKey
      ? sourcePageState(current.scopeKey, current.cursor,
        current.items.map(item => String(item.sourceId) === String(refreshedSource.sourceId) ? refreshedSource : item), current.nextCursor)
      : current);
  }, [managedSourceResponse.data, managedSourceScopeKey]);
  useEffect(() => {
    if (!sources.some(item => String(item.sourceId) === sourceId)) setSourceId("");
  }, [sources, sourceId]);
  useEffect(() => {
    if (!managedSourceId) {
      setManagedSourceId(managedSources[0] ? String(managedSources[0].sourceId) : "");
      return;
    }
    if (managedSources.some(item => String(item.sourceId) === managedSourceId)
      || (canWrite && String(managedSourceResponse.data?.data?.sourceId || "") === managedSourceId)
      || (canWrite && managedSourceResponse.loading)) return;
    if (!canWrite) setManagedSourceId("");
  }, [canWrite, managedSources, managedSourceId, managedSourceResponse.data, managedSourceResponse.loading]);
  useEffect(() => {
    if (selectedManagedSource && String(selectedManagedSource.sourceId) === extractSubmittedId && selectedManagedSource.extractionStatus !== "pending") {
      setExtractSubmittedId("");
    }
  }, [selectedManagedSource, extractSubmittedId]);
  useEffect(() => {
    if (!claims.some(item => String(item.claimId) === selectedClaimId)) setSelectedClaimId(claims[0] ? String(claims[0].claimId) : "");
  }, [claims, selectedClaimId]);
  useEffect(() => {
    if (!conflicts.some(item => String(item.conflictId) === selectedConflictId)) setSelectedConflictId(conflicts[0] ? String(conflicts[0].conflictId) : "");
  }, [conflicts, selectedConflictId]);

  function selectSection(section) {
    setActiveSection(section);
    setActionError("");
    setSearchParams(current => {
      const next = new URLSearchParams(current);
      next.set("section", section);
      return next;
    });
  }

  async function refreshSourcePages() {
    const refreshSourcesInPlace = sourcesCursor === "" ? sourcesResponse.reload() : Promise.resolve();
    const refreshManagedInPlace = managedSourcesCursor === "" ? managedSourcesResponse.reload() : Promise.resolve();
    setSourcePage(sourcePageState(sourceScopeKey));
    setManagedSourcePage(sourcePageState(managedSourceScopeKey));
    await Promise.all([refreshSourcesInPlace, refreshManagedInPlace]);
  }
  const refreshScope = async () => {
    await Promise.all([claimsResponse.reload(), conflictsResponse.reload(), refreshSourcePages()]);
  };
  async function refreshSourceDetails() {
    await Promise.all([refreshSourcePages(), managedSourceResponse.reload(), managedRecordsResponse.reload()]);
  }

  function loadMoreSources() {
    if (!visibleSourcePage.nextCursor || sourcesResponse.loading) return;
    if (sourcesCursor === visibleSourcePage.nextCursor && sourcesResponse.error) {
      void sourcesResponse.reload();
      return;
    }
    setSourcePage(current => current.scopeKey === sourceScopeKey
      ? sourcePageState(sourceScopeKey, current.nextCursor, current.items, current.nextCursor)
      : sourcePageState(sourceScopeKey, visibleSourcePage.nextCursor));
  }

  function loadMoreManagedSources() {
    if (!visibleManagedSourcePage.nextCursor || managedSourcesResponse.loading) return;
    if (managedSourcesCursor === visibleManagedSourcePage.nextCursor && managedSourcesResponse.error) {
      void managedSourcesResponse.reload();
      return;
    }
    setManagedSourcePage(current => current.scopeKey === managedSourceScopeKey
      ? sourcePageState(managedSourceScopeKey, current.nextCursor, current.items, current.nextCursor)
      : sourcePageState(managedSourceScopeKey, visibleManagedSourcePage.nextCursor));
  }
  const identityId = currentIdentity?.userId || "";
  const mayReview = claim => Boolean(canWrite && identityId && claim?.status === "draft" && identityId !== claim.createdBy);
  const mayTransition = claim => Boolean(canWrite && identityId && claim && identityId !== claim.createdBy && identityId !== claim.reviewedBy);
  const claimHasOpenConflict = claim => conflicts.some(item => item.status === "open" &&
    (String(item.leftClaimId) === String(claim?.claimId) || String(item.rightClaimId) === String(claim?.claimId)));

  function startNewClaim() {
    if (!canWrite) return;
    setClaimForm(emptyClaimForm({ siteCode, processSpecificationId }));
    setFormError(""); setActionError(""); setEditorOpen(true);
  }
  function startEditClaim(claim) {
    if (!canWrite || claim?.status !== "draft") return;
    setClaimForm(claimFormFrom(claim, { siteCode, processSpecificationId }));
    setFormError(""); setActionError(""); setEditorOpen(true);
  }
  function patchClaim(field, value) { setClaimForm(current => ({ ...current, [field]: value })); }
  function patchArray(field, index, value) {
    setClaimForm(current => ({ ...current, [field]: current[field].map((item, itemIndex) => itemIndex === index ? { ...item, ...value } : item) }));
  }
  function addArrayRow(field, value) { setClaimForm(current => ({ ...current, [field]: [...current[field], value] })); }
  function removeArrayRow(field, index) { setClaimForm(current => ({ ...current, [field]: current[field].filter((_, itemIndex) => itemIndex !== index) })); }

  function selectRecord(record) {
    const evidence = {
      evidenceKind: "knowledge-fragment",
      referenceId: String(record.recordId),
      polarity: "supporting",
      contentHash: record.contentHash || record.citation?.contentHash || selectedSource?.sha256 || "",
    };
    const alreadyAdded = claimForm.evidence.some(item => item.evidenceKind === evidence.evidenceKind && item.referenceId === evidence.referenceId);
    patchClaim("evidence", alreadyAdded
      ? claimForm.evidence.filter(item => !(item.evidenceKind === evidence.evidenceKind && item.referenceId === evidence.referenceId))
      : [...claimForm.evidence, evidence]);
  }

  function validateClaimForm() {
    if (!claimForm.name.trim() || !claimForm.statement.trim() || !claimForm.falsificationCondition.trim()) return "声明名称、机理陈述和反证条件均为必填。";
    if (!claimForm.variables.length || claimForm.variables.some(item => !item.variableCode || !item.variableRole || !item.unit)) return "请补全至少一个变量及其单位。";
    if (!claimForm.evidence.length) return "请从当前站点知识来源中选择至少一条真实片段作为证据。";
    if (claimForm.constraints.some(item => !item.variableCode || !item.constraintKind || (isBlankValue(item.minimum) && isBlankValue(item.maximum)) || !item.unit)) return "请补全机理约束变量、边界和单位。";
    if (claimForm.forbiddenCombinations.some(item => !item.name.trim() || item.factors.length < 2 || item.factors.some(factor => !factor.variableCode || (isBlankValue(factor.minimum) && isBlankValue(factor.maximum)) || !factor.unit))) return "禁止组合需要名称、至少两个变量因子，并为每个因子填写单位和边界。";
    return "";
  }

  async function saveClaim(event) {
    event.preventDefault();
    if (!canWrite) return;
    const validation = validateClaimForm();
    if (validation) { setFormError(validation); return; }
    setBusy(true); setFormError(""); setActionError("");
    try {
      const payload = {
        claimId: claimForm.claimId || undefined,
        siteCode, processSpecificationId, name: claimForm.name.trim(), mechanismType: claimForm.mechanismType,
        statement: claimForm.statement.trim(), expectedSignature: claimForm.expectedSignature.trim() || null,
        falsificationCondition: claimForm.falsificationCondition.trim(), evidenceLevel: claimForm.evidenceLevel,
        variables: claimForm.variables.map(item => ({
          variableCode: item.variableCode, variableRole: item.variableRole,
          direction: item.direction || null,
          delayMilliseconds: item.delayMilliseconds === "" ? null : Number(item.delayMilliseconds), unit: item.unit,
        })),
        applicability: claimForm.applicability.filter(item => item.dimensionCode && item.dimensionValue.trim()).map(item => ({ dimensionCode: item.dimensionCode, dimensionValue: item.dimensionValue.trim() })),
        constraints: claimForm.constraints.map(item => ({
          variableCode: item.variableCode, constraintKind: item.constraintKind,
          minimum: item.minimum === "" ? null : Number(item.minimum), maximum: item.maximum === "" ? null : Number(item.maximum),
          unit: item.unit, severity: item.severity || "hard",
        })),
        forbiddenCombinations: claimForm.forbiddenCombinations.map(item => ({
          name: item.name.trim(),
          factors: item.factors.map(factor => ({
            variableCode: factor.variableCode,
            minimum: factor.minimum === "" ? null : Number(factor.minimum),
            maximum: factor.maximum === "" ? null : Number(factor.maximum),
            unit: factor.unit,
          })),
        })),
        evidence: claimForm.evidence,
      };
      const saved = await postJson(`${MECHANISM_API}/claims`, payload);
      setEditorOpen(false);
      await refreshScope();
      const savedClaim = saved?.claim || saved;
      if (savedClaim?.claimId) setSelectedClaimId(String(savedClaim.claimId));
      notify("机理声明草稿已保存，仍需独立审核和真实运行证据验证。");
    } catch (error) { setFormError(error.message || "草稿保存失败。"); }
    finally { setBusy(false); }
  }

  async function requestSuggestion(event) {
    event.preventDefault();
    if (!canWrite) return;
    if (!sourceId || !processSpecificationId) { setActionError("请先选择配方和真实知识来源。"); return; }
    setBusy(true); setActionError("");
    try {
      const value = await postJson(`${MECHANISM_API}/draft-suggestions`, {
        sourceId, processSpecificationId, focus: suggestionFocus.trim() || null,
      });
      const suggestion = value?.draft || value;
      setClaimForm(claimFormFrom(suggestion, { siteCode, processSpecificationId }));
      setEditorOpen(true); setFormError("");
      notify("已生成未验证的草稿建议。请核对内容、变量和来源片段后再保存。");
    } catch (error) { setActionError(error.message || "草稿建议生成失败。"); }
    finally { setBusy(false); }
  }

  async function reviewClaim(claim, decision) {
    if (!canWrite || !claim || !mayReview(claim)) return;
    setBusy(true); setActionError("");
    try {
      await postJson(`${MECHANISM_API}/claims/${encodeURIComponent(claim.claimId)}/review`, { decision, comment: reviewComment.trim() || null });
      setReviewComment(""); await claimsResponse.reload();
      notify(decision === "approve" ? "审核已提交。" : "已驳回该草稿。");
    } catch (error) { setActionError(error.message || "审核操作失败。"); }
    finally { setBusy(false); }
  }

  function startLifecycle(claim, target) {
    if (!canWrite || !mayTransition(claim)) return;
    if (target === "active" && claimHasOpenConflict(claim)) return;
    setLifecycleTarget(target);
    setLifecycleEvidence({ referenceId: "", contentHash: "", evaluationSummary: "", comment: "" });
  }

  async function submitLifecycle(event) {
    event.preventDefault();
    if (!canWrite || !selectedClaim || !lifecycleTarget) return;
    const needsRunEvidence = ["supported", "validated", "falsified"].includes(lifecycleTarget);
    if (needsRunEvidence && (!lifecycleEvidence.referenceId.trim() || !/^[0-9a-f]{64}$/i.test(lifecycleEvidence.contentHash.trim()) || !lifecycleEvidence.evaluationSummary.trim())) {
      setActionError("请提供真实配方建议运行结果引用、64 位 SHA-256 和证据评价说明。"); return;
    }
    setBusy(true); setActionError("");
    try {
      await postJson(`${MECHANISM_API}/claims/${encodeURIComponent(selectedClaim.claimId)}/lifecycle`, {
        targetStatus: lifecycleTarget,
        ...(needsRunEvidence ? {
          evidenceKind: "recipe-recommendation-outcome", referenceId: lifecycleEvidence.referenceId.trim(),
          contentHash: lifecycleEvidence.contentHash.trim().toLowerCase(),
          evaluationOutcome: lifecycleTarget === "falsified" ? "falsifies" : "supports",
          evaluationSummary: lifecycleEvidence.evaluationSummary.trim(),
        } : {}),
        comment: lifecycleEvidence.comment.trim() || null,
      });
      setLifecycleTarget(""); await claimsResponse.reload();
      notify("生命周期决定已提交。");
    } catch (error) { setActionError(error.message || "生命周期操作失败。"); }
    finally { setBusy(false); }
  }

  async function createConflict(event) {
    event.preventDefault();
    if (!canWrite) return;
    if (!conflictDraft.leftClaimId || !conflictDraft.rightClaimId || conflictDraft.leftClaimId === conflictDraft.rightClaimId || !conflictDraft.conflictKind.trim() || !conflictDraft.rationale.trim()) {
      setActionError("请选择两条不同声明，并填写冲突类型和说明。"); return;
    }
    setBusy(true); setActionError("");
    try {
      await postJson(`${MECHANISM_API}/conflicts`, {
        leftClaimId: conflictDraft.leftClaimId, leftClaimVersion: Number(conflictDraft.leftClaimVersion || claims.find(item => String(item.claimId) === conflictDraft.leftClaimId)?.version || 1),
        rightClaimId: conflictDraft.rightClaimId, rightClaimVersion: Number(conflictDraft.rightClaimVersion || claims.find(item => String(item.claimId) === conflictDraft.rightClaimId)?.version || 1),
        conflictKind: conflictDraft.conflictKind.trim(), rationale: conflictDraft.rationale.trim(),
      });
      setConflictDraft({ leftClaimId: "", rightClaimId: "", leftClaimVersion: "", rightClaimVersion: "", conflictKind: "", rationale: "" });
      await conflictsResponse.reload(); notify("冲突已登记。");
    } catch (error) { setActionError(error.message || "冲突登记失败。"); }
    finally { setBusy(false); }
  }

  async function resolveConflict(event) {
    event.preventDefault();
    if (!canWrite || !selectedConflict || selectedConflict.status !== "open" || identityId === selectedConflict.createdBy || !conflictResolution.trim()) return;
    setBusy(true); setActionError("");
    try {
      await postJson(`${MECHANISM_API}/conflicts/${encodeURIComponent(selectedConflict.conflictId)}/resolve`, { resolution: conflictResolution.trim() });
      setConflictResolution(""); await conflictsResponse.reload(); notify("冲突解决结论已记录。");
    } catch (error) { setActionError(error.message || "冲突解决失败。"); }
    finally { setBusy(false); }
  }

  async function uploadKnowledgeSource(event) {
    event.preventDefault();
    if (!canWrite || !siteCode || !uploadForm.title.trim() || !uploadForm.file) return;
    const body = new FormData();
    body.append("file", uploadForm.file);
    body.append("title", uploadForm.title.trim());
    body.append("sourceKind", uploadForm.sourceKind.trim() || "document");
    if (uploadForm.processSpecificationId) body.append("processSpecificationId", uploadForm.processSpecificationId);
    setBusy(true); setActionError("");
    try {
      body.append("siteCode", siteCode);
      const saved = await postForm(`${MECHANISM_API}/sources`, body);
      const responseData = saved?.data || saved;
      const source = responseData?.source || responseData;
      const registrationStatus = saved?.registrationStatus || responseData?.registrationStatus;
      setUploadForm({ title: "", sourceKind: "document", processSpecificationId: "", file: null });
      if (source?.sourceId && ["pending", "running"].includes(source.extractionStatus)) setExtractSubmittedId(String(source.sourceId));
      const refreshManagedInPlace = managedSourcesCursor === "" ? managedSourcesResponse.reload() : Promise.resolve();
      setManagedSourcePage(sourcePageState(managedSourceScopeKey));
      await refreshManagedInPlace;
      if (source?.sourceId) setManagedSourceId(String(source.sourceId));
      const registrationLabel = registrationStatus === "existing" ? "已复用现有资料来源" : "资料来源已登记或复用";
      const sourceMessage = source?.status === "reviewed"
        ? `${registrationLabel}；来源已复核，可作为证据。`
        : `${registrationLabel}；当前来源状态：${sourceStatusLabel(source?.status)}，抽取状态：${extractionStatusLabel(source?.extractionStatus)}。`;
      notify(sourceMessage);
    } catch (error) { setActionError(error.message || "资料上传失败。"); }
    finally { setBusy(false); }
  }

  async function extractKnowledgeSource(source) {
    if (!canWrite || !source || source.status === "reviewed" || source.status === "retired" || source.extractionStatus === "completed" || source.extractionStatus === "running") return;
    setBusy(true); setActionError("");
    setExtractSubmittedId(String(source.sourceId));
    try {
      await postJson(`${MECHANISM_API}/sources/${encodeURIComponent(source.sourceId)}/extract`);
      await refreshSourceDetails();
      notify("抽取任务已提交，后台处理中；来源状态和片段会自动更新。");
    } catch (error) { setExtractSubmittedId(""); setActionError(error.message || "来源抽取失败，可检查状态后重试。"); await refreshSourceDetails(); }
    finally { setBusy(false); }
  }

  async function indexKnowledgeSource(source) {
    if (!canWrite || !source || source.status !== "uploaded" || source.extractionStatus !== "completed") return;
    setBusy(true); setActionError("");
    try {
      await postJson(`${MECHANISM_API}/sources/${encodeURIComponent(source.sourceId)}/index`);
      await refreshSourceDetails();
      notify("来源索引状态已更新。");
    } catch (error) { setActionError(error.message || "来源索引更新失败。"); }
    finally { setBusy(false); }
  }

  async function reviewKnowledgeRecord(source, record) {
    if (!canWrite || !source || !record || record.humanReviewed || source.status !== "indexed" || source.extractionStatus !== "completed") return;
    setBusy(true); setActionError("");
    try {
      await postJson(`${MECHANISM_API}/sources/${encodeURIComponent(source.sourceId)}/records/${encodeURIComponent(record.recordId)}/review`);
      await refreshSourceDetails();
      notify("片段人工复核已记录。");
    } catch (error) { setActionError(error.message || "片段复核失败。"); }
    finally { setBusy(false); }
  }

  async function reviewKnowledgeSource(source) {
    const recordsAreReviewed = managedRecords.length > 0 && managedRecords.every(item => item.humanReviewed);
    if (!canWrite || !source || source.status !== "indexed" || source.extractionStatus !== "completed" || !recordsAreReviewed) return;
    setBusy(true); setActionError("");
    try {
      await postJson(`${MECHANISM_API}/sources/${encodeURIComponent(source.sourceId)}/review`);
      await refreshSourceDetails();
      notify("来源审核已完成；已复核来源现可用于机理声明证据。");
    } catch (error) { setActionError(error.message || "来源审核失败。"); }
    finally { setBusy(false); }
  }

  const pageError = [identityResponse.error, specificationResponse.error].filter(Boolean).join(" ");
  const hasScopeResources = sources.length > 0;

  return (
    <Page title="机理知识">
      <div className="space-y-5">
        <KnowledgeCitationCard sourceId={citationSourceId} recordId={citationRecordId} />
        {pageError && <Alert tone="danger">{pageError}</Alert>}
        {invalidRequestedSite && <Alert tone="warning">URL 中指定的站点不在当前账号的可访问范围内。请选择一个有权访问的站点后继续。</Alert>}
        <Card title="知识范围" description="资料来源按站点管理，上传时可限定到配方；机理声明和冲突按站点与配方隔离。知识片段本身不构成机理验证。">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="站点">
              <Select value={siteCode} onChange={event => { siteSelectionRequested.current = true; setSiteCode(event.target.value); setSelectedClaimId(""); setEditorOpen(false); }}>
                <option value="">选择站点</option>
                {siteChoices.map(value => <option key={value} value={value}>{value}</option>)}
              </Select>
            </Field>
            <Field label="配方版本" hint="使用该配方最新的非草稿版本定义变量和约束。">
              <Select value={processSpecificationId} onChange={event => { setProcessSpecificationId(event.target.value); setSelectedClaimId(""); setEditorOpen(false); }}>
                <option value="">选择配方</option>
                {specifications.map(item => <option key={item.processSpecificationId} value={item.processSpecificationId}>{item.name} · {item.processSpecificationId} · V{item.version}</option>)}
              </Select>
            </Field>
          </div>
          {(!siteChoices.length || !specifications.length) && <p className="mt-3 text-sm text-amber-800">{!siteChoices.length ? "当前账号没有可用站点。管理员账号可从已注册 Edge 的站点读取。" : "没有可用于机理声明的非草稿配方版本；仍可按站点管理通用资料来源。"}</p>}
          {scopeReady && <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-slate-500"><span>关联变量模型</span><span className="rounded bg-slate-100 px-2 py-1">{selectedModel ? `${selectedModel.name} · V${selectedModel.version}` : "配方引用的数据模型暂不可用"}</span></div>}
        </Card>

        {!siteCode ? (
          <EmptyState title="先选择站点" description="选择有权访问的站点后，可管理资料来源；机理声明和冲突还需要选择配方。" />
        ) : (
          <>
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200">
              <div className="flex gap-1" role="tablist" aria-label="机理知识工作区">
                <button type="button" role="tab" aria-selected={activeSection === "claims"} onClick={() => selectSection("claims")} className={`border-b-2 px-4 py-2.5 text-sm font-semibold ${activeSection === "claims" ? "border-trajectory-600 text-slate-950" : "border-transparent text-slate-500 hover:text-slate-800"}`}>机理声明 <span className="ml-1 text-xs text-slate-400">{claims.length}</span></button>
                <button type="button" role="tab" aria-selected={activeSection === "conflicts"} onClick={() => selectSection("conflicts")} className={`border-b-2 px-4 py-2.5 text-sm font-semibold ${activeSection === "conflicts" ? "border-trajectory-600 text-slate-950" : "border-transparent text-slate-500 hover:text-slate-800"}`}>冲突 <span className="ml-1 text-xs text-slate-400">{conflicts.filter(item => item.status === "open").length} 未解决</span></button>
                <button type="button" role="tab" aria-selected={activeSection === "sources"} onClick={() => selectSection("sources")} className={`border-b-2 px-4 py-2.5 text-sm font-semibold ${activeSection === "sources" ? "border-trajectory-600 text-slate-950" : "border-transparent text-slate-500 hover:text-slate-800"}`}>资料来源 <span className="ml-1 text-xs text-slate-400">已加载 {managedSources.length}</span></button>
              </div>
              {activeSection === "claims" && canWrite && <Button onClick={startNewClaim} disabled={!selectedModel}>新增草稿</Button>}
            </div>

            {actionError && <Alert tone="warning">{actionError}</Alert>}
            {(claimsResponse.error || conflictsResponse.error || sourcesResponse.error || managedSourcesResponse.error || managedSourceResponse.error) && <Alert tone="warning">{claimsResponse.error || conflictsResponse.error || sourcesResponse.error || managedSourcesResponse.error || managedSourceResponse.error}</Alert>}

            {activeSection === "claims" && (!scopeReady ? (
              <EmptyState title="先选择配方范围" description="机理声明按站点与配方隔离。资料来源可先按站点管理，选择可用配方后再查看声明。" />
            ) : editorOpen && canWrite ? (
              <ClaimEditor
                form={claimForm} onChange={patchClaim} onArrayChange={patchArray} onAdd={addArrayRow} onRemove={removeArrayRow}
                onSubmit={saveClaim} onCancel={() => setEditorOpen(false)} busy={busy} error={formError}
                variableOptions={variableOptions} adjustableVariables={adjustableVariables}
                sources={sources} selectedSource={selectedSource} selectedSourceId={sourceId} onSourceChange={setSourceId}
                records={records} recordsLoading={recordsResponse.loading} recordsError={recordsResponse.error}
                onRecordToggle={selectRecord} onSuggest={requestSuggestion} suggestionFocus={suggestionFocus} onSuggestionFocus={setSuggestionFocus}
                hasSources={hasScopeResources} sourcesLoading={sourcesResponse.loading}
                sourcesHasMore={Boolean(visibleSourcePage.nextCursor)} onLoadMoreSources={loadMoreSources}
              />
            ) : (
              <div className="grid items-start gap-4 xl:grid-cols-[minmax(16rem,0.8fr)_minmax(0,1.6fr)]">
                <Card title="声明列表" description="按所选站点和配方版本过滤。">
                  {claimsResponse.loading && !claimsResponse.data ? <p className="text-sm text-slate-500">正在读取声明…</p> : claims.length ? (
                    <div className="space-y-2">
                      {claims.map(claim => <button key={`${claim.claimId}:${claim.version}`} type="button" onClick={() => { setSelectedClaimId(String(claim.claimId)); setActionError(""); }} className={`w-full rounded-md border px-3 py-3 text-left transition ${String(claim.claimId) === selectedClaimId ? "border-trajectory-500 bg-trajectory-50 ring-1 ring-trajectory-200" : "border-slate-200 bg-white hover:border-slate-400"}`}>
                        <div className="flex items-start justify-between gap-3"><span className="font-semibold text-slate-900">{claim.name}</span><StatusBadge value={claim.status} label={claimStatusLabels[claim.status] || claim.status} /></div>
                        <p className="mt-1 line-clamp-2 text-xs leading-5 text-slate-600">{claim.statement}</p>
                        <div className="mt-2 flex gap-2 text-[11px] text-slate-500"><span>V{claim.version}</span><span>{claim.variables?.length || 0} 个变量</span><span>{claim.evidence?.length || 0} 条来源证据</span></div>
                      </button>)}
                    </div>
                  ) : <div className="space-y-3">
                    <EmptyState title="暂无机理声明" description={hasScopeResources ? "可以从真实来源片段开始整理一份未验证草稿。" : canWrite ? "当前范围尚无已复核资料。先进入“资料来源”上传文件并完成片段复核，再创建机理声明。" : "当前范围没有可见的已复核资料，请联系有资料管理权限的工艺工程师。"} />
                    {!hasScopeResources && canWrite && <Button onClick={() => selectSection("sources")}>前往资料来源</Button>}
                  </div>}
                </Card>
                <Card title="声明详情" description="审核、证据升级和生命周期操作按状态逐步开放。">
                  {!selectedClaim ? <EmptyState title="选择一条声明" description="详情和可执行操作会显示在这里。" /> : <ClaimDetails
                    claim={selectedClaim} identityId={identityId} canWrite={canWrite} canReview={mayReview(selectedClaim)} canTransition={mayTransition(selectedClaim)}
                    hasOpenConflict={claimHasOpenConflict(selectedClaim)} conflictStateReady={Boolean(conflictsResponse.data && !conflictsResponse.loading && !conflictsResponse.error)} reviewComment={reviewComment} onReviewComment={setReviewComment}
                    onReview={reviewClaim} onEdit={() => startEditClaim(selectedClaim)} busy={busy}
                    lifecycleTarget={lifecycleTarget} lifecycleEvidence={lifecycleEvidence} onStartLifecycle={startLifecycle}
                    onLifecycleEvidence={value => setLifecycleEvidence(current => ({ ...current, ...value }))} onSubmitLifecycle={submitLifecycle}
                    onCancelLifecycle={() => setLifecycleTarget("")}
                  />}
                </Card>
              </div>
            ))}

            {activeSection === "conflicts" && (scopeReady ? <div className={`grid items-start gap-4 ${canWrite ? "xl:grid-cols-[minmax(18rem,0.9fr)_minmax(0,1.4fr)]" : ""}`}>
              {canWrite && <Card title="登记冲突" description="选择同一范围内的两条机理声明并说明冲突依据。">
                {claims.length < 2 ? <EmptyState title="需要至少两条声明" description="同一站点与配方范围内至少有两条声明后，才可以登记冲突。" /> : <form className="space-y-4" onSubmit={createConflict}>
                  <Field label="左侧声明"><Select value={conflictDraft.leftClaimId} onChange={event => setConflictDraft(current => ({ ...current, leftClaimId: event.target.value, leftClaimVersion: "" }))}><option value="">选择声明</option>{claims.map(item => <option key={item.claimId} value={item.claimId}>{item.name} · V{item.version}</option>)}</Select></Field>
                  <Field label="右侧声明"><Select value={conflictDraft.rightClaimId} onChange={event => setConflictDraft(current => ({ ...current, rightClaimId: event.target.value, rightClaimVersion: "" }))}><option value="">选择不同声明</option>{claims.filter(item => String(item.claimId) !== conflictDraft.leftClaimId).map(item => <option key={item.claimId} value={item.claimId}>{item.name} · V{item.version}</option>)}</Select></Field>
                  <Field label="冲突类型"><Input value={conflictDraft.conflictKind} onChange={event => setConflictDraft(current => ({ ...current, conflictKind: event.target.value }))} placeholder="填写冲突分类" /></Field>
                  <Field label="冲突说明"><Textarea rows={4} value={conflictDraft.rationale} onChange={event => setConflictDraft(current => ({ ...current, rationale: event.target.value }))} placeholder="记录不一致的观测、适用范围或推理依据" /></Field>
                  <Button type="submit" variant="primary" disabled={busy}>登记冲突</Button>
                </form>}
              </Card>}
              <div className="grid items-start gap-4 lg:grid-cols-[minmax(15rem,0.85fr)_minmax(0,1.2fr)]">
                <Card title="冲突列表">{conflicts.length ? <div className="space-y-2">{conflicts.map(item => <button key={item.conflictId} type="button" onClick={() => setSelectedConflictId(String(item.conflictId))} className={`w-full rounded-md border px-3 py-3 text-left ${String(item.conflictId) === selectedConflictId ? "border-trajectory-500 bg-trajectory-50" : "border-slate-200 hover:border-slate-400"}`}><div className="flex justify-between gap-2"><span className="font-medium text-slate-900">{item.conflictKind}</span><StatusBadge value={item.status} label={item.status === "resolved" ? "已解决" : "待解决"} /></div><p className="mt-1 text-xs text-slate-600">声明 {String(item.leftClaimId).slice(0, 8)} ↔ {String(item.rightClaimId).slice(0, 8)}</p></button>)}</div> : <EmptyState title="暂无冲突记录" description={canWrite ? "发现互相矛盾的声明时，可在左侧登记并保留依据。" : "当前范围暂无冲突记录。"} />}</Card>
                <Card title="冲突详情">{!selectedConflict ? <EmptyState title="选择一条冲突" /> : <div className="space-y-4 text-sm"><div className="flex items-center justify-between"><h3 className="font-semibold text-slate-900">{selectedConflict.conflictKind}</h3><StatusBadge value={selectedConflict.status} label={selectedConflict.status === "resolved" ? "已解决" : "待解决"} /></div><p className="whitespace-pre-wrap leading-6 text-slate-700">{selectedConflict.rationale}</p><p className="text-xs text-slate-500">左侧 {selectedConflict.leftClaimId} · V{selectedConflict.leftClaimVersion} / 右侧 {selectedConflict.rightClaimId} · V{selectedConflict.rightClaimVersion}</p>{selectedConflict.resolution && <div className="rounded-md bg-emerald-50 p-3 text-emerald-900"><div className="mb-1 text-xs font-semibold">解决结论</div>{selectedConflict.resolution}</div>}{canWrite && selectedConflict.status === "open" && identityId && identityId !== selectedConflict.createdBy && <form className="space-y-3 border-t border-slate-200 pt-4" onSubmit={resolveConflict}><Field label="解决结论"><Textarea rows={3} value={conflictResolution} onChange={event => setConflictResolution(event.target.value)} placeholder="记录独立复核后的处理结论" /></Field><Button type="submit" variant="primary" disabled={busy || !conflictResolution.trim()}>解决冲突</Button></form>}{canWrite && selectedConflict.status === "open" && identityId === selectedConflict.createdBy && <p className="text-xs text-amber-800">冲突登记人不能独自解决该冲突。</p>}</div>}</Card>
              </div>
            </div> : <EmptyState title="先选择配方范围" description="冲突记录按站点与配方隔离。资料来源可先按站点管理，选择可用配方后再查看冲突。" />)}

            {activeSection === "sources" && <div className="space-y-4">
              {canWrite && <Card title="上传工艺资料" description="站点通用资料可供本站点的所有配方引用；选择配方后只适用于该配方。同一文件的适用范围不可通过重复上传更改。抽取片段须逐条复核后才能用于机理声明。">
                <form className="grid items-end gap-3 md:grid-cols-2 xl:grid-cols-[1.1fr_0.7fr_1.1fr_1.4fr_auto]" onSubmit={uploadKnowledgeSource}>
                  <Field label="资料标题"><Input value={uploadForm.title} onChange={event => setUploadForm(current => ({ ...current, title: event.target.value }))} required /></Field>
                  <Field label="来源类型"><Input value={uploadForm.sourceKind} onChange={event => setUploadForm(current => ({ ...current, sourceKind: event.target.value }))} placeholder="document" required /></Field>
                  <Field label="适用范围"><Select value={uploadForm.processSpecificationId} onChange={event => setUploadForm(current => ({ ...current, processSpecificationId: event.target.value }))}><option value="">站点通用</option>{publishedSpecifications.map(item => <option key={item.processSpecificationId} value={item.processSpecificationId}>{item.name} · {item.processSpecificationId}</option>)}</Select></Field>
                  <Field label="文件"><input key={uploadForm.file?.name || "empty-file"} type="file" onChange={event => setUploadForm(current => ({ ...current, file: event.target.files?.[0] || null }))} className="block min-h-10 w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-700 file:mr-3 file:rounded file:border-0 file:bg-slate-100 file:px-3 file:py-1 file:text-xs file:font-semibold" required /></Field>
                  <Button type="submit" variant="primary" disabled={busy || !siteCode || !uploadForm.title.trim() || !uploadForm.file}>上传资料</Button>
                </form>
              </Card>}
              <div className="grid items-start gap-4 xl:grid-cols-[minmax(18rem,0.85fr)_minmax(0,1.6fr)]">
                <Card title="来源列表" description={canWrite ? "列出当前站点各状态来源，并显示每份资料的适用范围。" : "只列出已审核且适用于所选配方的资料；未选择配方时仅列出站点通用资料。"}
                  actions={<Button variant="ghost" disabled={displayedManagedSourceResponse.loading}
                    onClick={() => void refreshSourcePages()}>刷新来源</Button>}>
                  {displayedManagedSourceResponse.loading && !displayedManagedSourceResponse.data ? <p className="text-sm text-slate-500">正在读取资料来源…</p> : managedSources.length ? <div className="space-y-2">
                    {managedSources.map(source => <button key={source.sourceId} type="button" onClick={() => setManagedSourceId(String(source.sourceId))} className={`w-full rounded-md border px-3 py-3 text-left ${String(source.sourceId) === managedSourceId ? "border-trajectory-500 bg-trajectory-50 ring-1 ring-trajectory-200" : "border-slate-200 hover:border-slate-400"}`}>
                      <div className="flex items-start justify-between gap-2"><span className="font-semibold text-slate-900">{source.title}</span><StatusBadge value={source.status} label={sourceStatusLabel(source.status)} /></div>
                      <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-slate-500"><span>{source.fileName}</span><span>{sourceKindLabel(source.sourceKind)}</span><span>{sourceScopeLabel(source, specifications)}</span><span>抽取：{extractionStatusLabel(source.extractionStatus)}</span></div>
                    </button>)}
                  </div> : <EmptyState title={canWrite ? "暂无资料来源" : "暂无已复核来源"} description={canWrite ? "上传真实工艺资料后，可在此抽取并复核片段。" : "当前站点没有可读的已复核资料来源。"} />}
                  {managedSources.length > 0 && <div className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-3 text-xs text-slate-500">
                    <span>当前已加载 {managedSources.length} 项{canWrite ? "管理来源" : "已审核来源"}</span>
                    {(canWrite ? visibleManagedSourcePage.nextCursor : visibleSourcePage.nextCursor) && <Button variant="ghost"
                      disabled={canWrite ? managedSourcesResponse.loading : sourcesResponse.loading}
                      onClick={canWrite ? loadMoreManagedSources : loadMoreSources}>
                      {(canWrite ? managedSourcesResponse.loading : sourcesResponse.loading) ? "正在加载…"
                        : (canWrite ? managedSourcesResponse.error : sourcesResponse.error) ? "重试加载" : "加载更多来源"
                      }
                    </Button>}
                  </div>}
                </Card>
                <Card title="来源详情" description="抽取片段需逐条人工复核；审核来源前要求抽取完成且所有片段通过复核。">
                  {!selectedManagedSource ? <EmptyState title="选择一项资料来源" description="来源文件信息、抽取状态和片段复核记录会显示在这里。" /> : <div className="space-y-4">
                    <div className="flex flex-wrap items-start justify-between gap-3"><div><h3 className="text-lg font-semibold text-slate-950">{selectedManagedSource.title}</h3><div className="mt-2 flex flex-wrap items-center gap-2"><StatusBadge value={selectedManagedSource.status} label={sourceStatusLabel(selectedManagedSource.status)} /><span className="text-xs text-slate-500">{sourceScopeLabel(selectedManagedSource, specifications)}</span><span className="text-xs text-slate-500">抽取：{extractionStatusLabel(selectedManagedSource.extractionStatus)}</span><span className="text-xs text-slate-500">{selectedManagedSource.sourceKind}</span></div></div>
                      {canWrite && selectedManagedSource.status !== "reviewed" && selectedManagedSource.status !== "retired" && selectedManagedSource.extractionStatus !== "completed" && <Button onClick={() => extractKnowledgeSource(selectedManagedSource)} disabled={busy || selectedManagedSource.extractionStatus === "running" || extractSubmittedId === String(selectedManagedSource.sourceId)}>{selectedManagedSource.extractionStatus === "running" ? "抽取中" : extractSubmittedId === String(selectedManagedSource.sourceId) ? "已提交，等待抽取" : selectedManagedSource.extractionStatus === "failed" ? "重试抽取" : "开始抽取"}</Button>}
                      {canWrite && selectedManagedSource.status === "uploaded" && selectedManagedSource.extractionStatus === "completed" && <Button onClick={() => indexKnowledgeSource(selectedManagedSource)} disabled={busy}>补全索引状态</Button>}
                      {canWrite && selectedManagedSource.status === "indexed" && selectedManagedSource.extractionStatus === "completed" && <Button variant="primary" onClick={() => reviewKnowledgeSource(selectedManagedSource)} disabled={busy || managedRecords.length === 0 || managedRecords.some(item => !item.humanReviewed)}>审核来源</Button>}
                    </div>
                    <dl className="grid gap-3 rounded-md bg-slate-50 p-3 text-xs sm:grid-cols-2"><div><dt className="text-slate-500">文件名</dt><dd className="mt-1 break-all font-medium text-slate-800">{selectedManagedSource.fileName}</dd></div><div><dt className="text-slate-500">SHA-256</dt><dd className="mt-1 break-all font-mono text-slate-700">{selectedManagedSource.sha256}</dd></div><div><dt className="text-slate-500">上传人 / 时间</dt><dd className="mt-1 text-slate-800">{selectedManagedSource.uploadedBy || "未知"} · {formatKnowledgeTime(selectedManagedSource.uploadedAt)}</dd></div><div><dt className="text-slate-500">审核人 / 时间</dt><dd className="mt-1 text-slate-800">{selectedManagedSource.reviewedBy || "未审核"}{selectedManagedSource.reviewedAt ? ` · ${formatKnowledgeTime(selectedManagedSource.reviewedAt)}` : ""}</dd></div></dl>
                    {selectedManagedSource.extractionError && <Alert tone="warning">抽取失败：{selectedManagedSource.extractionError}</Alert>}
                    {selectedManagedSource.status === "indexed" && selectedManagedSource.extractionStatus === "completed" && canWrite && <p className="text-xs text-slate-500">片段复核进度：{managedRecords.filter(item => item.humanReviewed).length}/{managedRecords.length}。全部复核后才可审核来源。</p>}
                    {managedRecordsResponse.error && <Alert tone="warning">{managedRecordsResponse.error}</Alert>}
                    {managedRecordsResponse.loading && !managedRecordsResponse.data ? <p className="py-4 text-sm text-slate-500">正在读取来源片段…</p> : managedRecords.length ? <div className="max-h-[65vh] space-y-3 overflow-y-auto pr-1">
                      {managedRecords.map(record => <article key={record.recordId} className="rounded-md border border-slate-200 p-3"><div className="flex flex-wrap items-center justify-between gap-2"><div className="flex flex-wrap items-center gap-2 text-xs font-semibold text-slate-700"><span>{record.pageOrSheet || record.region || `片段 ${String(record.recordId).slice(0, 8)}`}</span><StatusBadge value={record.humanReviewed ? "reviewed" : "pending"} label={record.humanReviewed ? "已复核" : "待复核"} />{record.category && <span className="font-normal text-slate-500">{record.category}</span>}</div>{canWrite && !record.humanReviewed && selectedManagedSource.status === "indexed" && selectedManagedSource.extractionStatus === "completed" && <Button onClick={() => reviewKnowledgeRecord(selectedManagedSource, record)} disabled={busy}>复核片段</Button>}</div><p className="mt-2 whitespace-pre-wrap text-sm leading-6 text-slate-700">{record.content}</p><p className="mt-2 break-all font-mono text-[10px] text-slate-400">片段 ID {record.recordId} · SHA-256 {record.contentHash || record.citation?.contentHash || selectedManagedSource.sha256}</p></article>)}
                    </div> : <EmptyState title={selectedManagedSource.extractionStatus === "completed" ? "没有可复核片段" : "尚未生成片段"} description={selectedManagedSource.extractionStatus === "failed" ? "检查抽取错误后可重试。" : "来源抽取成功后，片段会显示在这里。"} />}
                    {canWrite && selectedManagedSource.status === "indexed" && managedRecords.length > 0 && managedRecords.some(item => !item.humanReviewed) && <p className="text-xs text-amber-800">仍有待复核片段，来源审核暂不可用。</p>}
                  </div>}
                </Card>
              </div>
            </div>}
          </>
        )}
      </div>
    </Page>
  );
}

function ClaimEditor({ form, onChange, onArrayChange, onAdd, onRemove, onSubmit, onCancel, busy, error,
  variableOptions, adjustableVariables, sources, selectedSource, selectedSourceId, onSourceChange, records, recordsLoading,
  recordsError, onRecordToggle, onSuggest, suggestionFocus, onSuggestionFocus, hasSources, sourcesLoading, sourcesHasMore, onLoadMoreSources }) {
  const sourceEvidenceIds = new Set(form.evidence.map(item => `${item.evidenceKind}:${item.referenceId}`));
  return <Card title={form.claimId ? "编辑机理草稿" : "新建机理草稿"} description="草稿是待审核主张，不代表已验证机理。至少引用一个可追溯知识片段；升级验证必须引用真实运行闭环。" actions={<Button onClick={onCancel}>返回详情</Button>}>
    {!hasSources && !sourcesLoading && <Alert tone="warning">当前站点尚无已复核的真实资料。请先在上方“资料来源”选项卡上传文件并完成片段复核；此页不会补造来源或验证结论。</Alert>}
    {error && <Alert tone="danger">{error}</Alert>}
    <form className="mt-4 space-y-6" onSubmit={onSubmit}>
      <section className="space-y-4">
        <h3 className="text-sm font-semibold text-slate-900">声明内容</h3>
        <div className="grid gap-4 md:grid-cols-2"><Field label="声明名称"><Input value={form.name} onChange={event => onChange("name", event.target.value)} required /></Field><Field label="机理类型"><Select value={form.mechanismType} onChange={event => onChange("mechanismType", event.target.value)}>{mechanismTypes.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select></Field></div>
        <Field label="机理陈述"><Textarea rows={4} value={form.statement} onChange={event => onChange("statement", event.target.value)} placeholder="描述变量之间的作用关系，并保持可反驳" required /></Field>
        <Field label="预期效应签名" hint="可选；记录可观测的方向或时序模式。"><Textarea rows={2} value={form.expectedSignature || ""} onChange={event => onChange("expectedSignature", event.target.value)} /></Field>
        <Field label="反证条件"><Textarea rows={3} value={form.falsificationCondition} onChange={event => onChange("falsificationCondition", event.target.value)} placeholder="说明哪些观测会否定该主张" required /></Field>
      </section>
      <section className="space-y-3 border-t border-slate-200 pt-5">
        <div className="flex flex-wrap items-center justify-between gap-2"><div><h3 className="text-sm font-semibold text-slate-900">变量</h3><p className="mt-1 text-xs text-slate-500">仅列出所选配方版本引用的数据模型中的参数和采集项。</p></div><Button onClick={() => onAdd("variables", { variableCode: "", variableRole: "cause", direction: "", delayMilliseconds: "", unit: "" })}>添加变量</Button></div>
        {form.variables.map((item, index) => <div key={index} className="grid gap-3 rounded-md border border-slate-200 bg-slate-50 p-3 md:grid-cols-[1.3fr_0.9fr_0.9fr_0.8fr_0.7fr_auto]">
          <Field label="变量"><Select value={item.variableCode} onChange={event => { const selected = variableOptions.find(option => option.code === event.target.value); onArrayChange("variables", index, { variableCode: event.target.value, unit: selected?.unit || "" }); }}><option value="">选择配方变量</option>{variableOptions.map(option => <option key={`${option.kind}:${option.code}`} value={option.code}>{option.displayName} · {option.code}</option>)}</Select></Field>
          <Field label="作用"><Select value={item.variableRole} onChange={event => onArrayChange("variables", index, { variableRole: event.target.value })}>{variableRoles.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select></Field>
          <Field label="方向"><Select value={item.direction || ""} onChange={event => onArrayChange("variables", index, { direction: event.target.value })}><option value="">未指定</option><option value="increase">增加</option><option value="decrease">降低</option><option value="nonlinear">非线性</option></Select></Field>
          <Field label="时滞 (ms)"><Input type="number" min="0" value={item.delayMilliseconds ?? ""} onChange={event => onArrayChange("variables", index, { delayMilliseconds: event.target.value })} /></Field>
          <Field label="单位"><Input value={item.unit || ""} onChange={event => onArrayChange("variables", index, { unit: event.target.value })} /></Field>
          <div className="flex items-end"><Button onClick={() => onRemove("variables", index)} disabled={form.variables.length === 1} aria-label="移除变量">移除</Button></div>
        </div>)}
        {!variableOptions.length && <p className="text-xs text-amber-800">无法读取该配方绑定的数据模型，需先选择有效版本。</p>}
      </section>
      <section className="space-y-3 border-t border-slate-200 pt-5">
        <div className="flex items-center justify-between"><div><h3 className="text-sm font-semibold text-slate-900">机理约束</h3><p className="mt-1 text-xs text-slate-500">约束仅引用该配方中允许调整的参数。</p></div><Button onClick={() => onAdd("constraints", { variableCode: "", constraintKind: "range", minimum: "", maximum: "", unit: "", severity: "hard" })}>添加约束</Button></div>
        {form.constraints.length === 0 && <p className="text-sm text-slate-500">此声明暂未登记约束。</p>}
        {form.constraints.map((item, index) => <div key={index} className="grid gap-3 rounded-md border border-slate-200 p-3 md:grid-cols-[1.3fr_1fr_0.7fr_0.7fr_0.6fr_0.7fr_auto]">
          <Field label="可调整参数"><Select value={item.variableCode} onChange={event => { const selected = adjustableVariables.find(option => option.code === event.target.value); onArrayChange("constraints", index, { variableCode: event.target.value, unit: selected?.unit || "" }); }}><option value="">选择参数</option>{adjustableVariables.map(option => <option key={option.code} value={option.code}>{option.displayName} · {option.code}</option>)}</Select></Field>
          <Field label="约束类型"><Select value={item.constraintKind} onChange={event => onArrayChange("constraints", index, { constraintKind: event.target.value })}><option value="range">范围</option><option value="safe-range">安全范围</option><option value="preferred-range">偏好范围</option></Select></Field>
          <Field label="最小值"><Input type="number" value={item.minimum} onChange={event => onArrayChange("constraints", index, { minimum: event.target.value })} /></Field>
          <Field label="最大值"><Input type="number" value={item.maximum} onChange={event => onArrayChange("constraints", index, { maximum: event.target.value })} /></Field>
          <Field label="单位"><Input value={item.unit} onChange={event => onArrayChange("constraints", index, { unit: event.target.value })} /></Field>
          <Field label="级别"><Select value={item.severity} onChange={event => onArrayChange("constraints", index, { severity: event.target.value })}><option value="hard">硬约束</option><option value="soft">软约束</option></Select></Field>
          <div className="flex items-end"><Button onClick={() => onRemove("constraints", index)}>移除</Button></div>
        </div>)}
      </section>
      <section className="space-y-3 border-t border-slate-200 pt-5">
        <div className="flex items-center justify-between gap-3"><div><h3 className="text-sm font-semibold text-slate-900">禁止组合</h3><p className="mt-1 text-xs text-slate-500">组合条件只引用可调整参数；每个因子都需要边界和单位，至少包含两个因子。</p></div><Button onClick={() => onAdd("forbiddenCombinations", { name: "", factors: [{ variableCode: "", minimum: "", maximum: "", unit: "" }, { variableCode: "", minimum: "", maximum: "", unit: "" }] })}>添加组合</Button></div>
        {form.forbiddenCombinations.length === 0 && <p className="text-sm text-slate-500">此声明暂未登记禁止组合。</p>}
        {form.forbiddenCombinations.map((combination, combinationIndex) => <div key={combinationIndex} className="space-y-3 rounded-md border border-slate-200 bg-slate-50 p-3">
          <div className="flex items-end gap-3"><Field label="组合名称" className="flex-1"><Input value={combination.name} onChange={event => onArrayChange("forbiddenCombinations", combinationIndex, { name: event.target.value })} /></Field><Button onClick={() => onRemove("forbiddenCombinations", combinationIndex)}>移除组合</Button></div>
          {combination.factors.map((factor, factorIndex) => <div key={factorIndex} className="grid gap-3 md:grid-cols-[1.4fr_0.8fr_0.8fr_0.7fr_auto]">
            <Field label={`因子 ${factorIndex + 1}`}><Select value={factor.variableCode} onChange={event => { const selected = adjustableVariables.find(option => option.code === event.target.value); const factors = combination.factors.map((current, index) => index === factorIndex ? { ...current, variableCode: event.target.value, unit: selected?.unit || "" } : current); onArrayChange("forbiddenCombinations", combinationIndex, { factors }); }}><option value="">选择可调整参数</option>{adjustableVariables.map(option => <option key={option.code} value={option.code}>{option.displayName} · {option.code}</option>)}</Select></Field>
            <Field label="最小值"><Input type="number" value={factor.minimum} onChange={event => { const factors = combination.factors.map((current, index) => index === factorIndex ? { ...current, minimum: event.target.value } : current); onArrayChange("forbiddenCombinations", combinationIndex, { factors }); }} /></Field>
            <Field label="最大值"><Input type="number" value={factor.maximum} onChange={event => { const factors = combination.factors.map((current, index) => index === factorIndex ? { ...current, maximum: event.target.value } : current); onArrayChange("forbiddenCombinations", combinationIndex, { factors }); }} /></Field>
            <Field label="单位"><Input value={factor.unit} onChange={event => { const factors = combination.factors.map((current, index) => index === factorIndex ? { ...current, unit: event.target.value } : current); onArrayChange("forbiddenCombinations", combinationIndex, { factors }); }} /></Field>
            <div className="flex items-end"><Button onClick={() => { const factors = combination.factors.filter((_, index) => index !== factorIndex); onArrayChange("forbiddenCombinations", combinationIndex, { factors }); }} disabled={combination.factors.length <= 2}>移除因子</Button></div>
          </div>)}
          <Button onClick={() => onArrayChange("forbiddenCombinations", combinationIndex, { factors: [...combination.factors, { variableCode: "", minimum: "", maximum: "", unit: "" }] })}>添加因子</Button>
        </div>)}
      </section>
      <section className="space-y-3 border-t border-slate-200 pt-5">
        <div><h3 className="text-sm font-semibold text-slate-900">适用范围补充</h3><p className="mt-1 text-xs text-slate-500">站点和配方已由顶部范围固定，可按实际情况补充产品或设备范围。</p></div>
        {form.applicability.map((item, index) => <div key={index} className="grid gap-3 md:grid-cols-[0.8fr_1.2fr_auto]"><Field label="维度"><Select value={item.dimensionCode} onChange={event => onArrayChange("applicability", index, { dimensionCode: event.target.value })}><option value="product">产品</option><option value="equipment">设备</option></Select></Field><Field label="范围值"><Input value={item.dimensionValue} onChange={event => onArrayChange("applicability", index, { dimensionValue: event.target.value })} /></Field><div className="flex items-end"><Button onClick={() => onRemove("applicability", index)}>移除</Button></div></div>)}
        <Button onClick={() => onAdd("applicability", { dimensionCode: "product", dimensionValue: "" })}>添加适用范围</Button>
      </section>
      <section className="space-y-3 border-t border-slate-200 pt-5">
        <div><h3 className="text-sm font-semibold text-slate-900">证据来源</h3><p className="mt-1 text-xs text-slate-500">必须从实际知识来源中选择片段；片段 ID 与内容哈希会随草稿保存。</p></div>
        <div className="grid gap-3 md:grid-cols-[1fr_1fr_auto]"><Field label="知识来源"><Select value={selectedSourceId} onChange={event => onSourceChange(event.target.value)}><option value="">选择当前站点来源</option>{sources.map(item => <option key={item.sourceId} value={item.sourceId}>{item.title} · {item.status}</option>)}</Select></Field><Field label="草稿关注点（可选）"><Input value={suggestionFocus} onChange={event => onSuggestionFocus(event.target.value)} placeholder="辅助整理方向" /></Field><div className="flex items-end"><Button type="button" onClick={onSuggest} disabled={busy || !selectedSourceId || selectedSource?.extractionStatus !== "completed"}>生成未验证建议</Button></div></div>
        {sourcesHasMore && <Button type="button" variant="ghost" onClick={onLoadMoreSources} disabled={sourcesLoading}>{sourcesLoading ? "正在加载来源…" : "加载更多已审核来源"}</Button>}
        {selectedSource && <div className="rounded-md border border-slate-200 bg-slate-50 p-3"><div className="mb-2 flex flex-wrap items-center gap-2 text-xs"><span className="font-semibold text-slate-800">{selectedSource.title}</span><StatusBadge value={selectedSource.status} label={selectedSource.status} /><span className="text-slate-500">提取：{selectedSource.extractionStatus}</span></div>{recordsError && <p className="text-sm text-rose-700">{recordsError}</p>}{recordsLoading ? <p className="text-sm text-slate-500">正在读取来源片段…</p> : records.length ? <div className="max-h-96 space-y-2 overflow-y-auto">{records.map(record => { const evidenceKey = `knowledge-fragment:${record.recordId}`; const selected = sourceEvidenceIds.has(evidenceKey); return <label key={record.recordId} className="flex cursor-pointer items-start gap-3 rounded border border-slate-200 bg-white p-3"><input type="checkbox" checked={selected} onChange={() => onRecordToggle(record)} className="mt-1" /><span className="min-w-0 flex-1"><span className="flex flex-wrap items-center gap-2 text-xs font-medium text-slate-600"><span>{record.pageOrSheet || record.region || `片段 ${String(record.recordId).slice(0, 8)}`}</span><span>{record.humanReviewed ? "已复核" : "未复核"}</span></span><span className="mt-1 block line-clamp-4 whitespace-pre-wrap text-sm leading-5 text-slate-700">{record.content}</span><span className="mt-1 block truncate text-[10px] text-slate-400">SHA-256 {record.citation?.contentHash || selectedSource.sha256}</span></span></label>; })}</div> : <p className="text-sm text-slate-500">该来源没有可引用的实际片段。</p>}</div>}
        {sourcesLoading && <p className="text-sm text-slate-500">正在读取站点知识来源…</p>}
        <div className="space-y-2">{form.evidence.map((item, index) => <div key={`${item.evidenceKind}:${item.referenceId}:${index}`} className="flex flex-wrap items-center justify-between gap-2 rounded border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-900"><span>{item.evidenceKind} · {item.referenceId} · {item.polarity}</span><span className="font-mono">{item.contentHash}</span><Button onClick={() => onChange("evidence", form.evidence.filter((_, itemIndex) => itemIndex !== index))}>移除</Button></div>)}</div>
      </section>
      <div className="flex flex-wrap items-center justify-end gap-2 border-t border-slate-200 pt-4"><Button onClick={onCancel}>取消</Button><Button type="submit" variant="primary" disabled={busy || !hasSources}>{busy ? "正在保存…" : "保存未验证草稿"}</Button></div>
    </form>
  </Card>;
}

function ClaimDetails({ claim, identityId, canWrite, canReview, canTransition, hasOpenConflict, conflictStateReady, reviewComment, onReviewComment, onReview, onEdit, busy,
  lifecycleTarget, lifecycleEvidence, onStartLifecycle, onLifecycleEvidence, onSubmitLifecycle, onCancelLifecycle }) {
  const transitions = {
    reviewed: [["supported", "记录真实运行支持"], ["falsified", "记录反证"]],
    supported: [["validated", "独立验证"], ["falsified", "记录反证"]],
    validated: [["active", "激活声明"], ["falsified", "记录反证"]],
    active: [["retired", "退役声明"], ["falsified", "记录反证"]],
  }[claim.status] || [];
  const requiresOutcome = target => ["supported", "validated", "falsified"].includes(target);
  const visibleTransitions = canTransition ? transitions.filter(([target]) => target !== "active" || (conflictStateReady && !hasOpenConflict)) : [];
  return <div className="space-y-5">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><div className="flex flex-wrap items-center gap-2"><h3 className="text-lg font-semibold text-slate-950">{claim.name}</h3><StatusBadge value={claim.status} label={claimStatusLabels[claim.status] || claim.status} /><span className="text-xs text-slate-500">V{claim.version} · {claim.mechanismType}</span></div><p className="mt-2 text-xs text-slate-500">创建人 {claim.createdBy || "未知"}{claim.reviewedBy ? ` · 审核人 ${claim.reviewedBy}` : ""}</p></div>{canWrite && claim.status === "draft" && <Button onClick={onEdit}>编辑草稿</Button>}</div>
    <div className="space-y-3"><DetailBlock title="机理陈述" content={claim.statement} /><DetailBlock title="预期效应签名" content={claim.expectedSignature} /><DetailBlock title="反证条件" content={claim.falsificationCondition} /></div>
    <div><h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">变量</h4><div className="flex flex-wrap gap-2">{(claim.variables || []).map((item, index) => <span key={`${item.variableCode}:${index}`} className="rounded border border-slate-200 bg-slate-50 px-2.5 py-1.5 text-xs text-slate-700">{item.variableCode} · {variableRoles.find(([value]) => value === item.variableRole)?.[1] || item.variableRole}{item.direction ? ` · ${item.direction}` : ""} · {item.unit}</span>)}</div></div>
    {(claim.constraints || []).length > 0 && <div><h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">机理约束</h4><div className="space-y-2">{claim.constraints.map((item, index) => <div key={index} className="rounded border border-slate-200 px-3 py-2 text-sm text-slate-700">{item.variableCode}: {item.minimum ?? "−∞"} 至 {item.maximum ?? "+∞"} {item.unit} · {item.constraintKind} · {item.severity}</div>)}</div></div>}
    {(claim.forbiddenCombinations || []).length > 0 && <div><h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">禁止组合</h4><div className="space-y-2">{claim.forbiddenCombinations.map((item, index) => <div key={item.combinationId || index} className="rounded border border-slate-200 px-3 py-2"><div className="text-sm font-medium text-slate-800">{item.name}</div><div className="mt-1 flex flex-wrap gap-2">{(item.factors || []).map((factor, factorIndex) => <span key={`${factor.variableCode}:${factorIndex}`} className="rounded bg-slate-50 px-2 py-1 text-xs text-slate-600">{factor.variableCode}: {factor.minimum ?? "−∞"} 至 {factor.maximum ?? "+∞"} {factor.unit}</span>)}</div></div>)}</div></div>}
    <div><h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">来源证据</h4>{claim.evidence?.length ? <div className="space-y-2">{claim.evidence.map(item => <div key={item.evidenceLinkId || `${item.evidenceKind}:${item.referenceId}`} className="rounded border border-slate-200 px-3 py-2 text-xs"><div className="font-medium text-slate-700">{item.evidenceKind} · {item.polarity}</div><div className="mt-1 break-all text-slate-500">{item.referenceId}</div><div className="mt-1 break-all font-mono text-[10px] text-slate-400">{item.contentHash}</div></div>)}</div> : <p className="text-sm text-rose-700">缺少来源证据。</p>}</div>
    {claim.applicability?.length > 0 && <div><h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">适用范围</h4><div className="flex flex-wrap gap-2">{claim.applicability.map((item, index) => <span key={index} className="rounded bg-slate-100 px-2.5 py-1 text-xs text-slate-600">{item.dimensionCode}: {item.dimensionValue}</span>)}</div></div>}
    {claim.status === "draft" && <div className="space-y-3 border-t border-slate-200 pt-4">{canReview ? <><h4 className="text-sm font-semibold text-slate-900">独立结构审核</h4><Field label="审核意见（可选）"><Textarea rows={2} value={reviewComment} onChange={event => onReviewComment(event.target.value)} /></Field><div className="flex flex-wrap gap-2"><Button variant="primary" disabled={busy || !claim.evidence?.length || !claim.applicability?.length} onClick={() => onReview(claim, "approve")}>通过审核</Button><Button disabled={busy} onClick={() => onReview(claim, "reject")}>驳回</Button></div></> : <p className="text-xs text-slate-500">审核只对创建人以外的已登录人员开放；审核通过也不代表机理已由真实运行验证。</p>}</div>}
    {(visibleTransitions.length > 0 || claim.status === "validated") && <div className="space-y-3 border-t border-slate-200 pt-4"><h4 className="text-sm font-semibold text-slate-900">生命周期</h4>{visibleTransitions.length > 0 && <div className="flex flex-wrap gap-2">{visibleTransitions.map(([target, label]) => <Button key={target} onClick={() => onStartLifecycle(claim, target)} disabled={busy}>{label}</Button>)}</div>}{claim.status === "validated" && !conflictStateReady && <p className="text-xs text-slate-500">正在读取冲突记录；完成核对前不会开放激活。</p>}{claim.status === "validated" && hasOpenConflict && <p className="text-xs text-amber-800">存在未解决冲突，解决冲突后才能激活。</p>}{lifecycleTarget && canWrite && canTransition && <form className="space-y-3 rounded-md border border-slate-200 bg-slate-50 p-4" onSubmit={onSubmitLifecycle}><h5 className="text-sm font-semibold">{claimStatusLabels[lifecycleTarget] || lifecycleTarget}</h5>{requiresOutcome(lifecycleTarget) && <><p className="text-xs leading-5 text-slate-600">此步骤必须引用同一站点和配方下、已完成闭环且内容校验通过的真实配方建议运行结果。服务端会再次核验来源、哈希和独立决策人。</p><Field label="运行结果 / 决策引用"><Input value={lifecycleEvidence.referenceId} onChange={event => onLifecycleEvidence({ referenceId: event.target.value })} placeholder="真实运行结果 ID" required /></Field><Field label="运行结果 SHA-256"><Input value={lifecycleEvidence.contentHash} onChange={event => onLifecycleEvidence({ contentHash: event.target.value })} placeholder="64 位十六进制哈希" required /></Field><Field label="证据评价说明"><Textarea rows={3} value={lifecycleEvidence.evaluationSummary} onChange={event => onLifecycleEvidence({ evaluationSummary: event.target.value })} placeholder={lifecycleTarget === "falsified" ? "说明运行证据如何否定该主张" : "说明运行证据如何支持该主张"} required /></Field></>}<Field label="补充意见"><Textarea rows={2} value={lifecycleEvidence.comment} onChange={event => onLifecycleEvidence({ comment: event.target.value })} /></Field><div className="flex gap-2"><Button type="submit" variant="primary" disabled={busy}>提交决定</Button><Button onClick={onCancelLifecycle}>取消</Button></div></form>}</div>}
    {!canTransition && ["reviewed", "supported", "validated", "active"].includes(claim.status) && <p className="border-t border-slate-200 pt-4 text-xs text-slate-500">生命周期操作只对非创建人和非结构审核人开放，并要求阶段证据及独立决策。当前账号不满足已知的独立性条件。</p>}
    <p className="border-t border-slate-200 pt-4 text-xs leading-5 text-slate-500">生命周期顺序：审核 → 真实运行支持 → 独立验证 → 激活 → 退役。草稿、审核通过或模型生成建议都不代表已验证机理。{identityId && claim.createdBy === identityId ? " 当前账号是创建人，不能审核或执行升级决定。" : ""}</p>
  </div>;
}

function DetailBlock({ title, content }) {
  if (!content) return null;
  return <div><h4 className="text-xs font-semibold uppercase tracking-wide text-slate-500">{title}</h4><p className="mt-1 whitespace-pre-wrap text-sm leading-6 text-slate-700">{content}</p></div>;
}
