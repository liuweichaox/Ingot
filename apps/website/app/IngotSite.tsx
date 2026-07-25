"use client";

import { Disclosure, DisclosureButton, DisclosurePanel } from "@headlessui/react";
import { useEffect, useRef } from "react";

type Locale = "zh" | "en";

/* Brand mark: molten ingot + two verified bars. */
function Mark({ size = 26 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true">
      <path d="M9 8 L23 8 L26 15 L6 15 Z" fill="#F5B93E" />
      <path d="M6 17 L15 17 L17.5 24 L3.5 24 Z" fill="#5FD4C8" opacity=".9" />
      <path d="M17 17 L26 17 L28.5 24 L14.5 24 Z" fill="#526476" />
    </svg>
  );
}

/* A grounded number: a value that traces to a real query result. */
function G({ src, children }: { src: string; children: React.ReactNode }) {
  return (
    <span className="g" data-src={src}>
      {children}
    </span>
  );
}

const DOCS = "https://docs.ingotstack.com";

const COPY = {
  zh: {
    docs: `${DOCS}/zh`,
    nav: { g1: "核心能力", g2: "研发场景", g3: "研发闭环", g4: "系统组成", cta: "了解 Ingot →", lang: "EN", langHref: "/en/" },
    hero: {
      eyebrow: "Ingot · AI 工艺研发系统",
      h1a: "用更少的实验,",
      h1b: "加速工艺研发",
      leadA: "融合实验数据、实时过程数据、物理机理和专家知识。",
      leadB: "辅助工艺工程师设计实验、发现规律、优化参数并验证工艺窗口,",
      leadC: "缩短工艺研发周期。",
      pill1: "数据 · 机理 · 知识融合",
      pill2: "实验建议可验证",
      ctaPrimary: "了解研发闭环",
      ctaSecondary: "查看核心能力",
      cardTtl: "Ingot 研发助手",
      ro: "示例 · 研发项目",
      you: "你",
      q: "已经完成 12 次模压实验。下一组参数怎么选,才能更快达到面形规格?",
      sparkHead: "面形误差 / 连续实验",
      sparkUnit: "μm",
    },
    problem: {
      eyebrow: "工艺研发",
      h2a: "让每一次实验,",
      h2b: "都推动",
      h2em: "工艺认知",
      h2c: "向前。",
      cells: [
        ["数据 · 真实", "计划、过程和结果完整关联", "区分计划参数、实际设置、真实过程曲线和检测结果,让每次实验都有完整、可复算的证据。"],
        ["机理 · 融合", "数据与物理规律共同判断", "把时序特征、统计证据、物理机理和专家边界放进同一分析,提高小样本研发效率。"],
        ["实验 · 迭代", "下一次试验有明确价值", "根据目标差距、信息增益、不确定性和安全约束推荐候选实验,持续收紧工艺窗口。"],
      ],
    },
    guar: {
      eyebrow: "核心能力",
      h2a: "从研发证据到",
      h2u: "下一组最值得做",
      h2b: "的实验。",
      sub: "Ingot 把数据采集、工艺语义、实验设计、模型机理和工程审核连接成一个研发闭环。",
      cards: [
        { v: true, tag: "R&D Context", h: "围绕研发目标组织一切", pa: "把目标、变量、约束、材料、设备、实验和检测结果放进", hl: "同一个工艺研发项目", pb: ",让数据始终具有明确研发含义。", foot: "目标 · 变量 · 约束 · 实验" },
        { v: false, tag: "Reproducible Evidence", h: "每个结论都能复算", pa: "数据集、特征、模型和机理全部版本化,", hl: "分析结果保留完整证据链", pb: ",工程师可以回到真实实验和过程曲线。", foot: "数据集 · 特征 · 模型 · 机理" },
        { v: false, tag: "Sequential Optimization", h: "每轮实验都更新认知", pa: "系统结合已有结果和不确定性提出候选实验。", hl: "实验结果继续更新假设、模型和参数窗口", pb: ",直到达到并验证目标规格。", foot: "建议 · 执行 · 更新 · 验证" },
      ],
    },
    quest: {
      eyebrow: "核心研发场景",
      h2a: "围绕工艺工程师",
      h2b: "真正要完成的研发目标。",
      sub: "Ingot 不止解释已有结果,还帮助工程师决定下一步怎样实验,并把验证过的结论沉淀为工艺知识。",
      rank: "北极星场景",
      heroH: "怎样更快找到满足规格的工艺窗口?",
      heroP: "在设备、安全、材料和实验预算约束下,融合历史实验、实时过程、物理机理和专家经验,识别最有价值的下一组实验。每次结果都更新对工艺空间的理解,直到目标规格得到稳定验证。",
      dim1a: "目标 · ", dim1b: "达到规格",
      dim2a: "优化 · ", dim2b: "实验次数 · 时间",
      small: [
        ["新产品怎样从历史工艺开始?", "按材料、几何、设备和模具检索相似研发项目,复用经过验证的知识和参数窗口,从接近可行解的位置开始。", "知识迁移 · 暖启动"],
        ["多个指标和约束怎样同时满足?", "同时考虑质量、效率、能耗和安全边界,用带不确定性的多目标实验建议探索可行工艺空间。", "多目标 · 约束 · 不确定性"],
      ],
    },
    how: {
      eyebrow: "研发闭环",
      h2: "四步,从研发目标到验证知识。",
      steps: [
        { n: "01 · 定义", h: "明确目标与约束", pa: "定义目标规格、评价指标、可控变量、设备能力、安全边界和项目预算。", code: "", pb: "" },
        { n: "02 · 融合", h: "建立研发证据", pa: "采集真实过程和检测结果,结合历史实验、物理机理与专家知识形成可复算数据集。", code: "", pb: "" },
        { n: "03 · 实验", h: "设计下一组实验", pa: "分析变量、阶段、交互和不确定性,提出带理由、预期结果和安全检查的候选实验。", code: "", pb: "" },
        { n: "04 · 验证", h: "收紧窗口并沉淀知识", pa: "用实验结果更新模型和假设,验证工艺窗口,把审核后的结论保存为可复用知识。", code: "", pb: "" },
      ],
    },
    bound: {
      eyebrow: "系统组成",
      h2: "从真实数据到研发决策,形成一套完整系统。",
      yesLbl: "数据与研发基础",
      noLbl: "智能研发闭环",
      yes: [
        "Edge 通过主流协议和设备适配采集真实过程",
        "项目统一管理目标、变量、约束、实验和检测",
        "工艺阶段、特征、数据集和模型按版本维护",
        "原始数据、计算结果和知识来源完整追溯",
      ],
      no: [
        "识别关键变量、工艺阶段和候选规律",
        "融合数据模型、物理机理和专家知识",
        "设计带约束和不确定性的下一组实验",
        "验证参数窗口并沉淀可复用工艺知识",
      ],
    },
    cta: {
      eyebrow: "开始一个真实项目",
      h2a: "从一个工艺目标出发,",
      h2b: "建立第一条",
      h2g: "可验证的研发闭环",
      h2c: "。",
      p: "选择一个有明确规格、可控参数、真实设备和检测条件的研发项目,用实验次数、研发时间和资源成本验证价值。",
      primary: "查看落地验证",
      secondary: "浏览项目文档",
    },
    foot: "AI 工艺研发系统 · ",
    footB: "实验设计 · 机理融合 · 工艺窗口",
  },
  en: {
    docs: `${DOCS}/en`,
    nav: { g1: "Capabilities", g2: "R&D scenarios", g3: "R&D loop", g4: "System", cta: "Explore Ingot →", lang: "中文", langHref: "/" },
    hero: {
      eyebrow: "Ingot · AI Process R&D for Manufacturing",
      h1a: "Use fewer experiments",
      h1b: "to find reliable processes faster",
      leadA: "Fuse experimental data, real-time process data, physical mechanisms, and expert knowledge. ",
      leadB: "Help process engineers design experiments, discover patterns, optimize parameters, and validate process windows ",
      leadC: "to shorten development cycles.",
      pill1: "Data · mechanisms · knowledge",
      pill2: "Verifiable experiment guidance",
      ctaPrimary: "Explore the R&D loop",
      ctaSecondary: "See capabilities",
      cardTtl: "Ingot R&D Assistant",
      ro: "DEMO · R&D PROJECT",
      you: "You",
      q: "We have completed 12 molding experiments. Which parameters should we try next to reach the form specification faster?",
      sparkHead: "Form error / sequential experiments",
      sparkUnit: "μm",
    },
    problem: {
      eyebrow: "Process R&D",
      h2a: "Make every experiment",
      h2b: "advance ",
      h2em: "process understanding",
      h2c: ".",
      cells: [
        ["Data · real", "Plan, process, and outcome stay connected", "Separate planned parameters, actual settings, real process traces, and inspections so every experiment has complete, reproducible evidence."],
        ["Mechanisms · fused", "Data and physical laws work together", "Combine time-series features, statistical evidence, physical mechanisms, and expert boundaries for sample-efficient R&D."],
        ["Experiments · iterative", "Every next trial has explicit value", "Recommend candidates from target gaps, information gain, uncertainty, and safety constraints to tighten the process window."],
      ],
    },
    guar: {
      eyebrow: "Core capabilities",
      h2a: "From R&D evidence to the ",
      h2u: "next most valuable",
      h2b: " experiment.",
      sub: "Ingot connects acquisition, process semantics, experiment design, models, mechanisms, and engineering review in one loop.",
      cards: [
        { v: true, tag: "R&D Context", h: "Everything follows the objective", pa: "Place objectives, variables, constraints, materials, equipment, experiments, and outcomes in ", hl: "one process-development project", pb: " so every record has clear R&D meaning.", foot: "Objectives · variables · constraints · experiments" },
        { v: false, tag: "Reproducible Evidence", h: "Every conclusion can be reproduced", pa: "Version datasets, features, models, and mechanisms. ", hl: "Results retain a complete evidence trail", pb: " back to real experiments and process traces.", foot: "Datasets · features · models · mechanisms" },
        { v: false, tag: "Sequential Optimization", h: "Every experiment updates understanding", pa: "Use results and uncertainty to propose candidates. ", hl: "New outcomes update hypotheses, models, and process windows", pb: " until the target specification is validated.", foot: "Recommend · execute · update · validate" },
      ],
    },
    quest: {
      eyebrow: "Core R&D scenarios",
      h2a: "Built around what process engineers",
      h2b: "must achieve in development.",
      sub: "Ingot goes beyond explaining past outcomes: it helps engineers decide what to try next and preserve validated conclusions as process knowledge.",
      rank: "North-star scenario",
      heroH: "How can we reach a process window that meets specification faster?",
      heroP: "Under equipment, safety, material, and budget constraints, combine historical experiments, real-time processes, physical mechanisms, and expert knowledge to select the next most valuable experiments. Every outcome updates the process space until the target is repeatedly validated.",
      dim1a: "Objective · ", dim1b: "reach specification",
      dim2a: "Optimize · ", dim2b: "experiments · time",
      small: [
        ["How can a new product start from prior process knowledge?", "Find similar projects by material, geometry, equipment, and tooling, then reuse validated knowledge and process windows as a warm start.", "Knowledge transfer · warm start"],
        ["How can multiple objectives and constraints be met together?", "Balance quality, efficiency, energy, and safety with uncertainty-aware multi-objective experiment recommendations.", "Multi-objective · constraints · uncertainty"],
      ],
    },
    how: {
      eyebrow: "R&D loop",
      h2: "Four steps, from objective to validated knowledge.",
      steps: [
        { n: "01 · Define", h: "Set objectives and constraints", pa: "Define target specifications, metrics, controllable variables, equipment capability, safety boundaries, and budget.", code: "", pb: "" },
        { n: "02 · Fuse", h: "Build R&D evidence", pa: "Acquire real processes and outcomes, then combine them with history, physical mechanisms, and expert knowledge.", code: "", pb: "" },
        { n: "03 · Experiment", h: "Design the next experiments", pa: "Analyze variables, phases, interactions, and uncertainty to propose candidates with rationale, outcomes, and safety checks.", code: "", pb: "" },
        { n: "04 · Validate", h: "Tighten windows and preserve knowledge", pa: "Update models and hypotheses, validate process windows, and save reviewed conclusions as reusable knowledge.", code: "", pb: "" },
      ],
    },
    bound: {
      eyebrow: "System",
      h2: "One system from real data to R&D decisions.",
      yesLbl: "Data and R&D foundation",
      noLbl: "Intelligent R&D loop",
      yes: [
        "Edge acquires real processes through mainstream protocols and equipment adaptations",
        "Projects manage objectives, variables, constraints, experiments, and inspections",
        "Phases, features, datasets, and models remain versioned",
        "Raw data, computations, and knowledge sources remain traceable",
      ],
      no: [
        "Identify critical variables, phases, and candidate process laws",
        "Fuse data models, physical mechanisms, and expert knowledge",
        "Design constraint- and uncertainty-aware next experiments",
        "Validate process windows and preserve reusable process knowledge",
      ],
    },
    cta: {
      eyebrow: "Start a real project",
      h2a: "Begin with one process objective",
      h2b: "and build the first ",
      h2g: "verifiable R&D loop",
      h2c: ".",
      p: "Choose a project with a target specification, controllable parameters, real equipment, and inspection conditions. Validate value through experiments, time, and resource cost.",
      primary: "See rollout and validation",
      secondary: "Browse project docs",
    },
    foot: "AI Process R&D for Manufacturing · ",
    footB: "Experiment design · Mechanism fusion · Process windows",
  },
} as const;

function AnswerBody({ locale }: { locale: Locale }) {
  if (locale === "en") {
    return (
      <>
        The current project contains <G src="Experiment set · valid runs">12 valid experiments</G>. The
        strongest candidate interaction is holding temperature × pressing speed. The next candidate increases
        holding temperature by <G src="Candidate experiment · temperature">8°C</G> and reduces pressing speed by{" "}
        <G src="Candidate experiment · speed">10%</G>. Predicted form error is{" "}
        <G src="Surrogate model · prediction">0.90 ± 0.20 μm</G>, with an estimated{" "}
        <G src="Constraint evaluation · feasibility">82% feasibility</G> under the recorded equipment and safety
        constraints.
      </>
    );
  }
  return (
    <>
      当前项目包含 <G src="实验集 · 有效运行">12 次有效实验</G>。现有证据中最强的候选交互是保温温度 ×
      压制速度。下一组候选将保温温度提高 <G src="候选实验 · 温度">8℃</G>,压制速度降低{" "}
      <G src="候选实验 · 速度">10%</G>。模型预测面形误差为{" "}
      <G src="代理模型 · 预测">0.90 ± 0.20 μm</G>,在已登记的设备与安全约束下,预计可行概率为{" "}
      <G src="约束检查 · 可行性">82%</G>。
    </>
  );
}

export default function IngotSite({ initialLocale }: { initialLocale: Locale }) {
  const locale = initialLocale;
  const t = COPY[locale];
  const answerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const card = answerRef.current;
    if (!card) return;
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const chips = Array.from(card.querySelectorAll<HTMLElement>(".g"));
    const plot = card.querySelector<SVGPathElement>("#plot");
    const endpt = card.querySelector<SVGCircleElement>("#endpt");
    let done = false;

    const run = () => {
      if (done) return;
      done = true;
      chips.forEach((c, i) => {
        const delay = reduce ? 0 : 360 + i * 240;
        window.setTimeout(() => c.classList.add("on"), delay);
      });
      if (!reduce && plot) {
        const len = plot.getTotalLength();
        plot.style.strokeDasharray = String(len);
        plot.style.strokeDashoffset = String(len);
        plot.getBoundingClientRect();
        plot.style.transition = "stroke-dashoffset 1s ease .3s";
        plot.style.strokeDashoffset = "0";
        if (endpt) {
          endpt.style.opacity = "0";
          window.setTimeout(() => {
            endpt.style.transition = "opacity .4s";
            endpt.style.opacity = "1";
          }, 1300);
        }
      }
    };

    const io = new IntersectionObserver(
      (entries) => {
        entries.forEach((e) => {
          if (e.isIntersecting) {
            run();
            io.disconnect();
          }
        });
      },
      { threshold: 0.35 },
    );
    io.observe(card);
    return () => io.disconnect();
  }, []);

  // Pointer-tracked spotlight: feed cursor position into CSS custom props.
  const spot = (e: React.PointerEvent<HTMLElement>) => {
    const el = e.currentTarget;
    const r = el.getBoundingClientRect();
    el.style.setProperty("--mx", `${e.clientX - r.left}px`);
    el.style.setProperty("--my", `${e.clientY - r.top}px`);
  };

  return (
    <>
      <div className="scroll-progress" aria-hidden="true" />
      <Disclosure as="header" className="nav">
        <div className="wrap nav-in">
          <a className="brand" href={locale === "en" ? "/en/" : "/"} aria-label="Ingot">
            <Mark />
            <b>INGOT</b>
          </a>
          <nav className="nav-links" aria-label={locale === "en" ? "Main navigation" : "主导航"}>
            <a className="hide-sm" href="#guar">{t.nav.g1}</a>
            <a className="hide-sm" href="#quest">{t.nav.g2}</a>
            <a className="hide-sm" href="#how">{t.nav.g3}</a>
            <a className="hide-sm" href="#bound">{t.nav.g4}</a>
            <a className="nav-lang" href={t.nav.langHref}>{t.nav.lang}</a>
            <a className="nav-cta" href="#cta">{t.nav.cta}</a>
            <DisclosureButton
              className="grid size-9 place-items-center rounded-full border border-white/15 text-white md:hidden"
              aria-label={locale === "en" ? "Open navigation" : "打开导航"}
            >
              <span aria-hidden="true">☰</span>
            </DisclosureButton>
          </nav>
        </div>
        <DisclosurePanel className="border-t border-white/10 bg-[#0f0d0a] px-5 py-4 md:hidden">
          <nav className="mx-auto grid max-w-7xl gap-1 text-sm text-[#c6c0b4]" aria-label={locale === "en" ? "Mobile navigation" : "移动导航"}>
            <DisclosureButton as="a" href="#guar" className="rounded-lg px-3 py-2 hover:bg-white/5">{t.nav.g1}</DisclosureButton>
            <DisclosureButton as="a" href="#quest" className="rounded-lg px-3 py-2 hover:bg-white/5">{t.nav.g2}</DisclosureButton>
            <DisclosureButton as="a" href="#how" className="rounded-lg px-3 py-2 hover:bg-white/5">{t.nav.g3}</DisclosureButton>
            <DisclosureButton as="a" href="#bound" className="rounded-lg px-3 py-2 hover:bg-white/5">{t.nav.g4}</DisclosureButton>
          </nav>
        </DisclosurePanel>
      </Disclosure>

      {/* HERO */}
      <section className="hero" id="top">
        <div className="wrap hero-grid">
          <div className="hero-copy">
            <span className="eyebrow">{t.hero.eyebrow}</span>
            <h1>
              {t.hero.h1a}
              <br />
              <span className="q">{t.hero.h1b}</span>
            </h1>
            <p className="lead">
              {t.hero.leadA}
              <b>{t.hero.leadB}</b>
              {t.hero.leadC}
            </p>

            <div className="pills">
              <span className="pill">
                <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true">
                  <path d="M8 1.5 2 4v3.6c0 3.5 2.4 5.7 6 6.9 3.6-1.2 6-3.4 6-6.9V4L8 1.5Z" fill="none" stroke="#FFD06D" strokeWidth="1.3" />
                  <path d="M5.5 8.2 7.2 10l3.3-3.6" fill="none" stroke="#FFD06D" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
                {t.hero.pill1}
              </span>
              <span className="pill">
                <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true">
                  <rect x="2.2" y="3" width="11.6" height="8" rx="1.4" fill="none" stroke="#FFD06D" strokeWidth="1.3" />
                  <path d="M3 13.2h10" stroke="#FFD06D" strokeWidth="1.3" strokeLinecap="round" />
                  <path d="M4.6 3 11.4 11" stroke="#FFD06D" strokeWidth="1.3" strokeLinecap="round" />
                </svg>
                {t.hero.pill2}
              </span>
            </div>

            <div className="cta-row">
              <a className="btn btn-primary" href="#how">
                {t.hero.ctaPrimary} <span className="arr">→</span>
              </a>
              <a className="btn btn-ghost" href="#guar">{t.hero.ctaSecondary}</a>
            </div>
          </div>

          <div className="answer" ref={answerRef} role="img" aria-label={t.hero.cardTtl}>
            <div className="answer-bar">
              <span className="dot live" />
              <span className="ttl">{t.hero.cardTtl}</span>
              <span className="ro">{t.hero.ro}</span>
            </div>
            <div className="answer-body">
              <div className="q-line">
                <span className="who">{t.hero.you}</span>
                <span className="txt">{t.hero.q}</span>
              </div>
              <div className="a-txt">
                <span className="who">INGOT</span>
                <AnswerBody locale={locale} />

                <div className="limitation">
                  {locale === "en" ? (
                    <>
                      <b>Evidence note</b> · Uncertainty remains moderate outside the explored region. This candidate
                      is intended to improve both the objective and the information available for the next iteration.
                    </>
                  ) : (
                    <>
                      <b>证据说明</b> · 未探索区域的不确定性仍为中等。本次候选同时兼顾接近目标与增加下一轮可用信息。
                    </>
                  )}
                </div>

                <div className="spark">
                  <div className="spark-head">
                    <span>{t.hero.sparkHead}</span>
                    <span>{t.hero.sparkUnit}</span>
                  </div>
                  <svg viewBox="0 0 320 76" preserveAspectRatio="none" aria-hidden="true">
                    <line className="thresh" x1="0" y1="16" x2="320" y2="16" />
                    <path className="plot" id="plot" d="M4,24 L48,22 L92,25 L136,21 L180,24 L224,22 L268,25" />
                    <path className="proj" id="proj" d="M268,25 L302,52" />
                    <circle className="end" id="endpt" cx="302" cy="52" r="3.4" />
                  </svg>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* PROBLEM */}
      <section className="problem sec-line">
        <div className="wrap">
          <span className="eyebrow">{t.problem.eyebrow}</span>
          <h2>
            {t.problem.h2a}
            <br />
            {t.problem.h2b}
            <em>{t.problem.h2em}</em>
            {t.problem.h2c}
          </h2>
          <div className="cols reveal">
            {t.problem.cells.map((c) => (
              <div className="cell" key={c[0]}>
                <span className="n">{c[0]}</span>
                <h4>{c[1]}</h4>
                <p>{c[2]}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* GUARANTEES */}
      <section className="guar sec-line" id="guar">
        <div className="wrap">
          <div className="head">
            <span className="eyebrow">{t.guar.eyebrow}</span>
            <h2>
              {t.guar.h2a}
              <br />
              <span className="u">{t.guar.h2u}</span>
              {t.guar.h2b}
            </h2>
            <p className="sub">{t.guar.sub}</p>
          </div>
          <div className="cards3">
            {t.guar.cards.map((card) => (
              <div className={`gcard spot reveal${card.v ? " v" : ""}`} key={card.tag} onPointerMove={spot}>
                <span className="tag">{card.tag}</span>
                <div className="icn">
                  {card.v ? (
                    <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true">
                      <circle cx="9" cy="9" r="6" fill="none" stroke="#5FD4C8" strokeWidth="1.5" />
                      <path d="m13.5 13.5 3.5 3.5" stroke="#5FD4C8" strokeWidth="1.7" strokeLinecap="round" />
                    </svg>
                  ) : (
                    <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true">
                      <rect x="4" y="9" width="12" height="8" rx="1.6" fill="none" stroke="#E6A73A" strokeWidth="1.5" />
                      <path d="M7 9V6.5a3 3 0 0 1 6 0V9" fill="none" stroke="#E6A73A" strokeWidth="1.5" />
                    </svg>
                  )}
                </div>
                <h3>{card.h}</h3>
                <p>
                  {card.pa}
                  <span className="hl">{card.hl}</span>
                  {card.pb}
                </p>
                <div className="foot">{card.foot}</div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* QUESTIONS */}
      <section className="quest sec-line" id="quest">
        <div className="wrap">
          <span className="eyebrow">{t.quest.eyebrow}</span>
          <h2>
            {t.quest.h2a}
            <br />
            {t.quest.h2b}
          </h2>
          <p className="sub">{t.quest.sub}</p>
          <div className="qgrid">
            <article className="qhero spot reveal" onPointerMove={spot}>
              <span className="rank">{t.quest.rank}</span>
              <h3>{t.quest.heroH}</h3>
              <p>{t.quest.heroP}</p>
              <div className="dims">
                <span className="dim">{t.quest.dim1a}<b>{t.quest.dim1b}</b></span>
                <span className="dim">{t.quest.dim2a}<b>{t.quest.dim2b}</b></span>
              </div>
            </article>
            {t.quest.small.map((s) => (
              <article className="qsmall spot reveal" key={s[0]} onPointerMove={spot}>
                <h4>{s[0]}</h4>
                <p>{s[1]}</p>
                <div className="by">{s[2]}</div>
              </article>
            ))}
          </div>
        </div>
      </section>

      {/* HOW */}
      <section className="how sec-line" id="how">
        <div className="wrap">
          <span className="eyebrow">{t.how.eyebrow}</span>
          <h2>{t.how.h2}</h2>
          <div className="steps reveal">
            {t.how.steps.map((s) => (
              <div className="step" key={s.n}>
                <span className="s-n">{s.n}</span>
                <h4>{s.h}</h4>
                <p>
                  {s.pa}
                  {s.pb}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* BOUNDARY */}
      <section className="bound sec-line" id="bound">
        <div className="wrap">
          <span className="eyebrow">{t.bound.eyebrow}</span>
          <h2>{t.bound.h2}</h2>
          <div className="split reveal">
            <div className="bcol yes">
              <div className="lbl">
                <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
                  <path d="M3 8.5 6.5 12 13 4.5" fill="none" stroke="#5FD4C8" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
                {t.bound.yesLbl}
              </div>
              <ul>
                {t.bound.yes.map((li) => (
                  <li key={li}><span className="mk">·</span>{li}</li>
                ))}
              </ul>
            </div>
            <div className="bcol no">
              <div className="lbl">
                <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
                  <circle cx="8" cy="8" r="6" fill="none" stroke="#E6A73A" strokeWidth="1.5" />
                  <path d="M5 8h6" stroke="#E6A73A" strokeWidth="1.6" strokeLinecap="round" />
                </svg>
                {t.bound.noLbl}
              </div>
              <ul>
                {t.bound.no.map((li) => (
                  <li key={li}><span className="mk">·</span>{li}</li>
                ))}
              </ul>
            </div>
          </div>
        </div>
      </section>

      {/* CTA */}
      <section className="cta sec-line" id="cta">
        <div className="wrap">
          <span className="eyebrow">{t.cta.eyebrow}</span>
          <h2>
            {t.cta.h2a}
            <br />
            {t.cta.h2b}
            <span className="g2">{t.cta.h2g}</span>
            {t.cta.h2c}
          </h2>
          <p>{t.cta.p}</p>
          <div className="cta-row">
            <a className="btn btn-primary" href={`${t.docs}/rollout`}>
              {t.cta.primary} <span className="arr">→</span>
            </a>
            <a className="btn btn-ghost" href={`${t.docs}/`}>{t.cta.secondary}</a>
          </div>
        </div>
      </section>

      <footer>
        <div className="wrap foot-in">
          <div className="brand">
            <Mark size={22} />
            <b>INGOT</b>
          </div>
          <div className="m">
            {t.foot}
            <b>{t.footB}</b>
          </div>
        </div>
      </footer>
    </>
  );
}
