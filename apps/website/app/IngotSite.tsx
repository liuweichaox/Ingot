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
      ["产品能力", "#product"],
      ["工作流", "#loop"],
      ["优化引擎", "#optimizer"],
      ["架构", "#architecture"],
      ["开源", "#open-source"],
    ],
    github: "查看 GitHub",
    eyebrow: "AI PROCESS R&D · DIAGNOSE & OPTIMIZE",
    titleA: "看清这次运行，",
    titleB: "优化下一次运行。",
    lead: "Ingot 把设备轨迹、实际配方和质量结果连成可追溯证据：先定位这次运行的偏差来自哪个变量、哪段轨迹，再让物理先验与贝叶斯优化决定下一次运行——更少试验，更快收敛，并始终尊重工艺安全边界。",
    primary: "探索产品能力",
    secondary: "查看源代码",
    truth: ["工艺追因", "工艺优化", "小样本学习", "安全约束"],
    panelKicker: "NEXT RUN · RECOMMENDATION",
    panelTitle: "下一次运行建议",
    panelCampaign: "MANUFACTURING R&D · CAMPAIGN-042",
    panelBadge: "安全可执行",
    parameters: [
      ["控制变量 A", "42.0", ""],
      ["控制变量 B", "11.8", ""],
      ["控制变量 C", "0.72", ""],
    ],
    predictions: [
      ["主质量指标", "0.21 ± 0.06"],
      ["满足约束概率", "94%"],
      ["信息价值", "高"],
    ],
    panelFoot: "产品界面示意 · 推荐同时给出置信区间、约束风险与推荐原因",
    productKicker: "THE COMPLETE PRODUCT",
    productTitle: "从研发目标到稳定工艺窗口，一个系统完成。",
    productText: "不是在采集系统上附加一个算法页面，而是围绕“这次运行为什么不行、下一次该做什么”这两个问题重新组织数据、模型、决策和知识。",
    productCards: [
      ["01", "研发 Campaign", "定义控制变量、目标、硬边界、质量约束、试验成本和停止条件。"],
      ["02", "证据成组与工艺追因", "用 RunKey 关联设定值、实际轨迹、过程特征和检验结果，比对周期定位偏差来源与候选变量。"],
      ["03", "工艺优化", "给出下一次运行的单点或批量候选、预测区间、安全概率、信息增益和推荐理由。"],
      ["04", "工程师决策台", "比较候选、锁定变量、调整边界、批准执行，并保留完整决策依据。"],
      ["05", "收敛与监测", "识别工艺窗口、重复性与漂移，在收益不足或目标达成时建议停止。"],
      ["06", "知识迁移", "把验证过的关系和模型后验沉淀为相似产品的暖启动先验。"],
    ],
    loopKicker: "THE SELF-IMPROVING LOOP",
    loopTitle: "一次真实结果，立即改变下一次决策。",
    loopText: "每次试验都是闭环中的证据。系统持续更新对工艺的理解，而不是定期离线生成一份不会再变化的模型报告。",
    loopSteps: [
      ["01", "定义", "目标 · 约束 · 变量 · 成本"],
      ["02", "观察", "设定值 · 实际轨迹 · 质量"],
      ["03", "学习", "物理均值 + GP 残差"],
      ["04", "建议", "安全 · 多目标 · 成本感知"],
      ["05", "验证", "执行 · 检验 · 更新 · 收敛"],
    ],
    optimizerKicker: "THE NUMERICAL CORE",
    optimizerTitle: "真正为昂贵、小样本实验设计的优化大脑。",
    optimizerText: "数值决策由 PyTorch、GPyTorch 和 BoTorch 完成。LLM 负责意图解析、知识整理和解释，但不会凭语言概率生成工艺配方。",
    modelA: "轨迹代理",
    modelAText: "GP₁ 学习控制设定如何形成温度、压力、位移等真实过程轨迹。",
    modelB: "质量代理",
    modelBText: "GP₂ 融合设定、轨迹和上下文，预测光学指标与安全结果。",
    acquisition: "决策策略",
    acquisitionText: "qLogNEI、qLogNEHVI、批量和成本感知策略平衡探索与利用。",
    constraints: "可信与安全",
    constraintsText: "硬边界、结果约束、不确定性校准、漂移检测和回退策略共同生效。",
    engineFeatures: ["多输出与多目标", "噪声建模", "物理均值函数", "待执行点避让", "批量并行实验", "可插拔采集函数"],
    archKicker: "ONE OPTIMIZATION SYSTEM",
    archTitle: "从任意工业实验开始，核心闭环不绑定设备或工艺。",
    archText: "设备、传感器、制造执行和质量系统都只是数据入口。换一个工艺时，只替换数据映射、特征定义、目标约束与机理先验，实验模型和优化引擎保持不变。",
    layers: [
      ["接入层", "Industrial data mapping", "把设备、传感器、制造系统和质量系统映射成统一实验观察"],
      ["工艺定义", "Feature & objective spec", "配置阶段、轨迹特征、控制变量、目标、约束与成本"],
      ["优化内核", "GP · BO · physics prior", "同一内核处理少样本、多目标、噪声和安全边界"],
      ["知识资产", "Transferable prior", "把已验证关系迁移给下一型号、材料或工艺场景"],
    ],
    visionKicker: "FROM ONE PROCESS TO THE NEXT",
    visionTitle: "换场景，不换追因与优化内核。",
    visionText: "Ingot 的可复用资产不是任何设备地址表，而是完整的偏差归因与序贯实验优化能力。",
    reusable: [
      ["保持不变", "Campaign 工作流、实验数据模型、GP/BO 内核、审核与收敛逻辑"],
      ["按场景配置", "设备信号映射、阶段特征、质量目标、约束与机理先验"],
      ["持续积累", "跨产品先验、材料知识、工艺窗口和可解释的证据链"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "开放完整闭环，而不只是算法样例。",
    openText: "MIT 许可。边缘采集、实验平台、优化器、工程师工作台和文档都在同一个仓库。用历史项目回放评估，再连接真实设备进入在线闭环。",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "阅读快速开始",
    contribute: "参与贡献",
    ctaKicker: "BUILD THE FIRST CLOSED LOOP",
    ctaTitle: "从一个真实工艺开始，建立可迁移的优化能力。",
    ctaText: "先回放历史，再辅助下一次运行，最终让每一种新工艺都从上一种工艺的知识出发。",
    ctaPrimary: "启动第一个 Campaign",
    ctaSecondary: "打开 GitHub",
    footer: "Ingot · 面向真实制造实验的开源工艺追因与优化系统。",
  },
  en: {
    switchLabel: "中文",
    switchHref: "/",
    docs: "https://docs.ingotstack.com/en",
    nav: [
      ["Product", "#product"],
      ["Workflow", "#loop"],
      ["Optimizer", "#optimizer"],
      ["Architecture", "#architecture"],
      ["Open source", "#open-source"],
    ],
    github: "View on GitHub",
    eyebrow: "AI PROCESS R&D · DIAGNOSE & OPTIMIZE",
    titleA: "Explain this run. ",
    titleB: "Optimize the next.",
    lead: "Ingot links equipment trajectories, actual recipes, and quality outcomes into traceable evidence: first locate which variable and which trajectory segment made this run miss spec, then let physical priors and Bayesian optimization choose the next run—fewer experiments, faster convergence, always inside safe process boundaries.",
    primary: "Explore the product",
    secondary: "View source",
    truth: ["Deviation attribution", "Multi-objective", "Small-data learning", "Safety constraints"],
    panelKicker: "NEXT RUN · RECOMMENDATION",
    panelTitle: "Recommended next run",
    panelCampaign: "MANUFACTURING R&D · CAMPAIGN-042",
    panelBadge: "SAFE TO EXECUTE",
    parameters: [
      ["Control variable A", "42.0", ""],
      ["Control variable B", "11.8", ""],
      ["Control variable C", "0.72", ""],
    ],
    predictions: [
      ["Primary quality metric", "0.21 ± 0.06"],
      ["Constraint pass probability", "94%"],
      ["Information value", "HIGH"],
    ],
    panelFoot: "Product UI illustration · every recommendation includes intervals, risk, and rationale",
    productKicker: "THE COMPLETE PRODUCT",
    productTitle: "From R&D target to a stable process window, in one system.",
    productText: "This is not an algorithm tab attached to data collection. Data, models, decisions, and knowledge are organized around two questions: why did this run miss spec, and what experiment should run next?",
    productCards: [
      ["01", "R&D Campaign", "Define controls, objectives, hard bounds, quality constraints, experiment cost, and stopping rules."],
      ["02", "Evidence and attribution", "RunKey joins setpoints, realized trajectories, process features, and inspections; cycle comparison locates deviation sources and candidate variables."],
      ["03", "Next-run recommendations", "Return one or many candidates with intervals, safety probability, information value, and rationale."],
      ["04", "Engineer decision desk", "Compare candidates, lock variables, adjust bounds, approve execution, and preserve evidence."],
      ["05", "Convergence monitoring", "Identify process windows, repeatability, and drift; recommend stopping when marginal value falls."],
      ["06", "Knowledge transfer", "Turn validated relationships and model posteriors into warm-start priors for similar products."],
    ],
    loopKicker: "THE SELF-IMPROVING LOOP",
    loopTitle: "Each real outcome changes the next decision.",
    loopText: "Every run becomes evidence in the loop. The system continuously updates its process belief instead of periodically producing a model report that never learns again.",
    loopSteps: [
      ["01", "Define", "objectives · constraints · variables · cost"],
      ["02", "Observe", "setpoints · trajectories · quality"],
      ["03", "Learn", "physical mean + GP residual"],
      ["04", "Suggest", "safe · multi-objective · cost-aware"],
      ["05", "Verify", "execute · inspect · update · converge"],
    ],
    optimizerKicker: "THE NUMERICAL CORE",
    optimizerTitle: "An optimization brain built for expensive, small-data experiments.",
    optimizerText: "PyTorch, GPyTorch, and BoTorch make numerical decisions. An LLM can parse intent, structure knowledge, and explain recommendations, but never invents process settings.",
    modelA: "Trajectory surrogate",
    modelAText: "GP₁ learns how controls create realized temperature, pressure, displacement, and other trajectories.",
    modelB: "Quality surrogate",
    modelBText: "GP₂ combines settings, trajectories, and context to predict optical objectives and safety outcomes.",
    acquisition: "Decision policy",
    acquisitionText: "qLogNEI, qLogNEHVI, batching, and cost-aware strategies balance exploration and exploitation.",
    constraints: "Trust and safety",
    constraintsText: "Hard bounds, outcome constraints, calibration, drift detection, and fallback policies work together.",
    engineFeatures: ["Multi-output objectives", "Noise modeling", "Physical mean functions", "Pending-point avoidance", "Parallel batches", "Pluggable acquisition"],
    archKicker: "ONE OPTIMIZATION SYSTEM",
    archTitle: "Start with any industrial experiment. Do not bind the core loop to equipment or process.",
    archText: "Equipment, sensors, manufacturing execution, and quality systems are data sources—not platform boundaries. For another process, replace mappings, feature definitions, objectives, constraints, and physical priors; the experiment model and optimization engine remain.",
    layers: [
      ["CONNECT", "Industrial data mapping", "Map equipment, sensors, manufacturing systems, and quality systems into one experimental observation"],
      ["DEFINE", "Feature & objective spec", "Configure stages, trajectory features, controls, objectives, constraints, and cost"],
      ["OPTIMIZE", "GP · BO · physics prior", "Use one core for small data, multiple objectives, noise, and safety boundaries"],
      ["TRANSFER", "Transferable prior", "Carry validated relationships into the next model, material, or process scenario"],
    ],
    visionKicker: "FROM ONE PROCESS TO THE NEXT",
    visionTitle: "Change the scenario, not the diagnosis and optimization core.",
    visionText: "The reusable asset is not an address map for any device. It is the complete capability for deviation attribution and sequential experiment optimization.",
    reusable: [
      ["Stays the same", "Campaign workflow, experiment model, GP/BO core, review, and convergence logic"],
      ["Configured per process", "Device mappings, stage features, quality objectives, constraints, and physical priors"],
      ["Compounds over time", "Cross-product priors, material knowledge, process windows, and explainable evidence"],
    ],
    openKicker: "RUN IT YOURSELF",
    openTitle: "The complete loop, not just an algorithm sample.",
    openText: "MIT licensed. Edge acquisition, experiment platform, optimizer, engineering workbench, and documentation live in one repository. Evaluate with historical replay, then connect real equipment.",
    command: "git clone https://github.com/liuweichaox/Ingot.git\ncd Ingot\ncp .env.example .env\ndocker compose -f docker-compose.app.yml up -d --build",
    readDocs: "Read the quickstart",
    contribute: "Contribute",
    ctaKicker: "BUILD THE FIRST CLOSED LOOP",
    ctaTitle: "Start with one real process. Build optimization that transfers.",
    ctaText: "Replay history first, assist the next run, then let every new process begin with knowledge from the last.",
    ctaPrimary: "Start a campaign",
    ctaSecondary: "Open GitHub",
    footer: "Ingot · Open-source process diagnosis and optimization for real manufacturing experiments.",
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
            <div className="panel-top">
              <span>{t.panelKicker}</span>
              <strong>{t.panelBadge}</strong>
            </div>
            <h2>{t.panelTitle}</h2>
            <p className="panel-campaign">{t.panelCampaign}</p>
            <div className="parameter-grid">
              {t.parameters.map(([name, value, unit]) => (
                <div className="parameter" key={name}>
                  <span>{name}</span>
                  <strong>{value}<small>{unit}</small></strong>
                </div>
              ))}
            </div>
            <div className="prediction-list">
              {t.predictions.map(([name, value]) => (
                <div key={name}><span>{name}</span><strong>{value}</strong></div>
              ))}
            </div>
            <p className="panel-foot">{t.panelFoot}</p>
          </div>
        </div>
      </section>

      <section className="product section" id="product">
        <div className="frame">
          <div className="section-heading">
            <p className="eyebrow">{t.productKicker}</p>
            <h2>{t.productTitle}</h2>
            <p>{t.productText}</p>
          </div>
          <div className="product-grid">
            {t.productCards.map(([number, title, text]) => (
              <article key={title}>
                <span>{number}</span>
                <h3>{title}</h3>
                <p>{text}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      <section className="closed-loop section" id="loop">
        <div className="frame">
          <div className="section-heading wide">
            <p className="eyebrow">{t.loopKicker}</p>
            <h2>{t.loopTitle}</h2>
            <p>{t.loopText}</p>
          </div>
          <div className="loop-rail">
            {t.loopSteps.map(([number, title, text]) => (
              <article key={number}>
                <span>{number}</span>
                <h3>{title}</h3>
                <p>{text}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      <section className="optimizer section" id="optimizer">
        <div className="frame optimizer-layout">
          <div className="optimizer-copy">
            <p className="eyebrow">{t.optimizerKicker}</p>
            <h2>{t.optimizerTitle}</h2>
            <p>{t.optimizerText}</p>
            <div className="tech-line">
              <span>PYTORCH</span><span>GPYTORCH</span><span>BOTORCH</span>
            </div>
          </div>
          <div className="model-map">
            <article className="model-card gold"><small>GP₁</small><h3>{t.modelA}</h3><p>{t.modelAText}</p></article>
            <article className="model-card cyan"><small>GP₂</small><h3>{t.modelB}</h3><p>{t.modelBText}</p></article>
            <article className="model-card"><small>ACQ</small><h3>{t.acquisition}</h3><p>{t.acquisitionText}</p></article>
            <article className="model-card"><small>SAFE</small><h3>{t.constraints}</h3><p>{t.constraintsText}</p></article>
          </div>
        </div>
        <div className="frame engine-feature-row">
          {t.engineFeatures.map((feature) => <span key={feature}>{feature}</span>)}
        </div>
      </section>

      <section className="architecture section" id="architecture">
        <div className="frame">
          <div className="section-heading wide">
            <p className="eyebrow">{t.archKicker}</p>
            <h2>{t.archTitle}</h2>
            <p>{t.archText}</p>
          </div>
          <div className="layer-stack">
            {t.layers.map(([name, tech, text], index) => (
              <article key={name}>
                <span className="layer-number">0{index + 1}</span>
                <strong>{name}</strong>
                <code>{tech}</code>
                <p>{text}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      <section className="vision section">
        <div className="frame">
          <div className="section-heading wide">
            <p className="eyebrow">{t.visionKicker}</p>
            <h2>{t.visionTitle}</h2>
            <p>{t.visionText}</p>
          </div>
          <div className="reusable-grid">
            {t.reusable.map(([title, text], index) => (
              <article key={title}>
                <span>0{index + 1}</span>
                <h3>{title}</h3>
                <p>{text}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      <section className="open-source section" id="open-source">
        <div className="frame open-layout">
          <div>
            <p className="eyebrow">{t.openKicker}</p>
            <h2>{t.openTitle}</h2>
            <p className="open-copy">{t.openText}</p>
            <div className="button-row">
              <a className="button primary" href={`${t.docs}/getting-started`}>{t.readDocs}</a>
              <a className="button secondary" href={`${github}/blob/main/CONTRIBUTING${initialLocale === "en" ? ".en" : ""}.md`}>{t.contribute}</a>
            </div>
          </div>
          <div className="terminal">
            <div className="terminal-bar"><i /><i /><i /><span>QUICKSTART</span></div>
            <pre><code>{t.command}</code></pre>
          </div>
        </div>
      </section>

      <section className="final-cta section">
        <div className="frame">
          <p className="eyebrow">{t.ctaKicker}</p>
          <h2>{t.ctaTitle}</h2>
          <p>{t.ctaText}</p>
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
