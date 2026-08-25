
"use client";

// Renders the bilingual public product narrative; validation detail stays in linked evidence documents.

import { Disclosure, DisclosureButton, DisclosurePanel } from "@headlessui/react";
import Image from "next/image";

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
    eyebrow: "PROCESS DIAGNOSIS · CONSTRAINED OPTIMIZATION",
    titleA: "看清这次运行，",
    titleB: "做对下一项实验。",
    lead: "开源工艺追因与优化系统。关联设备、生产和检验数据，支持运行证据关联、候选原因验证和安全边界内的下一步实验决策。",
    primary: "五分钟体验",
    secondary: "了解工作方式",
    truth: ["证据可追溯", "原因可验证", "建议可审核", "结论可复用"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "一次运行的工程证据",
    panelCampaign: "PROCESS R&D · RUN-042",
    panelBadge: "下一步实验建议待审批",
    parameters: [
      ["实际控制变量", "42.0", ""],
      ["阶段轨迹偏差", "+1.8", "σ"],
      ["工装版本", "TOOLING-A", ""],
    ],
    predictions: [
      ["关键差异", "保压阶段"],
      ["适用方法", "二次响应面"],
      ["下一步建议", "区组验证"],
    ],
    panelFoot: "产品界面示意 · 同时呈现事实、差异、不确定性和可执行下一步",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "形成从真实运行到已验证工艺条件的证据闭环。",
    productText: "现场数据、工程判断和实验结果统一进入可复核的工艺研发闭环。每项下一步实验建议均以可信证据、明确目标和安全边界为前提。",
    productCards: [
      ["01", "建立运行证据", "用同一个运行身份关联实际条件、阶段轨迹、材料、工装和质量结果。"],
      ["02", "开展工艺追因", "比较满足可比条件的运行，呈现关键差异、候选原因、反证和证据缺口。"],
      ["03", "设计验证实验", "把候选原因转成有对照、重复、区组、停止规则和安全边界的实验。"],
      ["04", "优化下一项实验", "按当前证据选择 DOE、响应面或受约束序贯优化，输出可审核的候选工艺设置。"],
    ],
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "系统提出建议，工程师掌握决策。",
    loopText: "Ingot 负责整理事实、执行计算、披露不确定性并提出下一步实验建议；工程师负责定义目标与安全边界、审核候选工艺设置、批准实验并确认结论的适用范围。",
    loopSteps: [
      ["01", "定义", "问题 · 变量 · 边界"],
      ["02", "接入", "协议 · 点位 · 单位"],
      ["03", "记录", "运行 · 轨迹 · 上下文"],
      ["04", "核验", "质量 · 来源 · 完整性"],
      ["05", "判断", "比较 · 候选 · 反证"],
      ["06", "实验", "验证 · 优化 · 沉淀"],
    ],
    optimizerKicker: "THE METHOD TOOLBOX",
    optimizerTitle: "先确认数据是否可靠，再选择分析与优化方法。",
    optimizerText: "系统先判断不同运行能否合理比较，再根据样本数量、数据覆盖和安全约束选择实验设计（DOE）、响应面或受约束贝叶斯优化。每项建议均说明采用的方法、依据和不确定性。",
    methodA: "确认数据可用",
    methodAText: "核对数据是否完整、实际值与单位是否一致、时间和来源是否明确，并识别版本变化与漂移。",
    methodB: "工艺追因",
    methodBText: "使用匹配比较、稳健统计、阶段轨迹和上下文分层缩小候选范围。",
    methodC: "实验设计",
    methodCText: "通过对照、重复、区组、随机化和干预判断候选是否成立。",
    methodD: "选择下一项实验",
    methodDText: "在目标、允许变量和安全边界内提出候选工艺设置，同时说明预期、风险和选择理由。",
    engineFeatures: ["数据质量", "可比运行", "实验设计", "多目标权衡", "约束优化", "机理知识"],
    archKicker: "WORKS WITH YOUR EXISTING SYSTEMS",
    archTitle: "连接现有系统，不接管生产控制。",
    archText: "系统通过统一的运行身份关联现有系统中的运行、过程和质量数据，形成用于工艺追因与实验决策的工程证据。生产执行、实时控制和合规审批仍由原有系统及工程团队负责。",
    layers: [
      ["生产与设备", "MES · SCADA · Historian", "接收运行和过程事实，但不替代生产执行、监控或实时控制"],
      ["质量与研发", "LIMS · QMS · ELN", "关联检验、审核和研发记录，但不替代完整的质量或文档管理"],
      ["分析与优化", "DOE · 响应面 · 贝叶斯优化", "按问题和数据条件选择方法，不把一种算法套在所有场景上"],
      ["工程决策", "审核 · 执行 · 验证", "系统提出建议；工程师设定边界、批准实验并确认结论能否用于生产"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "优化能力持续升级，证据边界始终不变。",
    visionText: "设备、产品或工艺场景变更时，需要重新配置数据映射、变量、目标、约束和上下文；运行身份、证据原则、实验状态和工程师决策权保持稳定。",
    reusable: [
      ["长期不变", "真实数据支持工程判断，观察结论必须能够回到来源并接受验证"],
      ["按场景配置", "设备映射、变量、阶段、质量目标、安全约束、上下文和机理知识"],
      ["持续演进", "统计方法、代理模型、实验策略、页面布局和语言模型"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "开源覆盖完整工艺研发闭环。",
    openText: "Ingot 采用 Apache-2.0 许可，可在厂内自托管。现场采集、运行证据、工艺追因、实验设计、受约束优化和知识沉淀位于同一仓库；公开验证协议与结果可以独立复现。",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "阅读快速开始",
    contribute: "参与贡献",
    reportIssue: "报告问题",
    statusLabel: "当前成熟度",
    statusText: "主要软件流程已经实现并有自动化测试；真实工厂收益验证尚未完成。系统可用于产品评估和受控试点，现有证据不能证明其已稳定减少实验或缩短研发周期。",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "从一个真实工艺问题开始。",
    ctaText: "导入一组可比较的真实运行，核对证据、缩小候选原因，并形成第一项可审核的验证实验。",
    ctaPrimary: "建立第一个数据闭环",
    ctaSecondary: "打开 GitHub",
    footer: "Ingot · 看清这次运行，做对下一项实验。",
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
    eyebrow: "PROCESS DIAGNOSIS · CONSTRAINED OPTIMIZATION",
    titleA: "Understand this run.",
    titleB: "Choose the right next experiment.",
    lead: "An open-source process diagnosis and optimization system that links equipment, production, and inspection data for traceable run evidence, cause validation, and next-experiment decisions within safety boundaries.",
    primary: "Take the five-minute tour",
    secondary: "See how it works",
    truth: ["Traceable evidence", "Testable causes", "Reviewable recommendations", "Reusable conclusions"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "Evidence for one real run",
    panelCampaign: "PROCESS R&D · RUN-042",
    panelBadge: "Next-experiment recommendation awaiting approval",
    parameters: [
      ["Actual control", "42.0", ""],
      ["Stage deviation", "+1.8", "σ"],
      ["Tooling revision", "TOOLING-A", ""],
    ],
    predictions: [
      ["Key difference", "Holding stage"],
      ["Applicable method", "Quadratic surface"],
      ["Next recommendation", "Blocked-design validation"],
    ],
    panelFoot: "Product illustration · facts, differences, uncertainty, and an actionable next step",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "An evidence loop from real runs to validated process conditions.",
    productText: "Field data, engineering judgment, and experiment outcomes enter one reviewable Process R&D loop. Every next-experiment recommendation depends on trustworthy evidence, explicit objectives, and safety boundaries.",
    productCards: [
      ["01", "Build run evidence", "Link actual conditions, stage trajectories, material, tooling, and quality outcomes through one run identity."],
      ["02", "Diagnose the process", "Compare eligible runs and expose key differences, candidate causes, counterevidence, and evidence gaps."],
      ["03", "Design validation", "Turn candidate causes into experiments with controls, repetitions, blocks, stop rules, and safety boundaries."],
      ["04", "Optimize the next experiment", "Select DOE, response surfaces, or constrained sequential optimization from current evidence and return reviewable candidate process settings."],
    ],
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "The system proposes. Engineers decide.",
    loopText: "Ingot organizes facts, computes, exposes uncertainty, and proposes the next experiment. Engineers define objectives and safety boundaries, review candidate process settings, approve execution, and confirm where conclusions apply.",
    loopSteps: [
      ["01", "Define", "question · variables · boundaries"],
      ["02", "Connect", "protocols · points · units"],
      ["03", "Record", "runs · trajectories · context"],
      ["04", "Qualify", "quality · provenance · completeness"],
      ["05", "Judge", "comparison · candidates · counterevidence"],
      ["06", "Experiment", "validate · optimize · preserve"],
    ],
    optimizerKicker: "THE METHOD TOOLBOX",
    optimizerTitle: "Confirm that the data are trustworthy before choosing an analysis or optimization method.",
    optimizerText: "The system first determines whether runs can be compared fairly, then selects design of experiments (DOE), response surfaces, or constrained Bayesian optimization according to sample size, data coverage, and safety constraints. Every recommendation explains the method, rationale, and uncertainty.",
    methodA: "Confirm data usability",
    methodAText: "Check completeness, actual values, units, time, and provenance, and identify version changes or drift.",
    methodB: "Process diagnosis",
    methodBText: "Use matching, robust statistics, stage trajectories, and context stratification to narrow candidates.",
    methodC: "Experiment design",
    methodCText: "Use controls, repetitions, blocks, randomization, and interventions to test whether a candidate survives.",
    methodD: "Choose the next experiment",
    methodDText: "Propose candidate process settings within objectives, allowed variables, and safety boundaries, with expected outcomes, risks, and rationale.",
    engineFeatures: ["Data quality", "Comparable runs", "Experiment design", "Multiple objectives", "Constrained optimization", "Process knowledge"],
    archKicker: "WORKS WITH YOUR EXISTING SYSTEMS",
    archTitle: "Connect existing systems without taking over production control.",
    archText: "A shared run identity links run, process, and quality data from existing systems into engineering evidence for process diagnosis and experiment decisions. Production execution, real-time control, and compliance approval remain with the systems and teams that own them.",
    layers: [
      ["PRODUCTION AND EQUIPMENT", "MES · SCADA · Historian", "Receive run and process facts without replacing execution, monitoring, or real-time control"],
      ["QUALITY AND R&D", "LIMS · QMS · ELN", "Link inspection, review, and research records without replacing full quality or document management"],
      ["ANALYSIS AND OPTIMIZATION", "DOE · response surfaces · Bayesian optimization", "Select methods by question and data instead of forcing one algorithm onto every scenario"],
      ["ENGINEERING DECISION", "Review · execute · validate", "The system proposes; engineers set boundaries, approve experiments, and decide whether conclusions may enter production"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "Optimization capabilities evolve. Evidence boundaries remain fixed.",
    visionText: "A change in machine, product, or process requires new mappings, variables, objectives, constraints, and context. Run identity, evidence principles, experiment state, and engineering authority remain stable.",
    reusable: [
      ["Stays stable", "Real data supports engineering judgment, and every conclusion traces to sources and remains testable"],
      ["Configured per scenario", "Equipment mappings, variables, stages, quality objectives, safety constraints, context, and mechanism knowledge"],
      ["Continues evolving", "Statistics, surrogate models, experiment strategies, page layouts, and language models"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "Open source across the complete Process R&D loop.",
    openText: "Ingot is Apache-2.0 licensed and self-hostable inside the plant. Field acquisition, run evidence, process diagnosis, experiment design, constrained optimization, and knowledge preservation live in one repository; public validation protocols and results are independently reproducible.",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "Read the quickstart",
    contribute: "Contribute",
    reportIssue: "Report an issue",
    statusLabel: "Current maturity",
    statusText: "The main software workflow is implemented and has automated tests; real-factory benefit validation remains incomplete. The system may be used for product evaluation and controlled pilots, but current evidence does not establish consistent reductions in experiments or development time.",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "Begin with one real process problem.",
    ctaText: "Import a set of comparable real runs, qualify the evidence, narrow candidate causes, and form the first reviewable validation experiment.",
    ctaPrimary: "Build the first data loop",
    ctaSecondary: "Open GitHub",
    footer: "Ingot · Understand this run. Choose the right next experiment.",
  },
} as const;

const github = "https://github.com/liuweichaox/Ingot";

export default function IngotSite({ initialLocale }: { initialLocale: Locale }) {
  const t = copy[initialLocale];

  return (
    <main>
      <header className="site-header">
        <div className="frame header-inner">
          <a className="brand" href={initialLocale === "zh" ? "/" : "/en/"} aria-label="Ingot home">
            <Image src="/brand/ingot-lockup-dark.svg" alt="Ingot" width={142} height={36} priority />
          </a>
          <nav className="desktop-nav" aria-label="Primary navigation">
            {t.nav.map(([label, href]) => <a key={href} href={href}>{label}</a>)}
          </nav>
          <div className="header-actions">
            <a className="lang" href={t.switchHref}>{t.switchLabel}</a>
            <a className="header-github" href={github}>{t.github} <span>↗</span></a>
          </div>
          <Disclosure>
            <DisclosureButton className="menu-button" aria-label="Toggle navigation">MENU</DisclosureButton>
            <DisclosurePanel className="mobile-nav">
              {t.nav.map(([label, href]) => <a key={href} href={href}>{label}</a>)}
              <a href={t.switchHref}>{t.switchLabel}</a>
              <a href={github}>{t.github}</a>
            </DisclosurePanel>
          </Disclosure>
        </div>
      </header>

      <section className="hero">
        <div className="hero-grid" aria-hidden="true" />
        <div className="frame hero-layout">
          <div className="hero-copy">
            <p className="eyebrow">{t.eyebrow}</p>
            <h1>{t.titleA}<span>{t.titleB}</span></h1>
            <p className="hero-lead">{t.lead}</p>
            <div className="button-row">
              <a className="button primary" href={`${t.docs}/getting-started`}>{t.primary} <span>→</span></a>
              <a className="button secondary" href="#product">{t.secondary} <span>↓</span></a>
            </div>
            <div className="truth-row">
              {t.truth.map((item) => <span key={item}><i />{item}</span>)}
            </div>
          </div>
          <div className="loop-panel">
            <div className="panel-top"><span>{t.panelKicker}</span><strong>{t.panelBadge}</strong></div>
            <h2>{t.panelTitle}</h2>
            <p className="panel-campaign">{t.panelCampaign}</p>
            <div className="parameter-grid">
              {t.parameters.map(([name, value, unit]) => (
                <div className="parameter" key={name}><span>{name}</span><strong>{value}<small>{unit}</small></strong></div>
              ))}
            </div>
            <div className="prediction-list">
              {t.predictions.map(([name, value]) => <div key={name}><span>{name}</span><strong>{value}</strong></div>)}
            </div>
            <p className="panel-foot">{t.panelFoot}</p>
          </div>
        </div>
      </section>

      <section className="product section" id="product">
        <div className="frame">
          <div className="section-heading"><p className="eyebrow">{t.productKicker}</p><h2>{t.productTitle}</h2><p>{t.productText}</p></div>
          <div className="product-grid">
            {t.productCards.map(([number, title, text]) => <article key={title}><span>{number}</span><h3>{title}</h3><p>{text}</p></article>)}
          </div>
        </div>
      </section>

      <section className="closed-loop section" id="loop">
        <div className="frame">
          <div className="section-heading wide"><p className="eyebrow">{t.loopKicker}</p><h2>{t.loopTitle}</h2><p>{t.loopText}</p></div>
          <div className="loop-rail">
            {t.loopSteps.map(([number, title, text]) => <article key={number}><span>{number}</span><h3>{title}</h3><p>{text}</p></article>)}
          </div>
        </div>
      </section>

      <section className="optimizer section" id="optimizer">
        <div className="frame optimizer-layout">
          <div className="optimizer-copy">
            <p className="eyebrow">{t.optimizerKicker}</p><h2>{t.optimizerTitle}</h2><p>{t.optimizerText}</p>
            <div className="tech-line"><span>STATISTICS</span><span>EXPERIMENTS</span><span>MODELS</span></div>
          </div>
          <div className="model-map">
            <article className="model-card gold"><small>DATA</small><h3>{t.methodA}</h3><p>{t.methodAText}</p></article>
            <article className="model-card cyan"><small>COMPARE</small><h3>{t.methodB}</h3><p>{t.methodBText}</p></article>
            <article className="model-card"><small>TEST</small><h3>{t.methodC}</h3><p>{t.methodCText}</p></article>
            <article className="model-card"><small>OPTIMIZE</small><h3>{t.methodD}</h3><p>{t.methodDText}</p></article>
          </div>
        </div>
        <div className="frame engine-feature-row">{t.engineFeatures.map((feature) => <span key={feature}>{feature}</span>)}</div>
      </section>

      <section className="architecture section" id="architecture">
        <div className="frame">
          <div className="section-heading wide"><p className="eyebrow">{t.archKicker}</p><h2>{t.archTitle}</h2><p>{t.archText}</p></div>
          <div className="layer-stack">
            {t.layers.map(([name, tech, text], index) => <article key={name}><span className="layer-number">0{index + 1}</span><strong>{name}</strong><code>{tech}</code><p>{text}</p></article>)}
          </div>
        </div>
      </section>

      <section className="vision section">
        <div className="frame">
          <div className="section-heading wide"><p className="eyebrow">{t.visionKicker}</p><h2>{t.visionTitle}</h2><p>{t.visionText}</p></div>
          <div className="reusable-grid">
            {t.reusable.map(([title, text], index) => <article key={title}><span>0{index + 1}</span><h3>{title}</h3><p>{text}</p></article>)}
          </div>
        </div>
      </section>

      <section className="open-source section" id="open-source">
        <div className="frame open-layout">
          <div>
            <p className="eyebrow">{t.openKicker}</p><h2>{t.openTitle}</h2><p className="open-copy">{t.openText}</p>
            <div className="button-row">
              <a className="button primary" href={`${t.docs}/getting-started`}>{t.readDocs}</a>
              <a className="button secondary" href={`${github}/blob/main/CONTRIBUTING${initialLocale === "en" ? ".en" : ""}.md`}>{t.contribute}</a>
              <a className="button secondary" href={`${github}/issues`}>{t.reportIssue}</a>
            </div>
            <div className="status-note"><strong>{t.statusLabel}</strong><p>{t.statusText}</p></div>
          </div>
          <div className="terminal"><div className="terminal-bar"><i /><i /><i /><span>QUICKSTART</span></div><pre><code>{t.command}</code></pre></div>
        </div>
      </section>

      <section className="final-cta section">
        <div className="frame">
          <p className="eyebrow">{t.ctaKicker}</p><h2>{t.ctaTitle}</h2><p>{t.ctaText}</p>
          <div className="button-row centered">
            <a className="button primary" href={`${t.docs}/getting-started`}>{t.ctaPrimary} <span>→</span></a>
            <a className="button secondary" href={github}>{t.ctaSecondary} <span>↗</span></a>
          </div>
        </div>
      </section>

      <footer>
        <div className="frame footer-inner">
          <Image src="/brand/ingot-lockup-dark.svg" alt="Ingot" width={120} height={30} />
          <p>{t.footer}</p>
          <div><a href={t.docs}>Docs</a><a href={github}>GitHub</a><a href={`${github}/blob/main/LICENSE`}>Apache-2.0</a></div>
        </div>
      </footer>
    </main>
  );
}
