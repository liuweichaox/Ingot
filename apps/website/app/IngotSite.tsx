
"use client";

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
    eyebrow: "DATA-SUPPORTED PROCESS R&D",
    titleA: "让真实数据，",
    titleB: "帮助工艺工程师抉择。",
    lead: "Ingot 让工艺研发从没有数据支撑走向有数据支撑：把每次运行的实际条件、过程轨迹和检验结果连成可信证据，再根据工程问题选择合适的统计、实验设计、机理模型或数值优化方法，帮助工程师判断下一步。",
    primary: "了解工作方式",
    secondary: "查看源代码",
    truth: ["真实运行", "工程师决策", "按问题选方法", "结论可验证"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "一次运行的工程证据",
    panelCampaign: "PROCESS R&D · RUN-042",
    panelBadge: "等待工程师判断",
    parameters: [
      ["实际控制变量", "42.0", ""],
      ["阶段轨迹偏差", "+1.8", "σ"],
      ["工装版本", "TOOLING-A", ""],
    ],
    predictions: [
      ["候选判断", "需要验证"],
      ["证据状态", "来源完整"],
      ["建议动作", "区组实验"],
    ],
    panelFoot: "产品界面示意 · 同时呈现事实、差异、不确定性和可执行下一步",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "先把一次真实运行说清楚，再谈追因和优化。",
    productText: "产品不是从某个算法开始，而是从工程师需要作出的决定开始。六步闭环让现场数据逐步成为能够比较、验证和复用的工程证据。",
    productCards: [
      ["01", "工艺定义", "明确产品、设备、变量、单位、质量目标和安全边界，让计算机知道工程问题是什么。"],
      ["02", "设备接入", "把控制系统、仪器、视觉、检验和业务数据映射成稳定、版本化的工艺语义。"],
      ["03", "生产采集", "按过程执行记录实际控制参数、阶段轨迹、材料、工装和其他生产上下文。"],
      ["04", "数据闭环", "检查缺失、时间、单位和来源，将质量结果唯一关联到同一次运行。"],
      ["05", "工艺追因", "比较可比运行，形成带证据、反证、混杂边界和验证建议的候选原因。"],
      ["06", "工艺研发", "通过受控实验验证候选，并在工程师批准的边界内选择更有价值的下一步。"],
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
    optimizerTitle: "不迷信单一算法，为工程问题选择有效方法。",
    optimizerText: "数据质量、稳健比较、实验设计、因果验证、机器学习、贝叶斯优化、机理模型和 LLM 各有边界。简单方法足够时不用复杂模型；证据不足时正确动作是补数据或拒绝回答。",
    methodA: "数据可信",
    methodAText: "检查完整率、单位、时间、来源、版本和漂移，先决定数据能不能用。",
    methodB: "比较与追因",
    methodBText: "使用匹配比较、稳健统计、阶段轨迹和上下文分层缩小候选范围。",
    methodC: "实验验证",
    methodCText: "通过对照、重复、区组、随机化和干预判断候选是否成立。",
    methodD: "序贯优化",
    methodDText: "在昂贵小样本问题中，用 GP、DOE 或受约束 BO 选择信息量更高的下一步实验。",
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
    visionTitle: "核心价值不变，方法随真实证据升级。",
    visionText: "换设备、产品或工艺时，配置数据映射、变量、目标、约束和上下文；运行身份、证据原则、实验状态和工程师决策边界保持稳定。",
    reusable: [
      ["长期不变", "真实数据支持工程判断，观察结论必须能够回到来源并接受验证"],
      ["按场景配置", "设备映射、变量、阶段、质量目标、安全约束、上下文和机理知识"],
      ["持续演进", "统计方法、代理模型、实验策略、页面布局和语言模型"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "开放完整数据与决策闭环，而不只是算法样例。",
    openText: "MIT 许可。现场采集、生产上下文、过程执行、检验、研发实验、数值服务、工程师工作台和双语文档位于同一仓库。代码能力与真实收益明确分开，真实价值通过历史回放、影子建议和受控在线实验验证。",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "阅读快速开始",
    contribute: "参与贡献",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "从一条真实运行记录开始。",
    ctaText: "先让生产条件、过程轨迹和质量结果可靠关联，再让计算机逐步参与比较、追因、实验和优化。",
    ctaPrimary: "建立第一个数据闭环",
    ctaSecondary: "打开 GitHub",
    footer: "Ingot · 让真实数据帮助工艺工程师抉择。",
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
    eyebrow: "DATA-SUPPORTED PROCESS R&D",
    titleA: "Help process engineers decide,",
    titleB: "with real data.",
    lead: "Ingot moves process R&D from decisions without data support to decisions grounded in real runs. It connects actual conditions, process trajectories, and inspections into trustworthy evidence, then selects statistics, experimental design, physical models, or numerical optimization according to the engineering question.",
    primary: "See how it works",
    secondary: "View source",
    truth: ["Real runs", "Engineer decisions", "Methods fit the question", "Testable conclusions"],
    panelKicker: "ENGINEERING DECISION · EVIDENCE",
    panelTitle: "Evidence for one real run",
    panelCampaign: "PROCESS R&D · RUN-042",
    panelBadge: "Awaiting engineer judgment",
    parameters: [
      ["Actual control", "42.0", ""],
      ["Stage deviation", "+1.8", "σ"],
      ["Tooling revision", "TOOLING-A", ""],
    ],
    predictions: [
      ["Candidate judgment", "Needs validation"],
      ["Evidence state", "Sources complete"],
      ["Suggested action", "Blocked experiment"],
    ],
    panelFoot: "Product illustration · facts, differences, uncertainty, and an actionable next step",
    productKicker: "FROM DATA TO DECISION",
    productTitle: "Understand one real run before claiming diagnosis or optimization.",
    productText: "The product starts from a decision the engineer must make, not from an algorithm. Six steps turn field data into evidence that can be compared, tested, and reused.",
    productCards: [
      ["01", "Define the process", "Declare products, equipment, variables, units, quality objectives, and safety boundaries so the computer understands the question."],
      ["02", "Connect equipment", "Map controls, instruments, vision, inspection, and business data to stable, versioned process semantics."],
      ["03", "Collect production data", "Record actual control parameters, stage trajectories, material, tooling, and other manufacturing context for each process execution."],
      ["04", "Close the data loop", "Check missingness, time, units, and provenance, then link quality outcomes uniquely to the same run."],
      ["05", "Diagnose the process", "Compare like-for-like runs and form candidates with evidence, counterevidence, confounding limits, and validation advice."],
      ["06", "Process R&D", "Validate candidates through controlled experiments and choose more valuable next steps inside engineer-approved boundaries."],
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
    optimizerTitle: "Choose an effective method for the engineering question—not one algorithm for everything.",
    optimizerText: "Data quality, robust comparison, experimental design, causal validation, machine learning, Bayesian optimization, physical models, and LLMs each have boundaries. Use a simple method when it is enough; collect data or refuse when evidence is insufficient.",
    methodA: "Data trust",
    methodAText: "Check completeness, units, time, provenance, versions, and drift before deciding whether data can be used.",
    methodB: "Comparison and diagnosis",
    methodBText: "Use matching, robust statistics, stage trajectories, and context stratification to narrow candidates.",
    methodC: "Experimental validation",
    methodCText: "Use controls, repetitions, blocks, randomization, and interventions to test whether a candidate survives.",
    methodD: "Sequential optimization",
    methodDText: "For expensive small-data problems, use GP, DOE, or constrained BO to select a more informative next experiment.",
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
    visionTitle: "Keep the core value stable while methods improve with real evidence.",
    visionText: "A new machine, product, or process configures mappings, variables, objectives, constraints, and context. Run identity, evidence principles, experiment state, and engineering authority remain stable.",
    reusable: [
      ["Stays stable", "Real data supports engineering judgment, and every conclusion traces to sources and remains testable"],
      ["Configured per scenario", "Equipment mappings, variables, stages, quality objectives, safety constraints, context, and mechanism knowledge"],
      ["Continues evolving", "Statistics, surrogate models, experiment strategies, page layouts, and language models"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "The complete data-to-decision loop, not just an algorithm sample.",
    openText: "MIT licensed. Field acquisition, manufacturing context, process executions, inspections, R&D experiments, numerical services, the engineering workbench, and bilingual documentation live in one repository. Code capability is separated from proven benefit, which requires replay, shadow recommendations, and controlled online experiments.",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "Read the quickstart",
    contribute: "Contribute",
    ctaKicker: "START WITH ONE REAL DATA LOOP",
    ctaTitle: "Begin with one trustworthy run record.",
    ctaText: "First connect production conditions, trajectories, and quality outcomes reliably. Then let computers participate in comparison, diagnosis, experiments, and optimization.",
    ctaPrimary: "Build the first data loop",
    ctaSecondary: "Open GitHub",
    footer: "Ingot · Help process engineers make decisions with real data.",
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
