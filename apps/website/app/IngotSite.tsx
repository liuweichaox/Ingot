
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
      ["分析方法", "#optimizer"],
      ["系统边界", "#architecture"],
      ["开源", "#open-source"],
    ],
    github: "查看 GitHub",
    eyebrow: "FEWER EXPERIMENTS · FASTER TARGET",
    titleA: "少做无效实验，",
    titleB: "更快找到达标工艺。",
    lead: "Ingot 把每次运行的实际条件、过程轨迹和检验结果连成可比较的工程证据，帮助工程师找到关键差异、设计验证，并用适合当前问题的方法选择下一项实验。",
    primary: "了解工作方式",
    secondary: "查看源代码",
    truth: ["运行可比较", "差异可定位", "实验可执行", "结果可复核"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "一次运行的工程证据",
    panelCampaign: "PROCESS R&D · RUN-042",
    panelBadge: "下一实验待审批",
    parameters: [
      ["实际控制变量", "42.0", ""],
      ["阶段轨迹偏差", "+1.8", "σ"],
      ["工装版本", "TOOLING-A", ""],
    ],
    predictions: [
      ["关键差异", "保压阶段"],
      ["适用方法", "二次响应面"],
      ["下一实验", "区组验证"],
    ],
    panelFoot: "产品界面示意 · 同时呈现事实、差异、不确定性和可执行下一步",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "从一次超差运行，到下一项最值得做的实验。",
    productText: "将产品主线收窄为四步：还原运行、比较差异、设计验证、选择下一项实验。数据接入和模型都服务于这条主线。",
    productCards: [
      ["01", "还原运行", "用一个运行编号找回实际条件、阶段轨迹、材料、工装和质量结果。"],
      ["02", "比较差异", "将超差运行与可比的合格运行并排，定位变量、阶段和上下文差异。"],
      ["03", "设计验证", "把候选原因变成有对照、重复、区组和安全边界的可执行实验。"],
      ["04", "选择下一项", "在简单响应面和贝叶斯优化之间自适应路由，优先执行更可能推进目标的实验。"],
    ],
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "计算机每一步都帮助，但不越过工程师判断。",
    loopText: "系统负责整理事实、执行计算、暴露不确定性和提出实验建议；工程师负责定义问题、审核现场约束、批准实验并作出最终结论。",
    loopSteps: [
      ["01", "定义", "问题 · 变量 · 边界"],
      ["02", "接入", "协议 · 点位 · 单位"],
      ["03", "记录", "运行 · 轨迹 · 上下文"],
      ["04", "核验", "质量 · 来源 · 完整性"],
      ["05", "判断", "比较 · 候选 · 反证"],
      ["06", "实验", "验证 · 优化 · 沉淀"],
    ],
    optimizerKicker: "THE METHOD TOOLBOX",
    optimizerTitle: "简单方法先行，复杂模型必须用数据证明自己。",
    optimizerText: "Ingot 先用线性或二次响应面建立可解释基线；只有当已观察数据支持额外复杂度时，才让 GP 或机理特征参与选点。目标是少做无效实验，不是展示更复杂的算法。",
    methodA: "数据可信",
    methodAText: "检查完整率、单位、时间、来源、版本和漂移，先决定数据能不能用。",
    methodB: "比较与追因",
    methodBText: "使用匹配比较、稳健统计、阶段轨迹和上下文分层缩小候选范围。",
    methodC: "实验验证",
    methodCText: "通过对照、重复、区组、随机化和干预判断候选是否成立。",
    methodD: "序贯优化",
    methodDText: "从 DOE、线性/二次响应面和受约束 BO 中选择当前证据支持的方法。",
    engineFeatures: ["数据质量", "稳健统计", "DOE 与区组", "混合效应", "GP / BO", "机理与 LLM"],
    archKicker: "ONE EVIDENCE SPINE",
    archTitle: "所有模块围绕同一次真实运行，不建立平行真相。",
    archText: "Process Executions 定义执行边界，Manufacturing 保存生产条件，Inspections 保存结果，Research 组织工程判断和实验。Optimizer 与 Agent 读取同一份受版本控制的证据。",
    layers: [
      ["现场事实", "Executions · context · inspections", "设备、产品、工艺规范、材料、工装、轨迹和质量结果"],
      ["证据主干", "Identity · provenance · versions", "稳定关联每次运行，保留缺失、来源、单位和内容哈希"],
      ["方法工具", "Statistics · DOE · ML · BO", "按问题和数据条件选择可复核的分析与实验方法"],
      ["工程决策", "Review · execute · validate", "工程师审核建议、执行实验并确认结论适用范围"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "目标固定：减少无效实验。方法按证据升级。",
    visionText: "换设备、产品或工艺时，配置数据映射、变量、目标、约束和上下文；运行身份、证据原则、实验状态和工程师决策边界保持稳定。",
    reusable: [
      ["长期不变", "真实数据支持工程判断，观察结论必须能够回到来源并接受验证"],
      ["按场景配置", "设备映射、变量、阶段、质量目标、安全约束、上下文和机理知识"],
      ["持续演进", "统计方法、代理模型、实验策略、页面布局和语言模型"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "开放完整数据与决策闭环，而不只是算法样例。",
    openText: "MIT 许可，可厂内自托管。现场采集、运行对比、质量结果、研发实验、数值服务和工程师工作台位于同一仓库；公开验证协议与结果可独立复现。",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "阅读快速开始",
    contribute: "参与贡献",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "从一次真实超差开始。",
    ctaText: "导入一次超差运行和一次可比的合格运行，找出差异，并产生下一项验证实验。",
    ctaPrimary: "建立第一个数据闭环",
    ctaSecondary: "打开 GitHub",
    footer: "Ingot · 少做无效实验，更快找到达标工艺。",
  },
  en: {
    switchLabel: "中文",
    switchHref: "/",
    docs: "https://docs.ingotstack.com/en",
    nav: [
      ["Core value", "#product"],
      ["Workflow", "#loop"],
      ["Methods", "#optimizer"],
      ["Boundaries", "#architecture"],
      ["Open source", "#open-source"],
    ],
    github: "View GitHub",
    eyebrow: "FEWER EXPERIMENTS · FASTER TARGET",
    titleA: "Avoid unproductive experiments.",
    titleB: "Reach target conditions faster.",
    lead: "Ingot connects the actual conditions, process trajectory, and inspection outcome of every run into comparable engineering evidence. It helps engineers locate important differences, design validation, and select the next experiment with a method suited to the current problem.",
    primary: "See how it works",
    secondary: "View source",
    truth: ["Comparable runs", "Located differences", "Executable experiments", "Reviewable results"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "Evidence for one real run",
    panelCampaign: "PROCESS R&D · RUN-042",
    panelBadge: "Next experiment awaiting approval",
    parameters: [
      ["Actual control", "42.0", ""],
      ["Stage deviation", "+1.8", "σ"],
      ["Tooling revision", "TOOLING-A", ""],
    ],
    predictions: [
      ["Key difference", "Holding stage"],
      ["Applicable method", "Quadratic surface"],
      ["Next experiment", "Blocked validation"],
    ],
    panelFoot: "Product illustration · facts, differences, uncertainty, and an actionable next step",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "From one out-of-spec run to the next experiment worth doing.",
    productText: "The product path is four steps: reconstruct the run, compare differences, design validation, and select the next experiment. Data integration and models both serve this path.",
    productCards: [
      ["01", "Reconstruct the run", "Use one run identifier to recover actual conditions, stage trajectories, material, tooling, and quality outcomes."],
      ["02", "Compare differences", "Place an out-of-spec run beside comparable passing runs and locate differences in variables, stages, and context."],
      ["03", "Design validation", "Turn a candidate cause into an executable experiment with controls, repetitions, blocks, and safety boundaries."],
      ["04", "Select what comes next", "Route between simple response surfaces and Bayesian optimization, prioritizing the experiment most likely to advance the target."],
    ],
    loopKicker: "ENGINEER IN THE LOOP",
    loopTitle: "The computer helps at every step without overruling engineering judgment.",
    loopText: "The system organizes facts, computes, exposes uncertainty, and proposes experiments. Engineers frame the problem, review field constraints, approve execution, and make the final conclusion.",
    loopSteps: [
      ["01", "Define", "question · variables · boundaries"],
      ["02", "Connect", "protocols · points · units"],
      ["03", "Record", "runs · trajectories · context"],
      ["04", "Qualify", "quality · provenance · completeness"],
      ["05", "Judge", "comparison · candidates · counterevidence"],
      ["06", "Experiment", "validate · optimize · preserve"],
    ],
    optimizerKicker: "THE METHOD TOOLBOX",
    optimizerTitle: "Simple methods go first. Complex models must earn their place from data.",
    optimizerText: "Ingot starts with interpretable linear or quadratic response surfaces. GP probability and mechanism features join selection only when visible observations support the extra complexity. The goal is fewer unproductive experiments, not a more complicated algorithm.",
    methodA: "Data trust",
    methodAText: "Check completeness, units, time, provenance, versions, and drift before deciding whether data can be used.",
    methodB: "Comparison and diagnosis",
    methodBText: "Use matching, robust statistics, stage trajectories, and context stratification to narrow candidates.",
    methodC: "Experimental validation",
    methodCText: "Use controls, repetitions, blocks, randomization, and interventions to test whether a candidate survives.",
    methodD: "Sequential optimization",
    methodDText: "Choose the method supported by current evidence from DOE, linear or quadratic response surfaces, and constrained BO.",
    engineFeatures: ["Data quality", "Robust statistics", "DOE and blocking", "Mixed effects", "GP / BO", "Physics and LLMs"],
    archKicker: "ONE EVIDENCE SPINE",
    archTitle: "Every module describes the same real run—never a parallel truth.",
    archText: "Process Executions define execution boundaries, Manufacturing preserves conditions, Inspections preserve outcomes, and Research organizes engineering judgment and experiments. Optimizer and Agent read the same versioned evidence.",
    layers: [
      ["FIELD FACTS", "Executions · context · inspections", "Equipment, product, process specification, material, tooling, trajectory, and quality outcomes"],
      ["EVIDENCE SPINE", "Identity · provenance · versions", "Stable run linkage with visible missingness, sources, units, and content hashes"],
      ["METHOD TOOLBOX", "Statistics · DOE · ML · BO", "Reviewable analysis and experiment methods selected by question and data"],
      ["ENGINEERING DECISION", "Review · execute · validate", "Engineers review recommendations, execute experiments, and confirm applicability"],
    ],
    visionKicker: "STABLE CORE, EVOLVING METHODS",
    visionTitle: "One fixed outcome: fewer unproductive experiments. Methods improve with evidence.",
    visionText: "A new machine, product, or process configures mappings, variables, objectives, constraints, and context. Run identity, evidence principles, experiment state, and engineering authority remain stable.",
    reusable: [
      ["Stays stable", "Real data supports engineering judgment, and every conclusion traces to sources and remains testable"],
      ["Configured per scenario", "Equipment mappings, variables, stages, quality objectives, safety constraints, context, and mechanism knowledge"],
      ["Continues evolving", "Statistics, surrogate models, experiment strategies, page layouts, and language models"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "The complete data-to-decision loop, not just an algorithm sample.",
    openText: "MIT licensed and self-hostable inside the plant. Field acquisition, run comparison, quality outcomes, R&D experiments, numerical services, and the engineering workbench live in one repository; public validation protocols and results are independently reproducible.",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "Read the quickstart",
    contribute: "Contribute",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "Begin with one real out-of-spec run.",
    ctaText: "Import one out-of-spec run and one comparable passing run, locate the difference, and produce the next validation experiment.",
    ctaPrimary: "Build the first data loop",
    ctaSecondary: "Open GitHub",
    footer: "Ingot · Fewer wasted experiments. Faster routes to target conditions.",
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
              <a className="button primary" href="#product">{t.primary} <span>→</span></a>
              <a className="button secondary" href={github}>{t.secondary} <span>↗</span></a>
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
            </div>
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
          <div><a href={t.docs}>Docs</a><a href={github}>GitHub</a><a href={`${github}/blob/main/LICENSE`}>MIT</a></div>
        </div>
      </footer>
    </main>
  );
}
