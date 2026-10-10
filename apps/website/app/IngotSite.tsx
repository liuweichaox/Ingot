
"use client";

// Renders the bilingual public product narrative; validation detail stays in linked evidence documents.

import { Disclosure, DisclosureButton, DisclosurePanel } from "@headlessui/react";
import Image from "next/image";
import { useEffect, useState, type CSSProperties, type ReactNode } from "react";

type Locale = "zh" | "en";

const copy = {
  zh: {
    switchLabel: "EN",
    switchHref: "/en/",
    docs: "https://docs.ingotstack.com/zh",
    nav: [
      ["核心价值", "#product"],
      ["工作方式", "#loop"],
      ["优化方法", "#optimizer"],
      ["系统边界", "#architecture"],
      ["开源", "#open-source"],
    ],
    github: "查看 GitHub",
    docsLabel: "文档",
    eyebrow: "PROCESS R&D · ANALYSIS · OPTIMIZATION",
    titleA: "从工艺数据，",
    titleB: "到有依据的研发决策。",
    lead: "开源工艺研发与优化系统。组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化。",
    primary: "阅读快速开始",
    secondary: "了解工作方式",
    truth: ["证据可追溯", "原因可验证", "建议可审核", "结论可复用"],
    productKicker: "FROM DATA TO DECISION",
    productTitle: "把研发记录、工艺分析和配方决策放在同一工作流中。",
    productText: "围绕配方版本组织目标、实验记录和工程判断；直接关联已完成运行的实际参数、过程上下文和质量结果，无需工程师重新归类配方。比较差异、检查证据，再审核建议与配方修订。",
    productCards: [
      ["01", "组织研发记录", "选定配方版本，定义质量目标、可调参数与边界。每次运行就是一次实验，实测值保存在运行与质量记录中。"],
      ["02", "核对运行与质量", "关联实际参数、过程轨迹与检验结果，检查完整性和适用条件。"],
      ["03", "分析工艺差异", "比较可比运行，整理关键差异、候选原因与证据缺口。"],
      ["04", "审核配方建议", "基于通过准入的运行证据提出下一轮校正。工程师审核后在同版本下验证，显著变更才创建修订草稿。"],
    ],
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "系统组织事实与建议，工程师掌握研发决策。",
    loopText: "配方版本保存参数设定，运行与质量记录保存实际过程和结果，分析说明差异和证据边界，建议保留输入与理由。工程师审核是否采用、如何校正，并根据后续结果继续判断。",
    loopSteps: [
      ["01", "定义", "问题 · 变量 · 边界"],
      ["02", "记录", "参数 · 轨迹 · 结果"],
      ["03", "核验", "质量 · 来源 · 完整性"],
      ["04", "追因", "比较 · 候选原因 · 证据"],
      ["05", "建议", "目标 · 约束 · 不确定性"],
      ["06", "复核", "决定 · 修订 · 后续结果"],
    ],
    optimizerKicker: "THE ENGINEERING TOOLBOX",
    optimizerTitle: "先确认数据是否可靠，再审核下一轮校正。",
    optimizerText: "推荐器先确认生产运行是否完整、可比且关联质量结果，再在声明的变量、安全边界和历史覆盖内形成候选建议。工程师负责审核建议；系统不自动修改生产参数。",
    admissionText: "至少三条有效运行、两种不同实际配方只是最低门槛，还需通过质量、覆盖、约束和方法检查。数据不足时停止建议，并说明原因。",
    admissionLink: "查看推荐准入与试点步骤",
    troubleshootingLink: "启动与运行排障",
    methodA: "确认数据可用",
    methodAText: "核对数据是否完整、实际值与单位是否一致、时间和来源是否明确，并检查版本变化与适用范围。",
    methodB: "工艺追因",
    methodBText: "使用匹配比较、稳健统计、阶段轨迹和上下文分层缩小候选范围。",
    methodC: "固化机理依据",
    methodCText: "将参数作用、已知边界和工程判断按站点与配方归属管理；建议冻结本次使用的知识版本，并引用对应运行、质量证据和已复核工艺资料片段。",
    methodD: "审核下一轮校正",
    methodDText: "小校正留在当前配方版本，决定后开始、已完成且符合条件的同版本运行用于关联。显著变更使用建议参数创建修订草稿，保留来源与理由。",
    engineFeatures: ["数据质量", "真实运行", "版本谱系", "片段级引用", "已复核知识", "工程决策"],
    archKicker: "RECORDS · ANALYSIS · DECISIONS",
    archTitle: "研发记录、分析与决策，各有明确职责。",
    archText: "Web、API、Worker、数据库与优化服务构成自身运行栈，连接器是可选的数据来源。正式记录保存来源、版本与审核状态，分析和建议引用这些事实；工程师确认参数变化与适用范围。",
    layers: [
      ["研发记录", "配方 · 变量 · 边界", "组织目标、参数设定、可调范围和适用条件"],
      ["运行与质量", "参数 · 轨迹 · 检验", "关联运行上下文与质量结果，核对版本、完整性和审核状态"],
      ["分析与建议", "比较 · 追因 · 优化", "整理候选原因，形成受证据和安全边界约束的配方建议"],
      ["工程决策", "审核 · 版本 · 知识", "保存采用、修改或拒绝的决定；修改和拒绝须说明理由，并引用经过复核的工艺资料"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "工艺能力持续升级，证据边界始终不变。",
    visionText: "设备、产品或工艺场景变更时，需要重新配置数据映射、变量、边界和上下文；运行身份、证据原则、配方版本谱系和工程师决策权保持稳定。",
    reusable: [
      ["长期不变", "真实数据支持工程判断，观察结论必须能够回到来源并接受验证"],
      ["按场景配置", "设备映射、变量、阶段、质量边界、上下文和机理依据"],
      ["持续演进", "统计方法、追因策略、页面布局和语言模型"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "开源工艺研发与优化系统。",
    openText: "Ingot 采用 Apache-2.0 许可，可在厂内自托管。源码、部署说明和方法边界均可查阅，具体场景评估由部署方用自己的数据完成。",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\n# 编辑 .env，替换所有 change-this 占位值\ndocker compose -f docker-compose.app.yml config --quiet\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "阅读快速开始",
    contribute: "参与贡献",
    reportIssue: "报告问题",
    statusLabel: "当前成熟度",
    statusText: "每次工艺运行就是一次实验，配方版本保存参数设定，运行记录关联实际参数、过程数据和质量结果；符合准入条件的运行证据用于配方建议，实际运行、参数回读和检验记录用于一次性冻结最终结果。主要软件流程已有自动化测试；仓库不提供现场收益验证结果，适用性与收益由部署方用自己的证据评估。默认 Compose 提供单机参考部署，部署方仍需完成站点安全、恢复、容量和运维验收。",
    statusLink: "当前能力与限制",
    deploymentLink: "部署与生产验收",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "从一个真实工艺问题开始。",
    ctaText: "发布配方版本明确变量与边界，关联运行和质量记录，或从已有运行开始比较差异并审核下一轮校正。",
    ctaPrimary: "完成第一轮配方试点",
    ctaSecondary: "打开 GitHub",
    footer: "Ingot · 从工艺数据，到有依据的研发决策。",
  },
  en: {
    switchLabel: "中文",
    switchHref: "/",
    docs: "https://docs.ingotstack.com/en",
    nav: [
      ["Core value", "#product"],
      ["Workflow", "#loop"],
      ["Optimization", "#optimizer"],
      ["Boundaries", "#architecture"],
      ["Open source", "#open-source"],
    ],
    github: "View GitHub",
    docsLabel: "Docs",
    eyebrow: "PROCESS R&D · ANALYSIS · OPTIMIZATION",
    titleA: "From process data",
    titleB: "to evidence-based R&D decisions.",
    lead: "Open-source Process R&D and Optimization System. Organize recipe versions, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.",
    primary: "Read the quickstart",
    secondary: "See how it works",
    truth: ["Traceable evidence", "Testable causes", "Reviewable recommendations", "Reusable conclusions"],
    productKicker: "FROM DATA TO DECISION",
    productTitle: "Bring research records, process analysis, and recipe decisions into one workflow.",
    productText: "Organize objectives, experiment records, and engineering judgment by recipe version. The system directly links actual settings, process context, and quality outcomes from completed runs without manual recipe reclassification. Compare differences and check evidence before reviewing recommendations and recipe revisions.",
    productCards: [
      ["01", "Organize research records", "Select a recipe version and define objectives, adjustable parameters, and boundaries. Each run is an experiment; run and quality records retain measured values."],
      ["02", "Check runs and quality", "Link actual settings, process trajectories, and inspection outcomes; check completeness and applicability."],
      ["03", "Analyze process differences", "Compare compatible runs and organize key differences, candidate causes, and evidence gaps."],
      ["04", "Review recipe recommendations", "Use qualified run evidence to propose the next correction. Engineers review it for another run on the same version; significant changes create a revision draft."],
    ],
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "The system organizes facts and recommendations; engineers make R&D decisions.",
    loopText: "Recipe versions retain parameter settings; run and quality records retain actual process data and outcomes. Analysis explains differences and evidence boundaries, and recommendations preserve inputs and rationale. Engineers review adoption and corrections, then use later outcomes to reassess their decisions.",
    loopSteps: [
      ["01", "Define", "question · variables · boundaries"],
      ["02", "Record", "settings · trajectories · outcomes"],
      ["03", "Qualify", "quality · provenance · completeness"],
      ["04", "Diagnose", "compare · candidates · evidence"],
      ["05", "Recommend", "objectives · constraints · uncertainty"],
      ["06", "Review", "decisions · revisions · later outcomes"],
    ],
    optimizerKicker: "THE ENGINEERING TOOLBOX",
    optimizerTitle: "Confirm that data are trustworthy before reviewing the next correction.",
    optimizerText: "The recommender checks whether production runs are complete, comparable, and linked to quality outcomes before forming a candidate within declared variables, safety boundaries, and observed coverage. Engineers review recommendations; the system never changes production parameters automatically.",
    admissionText: "At least three valid runs and two distinct actual recipes are minimum requirements. Quality, coverage, constraint, and method checks must also pass; insufficient evidence stops recommendations with an explanation.",
    admissionLink: "Review admission and pilot steps",
    troubleshootingLink: "Troubleshoot startup and operation",
    methodA: "Confirm data usability",
    methodAText: "Check completeness, actual values, units, time, and provenance, together with version changes and applicability.",
    methodB: "Process diagnosis",
    methodBText: "Use matching, robust statistics, stage trajectories, and context stratification to narrow candidates.",
    methodC: "Preserve mechanism notes",
    methodCText: "Manage parameter effects, known boundaries, and engineering judgment by site and recipe. Recommendations freeze the knowledge versions used, with run, quality, and reviewed process-document references.",
    methodD: "Review the next correction",
    methodDText: "Small corrections stay on the current recipe version; a completed, eligible same-version run started after the decision is used for linkage. Significant changes use proposed settings to create a revision draft with provenance and rationale.",
    engineFeatures: ["Data quality", "Real runs", "Version lineage", "Fragment citations", "Reviewed knowledge", "Engineering decisions"],
    archKicker: "RECORDS · ANALYSIS · DECISIONS",
    archTitle: "Research records, analysis, and decisions have clear responsibilities.",
    archText: "The Web app, API, Worker, database, and optimizer form the runtime stack; connectors are optional data sources. Formal records retain provenance, versions, and review status, and analysis and recommendations reference those facts. Engineers confirm parameter changes and applicability.",
    layers: [
      ["RESEARCH RECORDS", "recipes · variables · boundaries", "Organize objectives, settings, adjustable scope, and applicability"],
      ["RUNS AND QUALITY", "settings · trajectories · inspections", "Link run context and quality outcomes; check versions, completeness, and review status"],
      ["ANALYSIS AND RECOMMENDATIONS", "comparison · diagnosis · optimization", "Organize candidate causes and recipe recommendations constrained by evidence and safety boundaries"],
      ["ENGINEERING DECISIONS", "review · versions · knowledge", "Retain adoption, modification, or rejection decisions; modification and rejection require reasons, with reviewed process-material references"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "Process capabilities evolve. Evidence boundaries remain fixed.",
    visionText: "A change in machine, product, or process requires new mappings, variables, boundaries, and context. Run identity, evidence principles, recipe version lineage, and engineering authority remain stable.",
    reusable: [
      ["Stays stable", "Real data supports engineering judgment, and every conclusion traces to sources and remains testable"],
      ["Configured per scenario", "Equipment mappings, variables, stages, quality boundaries, context, and mechanism notes"],
      ["Continues evolving", "Statistics, diagnostic strategies, page layouts, and language models"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "Open-source Process R&D and Optimization System.",
    openText: "Ingot is Apache-2.0 licensed and self-hostable inside the plant. Source code, deployment instructions, and method boundaries are available for inspection; scenario-specific evaluation belongs to the deployer's own data.",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\n# Edit .env: replace every change-this placeholder\ndocker compose -f docker-compose.app.yml config --quiet\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "Read the quickstart",
    contribute: "Contribute",
    reportIssue: "Report an issue",
    statusLabel: "Current maturity",
    statusText: "Each process run is an experiment. Recipe versions hold parameter settings, and run records link actual parameters, process data, and quality outcomes. Admitted run evidence feeds recipe recommendations. Actual runs, parameter readback, and inspection records freeze the final outcome once. Core software workflows have automated tests. The repository provides no field-benefit validation results; deployers assess suitability and benefits with their own evidence. Default Compose provides a single-host reference deployment; deployers still own site security, recovery, capacity, and operational acceptance.",
    statusLink: "Current capabilities and limits",
    deploymentLink: "Deployment and production acceptance",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "Begin with a process question.",
    ctaText: "Publish a recipe version with variables and boundaries, link run and quality records, or start with existing runs to compare differences and review the next correction.",
    ctaPrimary: "Complete the first recipe pilot",
    ctaSecondary: "Open GitHub",
    footer: "Ingot · From process data to evidence-based R&D decisions.",
  },
} as const;

const github = "https://github.com/liuweichaox/Ingot";

type SiteCopy = (typeof copy)[Locale];

const workflows = {
  zh: {
    title: "每一轮判断，都能回到运行证据。",
    note: "工作方式示意 · 非实际产品界面 · 无收益数据",
    nodes: [["配方版本", "目标、参数与安全边界"], ["运行", "实际参数、轨迹与上下文"], ["质量", "检验结果与来源"], ["证据", "可比运行、差异与候选原因"], ["校正", "工程师决定与下一轮验证"]],
    next: "下一轮同版本运行 → 检验结果 → 继续判断",
    records: [
      [["配方版本", "已发布参数 · 目标 · 可调范围"], ["运行记录", "使用的版本 · 实际参数 · 过程上下文"], ["实验事实", "由运行与质量记录承载，无独立实验录入"]],
      [["过程证据", "实际参数 · 阶段轨迹 · 时间与来源"], ["质量记录", "实测结果 · 检验依据 · 关联运行"], ["准入检查", "完整性 · 可比性 · 适用条件"]],
      [["运行比较", "核对版本、参数与生产上下文"], ["候选原因", "差异证据 · 不确定性 · 待验证项"], ["知识引用", "已复核工艺资料片段与适用范围"]],
      [["下一轮校正", "候选设置 · 约束 · 工程师采用、修改或拒绝"], ["版本分支", "小校正保持版本，显著变更创建修订草稿"], ["实际结果", "符合条件的同版本运行用于关联，按实际证据一次性冻结结果"]],
    ],
  },
  en: {
    title: "Every decision traces back to run evidence.",
    note: "Workflow illustration · Not the actual interface · No outcome data",
    nodes: [["Recipe version", "Objectives, settings, safety boundaries"], ["Run", "Actual settings, trajectories, context"], ["Quality", "Inspection outcomes and provenance"], ["Evidence", "Comparable runs, differences, candidate causes"], ["Correction", "Engineer decisions and the next validation"]],
    next: "Next same-version run → inspection outcomes → reassess",
    records: [
      [["Recipe version", "Published settings · objectives · adjustable scope"], ["Run record", "Applied version · actual settings · process context"], ["Experiment facts", "Retained in run and quality records, without separate experiment entry"]],
      [["Process evidence", "Actual settings · stage trajectories · time and provenance"], ["Quality record", "Measured outcomes · inspection basis · linked run"], ["Admission checks", "Completeness · comparability · applicability"]],
      [["Run comparison", "Check versions, parameters, and production context"], ["Candidate causes", "Difference evidence · uncertainty · validation gaps"], ["Knowledge references", "Reviewed process-document fragments and applicability"]],
      [["Next correction", "Candidate settings · constraints · engineer adoption, revision, or rejection"], ["Version branch", "Small corrections retain the version; significant changes create a revision draft"], ["Actual outcome", "Link an eligible same-version run and freeze the outcome once from actual evidence"]],
    ],
  },
} as const;

function usePageMotion() {
  useEffect(() => {
    const root = document.documentElement;
    const revealItems = Array.from(document.querySelectorAll<HTMLElement>("[data-reveal]"));
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    root.classList.add("motion-ready");

    if (reducedMotion.matches) {
      revealItems.forEach((item) => item.classList.add("is-visible"));
    }

    const observer = reducedMotion.matches
      ? null
      : new IntersectionObserver(
          (entries) => {
            entries.forEach((entry) => {
              if (entry.isIntersecting) {
                (entry.target as HTMLElement).classList.add("is-visible");
                observer?.unobserve(entry.target);
              }
            });
          },
          { rootMargin: "0px 0px -10%", threshold: 0.12 },
        );

    revealItems.forEach((item) => observer?.observe(item));

    return () => {
      observer?.disconnect();
      root.classList.remove("motion-ready");
    };
  }, []);
}

function Reveal({ children, className = "", delay = 0 }: { children: ReactNode; className?: string; delay?: number }) {
  return (
    <div className={`reveal ${className}`.trim()} data-reveal style={{ "--reveal-delay": `${delay}ms` } as CSSProperties}>
      {children}
    </div>
  );
}

function Hero({ t, locale }: { t: SiteCopy; locale: Locale }) {
  const flow = workflows[locale];
  return (
    <section className="hero" id="main-content">
      <div className="frame hero-layout">
        <div className="hero-copy">
          <p className="eyebrow">{t.eyebrow}</p>
          <h1>{t.titleA}<span>{t.titleB}</span></h1>
          <p className="hero-lead">{t.lead}</p>
          <div className="button-row">
            <a className="button primary" href={`${t.docs}/getting-started`}>{t.primary} <span aria-hidden="true">→</span></a>
            <a className="button quiet" href="#product">{t.secondary} <span aria-hidden="true">↓</span></a>
          </div>
        </div>
        <div className="product-frame hero-product">
          <div className="product-frame-bar"><i /><span>INGOT / EVIDENCE LOOP</span></div>
          <div className="evidence-flow">
            <p className="flow-title">{flow.title}</p>
            <ol className="flow-nodes">
              {flow.nodes.map(([title, description], index) => (
                <li key={title}><span className="flow-number">0{index + 1}</span><div><strong>{title}</strong><p>{description}</p></div><span className="flow-arrow" aria-hidden="true">↓</span></li>
              ))}
            </ol>
            <p className="flow-return"><span aria-hidden="true">↳</span>{flow.next}</p>
            <p className="flow-note">{flow.note}</p>
          </div>
        </div>
      </div>
      <div className="frame hero-truth" role="list" aria-label={locale === "zh" ? "产品原则" : "Product principles"}>
        {t.truth.map((item) => <span role="listitem" key={item}><i />{item}</span>)}
      </div>
    </section>
  );
}

function Story({ t, locale }: { t: SiteCopy; locale: Locale }) {
  const [activeStep, setActiveStep] = useState(0);
  const flow = workflows[locale];

  return (
    <>
      <section className="story section" id="product">
        <div className="frame story-layout">
          <div className="story-copy">
            <div className="story-heading">
              <p className="eyebrow">{t.productKicker}</p>
              <h2>{t.productTitle}</h2>
              <p>{t.productText}</p>
            </div>
            <ol className="story-steps">
              {t.productCards.map(([number, title, text], index) => (
                <li className={index === activeStep ? "is-active" : ""} key={title}>
                  <button type="button" onClick={() => setActiveStep(index)} aria-pressed={index === activeStep} aria-controls="workflow-records">
                    <span>{number}</span>
                    <span>
                      <strong>{title}</strong>
                      <span>{text}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ol>
          </div>
          <div className="story-visual">
            <div className="product-frame story-product" data-step={activeStep} id="workflow-records" role="group" aria-label={locale === "zh" ? "所选步骤的关联业务记录" : "Linked business records for the selected step"}>
              <div className="product-frame-bar"><i /><span>{locale === "zh" ? "关联记录" : "LINKED RECORDS"}</span><small>0{activeStep + 1} / 04</small></div>
              <div className="workflow-records" aria-live="polite" aria-atomic="true">
                <p className="record-step">{t.productCards[activeStep][1]}</p>
                {flow.records[activeStep].map(([title, description], index) => (
                  <div className="workflow-record" key={title}><span className="record-node" aria-hidden="true">0{index + 1}</span><div><h3>{title}</h3><p>{description}</p></div></div>
                ))}
              </div>
            </div>
            <p className="story-note">{flow.note}</p>
            <a className="evidence-link" href={`${t.docs}/status`}>{t.statusLink} <span aria-hidden="true">↗</span></a>
          </div>
        </div>
      </section>
    </>
  );
}

export default function IngotSite({ initialLocale }: { initialLocale: Locale }) {
  const t = copy[initialLocale];
  usePageMotion();

  return (
    <main className="site-shell">
      <a className="skip-link" href="#main-content">{initialLocale === "zh" ? "跳转到主要内容" : "Skip to main content"}</a>
      <header className="site-header">
        <div className="frame header-inner">
          <a className="brand" href={initialLocale === "zh" ? "/" : "/en/"} aria-label={initialLocale === "zh" ? "Ingot 首页" : "Ingot home"}>
            <Image src="/brand/ingot-lockup-dark.svg" alt="Ingot" width={136} height={51} priority />
          </a>
          <nav className="desktop-nav" aria-label={initialLocale === "zh" ? "主导航" : "Primary navigation"}>
            {t.nav.map(([label, href]) => <a key={href} href={href}>{label}</a>)}
          </nav>
          <div className="header-actions">
            <a className="lang" href={t.docs}>{t.docsLabel}</a>
            <a className="lang" href={t.switchHref}>{t.switchLabel}</a>
            <a className="header-github" href={github}>{t.github} <span aria-hidden="true">↗</span></a>
          </div>
          <Disclosure>
            <DisclosureButton className="menu-button" aria-label={initialLocale === "zh" ? "打开导航" : "Open navigation"}>
              <span aria-hidden="true" /><span aria-hidden="true" />
            </DisclosureButton>
            <DisclosurePanel className="mobile-nav">
              {t.nav.map(([label, href]) => <a key={href} href={href}>{label}</a>)}
              <a href={t.docs}>{t.docsLabel}</a>
              <a href={t.switchHref}>{t.switchLabel}</a>
              <a href={github}>{t.github}</a>
            </DisclosurePanel>
          </Disclosure>
        </div>
      </header>

      <Hero t={t} locale={initialLocale} />
      <Story t={t} locale={initialLocale} />

      <section className="closed-loop section" id="loop">
        <div className="frame">
          <Reveal className="principle-copy"><p className="eyebrow">{t.loopKicker}</p><h2>{t.loopTitle}</h2><p>{t.loopText}</p></Reveal>
          <div className="loop-rail" aria-label={initialLocale === "zh" ? "工程闭环" : "Engineering loop"}>
            {t.loopSteps.map(([number, title, text], index) => (
              <Reveal className="loop-step" delay={index * 80} key={number}>
                <span>{number}</span><h3>{title}</h3><p>{text}</p>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      <section className="optimizer section" id="optimizer">
        <div className="frame optimizer-layout">
          <Reveal className="optimizer-copy">
            <p className="eyebrow">{t.optimizerKicker}</p><h2>{t.optimizerTitle}</h2><p>{t.optimizerText}</p>
            <p>{t.admissionText}</p><a className="evidence-link" href={`${t.docs}/pilot`}>{t.admissionLink} ↗</a>
            <div className="tech-line"><span>RUNS</span><span>STATISTICS</span><span>MODELS</span></div>
          </Reveal>
          <div className="model-map">
            {[
              ["DATA", t.methodA, t.methodAText],
              ["COMPARE", t.methodB, t.methodBText],
              ["TEST", t.methodC, t.methodCText],
              ["REVISE", t.methodD, t.methodDText],
            ].map(([label, title, text], index) => (
              <Reveal className="model-card" delay={index * 90} key={label}><small>{label}</small><h3>{title}</h3><p>{text}</p></Reveal>
            ))}
          </div>
        </div>
        <Reveal className="frame engine-feature-row">{t.engineFeatures.map((feature) => <span key={feature}>{feature}</span>)}</Reveal>
      </section>

      <section className="architecture section" id="architecture">
        <div className="frame">
          <Reveal className="section-heading wide"><p className="eyebrow">{t.archKicker}</p><h2>{t.archTitle}</h2><p>{t.archText}</p></Reveal>
          <div className="layer-stack">
            {t.layers.map(([name, tech, text], index) => (
              <Reveal className="layer-row" delay={index * 80} key={name}><span className="layer-number">0{index + 1}</span><strong>{name}</strong><code>{tech}</code><p>{text}</p></Reveal>
            ))}
          </div>
        </div>
      </section>

      <section className="vision section">
        <div className="frame">
          <Reveal className="section-heading wide"><p className="eyebrow">{t.visionKicker}</p><h2>{t.visionTitle}</h2><p>{t.visionText}</p></Reveal>
          <div className="reusable-grid">
            {t.reusable.map(([title, text], index) => (
              <Reveal className="reusable-item" delay={index * 100} key={title}><span>0{index + 1}</span><h3>{title}</h3><p>{text}</p></Reveal>
            ))}
          </div>
        </div>
      </section>

      <section className="open-source section" id="open-source">
        <div className="frame open-layout">
          <Reveal>
            <p className="eyebrow">{t.openKicker}</p><h2>{t.openTitle}</h2><p className="open-copy">{t.openText}</p>
            <div className="button-row">
              <a className="button primary" href={`${t.docs}/getting-started`}>{t.readDocs}</a>
              <a className="button quiet" href={`${github}/blob/main/CONTRIBUTING${initialLocale === "en" ? ".en" : ""}.md`}>{t.contribute}</a>
              <a className="button quiet" href={`${github}/issues`}>{t.reportIssue}</a>
            </div>
            <div className="status-note"><strong>{t.statusLabel}</strong><p>{t.statusText}</p><div className="status-links"><a href={`${t.docs}/status`}>{t.statusLink} ↗</a><a href={`${t.docs}/deployment`}>{t.deploymentLink} ↗</a><a href={`${t.docs}/troubleshooting`}>{t.troubleshootingLink} ↗</a></div></div>
          </Reveal>
          <Reveal className="terminal" delay={140}><div className="terminal-bar"><i /><i /><i /><span>QUICKSTART</span></div><pre><code>{t.command}</code></pre></Reveal>
        </div>
      </section>

      <section className="final-cta section">
        <Reveal className="frame final-cta-inner">
          <p className="eyebrow">{t.ctaKicker}</p><h2>{t.ctaTitle}</h2><p>{t.ctaText}</p>
          <div className="button-row centered">
            <a className="button primary" href={`${t.docs}/pilot`}>{t.ctaPrimary} <span aria-hidden="true">→</span></a>
            <a className="button quiet" href={github}>{t.ctaSecondary} <span aria-hidden="true">↗</span></a>
          </div>
        </Reveal>
      </section>

      <footer className="site-footer">
        <div className="frame footer-inner">
          <Image src="/brand/ingot-lockup.svg" alt="Ingot" width={120} height={45} />
          <p>{t.footer}</p>
          <div><a href={t.docs}>{t.docsLabel}</a><a href={github}>GitHub</a><a href={`${github}/blob/main/LICENSE`}>Apache-2.0</a></div>
        </div>
      </footer>
    </main>
  );
}
