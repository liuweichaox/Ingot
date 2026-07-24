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
    nav: { g1: "核心能力", g2: "典型场景", g3: "工作方式", g4: "产品组成", cta: "了解项目 →", lang: "EN", langHref: "/en/" },
    hero: {
      eyebrow: "Ingot · 制造生产数据与工艺分析系统",
      h1a: "把每次生产过程,",
      h1b: "变成可追溯的工程依据",
      leadA: "连接设备过程、批次、工件、配方、工装和检测结果。",
      leadB: "从一个异常回到对应工位、阶段与原始曲线;",
      leadC: "再用一致的口径比较批次、设备和生产周期。",
      pill1: "生产履历贯通",
      pill2: "分析结果可回查",
      ctaPrimary: "了解典型场景",
      ctaSecondary: "查看工作方式",
      cardTtl: "Ingot Chat",
      ro: "示例 · 工艺调查",
      you: "你",
      q: "LOT-0716 一次通过率掉了,和上一批比,问题出在哪个环节?",
      sparkHead: "一次通过率 / 近 8 批",
      sparkUnit: "%",
    },
    problem: {
      eyebrow: "生产履历",
      h2a: "让分散的数据,",
      h2b: "围绕",
      h2em: "一次生产过程",
      h2c: "重新连接。",
      cells: [
        ["过程 · 连续", "设备参数成为完整过程", "温度、压力、转速、状态和周期按工件与生产周期关联,形成可以回看的阶段曲线和过程记录。"],
        ["质量 · 对齐", "检测结果回到对应现场", "尺寸、缺陷和判定结果与批次、工位、配方和工装关联,从结果可以直接找到过程。"],
        ["方法 · 复用", "专家经验沉淀为分析方案", "常用指标、比较范围和工艺阶段成为可复用配置,让团队沿用同一套分析口径。"],
      ],
    },
    guar: {
      eyebrow: "核心能力",
      h2a: "从生产履历到",
      h2u: "可核对",
      h2b: "的工艺调查。",
      sub: "Ingot 把数据组织、指标计算和工程复核放在同一条工作链路上。",
      cards: [
        { v: true, tag: "Production History", h: "过程与结果对齐", pa: "把设备、工件、批次、配方、工装和检测记录放进", hl: "同一条生产履历", pb: ",从异常结果可以回到对应工位和工艺阶段。", foot: "按批次 · 工件 · 生产周期组织" },
        { v: false, tag: "Evidence Trail", h: "每个指标都有依据", pa: "平台完成数据检查、聚合和对比,", hl: "数字与图表保留相关生产记录", pb: ",可以继续打开样本、曲线和检测明细。", foot: "范围 · 样本 · 曲线 · 检测记录" },
        { v: false, tag: "Reusable Analysis", h: "专家方法可以复用", pa: "用日常语言发起调查,系统完成检索、对齐和计算。", hl: "工艺阶段、特征和比较口径沉淀为分析方案", pb: ",让团队持续沿用经过确认的方法。", foot: "自然语言进入 · 工程依据输出" },
      ],
    },
    quest: {
      eyebrow: "价值在哪",
      h2a: "围绕制造现场",
      h2b: "最关键的工艺问题。",
      sub: "从良率、设备差异到工装趋势,Ingot 用同一套生产履历连接问题、指标和原始记录。",
      rank: "核心场景",
      heroH: "良率为什么突然下滑?",
      heroP: "从哪一批开始、掉在哪个环节、和哪个过程参数一起变的 —— 把“良率分析”从开会拍脑袋,变成一组能定位到工位和批次的可核对记录。少说清一天,就多一天的废品和返工。",
      dim1a: "分组 · ", dim1b: "批次",
      dim2a: "定位 · ", dim2b: "工位 · 过程参数",
      small: [
        ["工装 / 刀具到寿命了吗?", "固定一件工装,看关键指标随使用次数怎么走,预测该维护或更换的时点。修早了浪费,修晚了报废。", "趋势 · 设备 · 使用次数"],
        ["同配方,为什么这台不一样?", "同一套参数,不同设备做出的结果有系统性差异。按设备分层对比,把问题定位到具体那一台。", "分层 · 设备"],
      ],
    },
    how: {
      eyebrow: "工作方式",
      h2: "四步,从现场数据到工程结论。",
      steps: [
        { n: "01 · 汇集", h: "连接现场数据", pa: "持续采集设备过程、状态变化与检测结果,并带上工件、批次、配方和工装上下文。", code: "", pb: "" },
        { n: "02 · 成形", h: "建立生产履历", pa: "同一次加工的过程和结果按生产周期关联,形成可回看、可比较的阶段曲线与完整履历。", code: "", pb: "" },
        { n: "03 · 调查", h: "比较同类过程", pa: "从批次、设备、工件或日常问题进入,检查数据质量并按统一口径计算与比较。", code: "", pb: "" },
        { n: "04 · 复核", h: "回到原始记录", pa: "从摘要和图表打开相关周期、曲线与检测明细,让团队完成工程判断并沉淀分析方案。", code: "", pb: "" },
      ],
    },
    bound: {
      eyebrow: "产品组成",
      h2: "采集、履历、配置与分析,形成一套完整工作台。",
      yesLbl: "生产数据底座",
      noLbl: "工艺分析工作台",
      yes: [
        "Edge 持续采集设备参数、状态与现场记录",
        "Platform 关联批次、工件、周期、配方与工装",
        "工艺阶段、参数单位和检测定义按版本维护",
        "原始数据、阶段曲线和质量结果统一查询",
      ],
      no: [
        "从设备、批次、工件和异常结果进入调查",
        "检查完整性并比较同类生产周期",
        "Ingot Chat 组织问题、指标、图表与相关记录",
        "分析方案保存目标指标、范围和比较口径",
      ],
    },
    cta: {
      eyebrow: "开始了解",
      h2a: "从一个现场问题出发,",
      h2b: "建立第一条",
      h2g: "可分析的生产履历",
      h2c: "。",
      p: "选择一条产线、一个产品族和一个正在影响良率、节拍或维护成本的问题,完成数据准备、结果复核和方法沉淀。",
      primary: "查看落地实施",
      secondary: "浏览项目文档",
    },
    foot: "制造生产数据与工艺分析系统 · ",
    footB: "生产履历 · 工艺调查 · 结果回查",
  },
  en: {
    docs: `${DOCS}/en`,
    nav: { g1: "Capabilities", g2: "Use cases", g3: "How it works", g4: "Product", cta: "Explore →", lang: "中文", langHref: "/" },
    hero: {
      eyebrow: "Ingot · Manufacturing Production Data & Process Analysis",
      h1a: "Turn every production run",
      h1b: "into traceable engineering evidence",
      leadA: "Connect equipment processes, batches, workpieces, recipes, tooling, and inspections. ",
      leadB: "Move from an abnormal result to the matching station, stage, and original curve; ",
      leadC: "then compare batches, machines, and production cycles with consistent definitions.",
      pill1: "Connected production history",
      pill2: "Traceable analysis results",
      ctaPrimary: "Explore use cases",
      ctaSecondary: "See how it works",
      cardTtl: "Ingot Chat",
      ro: "DEMO · INVESTIGATION",
      you: "You",
      q: "LOT-0716's first-pass yield dropped. Compared to the last batch, which step is it?",
      sparkHead: "First-pass yield / last 8 batches",
      sparkUnit: "%",
    },
    problem: {
      eyebrow: "Production history",
      h2a: "Reconnect scattered data",
      h2b: "around ",
      h2em: "one production process",
      h2c: ".",
      cells: [
        ["Process · continuous", "Equipment data becomes a complete process", "Temperature, pressure, speed, states, and cycle time connect by workpiece and production cycle to form reviewable stage curves."],
        ["Quality · aligned", "Inspections return to the matching operation", "Dimensions, defects, and outcomes connect with the batch, station, recipe, and tooling, linking every result to its process."],
        ["Method · reusable", "Expert practice becomes an analysis plan", "Common metrics, comparison scopes, and process stages become reusable configuration, keeping the team on consistent definitions."],
      ],
    },
    guar: {
      eyebrow: "Core capabilities",
      h2a: "From production history to a ",
      h2u: "reviewable",
      h2b: " process investigation.",
      sub: "Ingot brings data organization, metric calculation, and engineering review into one workflow.",
      cards: [
        { v: true, tag: "Production History", h: "Process and outcome align", pa: "Place equipment, workpieces, batches, recipes, tooling, and inspections in ", hl: "one production history", pb: " so an abnormal result opens the matching station and process stage.", foot: "Organized by batch · workpiece · cycle" },
        { v: false, tag: "Evidence Trail", h: "Every metric has evidence", pa: "Platform checks, aggregates, and compares the data. ", hl: "Metrics and charts retain their production records", pb: " so teams can open samples, curves, and inspection detail.", foot: "Scope · samples · curves · inspections" },
        { v: false, tag: "Reusable Analysis", h: "Expert methods become reusable", pa: "Start in everyday language and let the system retrieve, align, and calculate. ", hl: "Process stages, features, and comparison definitions become analysis plans", pb: " the team can apply repeatedly.", foot: "Everyday language in · engineering evidence out" },
      ],
    },
    quest: {
      eyebrow: "Where the value is",
      h2a: "Built around the production floor's",
      h2b: "most important process questions.",
      sub: "From yield and machine differences to tooling trends, Ingot connects the question, metrics, and original records through the same production history.",
      rank: "Core use case",
      heroH: "Why did yield suddenly drop?",
      heroP: "From which batch, at which step, and moving together with which process parameter — turning yield analysis from a meeting-room guess into reviewable records that pin the change to a station and a batch. Every day it stays unclear is another day of scrap and rework.",
      dim1a: "Group · ", dim1b: "batch",
      dim2a: "Locate · ", dim2b: "station · parameter",
      small: [
        ["Is the tooling / cutter at end of life?", "Fix one tool, watch a key metric drift with usage count, predict when to service or replace. Too early wastes it; too late scraps parts.", "Trend · asset · usage count"],
        ["Same recipe — why is this machine different?", "Same parameters, systematic differences across machines. Compare stratified by asset to pin it to the specific one.", "Stratify · asset"],
      ],
    },
    how: {
      eyebrow: "How it works",
      h2: "Four steps, from plant data to engineering conclusions.",
      steps: [
        { n: "01 · Connect", h: "Bring plant data together", pa: "Collect equipment processes, state changes, and inspections with their workpiece, batch, recipe, and tooling context.", code: "", pb: "" },
        { n: "02 · Assemble", h: "Build the production history", pa: "Link process and outcome from the same run by production cycle into reviewable, comparable stage curves and histories.", code: "", pb: "" },
        { n: "03 · Investigate", h: "Compare similar processes", pa: "Start from a batch, machine, workpiece, or everyday question, check data quality, and compare with consistent definitions.", code: "", pb: "" },
        { n: "04 · Review", h: "Return to original records", pa: "Open cycles, curves, and inspections from summaries and charts, complete the engineering review, and preserve the analysis plan.", code: "", pb: "" },
      ],
    },
    bound: {
      eyebrow: "Product",
      h2: "Collection, history, configuration, and analysis in one workspace.",
      yesLbl: "Production data foundation",
      noLbl: "Process analysis workspace",
      yes: [
        "Edge continuously collects equipment parameters, states, and plant records",
        "Platform connects batches, workpieces, cycles, recipes, and tooling",
        "Process stages, parameter units, and inspection definitions are versioned",
        "Original data, stage curves, and quality outcomes share one query path",
      ],
      no: [
        "Start investigations from equipment, batches, workpieces, and abnormal outcomes",
        "Check completeness and compare similar production cycles",
        "Ingot Chat organizes questions, metrics, charts, and related records",
        "Analysis plans preserve target metrics, scope, and comparison definitions",
      ],
    },
    cta: {
      eyebrow: "Explore Ingot",
      h2a: "Start from one production question",
      h2b: "and build the first ",
      h2g: "analysis-ready production history",
      h2c: ".",
      p: "Choose one line, one product family, and one question affecting yield, cycle time, or maintenance cost. Prepare the data, review the result, and preserve the method.",
      primary: "See the rollout",
      secondary: "Browse project docs",
    },
    foot: "Manufacturing production data and process analysis · ",
    footB: "Production history · Investigation · Evidence",
  },
} as const;

function AnswerBody({ locale }: { locale: Locale }) {
  if (locale === "en") {
    return (
      <>
        LOT-0716 first-pass yield <G src="Batch coverage · LOT-0716">96.2%</G>,{" "}
        <G src="Batch comparison · difference">2.9 pts</G> below the previous batch&apos;s{" "}
        <G src="Batch comparison · previous batch">99.1%</G>. The gap is concentrated at station 07: this
        batch&apos;s average cycle <G src="Station 07 history · LOT-0716">51.3s</G> vs the previous{" "}
        <G src="Station 07 history · previous batch">47.2s</G>,{" "}
        <G src="Cycle comparison · difference">4.1s</G> longer, with{" "}
        <G src="Process limit · affected workpieces">12</G> parts crossing the process control limit. Other
        stations match the previous batch.
      </>
    );
  }
  return (
    <>
      LOT-0716 一次通过率 <G src="批次覆盖 · LOT-0716">96.2%</G>,比上批{" "}
      <G src="批次对比 · 上一批">99.1%</G> 低 <G src="批次对比 · 差值">2.9 个点</G>
      。差异集中在工位 07:该批平均周期 <G src="工位07履历 · LOT-0716">51.3s</G>,较上批{" "}
      <G src="工位07履历 · 上一批">47.2s</G> 长 <G src="周期对比 · 差值">4.1s</G>,同期{" "}
      <G src="过程上限 · 影响工件">12</G> 件过程参数越过控制上限。其余工位与上批持平。
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
              <a className="btn btn-primary" href="#cta">
                {t.hero.ctaPrimary} <span className="arr">→</span>
              </a>
              <a className="btn btn-ghost" href="#how">{t.hero.ctaSecondary}</a>
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
                      <b>8 parts missing</b> · 8 parts lack inspection records, excluded from this comparison and listed
                      separately.
                    </>
                  ) : (
                    <>
                      <b>缺 8 件</b> · 有 8 件缺检测记录,未纳入本次对比,已单列说明。
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
