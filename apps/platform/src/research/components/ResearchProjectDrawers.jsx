// 承载研发项目的影子决策、受控决策和通用任务表单，不负责页面级数据加载。
import { useEffect, useState } from "react";
import { getJson } from "../../api/http";
import { formatResearchNumber, taskTitles } from "../researchProjectModel";
import { Alert, Button, Card, Drawer, Field, Input, Select, Textarea } from "../../ui/components";

export function ShadowDecisionDrawer({ target, form, setForm, saving, variables, onClose, onSubmit }) {
  if (!target) return null;
  const variableByCode = new Map(variables.map(item => [item.code, item]));
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const updateDecision = event => setForm({
    ...form,
    decision: event.target.value,
    factors: event.target.value === "accepted"
      ? Object.fromEntries((target.run.factors || []).map(factor => [factor.variableCode, factor.value]))
      : form.factors,
  });
  return (
    <Drawer
      open
      onClose={onClose}
      title="登记影子选择"
      description="该记录只用于旁路比较，不批准生产运行，也不向设备写入参数。保存后不能修改。"
      size="lg"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="shadow-decision-form">{saving ? "正在冻结…" : "冻结影子决策"}</Button></>}
    >
      <form id="shadow-decision-form" className="space-y-4" onSubmit={onSubmit}>
        <Alert tone="info">模型建议 <code>{target.run.executionKey}</code>；请在知道检验结果之前登记实际选择。</Alert>
        <Field label="决策"><Select value={form.decision} onChange={updateDecision}><option value="accepted">采用模型建议</option><option value="modified">修改后采用</option><option value="rejected">不采用建议</option></Select></Field>
        <Field label="对工程判断是否有用" hint="与是否采用分开评价；有用但受现场约束的建议仍可标为有用。"><Select required value={form.usefulnessRating} onChange={update("usefulnessRating")}><option value="useful">有用</option><option value="partly-useful">部分有用</option><option value="not-useful">无用</option></Select></Field>
        <Field label="实际生产运行号" hint="必须与采集周期 ExecutionId 完全一致，结果将通过它自动关联。"><Input required value={form.actualExecutionKey} onChange={update("actualExecutionKey")} /></Field>
        <div className="grid gap-4 sm:grid-cols-2">
          {(target.run.factors || []).map(factor => (
            <Field key={factor.variableCode} label={variableByCode.get(factor.variableCode)?.name || factor.variableCode} hint={`模型建议 ${formatResearchNumber(factor.value)} ${factor.unit}`}>
              <Input
                required
                disabled={form.decision === "accepted"}
                type="number"
                step="any"
                value={form.factors?.[factor.variableCode] ?? ""}
                onChange={event => setForm({ ...form, factors: { ...form.factors, [factor.variableCode]: event.target.value } })}
              />
            </Field>
          ))}
        </div>
        {form.decision !== "accepted" && <Field label="修改或拒绝原因"><Textarea required rows={3} value={form.rejectionReason} onChange={update("rejectionReason")} placeholder="例如：夹具干涉、材料批次限制、设备升温能力不足。" /></Field>}
        <Field label="现场限制（每行一条，可选）"><Textarea rows={3} value={form.siteLimitations} onChange={update("siteLimitations")} /></Field>
        <Field label="决策时上下文快照" hint="每行 key=value；至少填写一个当时已知的设备、材料、工装或生产上下文。"><Textarea required rows={5} value={form.contextSnapshot} onChange={update("contextSnapshot")} /></Field>
      </form>
    </Drawer>
  );
}

export function ControlledDecisionDrawer({ target, form, setForm, saving, variables, onClose, onSubmit }) {
  if (!target) return null;
  const variableByCode = new Map(variables.map(item => [item.code, item]));
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const updateDecision = event => setForm({
    ...form,
    decision: event.target.value,
    factors: event.target.value === "accepted"
      ? Object.fromEntries((target.run.factors || []).map(factor => [factor.variableCode, factor.value]))
      : form.factors,
  });
  return (
    <Drawer
      open
      onClose={onClose}
      title="受控在线工程师决策"
      description="本次只处理一条建议。建议值、修改后的批准值、理由和决策人保存后均不可覆盖。"
      size="lg"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="controlled-decision-form">{saving ? "正在冻结…" : "冻结本次决策"}</Button></>}
    >
      <form id="controlled-decision-form" className="space-y-4" onSubmit={onSubmit}>
        <Alert tone="warning">这不是自动控制命令。确认后仍需独立批准，Platform 只生成设备无关的执行交接单。</Alert>
        <Field label="决策"><Select value={form.decision} onChange={updateDecision}><option value="accepted">接受原建议</option><option value="modified">修改后接受</option><option value="rejected">拒绝并停止本次建议</option></Select></Field>
        {form.decision !== "rejected" && (
          <div className="grid gap-4 sm:grid-cols-2">
            {(target.run.factors || []).map(factor => (
              <Field key={factor.variableCode} label={variableByCode.get(factor.variableCode)?.name || factor.variableCode} hint={`模型建议 ${formatResearchNumber(factor.value)} ${factor.unit}`}>
                <Input
                  required
                  disabled={form.decision === "accepted"}
                  type="number"
                  step="any"
                  value={form.factors?.[factor.variableCode] ?? ""}
                  onChange={event => setForm({ ...form, factors: { ...form.factors, [factor.variableCode]: event.target.value } })}
                />
              </Field>
            ))}
          </div>
        )}
        {form.decision !== "accepted" && <Field label="修改或拒绝原因"><Textarea required rows={4} value={form.reason} onChange={update("reason")} placeholder="例如：当前工装热负荷限制、材料批次不在适用范围、设备状态不允许。" /></Field>}
      </form>
    </Drawer>
  );
}

export function TaskDrawer({ task, form, setForm, workspace, memberCandidates, saving, experimentPreview, experimentValidation, onPreviewExperimentDesign, onClose, onSubmit }) {
  const [historicalProcessExecutions, setHistoricalProcessExecutions] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState("");
  const [historyFilter, setHistoryFilter] = useState("");

  useEffect(() => {
    if (task !== "history" || !workspace) return;
    let mounted = true;
    setHistoryLoading(true);
    setHistoryError("");
    const productCode = workspace.project.productName ? `&productCode=${encodeURIComponent(workspace.project.productName)}` : "";
    getJson(`/api/v1/process-executions?status=completed&limit=200${productCode}`)
      .then(response => {
        if (!mounted) return;
        const values = response?.data || [];
        setHistoricalProcessExecutions(values);
        setForm(current => ({ ...current, executionIds: values.map(item => item.executionId) }));
      })
      .catch(requestError => {
        if (!mounted) return;
        setHistoryError(requestError.message || "无法读取已完成运行。");
      })
      .finally(() => { if (mounted) setHistoryLoading(false); });
    return () => { mounted = false; };
  }, [task, workspace?.project?.productName, setForm]);

  if (!task || !workspace) return null;
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const variables = workspace.project.variables.filter(item => item.role === "control");
  const validatedOperatingRegions = workspace.operatingRegions.filter(item =>
    item.status === "validated" &&
    ["laboratory", "production"].includes(item.validationLevel));
  const beneficialTransfers = (workspace.transferAssessments || []).filter(item =>
    item.status === "reviewed" && item.outcome === "beneficial");
  const baselineRuns = workspace.experiments
    .filter(item => item.designMethod === "historical-observation" || item.status === "completed")
    .flatMap(experiment => (experiment.runPlan || []).map(run => ({
      ...run,
      experimentName: experiment.name,
    })));

  const selectedHistoryExecutionIds = new Set(form.executionIds || []);
  const normalizedHistoryFilter = historyFilter.trim().toLowerCase();
  const visibleHistoricalProcessExecutions = historicalProcessExecutions.filter(execution =>
    !normalizedHistoryFilter || [
      execution.executionId,
      execution.productFamilyCode,
      execution.productCode,
      execution.equipmentId,
      ...(execution.edgeIds || []),
      execution.externalBatchRef,
      execution.outputItemId,
      execution.processSpecificationId,
    ].some(value => String(value || "").toLowerCase().includes(normalizedHistoryFilter)));
  const updateHistoryExecution = (executionId, checked) => {
    const nextIds = new Set(form.executionIds || []);
    if (checked) nextIds.add(executionId);
    else nextIds.delete(executionId);
    setForm({ ...form, executionIds: Array.from(nextIds) });
  };
  const resultLabel = result => {
    const experiment = workspace.experiments.find(item => item.experimentId === result.experimentId);
    const metrics = (result.metrics || []).map(item =>
      `${item.objectiveCode} ${formatResearchNumber(item.observedValue)} ${item.unit}`).join("；");
    return `${experiment?.name || "已计算验证"} · ${result.runCount || 0} 个运行${metrics ? ` · ${metrics}` : ""}`;
  };
  return (
    <Drawer
      open
      onClose={onClose}
      title={taskTitles[task]}
      description="按研发事实填写，保存后进入项目证据链。"
      size={task === "history" ? "xl" : "lg"}
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving || (task === "history" && (form.executionIds?.length || 0) < 2)} type="submit" form="research-task-form">{saving ? "正在保存…" : "保存"}</Button></>}
    >
      <form id="research-task-form" className="space-y-4" onSubmit={onSubmit}>
        {task === "member" && (
          <Field label="成员账户" hint="选择平台账户；项目权限使用不可变用户 ID 关联。">
            <Select required value={form.member} onChange={update("member")}>
              <option value="">请选择账户</option>
              {(memberCandidates || [])
                .filter(user => !workspace.project.memberUserIds?.includes(user.userId) && user.userId !== workspace.project.ownerUserId)
                .map(user => (
                  <option key={user.userId} value={user.userId}>
                    {user.displayName || user.username} · {user.username}{user.disabled ? "（已停用）" : ""}
                  </option>
                ))}
            </Select>
          </Field>
        )}
        {task === "hypothesis" && <>
          <Field label="假设"><Textarea required rows={4} value={form.statement} onChange={update("statement")} placeholder="说明哪个变量通过什么机制影响目标。" /></Field>
          <Field label="提出依据"><Textarea required rows={4} value={form.rationale} onChange={update("rationale")} placeholder="填写历史数据、物理机理或专家经验。" /></Field>
          <VariableSelect variables={variables} value={form.variableCode} onChange={update("variableCode")} />
          <Field label="验证目标（可选）" hint="定义后可让优化器设计最有信息量的受控验证条件。"><Select value={form.validationOutcomeCode} onChange={update("validationOutcomeCode")}><option value="">暂不定义</option>{workspace.project.objectives.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}</Select></Field>
          {form.validationOutcomeCode && <div className="grid gap-4 sm:grid-cols-2">
            <Field label="预期效应方向"><Select required value={form.expectedEffectDirection} onChange={update("expectedEffectDirection")}><option value="">请选择</option><option value="increase">指标增加</option><option value="decrease">指标降低</option></Select></Field>
            <Field label="最小可辨别效应"><Input required type="number" min="0.0000001" step="any" value={form.minimumEffect} onChange={update("minimumEffect")} /></Field>
          </div>}
          <Field label="适用范围（可选）"><Textarea rows={3} value={form.applicability} onChange={update("applicability")} placeholder="说明产品、材料、设备或环境边界。" /></Field>
          <Field label="作用链（每行一条）" hint="格式：起点变量 -> 终点变量 | 作用机制 | increase/decrease/nonlinear"><Textarea rows={4} value={form.causalChain} onChange={update("causalChain")} placeholder="melt.temperature -> defect.rate | 黏度下降改善充填 | decrease" /></Field>
          <Field label="时间特征（每行一条）" hint="格式：变量 | 特征代码 | 阶段代码 | 时滞毫秒 | 窗口毫秒"><Textarea rows={4} value={form.temporalFeatures} onChange={update("temporalFeatures")} placeholder="cavity.pressure | pressure.rise-rate | holding | 500 | 3000" /></Field>
          <Field label="交互作用（每行一条）" hint="格式：变量1,变量2 | 交互说明"><Textarea rows={3} value={form.interactions} onChange={update("interactions")} placeholder="melt.temperature,holding.pressure | 高温会放大保压压力对收缩的影响" /></Field>
          <Field label="失效条件（每行一条）" hint="格式：触发条件 | 可观测征兆 | 必须采取的处置"><Textarea rows={3} value={form.failureConditions} onChange={update("failureConditions")} placeholder="材料温度超过降解阈值 | 挥发物或颜色异常 | 停止验证并恢复基线" /></Field>
          <Field label="反证条件（每行一条）" hint="写出出现什么结果时应否定或收缩该假设。"><Textarea required rows={3} value={form.falsificationConditions} onChange={update("falsificationConditions")} /></Field>
        </>}
        {task === "experiment" && <>
          <Field label="受控验证名称"><Input required value={form.name} onChange={update("name")} /></Field>
          <Field label="验证的假设"><Select value={form.hypothesisId} onChange={update("hypothesisId")}><option value="">不关联具体假设</option>{workspace.hypotheses.map(item => <option key={item.hypothesisId} value={item.hypothesisId}>{item.statement}</option>)}</Select></Field>
          <div className="rounded-xl border border-indigo-200 bg-indigo-50 p-4 space-y-4">
            <div>
              <p className="text-sm font-semibold text-indigo-950">生成受控验证条件</p>
              <p className="mt-1 text-[13px] leading-5 text-indigo-800">系统只生成可编辑运行表；保存后仍执行全部安全、目标与对照校验。</p>
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="设计方法"><Select value={form.designMethod} onChange={event => setForm({ ...form, designMethod: event.target.value, generatedRunPlan: [] })}><option value="full-factorial">全因子设计</option><option value="fractional-factorial">部分因子设计</option><option value="response-surface">响应面设计</option><option value="latin-hypercube">拉丁超立方</option></Select></Field>
              <Field label="设计变量" hint="按 Ctrl 或 Shift 选择多个可控变量。"><Select multiple size={Math.min(5, Math.max(2, variables.length))} value={form.designVariableCodes || []} onChange={event => setForm({ ...form, designVariableCodes: Array.from(event.target.selectedOptions, option => option.value), generatedRunPlan: [] })}>{variables.map(item => <option key={item.code} value={item.code}>{item.name}（{item.unit}）</option>)}</Select></Field>
            </div>
            <div className="grid gap-4 sm:grid-cols-3">
              <Field label="水平数"><Input disabled={form.designMethod === "fractional-factorial" || form.designMethod === "latin-hypercube"} type="number" min="2" max="5" value={form.designLevels} onChange={update("designLevels")} /></Field>
              <Field label="每条件重复"><Input type="number" min="1" max="5" value={form.designReplicates} onChange={update("designReplicates")} /></Field>
              <Field label="区组数"><Input type="number" min="1" max="5" value={form.designBlocks} onChange={update("designBlocks")} /></Field>
            </div>
            {form.designMethod === "latin-hypercube" && <Field label="样本数"><Input type="number" min="2" max="40" value={form.designSampleCount} onChange={update("designSampleCount")} /></Field>}
            {form.designMethod === "response-surface" && <Field label="响应面族"><Select value={form.responseSurfaceFamily} onChange={update("responseSurfaceFamily")}><option value="central-composite">中心复合设计（CCD）</option><option value="box-behnken">Box–Behnken</option></Select></Field>}
            <Button type="button" onClick={onPreviewExperimentDesign}>生成并预览运行表</Button>
            {experimentPreview && <div className="space-y-2 rounded-lg border border-indigo-200 bg-white p-3 text-[13px] text-slate-700">
              <div><strong>已生成 {experimentPreview.runPlan?.length || 0} 条运行</strong>{experimentPreview.aliasStructure ? ` · ${experimentPreview.aliasStructure}` : ""}</div>
              {(experimentPreview.warnings || []).map(item => <Alert key={item} tone="warning">{item}</Alert>)}
              <div className="max-h-48 overflow-auto rounded border border-slate-200">
                <table className="w-full text-left"><thead><tr className="bg-slate-50"><th className="p-2">顺序</th><th className="p-2">区组/重复</th><th className="p-2">变量设置</th></tr></thead><tbody>{(form.generatedRunPlan || []).map(run => <tr key={run.executionKey} className="border-t border-slate-100"><td className="p-2">{run.sequence}</td><td className="p-2">{run.blockKey || "—"} / {run.replicateKey || "—"}</td><td className="p-2">{(run.factors || []).map(factor => `${factor.variableCode}=${formatResearchNumber(factor.value)} ${factor.unit}`).join("；")}</td></tr>)}</tbody></table>
              </div>
            </div>}
          </div>
          <VariableSelect variables={variables} value={form.variableCode} onChange={update("variableCode")} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="低水平"><Input required type="number" step="any" value={form.low} onChange={update("low")} /></Field>
            <Field label="高水平"><Input required type="number" step="any" value={form.high} onChange={update("high")} /></Field>
          </div>
          {baselineRuns.length > 0 && (
            <Field
              label="独立对照运行（可选）"
              hint="按 Ctrl 或 Shift 选择至少两个同条件重复运行；不选择时只计算描述性差值，不生成效果置信区间。"
            >
              <Select
                multiple
                size={Math.min(8, Math.max(3, baselineRuns.length))}
                value={form.baselineExecutionKeys || []}
                onChange={event => setForm({
                  ...form,
                  baselineExecutionKeys: Array.from(
                    event.target.selectedOptions,
                    option => option.value,
                  ),
                })}
              >
                {baselineRuns.map(run => (
                  <option key={`${run.experimentName}:${run.executionKey}`} value={run.executionKey}>
                    {run.experimentName} · {run.executionKey}
                  </option>
                ))}
              </Select>
            </Field>
          )}
          <Field label="停止规则"><Textarea required rows={3} value={form.stopRule} onChange={update("stopRule")} /></Field>
          <Field label="回退方案"><Textarea required rows={3} value={form.rollbackPlan} onChange={update("rollbackPlan")} /></Field>
          {experimentValidation && <div className="rounded-xl border border-amber-200 bg-amber-50 p-4">
            <p className="text-sm font-semibold text-amber-950">本次验证还差什么</p>
            <ul className="mt-2 space-y-1 text-[13px] text-amber-900">
              {experimentValidation.isValid ? <li>✓ 当前预检已通过；提交时仍会校验最新项目版本。</li> : (experimentValidation.errors || []).map(issue => <li key={`${issue.field}-${issue.code}`}>✗ {issue.message}{issue.fixHint ? ` ${issue.fixHint}` : ""}</li>)}
            </ul>
          </div>}
        </>}
        {task === "history" && <>
          <Alert tone="info" title="把已有数据变成优化观察">
            系统只读取已完成运行的实际控制参数回读、过程特征和检验记录；不会向设备写入参数。至少选择两种实际工艺规范条件，导入后优化器才能使用这些观察。
          </Alert>
          {historyError && <Alert tone="danger">{historyError}</Alert>}
          {historyLoading ? <Alert tone="info">正在读取可导入的已完成运行…</Alert> : (
            <section className="overflow-hidden rounded-2xl border border-slate-200 bg-slate-50/70" aria-labelledby="history-execution-heading">
              <div className="border-b border-slate-200 bg-white p-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                  <div>
                    <h3 id="history-execution-heading" className="font-semibold text-slate-900">选择已完成运行</h3>
                    <p className="mt-1 text-sm text-slate-500">
                      已选 <strong className="font-semibold text-blue-700">{form.executionIds?.length || 0}</strong> / {historicalProcessExecutions.length} 条
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      onClick={() => setForm({
                        ...form,
                        executionIds: Array.from(new Set([
                          ...(form.executionIds || []),
                          ...visibleHistoricalProcessExecutions.map(item => item.executionId),
                        ])),
                      })}
                      disabled={visibleHistoricalProcessExecutions.length === 0}
                    >
                      选择当前结果
                    </Button>
                    <Button variant="ghost" onClick={() => setForm({ ...form, executionIds: [] })} disabled={!form.executionIds?.length}>清空</Button>
                  </div>
                </div>
                <Input
                  className="mt-4"
                  type="search"
                  value={historyFilter}
                  onChange={event => setHistoryFilter(event.target.value)}
                  placeholder="搜索运行号、产品、设备、Edge、批次或工艺规范"
                  aria-label="搜索已完成运行"
                />
                <p className="mt-2 text-[13px] leading-5 text-slate-500">
                  默认选中与项目产品匹配的运行。可跨节点多选；导入前请确认至少包含两种实际工艺规范条件。
                </p>
              </div>
              {visibleHistoricalProcessExecutions.length > 0 ? (
                <div className="grid max-h-[52vh] gap-3 overflow-y-auto p-3 md:grid-cols-2">
                  {visibleHistoricalProcessExecutions.map(execution => {
                    const selected = selectedHistoryExecutionIds.has(execution.executionId);
                    const product = execution.productFamilyCode || execution.productCode || "未标注产品";
                    return (
                      <label
                        key={execution.executionId}
                        className={`flex cursor-pointer gap-3 rounded-lg border p-4 transition ${selected ? "border-blue-400 bg-blue-50 ring-1 ring-blue-200" : "border-slate-200 bg-white hover:border-blue-300 hover:bg-blue-50/40"}`}
                      >
                        <input
                          type="checkbox"
                          className="mt-1 size-4 shrink-0 accent-blue-600"
                          checked={selected}
                          onChange={event => updateHistoryExecution(execution.executionId, event.target.checked)}
                        />
                        <span className="min-w-0 flex-1">
                          <span className="flex items-start justify-between gap-3">
                            <strong className="truncate text-sm font-semibold text-slate-900" title={execution.executionId}>{product}</strong>
                            <span className="shrink-0 text-[13px] text-slate-500">{execution.completedAt ? new Date(execution.completedAt).toLocaleString("zh-CN") : "完成时间未知"}</span>
                          </span>
                          <span className="mt-2 flex flex-wrap gap-1.5 text-[13px]">
                            <span className="rounded-md bg-slate-100 px-2 py-1 text-slate-700">设备 {execution.equipmentId || "未标注"}</span>
                            <span className="rounded-md bg-slate-100 px-2 py-1 text-slate-700">Edge {execution.edgeIds?.join(" / ") || "未标注"}</span>
                          </span>
                          <span className="mt-3 grid gap-1 text-[13px] leading-5 text-slate-600">
                            <span><span className="text-slate-400">工艺规范</span> {execution.processSpecificationId || "未标注"}</span>
                            {(execution.externalBatchRef || execution.outputItemId) && <span><span className="text-slate-400">追溯</span> {[execution.externalBatchRef && `批次 ${execution.externalBatchRef}`, execution.outputItemId && `工件 ${execution.outputItemId}`].filter(Boolean).join(" · ")}</span>}
                            <span className="truncate font-mono text-xs text-slate-400" title={execution.executionId}>{execution.executionId}</span>
                          </span>
                        </span>
                      </label>
                    );
                  })}
                </div>
              ) : (
                <div className="p-8 text-center text-sm text-slate-500">{historicalProcessExecutions.length ? "没有匹配的运行，请调整搜索条件。" : "当前没有可导入的已完成运行。"}</div>
              )}
              {(form.executionIds?.length || 0) < 2 && (
                <div className="border-t border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">至少选择 2 条运行后才能保存。</div>
              )}
            </section>
          )}
        </>}
        {task === "claim" && <>
          {beneficialTransfers.length > 0 && <Field label="知识证据类型"><Select value={form.knowledgeSourceType} onChange={update("knowledgeSourceType")}><option value="window">当前项目已验证工艺操作域</option><option value="transfer">经复核的迁移收益</option></Select></Field>}
          {form.knowledgeSourceType !== "transfer" ? (
            <Field label="来源工艺操作域"><Select required value={form.operatingRegionId} onChange={update("operatingRegionId")}>{validatedOperatingRegions.map(item => <option key={item.operatingRegionId} value={item.operatingRegionId}>{item.name}</option>)}</Select></Field>
          ) : (
            <Field label="来源迁移评估" hint="系统还会校验同一源窗口是否至少两次相对从零对照取得经复核收益。"><Select required value={form.transferAssessmentId} onChange={update("transferAssessmentId")}>{beneficialTransfers.map(item => <option key={item.assessmentId} value={item.assessmentId}>相对从零收益 {formatResearchNumber(Number(item.relativeGain) * 100)}% · {item.contextDifferences?.length || 0} 项条件变化</option>)}</Select></Field>
          )}
          <Field label="知识声明"><Textarea required rows={4} value={form.statement} onChange={update("statement")} /></Field>
          <Field label="适用范围"><Textarea required rows={4} value={form.applicability} onChange={update("applicability")} /></Field>
        </>}
        {task === "rollback-drill" && <>
          <Alert tone="warning">请填写已经执行的演练，不要把计划动作当成实际结果。失败演练同样应如实保存，但不会通过在线门禁。</Alert>
          <Field label="演练名称"><Input required value={form.drillName} onChange={update("drillName")} /></Field>
          <Field label="演练场景"><Textarea required rows={3} value={form.drillScenario} onChange={update("drillScenario")} /></Field>
          <Field label="停止触发条件"><Textarea required rows={3} value={form.drillStopTrigger} onChange={update("drillStopTrigger")} /></Field>
          <Field label="回退目标"><Textarea required rows={3} value={form.drillRollbackTarget} onChange={update("drillRollbackTarget")} /></Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="预期动作（每行一项）"><Textarea required rows={5} value={form.drillExpectedActions} onChange={update("drillExpectedActions")} /></Field>
            <Field label="实际完成动作（每行一项）"><Textarea required rows={5} value={form.drillObservedActions} onChange={update("drillObservedActions")} /></Field>
          </div>
          <Field label="演练结论"><Select value={form.drillPassed} onChange={update("drillPassed")}><option value="false">未通过</option><option value="true">通过</option></Select></Field>
          <Field label="证据引用" hint="填写运行号、日志归档号、演练记录编号或其他可定位引用。"><Input required value={form.drillEvidenceReference} onChange={update("drillEvidenceReference")} /></Field>
          <Field label="证据 SHA-256" hint="对原始演练日志或记录文件计算 SHA-256，防止复核后内容被替换。"><Input required minLength="64" maxLength="64" pattern="[a-fA-F0-9]{64}" value={form.drillEvidenceContentHash} onChange={update("drillEvidenceContentHash")} /></Field>
        </>}
        {task === "transfer" && <>
          <Alert tone="warning" title="迁移不是复制工艺规范">
            这里只比较已经完成的两组目标现场实测：一组按源窗口执行，一组从目标条件独立起步。系统不会向设备下发源参数；单次有收益也不能直接沉淀为通用知识。
          </Alert>
          <Field label="源生产工艺操作域" hint="仅列出当前用户可访问且已经生产发布的窗口。">
            <Select required value={form.sourceOperatingRegionId} onChange={update("sourceOperatingRegionId")}>
              {(workspace.transferSources || []).map(item => <option key={item.operatingRegionId} value={item.operatingRegionId}>{item.sourceProjectName} · {item.operatingRegionName} · {item.sourceMaterialName || "材料未声明"}</option>)}
            </Select>
          </Field>
          <Field label="迁移组实测结果" hint="实际设置必须全部位于源窗口内，且至少三个重复、两个区组。">
            <Select required value={form.transferResultId} onChange={update("transferResultId")}>
              {workspace.experimentResults.map(item => <option key={item.resultId} value={item.resultId}>{resultLabel(item)}</option>)}
            </Select>
          </Field>
          <Field label="从零对照组实测结果" hint="必须是当前目标项目中的另一组独立结果，不能与迁移组相同。">
            <Select required value={form.coldStartResultId} onChange={update("coldStartResultId")}>
              {workspace.experimentResults.map(item => <option key={item.resultId} value={item.resultId}>{resultLabel(item)}</option>)}
            </Select>
          </Field>
          <Field label="现场说明（可选）" hint="记录设备、材料、工装、产品或环境差异以及未纳入模型的边界。">
            <Textarea rows={4} value={form.transferNotes} onChange={update("transferNotes")} />
          </Field>
        </>}
        {task === "preregistration" && <>
          <Alert tone="warning" title="冻结前确认">保存后不能覆盖；项目定义变化时必须创建并独立复核新版本。这里记录的是验证协议和当前流程基线，不用于员工绩效评价。</Alert>
          <Field label="数据范围"><Textarea required rows={3} value={form.preregDataScope} onChange={update("preregDataScope")} /></Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="数据开始"><Input required type="datetime-local" value={form.preregDataFrom} onChange={update("preregDataFrom")} /></Field>
            <Field label="数据结束"><Input required type="datetime-local" value={form.preregDataTo} onChange={update("preregDataTo")} /></Field>
            <Field label="Edge 编号（可选）"><Input value={form.preregEdgeId} onChange={update("preregEdgeId")} /></Field>
            <Field label="设备编号（可选）"><Input value={form.preregEquipmentId} onChange={update("preregEquipmentId")} /></Field>
            <Field label="数据基线最大运行数"><Input required type="number" min="1" max="5000" value={form.preregMaximumRuns} onChange={update("preregMaximumRuns")} /></Field>
          </div>
          <Field label="纳入方式"><Textarea required rows={3} value={form.preregInclusionMethod} onChange={update("preregInclusionMethod")} /></Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="纳入规则（每行一项）"><Textarea required rows={5} value={form.preregInclusionRules} onChange={update("preregInclusionRules")} /></Field>
            <Field label="排除规则（每行一项）"><Textarea required rows={5} value={form.preregExclusionRules} onChange={update("preregExclusionRules")} /></Field>
            <Field label="匹配与分层规则（每行一项）"><Textarea required rows={5} value={form.preregMatchingRules} onChange={update("preregMatchingRules")} /></Field>
            <Field label="比较基线（每行一项）"><Textarea required rows={5} value={form.preregBaselineMethods} onChange={update("preregBaselineMethods")} /></Field>
            <Field label="主要指标（每行一项）"><Textarea required rows={5} value={form.preregPrimaryMetrics} onChange={update("preregPrimaryMetrics")} /></Field>
            <Field label="守门指标（每行一项）"><Textarea required rows={5} value={form.preregGuardrailMetrics} onChange={update("preregGuardrailMetrics")} /></Field>
            <Field label="停止条件（每行一项）"><Textarea required rows={5} value={form.preregStopConditions} onChange={update("preregStopConditions")} /></Field>
            <Field label="否证条件（每行一项）"><Textarea required rows={5} value={form.preregFalsificationConditions} onChange={update("preregFalsificationConditions")} /></Field>
          </div>
          <Card title="工程师当前流程基线" description="记录使用 Ingot 前完成同类任务实际需要的时间和步骤。">
            <Field label="流程名称"><Input required value={form.preregWorkflowName} onChange={update("preregWorkflowName")} /></Field>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="开始时间"><Input required type="datetime-local" value={form.preregWorkflowStart} onChange={update("preregWorkflowStart")} /></Field>
              <Field label="结束时间"><Input required type="datetime-local" value={form.preregWorkflowEnd} onChange={update("preregWorkflowEnd")} /></Field>
            </div>
            <Field label="步骤与耗时" hint="每行填写：步骤名称|分钟"><Textarea required rows={6} value={form.preregWorkflowSteps} onChange={update("preregWorkflowSteps")} /></Field>
            <Field label="说明（可选）"><Textarea rows={3} value={form.preregWorkflowNotes} onChange={update("preregWorkflowNotes")} /></Field>
          </Card>
        </>}
      </form>
    </Drawer>
  );
}


function VariableSelect({ variables, value, onChange }) {
  return (
    <Field label="可控变量">
      <Select required value={value} onChange={onChange}>
        {variables.map(item => <option key={item.code} value={item.code}>{item.name}（{item.unit}）</option>)}
      </Select>
    </Field>
  );
}
