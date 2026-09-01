
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
    titleA: "从运行证据，",
    titleB: "到下一份配方。",
    lead: "开源工艺追因与优化系统。把设备、生产和检验数据关联成可信证据，让每次真实配方运行持续支持下一份配方。",
    primary: "五分钟体验",
    secondary: "了解工作方式",
    truth: ["证据可追溯", "原因可验证", "建议可审核", "结论可复用"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "一次运行的工程证据",
    panelCampaign: "RECIPE OPTIMIZATION · RUN-042",
    panelBadge: "下一配方建议待确认",
    parameters: [
      ["实际控制变量", "42.0", ""],
      ["阶段轨迹偏差", "+1.8", "σ"],
      ["工装版本", "TOOLING-A", ""],
    ],
    predictions: [
      ["关键差异", "保压阶段"],
      ["有效运行", "12 条"],
      ["下一份配方", "已生成"],
    ],
    panelFoot: "产品界面示意 · 同时呈现事实、差异、不确定性和可执行下一步",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "正常生产本身，就是持续优化的数据来源。",
    productText: "无需先建立实验，也无需工程师重新归类配方。系统自动关联已完成运行的实际参数、过程上下文和质量结果，在证据允许时生成独立的下一配方建议。",
    productCards: [
      ["01", "建立运行证据", "用同一个运行身份关联实际条件、阶段轨迹、材料、工装和质量结果。"],
      ["02", "形成优化观察", "已完成的真实配方运行通过质量和覆盖准入后，自动成为可复核的优化样本。"],
      ["03", "推荐下一份配方", "在目标、安全边界和历史覆盖范围内给出一份候选工艺设置、预测区间和选择理由。"],
      ["04", "继续从生产学习", "工程师在原有生产流程中确认；新运行回流后再生成下一份建议。"],
    ],
    shotsKicker: "REAL WORKBENCH",
    shotsTitle: "同一套工作台，覆盖从运行证据到下一份配方。",
    shotsText: "以下为合成演示数据的真实界面：工作台汇总运行与证据状态，追因总览进入差异比较，配方优化工作区把真实运行组织成下一份建议。",
    shots: [
      ["/screenshots/workbench.png", "工作台", "集中查看待办、生产状态、质量风险与研发进展"],
      ["/screenshots/diagnosis.png", "追因总览", "从可信证据进入差异比较与候选原因"],
      ["/screenshots/optimization.png", "配方优化", "让真实配方运行形成观察并推荐下一份配方"],
    ],
    shotsNote: "界面来自合成演示，仅验证软件流程，不证明真实工艺收益。",
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "系统提出建议，工程师掌握决策。",
    loopText: "Ingot 负责吸收真实运行、检查数据准入、计算并披露不确定性；工程师负责定义目标与安全边界、确认是否采用下一份配方。建议不自动下发，系统也不建立独立的受控验证工作流。",
    loopSteps: [
      ["01", "定义", "问题 · 变量 · 边界"],
      ["02", "接入", "协议 · 点位 · 单位"],
      ["03", "记录", "运行 · 轨迹 · 上下文"],
      ["04", "核验", "质量 · 来源 · 完整性"],
      ["05", "优化", "观察 · 建模 · 建议"],
      ["06", "确认", "采用 · 回流 · 沉淀"],
    ],
    optimizerKicker: "THE METHOD TOOLBOX",
    optimizerTitle: "先确认数据是否可靠，再选择分析与优化方法。",
    optimizerText: "系统先判断真实运行是否完整、可比并覆盖至少两种实际配方，再按样本数量、数据覆盖和安全约束选择响应面或受约束优化。因果确认和越界探索需要在既有生产与合规流程中收集额外真实运行。",
    methodA: "确认数据可用",
    methodAText: "核对数据是否完整、实际值与单位是否一致、时间和来源是否明确，并识别版本变化与漂移。",
    methodB: "工艺追因",
    methodBText: "使用匹配比较、稳健统计、阶段轨迹和上下文分层缩小候选范围。",
    methodC: "形成优化观察",
    methodCText: "自动关联实际配方、过程上下文和已复核质量结果，并明确排除原因与证据范围。",
    methodD: "推荐下一份配方",
    methodDText: "在目标、安全边界和已观察参数包络内提出一份候选工艺设置，同时说明预期、风险和选择理由。",
    engineFeatures: ["数据质量", "真实运行", "优化观察", "多目标权衡", "约束优化", "机理知识"],
    archKicker: "WORKS WITH YOUR EXISTING SYSTEMS",
    archTitle: "连接现有系统，不接管生产控制。",
    archText: "系统通过统一的运行身份关联现有系统中的实际配方、过程和质量数据，形成用于工艺追因与配方优化的工程证据。生产执行、实时控制和合规审批仍由原有系统及工程团队负责。",
    layers: [
      ["生产与设备", "MES · SCADA · Historian", "接收运行和过程事实，但不替代生产执行、监控或实时控制"],
      ["质量与研发", "LIMS · QMS · ELN", "关联检验、审核和研发记录，但不替代完整的质量或文档管理"],
      ["分析与优化", "DOE · 响应面 · 贝叶斯优化", "按问题和数据条件选择方法，不把一种算法套在所有场景上"],
      ["工程决策", "确认 · 执行 · 回流", "系统提出下一份配方；工程师设定边界并通过现有生产流程确认是否采用"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "优化能力持续升级，证据边界始终不变。",
    visionText: "设备、产品或工艺场景变更时，需要重新配置数据映射、变量、目标、约束和上下文；运行身份、证据原则、独立建议记录和工程师决策权保持稳定。",
    reusable: [
      ["长期不变", "真实数据支持工程判断，观察结论必须能够回到来源并接受验证"],
      ["按场景配置", "设备映射、变量、阶段、质量目标、安全约束、上下文和机理知识"],
      ["持续演进", "统计方法、代理模型、优化策略、页面布局和语言模型"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "开源覆盖完整配方优化闭环。",
    openText: "Ingot 采用 Apache-2.0 许可，可在厂内自托管。现场采集、运行证据、工艺追因、真实运行优化和知识沉淀位于同一仓库；公开验证协议与结果可以独立复现。",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "阅读快速开始",
    contribute: "参与贡献",
    reportIssue: "报告问题",
    statusLabel: "当前成熟度",
    statusText: "主要软件流程已经实现并有自动化测试；真实工厂收益验证尚未完成。系统可用于产品评估和受控试点，现有证据不能证明其已稳定减少实验或缩短研发周期。",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "从一个真实工艺问题开始。",
    ctaText: "接入一组真实配方运行，核对实际参数和质量结果，让系统形成优化观察并给出第一份可审核的下一份配方。",
    ctaPrimary: "建立第一个数据闭环",
    ctaSecondary: "打开 GitHub",
    footer: "Ingot · 从运行证据，到下一份配方。",
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
    titleA: "From run evidence",
    titleB: "to the next recipe.",
    lead: "An open-source process diagnosis and optimization system that turns linked equipment, production, and inspection data into trustworthy evidence for the next recipe.",
    primary: "Take the five-minute tour",
    secondary: "See how it works",
    truth: ["Traceable evidence", "Testable causes", "Reviewable recommendations", "Reusable conclusions"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "Evidence for one real run",
    panelCampaign: "RECIPE OPTIMIZATION · RUN-042",
    panelBadge: "Next-recipe recommendation awaiting confirmation",
    parameters: [
      ["Actual control", "42.0", ""],
      ["Stage deviation", "+1.8", "σ"],
      ["Tooling revision", "TOOLING-A", ""],
    ],
    predictions: [
      ["Key difference", "Holding stage"],
      ["Valid runs", "12"],
      ["Next recipe", "Generated"],
    ],
    panelFoot: "Product illustration · facts, differences, uncertainty, and an actionable next step",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "Normal production becomes the source of continuous optimization.",
    productText: "No experiment setup or manual recipe reclassification is required. The system links actual settings, process context, and quality outcomes from completed runs and creates an independent next-recipe recommendation when the evidence qualifies.",
    productCards: [
      ["01", "Build run evidence", "Link actual conditions, stage trajectories, material, tooling, and quality outcomes through one run identity."],
      ["02", "Form observations", "Completed real recipe runs become reviewable optimization samples after quality and coverage admission."],
      ["03", "Recommend the next recipe", "Return one candidate process setting with prediction intervals and rationale inside objectives, safety boundaries, and observed coverage."],
      ["04", "Keep learning from production", "Engineers confirm through the existing production flow; each new run feeds the next recommendation."],
    ],
    shotsKicker: "REAL WORKBENCH",
    shotsTitle: "One workbench, from run evidence to the next recipe.",
    shotsText: "Real screenshots from the synthetic demo: the workbench summarizes runs and evidence, diagnosis enters difference comparison, and the optimization workspace turns real runs into the next recommendation.",
    shots: [
      ["/screenshots/workbench.png", "Workbench", "Review to-dos, production status, quality risks, and research progress in one place"],
      ["/screenshots/diagnosis.png", "Diagnosis", "Enter difference comparison and candidate causes from trustworthy evidence"],
      ["/screenshots/optimization.png", "Recipe optimization", "Turn real recipe runs into observations and the next recommendation"],
    ],
    shotsNote: "Screenshots come from the synthetic demo; they validate the software workflow, not real process outcomes.",
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "The system proposes. Engineers decide.",
    loopText: "Ingot absorbs real runs, checks admission, computes, and exposes uncertainty. Engineers define objectives and safety boundaries and decide whether to adopt the next recipe. Recommendations are never dispatched automatically, and the system does not create a separate controlled-validation workflow.",
    loopSteps: [
      ["01", "Define", "question · variables · boundaries"],
      ["02", "Connect", "protocols · points · units"],
      ["03", "Record", "runs · trajectories · context"],
      ["04", "Qualify", "quality · provenance · completeness"],
      ["05", "Optimize", "observe · model · recommend"],
      ["06", "Confirm", "adopt · learn · preserve"],
    ],
    optimizerKicker: "THE METHOD TOOLBOX",
    optimizerTitle: "Confirm that the data are trustworthy before choosing an analysis or optimization method.",
    optimizerText: "The system first checks that real runs are complete, comparable, and cover at least two actual recipes, then selects response-surface or constrained optimization methods by sample size, coverage, and safety constraints. Causal confirmation and extrapolation require additional real runs through existing production and compliance processes.",
    methodA: "Confirm data usability",
    methodAText: "Check completeness, actual values, units, time, and provenance, and identify version changes or drift.",
    methodB: "Process diagnosis",
    methodBText: "Use matching, robust statistics, stage trajectories, and context stratification to narrow candidates.",
    methodC: "Form observations",
    methodCText: "Link actual recipes, process context, and reviewed quality outcomes automatically, with explicit exclusions and evidence scope.",
    methodD: "Recommend the next recipe",
    methodDText: "Propose one candidate process setting inside objectives, safety boundaries, and the observed parameter envelope, with expected outcomes, risks, and rationale.",
    engineFeatures: ["Data quality", "Real runs", "Optimization observations", "Multiple objectives", "Constrained optimization", "Process knowledge"],
    archKicker: "WORKS WITH YOUR EXISTING SYSTEMS",
    archTitle: "Connect existing systems without taking over production control.",
    archText: "A shared run identity links actual recipes, process data, and quality outcomes from existing systems into engineering evidence for process diagnosis and recipe optimization. Production execution, real-time control, and compliance approval remain with the systems and teams that own them.",
    layers: [
      ["PRODUCTION AND EQUIPMENT", "MES · SCADA · Historian", "Receive run and process facts without replacing execution, monitoring, or real-time control"],
      ["QUALITY AND R&D", "LIMS · QMS · ELN", "Link inspection, review, and research records without replacing full quality or document management"],
      ["ANALYSIS AND OPTIMIZATION", "DOE · response surfaces · Bayesian optimization", "Select methods by question and data instead of forcing one algorithm onto every scenario"],
      ["ENGINEERING DECISION", "Confirm · execute · learn", "The system proposes the next recipe; engineers set boundaries and decide whether to adopt it through the existing production flow"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "Optimization capabilities evolve. Evidence boundaries remain fixed.",
    visionText: "A change in machine, product, or process requires new mappings, variables, objectives, constraints, and context. Run identity, evidence principles, independent recommendation records, and engineering authority remain stable.",
    reusable: [
      ["Stays stable", "Real data supports engineering judgment, and every conclusion traces to sources and remains testable"],
      ["Configured per scenario", "Equipment mappings, variables, stages, quality objectives, safety constraints, context, and mechanism knowledge"],
      ["Continues evolving", "Statistics, surrogate models, optimization strategies, page layouts, and language models"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "Open source across the complete recipe-optimization loop.",
    openText: "Ingot is Apache-2.0 licensed and self-hostable inside the plant. Field acquisition, run evidence, process diagnosis, real-run optimization, and knowledge preservation live in one repository; public validation protocols and results are independently reproducible.",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "Read the quickstart",
    contribute: "Contribute",
    reportIssue: "Report an issue",
    statusLabel: "Current maturity",
    statusText: "The main software workflow is implemented and has automated tests; real-factory benefit validation remains incomplete. The system may be used for product evaluation and controlled pilots, but current evidence does not establish consistent reductions in experiments or development time.",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "Begin with one real process problem.",
    ctaText: "Connect a set of real recipe runs, qualify actual settings and outcomes, and let the system form observations and return the first reviewable next recipe.",
    ctaPrimary: "Build the first data loop",
    ctaSecondary: "Open GitHub",
    footer: "Ingot · From run evidence to the next recipe.",
  },
} as const;

const github = "https://github.com/liuweichaox/Ingot";

export default function IngotSite({ initialLocale }: { initialLocale: Locale }) {
  const t = copy[initialLocale];

  return (
    <main>
      <a className="skip-link" href="#main-content">跳转到主要内容</a>
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

      <section className="hero" id="main-content">
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

      <section className="screenshots section" id="screenshots">
        <div className="frame">
          <div className="section-heading wide"><p className="eyebrow">{t.shotsKicker}</p><h2>{t.shotsTitle}</h2><p>{t.shotsText}</p></div>
          <div className="shots-grid">
            {t.shots.map(([src, title, text]) => (
              <figure className="shot" key={title}>
                <Image src={src} alt={`${title} — Ingot`} loading="lazy" width={1600} height={1000} unoptimized />
                <figcaption><strong>{title}</strong><span>{text}</span></figcaption>
              </figure>
            ))}
          </div>
          <p className="shots-note">{t.shotsNote}</p>
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
            <div className="tech-line"><span>RUNS</span><span>STATISTICS</span><span>MODELS</span></div>
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
