#!/usr/bin/env node

// 对实际 Platform 部署执行只读业务闭环验收；不会创建、发布或修改生产记录。

import { createHash } from "node:crypto";
import { chmod, mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";

function parseArgs(argv) {
  const options = {
    baseUrl: process.env.INGOT_PLATFORM_URL || "http://127.0.0.1:4010",
    username: process.env.INGOT_ACCEPTANCE_USERNAME || "",
    password: process.env.INGOT_ACCEPTANCE_PASSWORD || "",
    output: "",
  };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--help" || argument === "-h") options.help = true;
    else if (argument === "--base-url") options.baseUrl = argv[++index] || "";
    else if (argument === "--username") options.username = argv[++index] || "";
    else if (argument === "--password") options.password = argv[++index] || "";
    else if (argument === "--output") options.output = argv[++index] || "";
    else throw new Error(`未知参数：${argument}`);
  }
  options.baseUrl = options.baseUrl.replace(/\/+$/, "");
  return options;
}

function usage() {
  console.log(`用法：
  INGOT_ACCEPTANCE_USERNAME=... INGOT_ACCEPTANCE_PASSWORD=... \\
    node scripts/verify-pilot-workflow.mjs [--base-url URL] [--output FILE]

默认地址：http://127.0.0.1:4010
建议通过环境变量传入口令，避免写入 shell 历史。`);
}

const rows = payload => Array.isArray(payload) ? payload : payload?.data || payload?.items || [];
const published = payload => rows(payload).filter(item => item.status === "published");
const valuePresent = value => value !== null && value !== undefined && String(value).trim() !== "";

function taskIsRunning(task, edges) {
  const edge = edges.find(item => item.edgeId === task.edgeId);
  return edge?.acquisition?.state === "running" && (task.valueMappings?.length || 0) > 0;
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  if (options.help) {
    usage();
    return;
  }
  if (!options.baseUrl || !options.username || !options.password) {
    usage();
    throw new Error("必须提供平台地址、验收用户名和口令。");
  }

  let token = "";
  const request = async (path, init = {}) => {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 15_000);
    try {
      const response = await fetch(`${options.baseUrl}${path}`, {
        ...init,
        signal: controller.signal,
        headers: {
          Accept: "application/json",
          ...(init.body ? { "Content-Type": "application/json" } : {}),
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...init.headers,
        },
      });
      const text = await response.text();
      let body = null;
      if (text) {
        try { body = JSON.parse(text); } catch { body = { detail: text }; }
      }
      if (!response.ok) throw new Error(`${init.method || "GET"} ${path} 返回 ${response.status}：${body?.detail || body?.title || "请求失败"}`);
      return body;
    } finally {
      clearTimeout(timeout);
    }
  };

  const checks = [];
  const addCheck = (code, title, passed, evidence, remediation) => checks.push({ code, title, passed, evidence, remediation });

  try {
    const identity = await request("/api/v1/auth/login", {
      method: "POST",
      body: JSON.stringify({ username: options.username, password: options.password }),
    });
    token = identity?.token || "";
    if (!token) throw new Error("登录响应没有返回会话令牌。");

    const [
      health, edgesPayload, modelsPayload, specificationsPayload, analysisPayload,
      definitionsPayload, qualityPlansPayload, packagesPayload, tasksPayload,
      componentTypesPayload, componentsPayload, toolingTypesPayload, revisionsPayload,
      contextsPayload, executionsPayload, inspectionsPayload, reliability,
    ] = await Promise.all([
      request("/health"),
      request("/api/edges"),
      request("/api/v1/process-data-models"),
      request("/api/v1/process-specifications"),
      request("/api/v1/process-analysis-plans"),
      request("/api/v1/inspection-definitions"),
      request("/api/v1/inspection-plans"),
      request("/api/v1/scenario-packages"),
      request("/api/v1/ingestion-tasks"),
      request("/api/v1/tooling-component-types"),
      request("/api/v1/tooling-components"),
      request("/api/v1/tooling-types"),
      request("/api/v1/tooling-assemblies/revisions"),
      request("/api/v1/production-contexts"),
      request("/api/v1/process-executions?limit=200"),
      request("/api/v1/inspection-records"),
      request("/api/v1/data-reliability/baseline?maximumRuns=2000"),
    ]);

    const edges = rows(edgesPayload);
    const tasks = published(tasksPayload);
    const componentTypes = rows(componentTypesPayload);
    const components = rows(componentsPayload);
    const toolingTypes = published(toolingTypesPayload);
    const revisions = rows(revisionsPayload);
    const contexts = rows(contextsPayload);
    const executions = rows(executionsPayload);
    const inspections = rows(inspectionsPayload);

    addCheck(
      "platform-health",
      "平台健康与身份",
      health?.status === "ok" && Array.isArray(identity?.roles) && identity.roles.length > 0,
      `health=${health?.status || "unknown"}；用户=${identity?.username || identity?.displayName || "unknown"}；岗位=${(identity?.roles || []).join(",") || "none"}`,
      "恢复 Platform 健康检查，并为验收账户分配明确岗位。",
    );

    const configurationCounts = {
      dataModels: published(modelsPayload).length,
      specifications: published(specificationsPayload).length,
      analysisPlans: published(analysisPayload).length,
      inspectionDefinitions: rows(definitionsPayload).length,
      qualityPlans: published(qualityPlansPayload).length,
      packages: published(packagesPayload).length,
    };
    addCheck(
      "configuration-release",
      "版本化工艺配置",
      Object.values(configurationCounts).every(count => count > 0),
      Object.entries(configurationCounts).map(([key, count]) => `${key}=${count}`).join("；"),
      "发布数据字典、工艺规范、分析规则、检测定义、质量方案和最终配置版本。",
    );

    const runningSources = tasks.filter(task => taskIsRunning(task, edges));
    addCheck(
      "field-source",
      "真实来源与现场节点",
      runningSources.length > 0,
      `已发布数据源=${tasks.length}；运行且含点位映射=${runningSources.length}；协议=${[...new Set(tasks.map(task => task.protocol))].join(",") || "none"}`,
      "至少让一个已发布数据源在目标现场节点真实运行，并保留有效点位映射。",
    );

    const componentTypeCodes = new Set(componentTypes.map(item => item.componentTypeCode));
    const componentIds = new Set(components.map(item => item.componentId));
    const completeToolingRevision = revisions.find(revision => {
      const definition = toolingTypes.find(item => item.toolingTypeCode === revision.toolingTypeCode && Number(item.version) === Number(revision.toolingTypeVersion));
      if (!definition) return false;
      const memberRoles = new Set((revision.members || []).filter(member => componentIds.has(member.componentId)).map(member => member.roleCode));
      return (definition.roles || []).filter(role => role.required !== false).every(role => memberRoles.has(role.code))
        && (definition.roles || []).every(role => (role.acceptedComponentTypeCodes || []).every(code => componentTypeCodes.has(code)));
    });
    addCheck(
      "tooling-traceability",
      "工装结构与实际成员",
      Boolean(completeToolingRevision),
      `组件分类=${componentTypes.length}；组件资产=${components.length}；完整不可变总成版本=${completeToolingRevision?.assemblyRevisionId || "none"}`,
      "建立工装结构、独立组件资产和包含全部必需位置的不可变总成版本。",
    );

    const activeContexts = contexts.filter(item => !item.validTo && item.status !== "closed");
    const completeContext = activeContexts.find(item => [
      item.equipmentId, item.productCode, item.processSpecificationId,
      item.toolingAssemblyId || item.toolingInstallationId,
      item.externalBatchRef, item.materialLotRef,
    ].every(valuePresent) && item.calibrationStatus === "valid");
    addCheck(
      "production-context",
      "生产开始前上下文",
      Boolean(completeContext),
      `生效上下文=${activeContexts.length}；完整上下文=${completeContext?.contextId || "none"}`,
      "在生产切换中绑定设备、产品、工艺规范、工装、生产批次、物料批次和有效校准。",
    );

    const completedExecutions = executions.filter(item => item.status === "completed" && item.lifecycleComplete !== false);
    const linkedExecution = completedExecutions.find(execution => inspections.some(record => record.executionId === execution.executionId));
    addCheck(
      "run-quality-loop",
      "运行与检验闭环",
      Boolean(linkedExecution),
      `完整运行=${completedExecutions.length}；已关联检验的运行=${linkedExecution?.executionId || "none"}`,
      "完成一次真实运行，并让检验记录通过 executionId 唯一关联该运行。",
    );

    const admissionRate = (reliability?.rates || []).find(item => item.code === "analysis_admission")?.rate ?? 0;
    addCheck(
      "data-reliability",
      "数据可信度与分析准入",
      Number(reliability?.analyzedRunCount || 0) > 0 && Number(admissionRate) > 0,
      `分析运行=${reliability?.analyzedRunCount || 0}；正式分析准入率=${Number(admissionRate).toFixed(3)}；排除类型=${(reliability?.exclusions || []).length}`,
      "修复运行身份、过程数据、上下文或检验关联，使至少一条运行通过正式分析准入。",
    );

    let comparison = null;
    const comparisonTarget = completedExecutions.find(item => String(item.qualityStatus || "").toUpperCase() === "FAIL") || completedExecutions[0];
    if (comparisonTarget) {
      const siteQuery = comparisonTarget.siteId ? `?siteId=${encodeURIComponent(comparisonTarget.siteId)}` : "";
      comparison = await request(`/api/v1/execution-comparisons/${encodeURIComponent(comparisonTarget.executionId)}${siteQuery}`);
    }
    const candidates = comparison?.diagnosis?.candidates || [];
    const guardrail = comparison?.investigation?.conclusionGuardrail || "";
    addCheck(
      "diagnosis-boundary",
      "候选原因与证据边界",
      candidates.length > 0 && /候选|验证|因果|candidate|validation|causal/i.test(guardrail),
      `基线运行=${comparisonTarget?.executionId || "none"}；候选=${candidates.length}；边界说明=${guardrail || "none"}`,
      "让运行对比返回候选、反证/混杂信息，并明确说明候选必须通过工程师决定后的真实生产运行和质量结果复核。",
    );

    const projectsPayload = await request("/api/v1/research-projects?limit=100");
    const projects = rows(projectsPayload);
    const activeProject = projects.find(project => project.status === "active") || projects[0];
    let researchDetail = null;
    if (activeProject?.projectId) researchDetail = await request(`/api/v1/research-projects/${encodeURIComponent(activeProject.projectId)}`);
    addCheck(
      "recipe-recommendation-loop",
      "下一配方建议闭环",
      projects.length > 0
        && (researchDetail?.recipeRecommendationFlows || []).length > 0
        && (researchDetail?.recipeRecommendationFlows || []).some(flow => flow.decision)
        && (researchDetail?.recipeRecommendationFlows || []).some(flow => flow.actualExecutionKey && flow.outcome),
      `项目=${projects.length}；验收任务=${activeProject?.projectId || "none"}；建议=${researchDetail?.recipeRecommendationFlows?.length || 0}；已决定=${researchDetail?.recipeRecommendationFlows?.filter(flow => flow.decision).length || 0}；已闭环=${researchDetail?.recipeRecommendationFlows?.filter(flow => flow.actualExecutionKey && flow.outcome).length || 0}`,
      "从真实运行证据生成下一配方建议，冻结工程师决定，并关联后续生产运行和质量结果。",
    );

    let users = [];
    if ((identity.roles || []).includes("platform.admin")) users = rows(await request("/api/v1/users"));
    const enabledUsers = users.filter(user => !user.disabled);
    const availableRoles = new Set(enabledUsers.flatMap(user => user.roles || []));
    const roleSeparation = ["platform.admin", "process.engineer", "quality.inspector", "quality.reviewer"]
      .every(role => availableRoles.has(role));
    addCheck(
      "role-separation",
      "岗位分权",
      users.length > 0 && roleSeparation,
      users.length > 0 ? `启用账户=${enabledUsers.length}；岗位=${[...availableRoles].sort().join(",")}` : "验收账户不是平台管理员，无法核对用户分权。",
      "使用平台管理员执行验收，并确保管理、工艺、质量录入和质量复核岗位均有启用账户。",
    );

    const failedChecks = checks.filter(check => !check.passed);
    const report = {
      format: "ingot-controlled-pilot-workflow-v1",
      result: failedChecks.length ? "failed" : "business-workflow-passed",
      generatedAt: new Date().toISOString(),
      baseUrl: options.baseUrl,
      identity: { username: identity.username, roles: identity.roles, siteIds: identity.siteIds },
      checks,
      boundary: "本工件只证明只读业务闭环检查通过，不等于生产准入；生产准入仍必须完成备份恢复、故障演练、容量、告警路由和连续观察，并运行 verify-production-acceptance.sh。",
      requiredExternalEvidence: [
        "应用备份与独立恢复演练",
        "Edge 断网、积压和补传演练",
        "数据库 RPO/RTO 与 HA/PITR 证据",
        "峰值两倍容量证据",
        "监控告警实际送达证据",
        "达到声明时长的连续观察记录",
      ],
    };

    const serialized = `${JSON.stringify(report, null, 2)}\n`;
    if (options.output) {
      const output = resolve(options.output);
      await mkdir(dirname(output), { recursive: true });
      await writeFile(output, serialized, { flag: "wx", mode: 0o600 });
      await chmod(output, 0o600);
      const digest = createHash("sha256").update(serialized).digest("hex");
      await writeFile(`${output}.sha256`, `${digest}  ${output.split("/").pop()}\n`, { flag: "wx", mode: 0o600 });
      console.log(`验收工件：${output}`);
    }

    for (const check of checks) console.log(`${check.passed ? "✓" : "✗"} ${check.title}：${check.evidence}`);
    console.log(`\n结论：${report.result}`);
    console.log(`边界：${report.boundary}`);
    if (failedChecks.length) process.exitCode = 1;
  } finally {
    if (token) {
      await fetch(`${options.baseUrl}/api/v1/auth/logout`, {
        method: "POST",
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
        body: "{}",
      }).catch(() => null);
    }
  }
}

main().catch(error => {
  console.error(`试点业务闭环验收失败：${error.name === "AbortError" ? "请求超时。" : error.message}`);
  process.exitCode = 1;
});
