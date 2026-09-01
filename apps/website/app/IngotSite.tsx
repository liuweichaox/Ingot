
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
    eyebrow: "PROCESS DIAGNOSIS · SPECIFICATION REVISION",
    titleA: "从真实运行，",
    titleB: "到下一版工艺规范。",
    lead: "开源工艺追因与优化系统。把设备、生产和检验数据关联成可信证据，支持工程师修订下一版工艺规范。",
    primary: "五分钟体验",
    secondary: "了解工作方式",
    truth: ["证据可追溯", "原因可验证", "建议可审核", "结论可复用"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "一次运行的工程证据",
    panelCampaign: "SPECIFICATION REVISION · RUN-042",
    panelBadge: "下一版草稿待确认",
    parameters: [
      ["实际控制变量", "42.0", ""],
      ["阶段轨迹偏差", "+1.8", "σ"],
      ["工装版本", "TOOLING-A", ""],
    ],
    predictions: [
      ["关键差异", "保压阶段"],
      ["有效运行", "12 条"],
      ["下一版规范", "待修订"],
    ],
    panelFoot: "产品界面示意 · 同时呈现事实、差异、不确定性和可执行下一步",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "正常生产本身，就是工艺规范持续修订的依据。",
    productText: "系统关联已完成运行的实际参数、过程上下文和质量结果。工程师从工艺追因回到已发布规范，带着证据、修订理由和机理依据创建下一版草稿。",
    productCards: [
      ["01", "建立运行证据", "用同一个运行身份关联实际条件、阶段轨迹、材料、工装和质量结果。"],
      ["02", "完成工艺追因", "以同类真实运行、质量结果和过程轨迹缩小候选原因，并明确证据范围。"],
      ["03", "修订下一版规范", "工程师继承已发布版本的参数与适用条件，记录修订理由、机理依据和引用证据。"],
      ["04", "继续从生产回流", "新版本经既有生产流程投入运行；后续运行和质量结果继续成为下一次修订依据。"],
    ],
    shotsKicker: "REAL WORKBENCH",
    shotsTitle: "同一套工作台，覆盖从真实运行到下一版工艺规范。",
    shotsText: "以下为合成演示数据的真实界面：工作台汇总生产与质量状态，追因总览识别待处理运行，工程师在工艺规范中带着实际运行依据创建下一版草稿。",
    shots: [
      ["/screenshots/production-run.png", "真实生产运行", "查看实际使用的工艺规范、过程数据、质量结果和生产上下文"],
      ["/screenshots/diagnosis.png", "工艺追因", "从待分析运行进入差异比较与候选原因"],
      ["/screenshots/next-recipe.png", "规范修订草稿", "以已发布规范为基准，保存修订理由、机理依据与实际运行引用"],
    ],
    shotsNote: "界面来自合成演示，仅验证软件流程，不证明真实工艺收益。",
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "系统组织证据，工程师修订规范。",
    loopText: "Ingot 负责关联真实运行、质量结果和追因证据；工程师负责判断原因、记录机理依据、确认参数变化并发布下一版工艺规范。系统不自动修改生产参数。",
    loopSteps: [
      ["01", "定义", "问题 · 变量 · 边界"],
      ["02", "接入", "协议 · 点位 · 单位"],
      ["03", "记录", "运行 · 轨迹 · 上下文"],
      ["04", "核验", "质量 · 来源 · 完整性"],
      ["05", "追因", "比较 · 候选原因 · 证据"],
      ["06", "修订", "草稿 · 发布 · 回流"],
    ],
    optimizerKicker: "THE ENGINEERING TOOLBOX",
    optimizerTitle: "先确认数据是否可靠，再形成可审计的工艺修订。",
    optimizerText: "系统先确认真实运行是否完整、可比，且质量结果已关联；再通过同类比较、过程轨迹和上下文分层缩小候选原因。工艺规范只在工程师确认理由、机理和边界后修订。",
    methodA: "确认数据可用",
    methodAText: "核对数据是否完整、实际值与单位是否一致、时间和来源是否明确，并识别版本变化与漂移。",
    methodB: "工艺追因",
    methodBText: "使用匹配比较、稳健统计、阶段轨迹和上下文分层缩小候选范围。",
    methodC: "固化机理依据",
    methodCText: "将参数作用、已知边界和工程判断附着到具体工艺规范版本，并引用对应运行和质量证据。",
    methodD: "修订下一版规范",
    methodDText: "继承完整参数与适用条件，只调整确认需要变化的控制参数，并形成可追溯草稿。",
    engineFeatures: ["数据质量", "真实运行", "版本谱系", "证据引用", "机理依据", "工程决策"],
    archKicker: "WORKS WITH YOUR EXISTING SYSTEMS",
    archTitle: "连接现有系统，不接管生产控制。",
    archText: "系统通过统一的运行身份关联现有系统中的实际工艺规范、过程和质量数据，形成用于工艺追因与规范修订的工程证据。生产执行、实时控制和合规审批仍由原有系统及工程团队负责。",
    layers: [
      ["生产与设备", "MES · SCADA · Historian", "接收运行和过程事实，但不替代生产执行、监控或实时控制"],
      ["质量与研发", "LIMS · QMS · ELN", "关联检验、审核和研发记录，但不替代完整的质量或文档管理"],
      ["追因与修订", "对比 · 证据 · 版本", "用同类比较缩小候选原因，再把依据和机理写入下一版规范"],
      ["工程决策", "修订 · 发布 · 回流", "工程师确认边界和参数变化，再通过既有生产流程发布与回流"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "工艺能力持续升级，证据边界始终不变。",
    visionText: "设备、产品或工艺场景变更时，需要重新配置数据映射、变量、边界和上下文；运行身份、证据原则、规范版本谱系和工程师决策权保持稳定。",
    reusable: [
      ["长期不变", "真实数据支持工程判断，观察结论必须能够回到来源并接受验证"],
      ["按场景配置", "设备映射、变量、阶段、质量边界、上下文和机理依据"],
      ["持续演进", "统计方法、追因策略、页面布局和语言模型"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "开源覆盖完整工艺规范修订闭环。",
    openText: "Ingot 采用 Apache-2.0 许可，可在厂内自托管。现场采集、运行证据、工艺追因、规范版本和机理依据位于同一仓库；公开验证协议与结果可以独立复现。",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "阅读快速开始",
    contribute: "参与贡献",
    reportIssue: "报告问题",
    statusLabel: "当前成熟度",
    statusText: "主要软件流程已经实现并有自动化测试；真实工厂收益验证尚未完成。系统可用于产品评估和受控试点，现有证据不能证明其已稳定提升工艺指标或缩短工程决策周期。",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "从一个真实工艺问题开始。",
    ctaText: "接入一组真实生产运行，核对实际参数和质量结果，从追因证据开始创建第一份可审核的下一版工艺规范。",
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
    eyebrow: "PROCESS DIAGNOSIS · SPECIFICATION REVISION",
    titleA: "From real runs",
    titleB: "to the next process specification.",
    lead: "An open-source process diagnosis and optimization system that turns linked equipment, production, and inspection data into trustworthy evidence for engineers revising the next process specification.",
    primary: "Take the five-minute tour",
    secondary: "See how it works",
    truth: ["Traceable evidence", "Testable causes", "Reviewable recommendations", "Reusable conclusions"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "Evidence for one real run",
    panelCampaign: "SPECIFICATION REVISION · RUN-042",
    panelBadge: "Next-version draft awaiting confirmation",
    parameters: [
      ["Actual control", "42.0", ""],
      ["Stage deviation", "+1.8", "σ"],
      ["Tooling revision", "TOOLING-A", ""],
    ],
    predictions: [
      ["Key difference", "Holding stage"],
      ["Valid runs", "12"],
      ["Next specification", "Ready to revise"],
    ],
    panelFoot: "Product illustration · facts, differences, uncertainty, and an actionable next step",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "Normal production becomes the evidence for continuous specification revision.",
    productText: "The system links actual settings, process context, and quality outcomes from completed runs. Engineers return from diagnosis to a published specification and create the next draft with evidence, rationale, and mechanism notes.",
    productCards: [
      ["01", "Build run evidence", "Link actual conditions, stage trajectories, material, tooling, and quality outcomes through one run identity."],
      ["02", "Complete process diagnosis", "Use comparable real runs, quality outcomes, and process traces to narrow candidate causes and disclose evidence scope."],
      ["03", "Revise the next specification", "Engineers inherit parameters and applicability, then record a rationale, mechanism notes, and cited evidence."],
      ["04", "Return through production", "The new version is used through the existing production flow; later runs and quality outcomes become the next revision's evidence."],
    ],
    shotsKicker: "REAL WORKBENCH",
    shotsTitle: "One workbench, from real runs to the next process specification.",
    shotsText: "Real screenshots from the synthetic demo: the workbench summarizes production and quality, diagnosis identifies runs that need attention, and engineers create the next draft from a process specification with actual-run evidence.",
    shots: [
      ["/screenshots/production-run.png", "Real production run", "Review the applied specification, process data, quality outcome, and production context"],
      ["/screenshots/diagnosis.png", "Process diagnosis", "Start from runs that need attention and narrow candidate causes"],
      ["/screenshots/next-recipe.png", "Revision draft", "Use a published specification as the baseline and save rationale, mechanism notes, and real-run evidence"],
    ],
    shotsNote: "Screenshots come from the synthetic demo; they validate the software workflow, not real process outcomes.",
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "The system organizes evidence. Engineers revise specifications.",
    loopText: "Ingot links real runs, quality outcomes, and diagnostic evidence. Engineers judge causes, record mechanism notes, confirm parameter changes, and publish the next process specification. The system never changes production parameters automatically.",
    loopSteps: [
      ["01", "Define", "question · variables · boundaries"],
      ["02", "Connect", "protocols · points · units"],
      ["03", "Record", "runs · trajectories · context"],
      ["04", "Qualify", "quality · provenance · completeness"],
      ["05", "Diagnose", "compare · candidates · evidence"],
      ["06", "Revise", "draft · publish · return"],
    ],
    optimizerKicker: "THE ENGINEERING TOOLBOX",
    optimizerTitle: "Confirm that data are trustworthy before forming an auditable revision.",
    optimizerText: "The system first confirms that real runs are complete, comparable, and linked to quality outcomes; then matching, process traces, and context stratification narrow candidate causes. A specification is revised only after an engineer confirms the rationale, mechanism, and boundaries.",
    methodA: "Confirm data usability",
    methodAText: "Check completeness, actual values, units, time, and provenance, and identify version changes or drift.",
    methodB: "Process diagnosis",
    methodBText: "Use matching, robust statistics, stage trajectories, and context stratification to narrow candidates.",
    methodC: "Preserve mechanism notes",
    methodCText: "Attach parameter effects, known boundaries, and engineering judgment to a specific specification version with run and quality references.",
    methodD: "Revise the next specification",
    methodDText: "Inherit complete parameters and applicability, change only confirmed control values, and create a traceable draft.",
    engineFeatures: ["Data quality", "Real runs", "Version lineage", "Evidence references", "Mechanism notes", "Engineering decisions"],
    archKicker: "WORKS WITH YOUR EXISTING SYSTEMS",
    archTitle: "Connect existing systems without taking over production control.",
    archText: "A shared run identity links actual process specifications, process data, and quality outcomes from existing systems into engineering evidence for process diagnosis and specification revision. Production execution, real-time control, and compliance approval remain with the systems and teams that own them.",
    layers: [
      ["PRODUCTION AND EQUIPMENT", "MES · SCADA · Historian", "Receive run and process facts without replacing execution, monitoring, or real-time control"],
      ["QUALITY AND R&D", "LIMS · QMS · ELN", "Link inspection, review, and research records without replacing full quality or document management"],
      ["DIAGNOSIS AND REVISION", "comparison · evidence · versions", "Narrow candidate causes through comparable runs, then write evidence and mechanism notes into the next specification"],
      ["ENGINEERING DECISION", "revise · publish · return", "Engineers confirm boundaries and parameter changes, then publish and return through the existing production flow"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "Process capabilities evolve. Evidence boundaries remain fixed.",
    visionText: "A change in machine, product, or process requires new mappings, variables, boundaries, and context. Run identity, evidence principles, specification lineage, and engineering authority remain stable.",
    reusable: [
      ["Stays stable", "Real data supports engineering judgment, and every conclusion traces to sources and remains testable"],
      ["Configured per scenario", "Equipment mappings, variables, stages, quality boundaries, context, and mechanism notes"],
      ["Continues evolving", "Statistics, diagnostic strategies, page layouts, and language models"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "Open source across the complete process-specification revision loop.",
    openText: "Ingot is Apache-2.0 licensed and self-hostable inside the plant. Field acquisition, run evidence, process diagnosis, specification versions, and mechanism notes live in one repository; public validation protocols and results are independently reproducible.",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "Read the quickstart",
    contribute: "Contribute",
    reportIssue: "Report an issue",
    statusLabel: "Current maturity",
    statusText: "The main software workflow is implemented and has automated tests; real-factory benefit validation remains incomplete. The system may be used for product evaluation and controlled pilots, but current evidence does not establish consistent process improvements or shorter engineering decision cycles.",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "Begin with one real process problem.",
    ctaText: "Connect a set of real production runs, qualify actual settings and outcomes, and start from diagnostic evidence to create the first reviewable next process specification.",
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
            {t.shots.map(([src, title, text], index) => (
              <figure className={`shot${index === 2 ? " featured" : ""}`} key={title}>
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
