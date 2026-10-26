// 提供工艺资料与机理知识的业务入口，明确知识范围、证据边界和 AI 助手的使用关系。
import { useEffect, useState } from "react";
import { useSearchParams } from "react-router";
import { getJson } from "../api/http";
import { Alert, LinkButton, Page, Card } from "../ui/components";

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
  return (
    <Page title={content.title} description={content.description}>
      {sourceId && recordId && (
        <Card title="引用来源" description="仅展示当前账号有权访问且已复核的知识片段。">
          {citationError ? <Alert tone="warning">{citationError}</Alert> : citation ? (
            <div className="space-y-4 text-sm text-slate-700">
              <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-500">
                <span>{citation.source.title}</span>
                <span>{citation.source.fileName}</span>
                {citation.record.pageOrSheet && <span>{citation.record.pageOrSheet}</span>}
                {citation.record.region && <span>{citation.record.region}</span>}
              </div>
              <div className="border-l-2 border-trajectory-500 pl-4 leading-7 text-slate-800">{citation.record.content}</div>
              <p className="text-xs text-slate-400">内容哈希：{citation.record.citation?.contentHash || citation.source.sha256}</p>
            </div>
          ) : <p className="text-sm text-slate-500">正在读取引用来源…</p>}
        </Card>
      )}
      <div className="grid gap-5 lg:grid-cols-2">
        {content.cards.map(([title, description]) => (
          <Card key={title} title={title}>
            <p className="text-sm leading-6 text-slate-600">{description}</p>
          </Card>
        ))}
      </div>
      <Card title="进入 AI 助手" description="AI 助手只在授权站点和适用范围内检索已复核资料，并返回来源引用。">
        <LinkButton to="/chat">打开 AI 助手</LinkButton>
      </Card>
    </Page>
  );
}

export function ProcessMaterialsPage() {
  return <KnowledgePage kind="materials" />;
}

export function MechanismKnowledgePage() {
  return <KnowledgePage kind="mechanism" />;
}
