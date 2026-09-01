// 承载日常下一配方的工程师决定与实际运行关联表单。
import { formatResearchNumber } from "../researchProjectModel";
import { Alert, Button, Drawer, Field, Input, Select, Textarea } from "../../ui/components";

export function RecipeRecommendationDecisionDrawer({ target, form, setForm, saving, variables, onClose, onSubmit }) {
  if (!target) return null;
  const variableByCode = new Map(variables.map(item => [item.code, item]));
  const parameters = target.item.parameters || [];
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const updateDecision = event => setForm({
    ...form,
    decision: event.target.value,
    factors: event.target.value === "accepted"
      ? Object.fromEntries(parameters.map(parameter => [parameter.variableCode, parameter.value]))
      : form.factors,
  });

  return (
    <Drawer
      open
      onClose={onClose}
      title="登记下一配方决定"
      description="保存后会冻结建议、工程师选择和理由。实际运行必须在决定之后启动，并通过单独操作关联。"
      size="lg"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="recipe-recommendation-decision-form">{saving ? "正在冻结…" : "冻结工程师决定"}</Button></>}
    >
      <form id="recipe-recommendation-decision-form" className="space-y-4" onSubmit={onSubmit}>
        <Alert tone="info">建议项 <code>{target.item.recommendationKey}</code>；请登记实际生产采用的完整配方，而不是设备控制命令。</Alert>
        <Field label="工程师决定"><Select value={form.decision} onChange={updateDecision}><option value="accepted">采用模型建议</option><option value="modified">修改后采用</option><option value="rejected">不采用建议</option></Select></Field>
        <Field label="对工程判断是否有用"><Select value={form.usefulnessRating} onChange={update("usefulnessRating")}><option value="">未评价</option><option value="useful">有用</option><option value="partly-useful">部分有用</option><option value="not-useful">无用</option></Select></Field>
        {form.decision !== "rejected" && <div className="grid gap-4 sm:grid-cols-2">
          {parameters.map(parameter => (
            <Field key={parameter.variableCode} label={variableByCode.get(parameter.variableCode)?.name || parameter.variableCode} hint={`模型建议 ${formatResearchNumber(parameter.value)} ${parameter.unit}`}>
              <Input required disabled={form.decision === "accepted"} type="number" step="any" value={form.factors?.[parameter.variableCode] ?? ""} onChange={event => setForm({ ...form, factors: { ...form.factors, [parameter.variableCode]: event.target.value } })} />
            </Field>
          ))}
        </div>}
        {form.decision !== "accepted" && <Field label="修改或拒绝原因"><Textarea required rows={4} value={form.reason} onChange={update("reason")} placeholder="例如：材料批次限制、工装状态或现场安全边界。" /></Field>}
      </form>
    </Drawer>
  );
}

export function RecipeExecutionLinkDrawer({ decision, form, setForm, saving, onClose, onSubmit }) {
  if (!decision) return null;
  return (
    <Drawer
      open
      onClose={onClose}
      title="关联实际生产运行"
      description="该关联是追加证据；保存后不能替换为其他运行。"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="recipe-execution-link-form">{saving ? "正在关联…" : "冻结运行关联"}</Button></>}
    >
      <form id="recipe-execution-link-form" className="space-y-4" onSubmit={onSubmit}>
        <Alert tone="info">工程师决定 <code>{decision.decisionId}</code> 已冻结。此操作不会下发任何设备参数。</Alert>
        <Field label="实际生产运行号" hint="必须与采集周期 ExecutionId 完全一致。质量结果会通过此标识从源数据取证。"><Input required value={form.actualExecutionKey} onChange={event => setForm({ ...form, actualExecutionKey: event.target.value })} /></Field>
      </form>
    </Drawer>
  );
}
