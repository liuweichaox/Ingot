
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
    lead: "开源工艺研发与优化系统。组织研发项目、实验记录与运行证据，支持质量分析、工艺追因和配方优化。",
    primary: "阅读快速开始",
    secondary: "了解工作方式",
    truth: ["证据可追溯", "原因可验证", "建议可审核", "结论可复用"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "一次运行的工程证据",
    panelCampaign: "RECIPE RECOMMENDATION · RUN-042",
    panelBadge: "配方建议待确认",
    parameters: [
      ["实际控制变量", "42.0", ""],
      ["阶段轨迹偏差", "+1.8σ", ""],
      ["工装版本", "TOOLING-A", ""],
    ],
    predictions: [
      ["关键差异", "阶段 2"],
      ["有效运行", "12 条"],
      ["下一份配方", "待审核"],
    ],
    panelFoot: "产品界面示意 · 同时呈现事实、差异、不确定性和后续建议",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "把研发记录、工艺分析和配方决策放在同一工作流中。",
    productText: "围绕项目组织目标、实验记录和工程判断；直接关联已完成运行的实际参数、过程上下文和质量结果，无需工程师重新归类配方。比较差异、检查证据，再审核建议与配方修订。",
    productCards: [
      ["01", "组织研发记录", "定义项目目标、变量和范围，保存实验实测值与来源说明。"],
      ["02", "核对运行与质量", "关联实际参数、过程轨迹与检验结果，检查完整性和适用条件。"],
      ["03", "分析工艺差异", "比较可比运行，整理关键差异、候选原因与证据缺口。"],
      ["04", "审核配方建议", "基于合格运行证据提出候选设置，记录工程师的决定与修订理由。"],
    ],
    shotsKicker: "REAL WORKBENCH",
    shotsTitle: "查看运行、比较差异、审核建议与配方版本。",
    shotsText: "每一步都能查看关联事实、分析依据和工程师决定，便于核对建议形成的过程。",
    viewImage: "查看大图",
    shots: [
      ["/screenshots/production-run.png", "真实生产运行", "查看实际使用的配方版本、过程数据、质量结果和生产上下文"],
      ["/screenshots/diagnosis.png", "工艺追因", "从待分析运行进入差异比较与候选原因"],
      ["/screenshots/optimization.png", "配方优化", "按已确认的证据范围整理候选参数与下一步验证"],
      ["/screenshots/next-recipe.png", "配方建议与配方版本草稿", "审核建议后，以已发布配方版本为基准保存修订理由与实际运行引用"],
    ],
    shotsNote: "界面与数据用于产品说明，不证明真实工艺收益。配方建议页面为设计示意，当前 Web 支持范围见状态文档。",
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "系统组织事实与建议，工程师掌握研发决策。",
    loopText: "项目保存目标与记录，分析说明差异和证据边界，建议保留输入与理由。工程师审核是否采用、如何修订，并根据后续结果继续判断。",
    loopSteps: [
      ["01", "定义", "问题 · 变量 · 边界"],
      ["02", "记录", "参数 · 轨迹 · 结果"],
      ["03", "核验", "质量 · 来源 · 完整性"],
      ["04", "追因", "比较 · 候选原因 · 证据"],
      ["05", "建议", "目标 · 约束 · 不确定性"],
      ["06", "复核", "决定 · 修订 · 后续结果"],
    ],
    optimizerKicker: "THE ENGINEERING TOOLBOX",
    optimizerTitle: "先确认数据是否可靠，再形成可审计的工艺修订。",
    optimizerText: "推荐器先确认生产运行是否完整、可比且关联质量结果，再在声明的变量、安全边界和历史覆盖内形成候选建议。工程师负责审核建议；系统不自动修改生产参数。",
    methodA: "确认数据可用",
    methodAText: "核对数据是否完整、实际值与单位是否一致、时间和来源是否明确，并识别版本变化与漂移。",
    methodB: "工艺追因",
    methodBText: "使用匹配比较、稳健统计、阶段轨迹和上下文分层缩小候选范围。",
    methodC: "固化机理依据",
    methodCText: "将参数作用、已知边界和工程判断附着到具体配方版本，并引用对应运行、质量证据和已复核工艺资料片段。",
    methodD: "修订下一配方版本",
    methodDText: "继承完整参数与适用条件，只调整确认需要变化的控制参数，并形成可追溯草稿。",
    engineFeatures: ["数据质量", "真实运行", "版本谱系", "片段级引用", "已复核知识", "工程决策"],
    archKicker: "RECORDS · ANALYSIS · DECISIONS",
    archTitle: "研发记录、分析与决策，各有明确职责。",
    archText: "Web、API、数据库与优化服务构成自身运行栈，连接器是可选的数据来源。正式记录保存来源、版本与审核状态，分析和建议引用这些事实；工程师确认参数变化与适用范围。",
    layers: [
      ["研发记录", "项目 · 变量 · 实验", "组织目标、实际参数、实测结果和数据来源"],
      ["运行与质量", "参数 · 轨迹 · 检验", "关联运行上下文与质量结果，核对版本、完整性和审核状态"],
      ["分析与建议", "比较 · 追因 · 优化", "整理候选原因，形成受证据和安全边界约束的配方建议"],
      ["工程决策", "审核 · 版本 · 知识", "保存采用、修改或拒绝的理由，并引用经过复核的工艺资料"],
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
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "阅读快速开始",
    contribute: "参与贡献",
    reportIssue: "报告问题",
    statusLabel: "当前成熟度",
    statusText: "每次工艺运行就是一次实验，配方版本保存参数设定，运行记录关联实际参数、过程数据和质量结果；符合准入条件的运行证据用于配方建议。主要软件流程已有自动化测试，真实工厂收益验证尚未完成。详细能力与限制见当前状态文档。",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "从一个真实工艺问题开始。",
    ctaText: "创建研发项目记录变量与结果，或从已有运行开始核对质量、比较差异并审核配方建议。",
    ctaPrimary: "建立第一个数据闭环",
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
    lead: "Open-source Process R&D and Optimization System. Organize R&D projects, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.",
    primary: "Read the quickstart",
    secondary: "See how it works",
    truth: ["Traceable evidence", "Testable causes", "Reviewable recommendations", "Reusable conclusions"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "Engineering evidence from a run",
    panelCampaign: "RECIPE RECOMMENDATION · RUN-042",
    panelBadge: "Recommendation awaiting review",
    parameters: [
      ["Actual control value", "42.0", ""],
      ["Phase trajectory deviation", "+1.8σ", ""],
      ["Tooling version", "TOOLING-A", ""],
    ],
    predictions: [
      ["Key difference", "Phase 2"],
      ["Qualified runs", "12"],
      ["Next recipe", "Awaiting review"],
    ],
    panelFoot: "Product illustration · facts, differences, uncertainty, and follow-up recommendations",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "Bring research records, process analysis, and recipe decisions into one workflow.",
    productText: "Organize objectives, experiment records, and engineering judgment by project. The system directly links actual settings, process context, and quality outcomes from completed runs without manual recipe reclassification. Compare differences and check evidence before reviewing recommendations and recipe revisions.",
    productCards: [
      ["01", "Organize research records", "Define project objectives, variables, and scope, and retain measured experiment values and provenance."],
      ["02", "Check runs and quality", "Link actual settings, process trajectories, and inspection outcomes; check completeness and applicability."],
      ["03", "Analyze process differences", "Compare compatible runs and organize key differences, candidate causes, and evidence gaps."],
      ["04", "Review recipe recommendations", "Use qualified run evidence to propose candidate settings and retain engineering decisions and revision rationale."],
    ],
    shotsKicker: "REAL WORKBENCH",
    shotsTitle: "Review runs, compare differences, and inspect recommendations and recipe versions.",
    shotsText: "Inspect linked facts, analytical rationale, and engineering decisions at each step to check how a recommendation was formed.",
    viewImage: "View full-size image",
    shots: [
      ["/screenshots/production-run.png", "Real production run", "Review the applied recipe version, process data, quality outcome, and production context"],
      ["/screenshots/diagnosis.png", "Process diagnosis", "Start from runs that need attention and narrow candidate causes"],
      ["/screenshots/optimization.png", "Recipe optimization", "Organize candidate parameters and the next validation step within the evidence boundary"],
      ["/screenshots/next-recipe.png", "Recommendation and revision draft", "Review a recommendation, then carry its rationale and real-run evidence into a recipe version draft"],
    ],
    shotsNote: "Interfaces and data illustrate the product and do not establish real process outcomes. Recipe-recommendation screens are design illustrations; see current status for Web coverage.",
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "The system organizes facts and recommendations; engineers make R&D decisions.",
    loopText: "Projects retain objectives and records, analysis explains differences and evidence boundaries, and recommendations preserve inputs and rationale. Engineers review adoption and revisions, then use later outcomes to reassess their decisions.",
    loopSteps: [
      ["01", "Define", "question · variables · boundaries"],
      ["02", "Record", "settings · trajectories · outcomes"],
      ["03", "Qualify", "quality · provenance · completeness"],
      ["04", "Diagnose", "compare · candidates · evidence"],
      ["05", "Recommend", "objectives · constraints · uncertainty"],
      ["06", "Review", "decisions · revisions · later outcomes"],
    ],
    optimizerKicker: "THE ENGINEERING TOOLBOX",
    optimizerTitle: "Confirm that data are trustworthy before forming an auditable revision.",
    optimizerText: "The recommender checks whether production runs are complete, comparable, and linked to quality outcomes before forming a candidate within declared variables, safety boundaries, and observed coverage. Engineers review recommendations; the system never changes production parameters automatically.",
    methodA: "Confirm data usability",
    methodAText: "Check completeness, actual values, units, time, and provenance, and identify version changes or drift.",
    methodB: "Process diagnosis",
    methodBText: "Use matching, robust statistics, stage trajectories, and context stratification to narrow candidates.",
    methodC: "Preserve mechanism notes",
    methodCText: "Attach parameter effects, known boundaries, and engineering judgment to a specific recipe version with run, quality, and reviewed process-document references.",
    methodD: "Revise the next recipe version",
    methodDText: "Inherit complete parameters and applicability, change only confirmed control values, and create a traceable draft.",
    engineFeatures: ["Data quality", "Real runs", "Version lineage", "Fragment citations", "Reviewed knowledge", "Engineering decisions"],
    archKicker: "RECORDS · ANALYSIS · DECISIONS",
    archTitle: "Research records, analysis, and decisions have clear responsibilities.",
    archText: "The Web app, API, database, and optimizer form the runtime stack; connectors are optional data sources. Formal records retain provenance, versions, and review status, and analysis and recommendations reference those facts. Engineers confirm parameter changes and applicability.",
    layers: [
      ["RESEARCH RECORDS", "projects · variables · experiments", "Organize objectives, actual settings, measured outcomes, and provenance"],
      ["RUNS AND QUALITY", "settings · trajectories · inspections", "Link run context and quality outcomes; check versions, completeness, and review status"],
      ["ANALYSIS AND RECOMMENDATIONS", "comparison · diagnosis · optimization", "Organize candidate causes and recipe recommendations constrained by evidence and safety boundaries"],
      ["ENGINEERING DECISIONS", "review · versions · knowledge", "Retain adoption, revision, or rejection rationale and reference reviewed process material"],
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
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "Read the quickstart",
    contribute: "Contribute",
    reportIssue: "Report an issue",
    statusLabel: "Current maturity",
    statusText: "Each process run is an experiment. Recipe versions hold parameter settings, and run records link actual parameters, process data, and quality outcomes. Admitted run evidence feeds recipe recommendations. Core software workflows have automated tests; real-factory benefit validation remains incomplete. See the current-status document for detailed capabilities and limits.",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "Begin with a process question.",
    ctaText: "Create an R&D project to record variables and outcomes, or start with existing runs to check quality, compare differences, and review recipe recommendations.",
    ctaPrimary: "Build the first data loop",
    ctaSecondary: "Open GitHub",
    footer: "Ingot · From process data to evidence-based R&D decisions.",
  },
} as const;

const github = "https://github.com/liuweichaox/Ingot";
const storyShotIndex = [0, 1, 3, 2] as const;

type SiteCopy = (typeof copy)[Locale];

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
          <div className="product-frame-bar" aria-hidden="true"><i /><span>INGOT / WORKBENCH</span><small>ILLUSTRATIVE DATA</small></div>
          <Image
            src="/screenshots/workbench.png"
            alt={locale === "zh" ? "Ingot 工作台示意界面" : "Ingot workbench with illustrative data"}
            width={1600}
            height={1000}
            priority
            unoptimized
          />
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
  const activeShot = t.shots[storyShotIndex[activeStep]];

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
                  <button type="button" onClick={() => setActiveStep(index)} aria-pressed={index === activeStep}>
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
            <div className="product-frame story-product" data-step={activeStep} role="group" aria-label={locale === "zh" ? "对应步骤的产品界面" : "Product interface for the selected step"}>
              <div className="product-frame-bar"><i /><span>{t.panelCampaign}</span><small>ILLUSTRATIVE DATA</small></div>
              <div className="story-product-shots">
                {storyShotIndex.map((shotIndex, index) => {
                  const [src, title] = t.shots[shotIndex];
                  return (
                    <div className={`story-product-shot${index === activeStep ? " is-active" : ""}`} key={src}>
                      <Image src={src} alt={`${title} — Ingot`} width={1600} height={1000} unoptimized />
                    </div>
                  );
                })}
              </div>
            </div>
            <p className="story-shot-caption"><strong>{activeShot[1]}</strong>{activeShot[2]}</p>
            <p className="story-note">{t.shotsNote}</p>
          </div>
        </div>
      </section>
      <section className="screen-gallery" id="screenshots">
        <div className="frame">
          <div className="screen-gallery-heading">
            <div>
              <p className="eyebrow">{t.shotsKicker}</p>
              <h2>{t.shotsTitle}</h2>
            </div>
            <p className="screen-gallery-copy">{t.shotsText}</p>
          </div>
          <div className="screen-grid">
            {t.shots.map(([src, title, text]) => (
              <figure className="screen-card" key={title}>
                <a className="screen-card-media" href={src} target="_blank" rel="noreferrer" aria-label={`${title} — ${t.viewImage}`}>
                  <Image src={src} alt={`${title} — Ingot`} loading="lazy" width={1600} height={1000} unoptimized />
                </a>
                <figcaption><strong>{title}</strong><span>{text}</span></figcaption>
              </figure>
            ))}
          </div>
          <p className="shots-note">{t.shotsNote}</p>
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
            <div className="status-note"><strong>{t.statusLabel}</strong><p>{t.statusText}</p></div>
          </Reveal>
          <Reveal className="terminal" delay={140}><div className="terminal-bar"><i /><i /><i /><span>QUICKSTART</span></div><pre><code>{t.command}</code></pre></Reveal>
        </div>
      </section>

      <section className="final-cta section">
        <Reveal className="frame final-cta-inner">
          <p className="eyebrow">{t.ctaKicker}</p><h2>{t.ctaTitle}</h2><p>{t.ctaText}</p>
          <div className="button-row centered">
            <a className="button primary" href={`${t.docs}/getting-started`}>{t.ctaPrimary} <span aria-hidden="true">→</span></a>
            <a className="button quiet" href={github}>{t.ctaSecondary} <span aria-hidden="true">↗</span></a>
          </div>
        </Reveal>
      </section>

      <footer className="site-footer">
        <div className="frame footer-inner">
          <Image src="/brand/ingot-lockup.svg" alt="Ingot" width={120} height={45} />
          <p>{t.footer}</p>
          <div><a href={t.docs}>Docs</a><a href={github}>GitHub</a><a href={`${github}/blob/main/LICENSE`}>Apache-2.0</a></div>
        </div>
      </footer>
    </main>
  );
}
