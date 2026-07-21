"use client";

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
    nav: { g1: "两个保证", g2: "能回答什么", g3: "怎么做到", g4: "边界", cta: "开始追因 →", lang: "EN", langHref: "/en/" },
    hero: {
      eyebrow: "Ingot · 工艺追因引擎",
      h1a: "为什么这批",
      h1b: "和上批不一样?",
      leadA: "用日常语言问,30 秒得到答案。",
      leadB: "每个数字都来自真实生产数据、点开就是原始曲线;",
      leadC: "查不到,它会直接告诉你缺什么 —— 绝不圆场。",
      pill1: "永不编造数字",
      pill2: "永不触碰设备",
      ctaPrimary: "给我们两周试试",
      ctaSecondary: "看它怎么回答",
      cardTtl: "Ingot Chat",
      ro: "只读 · READ-ONLY",
      you: "你",
      q: "LOT-0716 一次通过率掉了,和上一批比,问题出在哪个环节?",
      sparkHead: "一次通过率 / 近 8 批",
      sparkUnit: "%",
    },
    problem: {
      eyebrow: "现状",
      h2a: "今天,这个问题",
      h2b: "只有",
      h2em: "一个人",
      h2c: "答得上来。",
      cells: [
        ["数据 · 两处", "过程和质量,从不在一起", "温度、压力、转速、周期在 historian 里;合格率、关键尺寸、缺陷判定在 MES 里。它们从没在“一个周期”这个粒度上对上过。"],
        ["分析 · 一个人", "答案活在王工的 Excel 里", "把两边数据对起来、判断“这批为什么不一样”,靠一位资深工艺工程师手工导表、凭经验比对。"],
        ["风险 · 断档", "他一休假,追因就停摆", "这份能力没有沉淀成系统。人走了、忙了、记错了,工厂就再也说不清废品到底出在哪一步。"],
      ],
    },
    guar: {
      eyebrow: "凭什么信它",
      h2a: "工厂对 AI 的两个恐惧,",
      h2u: "代码",
      h2b: "堵死了。",
      sub: "不是承诺,是机制。“会瞎编”和“会乱动设备”—— 这两颗雷,在架构里就拆掉了。",
      cards: [
        { v: true, tag: "Number Grounding", h: "不会瞎编", pa: "回答里", hl: "每一个数字", pb: ",都要在真实查询结果里找到来源,点开就是原始曲线。找不到,它直说缺什么 —— 而不是编一个看着合理的数糊弄过去。", foot: "数值归一化溯源,非子串匹配" },
        { v: false, tag: "Read-Only by Design", h: "不碰设备", pa: "只读你的生产数据,", hl: "永不写 PLC / CNC / 机器人", pb: "。责任边界清清楚楚 —— 过工厂安全审查,不用为它单独开会。", foot: "不参与任何实时控制回路" },
        { v: false, tag: "No Specialist Needed", h: "不用请专家", pa: "用日常语言提问就行。", hl: "原本只有资深工艺工程师做得了的分析", pb: ",现在产线上任何人都能问 —— 追因能力不再锁在一个人脑子里。", foot: "自然语言进,可核对的答案出" },
      ],
    },
    quest: {
      eyebrow: "价值在哪",
      h2a: "它不做泛泛的看板。",
      h2b: "它死磕最贵的那几个问题。",
      sub: "工艺分析的价值极度不均匀 —— 同一个平台,不同问题的回报差一到两个数量级。Ingot 只对准能换算成钱的那几类。这些问题,几乎每家工厂都在问,但今天很难系统回答:",
      rank: "最贵的那一类",
      heroH: "良率为什么突然下滑?",
      heroP: "从哪一批开始、掉在哪个环节、和哪个过程参数一起变的 —— 把“良率归因”从开会拍脑袋,变成一条能定位到工位和批次的证据链。少说清一天,就多一天的废品和返工。",
      dim1a: "分组 · ", dim1b: "批次",
      dim2a: "定位 · ", dim2b: "工位 · 过程参数",
      small: [
        ["工装 / 刀具到寿命了吗?", "固定一件工装,看关键指标随使用次数怎么走,预测该维护或更换的时点。修早了浪费,修晚了报废。", "趋势 · 设备 · 使用次数"],
        ["同配方,为什么这台不一样?", "同一套参数,不同设备做出的结果有系统性差异。按设备分层对比,把问题定位到具体那一台。", "分层 · 设备"],
      ],
    },
    how: {
      eyebrow: "怎么做到",
      h2: "四步,从一条事件到一个可核对的答案。",
      steps: [
        { n: "01 · 接入", h: "标准事件进来", pa: "把任意来源映射成标准 ", code: "ProductionEvent", pb: ",按批提交。平台负责去重、补序、按周期串联。" },
        { n: "02 · 成形", h: "拼成生产履历", pa: "同一次加工的过程和质量,用生产周期号对上,还原成一条可回看、可比对的完整履历。", code: "", pb: "" },
        { n: "03 · 提问", h: "用人话问", pa: "模型只负责听懂和组织语言;真正的查询、聚合、计算,由确定性代码在数据库里完成。", code: "", pb: "" },
        { n: "04 · 核对", h: "可核对的答案", pa: "每个数字带来源、可下钻;缺数据就写进 ", code: "Limitations", pb: "。结论交工程师确认,平台不替你放行。" },
      ],
    },
    bound: {
      eyebrow: "边界",
      h2: "边界画得越清楚,进厂越轻。",
      yesLbl: "Ingot 做这些",
      noLbl: "Ingot 不做这些",
      yes: [
        "汇集设备参数、检测结果、业务记录,成一条生产履历",
        "还原生产周期过程,比较同类周期,列出可能原因",
        "每个结论对应原始记录,数据不足时直接说明",
        "把判定权交给你的 QMS(通过通知),而不是替你决定",
      ],
      no: [
        "不写 PLC / CNC / 机器人,不参与安全相关的实时控制",
        "不做排产、库存、物流",
        "不做质量放行 / 阻断判定 —— 那是 QMS 的职责",
        "不改变现场设备、工艺设置或已有记录",
      ],
    },
    cta: {
      eyebrow: "开始",
      h2a: "不用先上平台。",
      h2b: "先让我们",
      h2g: "把一个问题回答出来",
      h2c: "。",
      p: "给我们两周,拿你现场的数据,挑你现在最说不清的那一个问题,端到端跑通。看到价值,再谈别的。",
      primary: "预约一次追因",
      secondary: "读文档",
    },
    foot: "工艺追因引擎 · 基于原始记录 · ",
    footB: "永不编造 · 永不控制",
  },
  en: {
    docs: `${DOCS}/en`,
    nav: { g1: "Two guarantees", g2: "What it answers", g3: "How", g4: "Boundary", cta: "Start →", lang: "中文", langHref: "/" },
    hero: {
      eyebrow: "Ingot · Process Root-Cause Engine",
      h1a: "Why is this batch",
      h1b: "different from the last?",
      leadA: "Ask in plain language, get an answer in 30 seconds. ",
      leadB: "Every number comes from real production data and opens to the original curve; ",
      leadC: "if it can't find the data, it tells you exactly what's missing — no bluffing.",
      pill1: "Never invents a number",
      pill2: "Never touches equipment",
      ctaPrimary: "Try it for two weeks",
      ctaSecondary: "See how it answers",
      cardTtl: "Ingot Chat",
      ro: "READ-ONLY",
      you: "You",
      q: "LOT-0716's first-pass yield dropped. Compared to the last batch, which step is it?",
      sparkHead: "First-pass yield / last 8 batches",
      sparkUnit: "%",
    },
    problem: {
      eyebrow: "The status quo",
      h2a: "Today, only",
      h2b: "",
      h2em: "one person",
      h2c: "can answer this.",
      cells: [
        ["Data · two places", "Process and quality never meet", "Temperature, pressure, speed, cycle live in the historian; yield, key dimensions, defect calls live in the MES. They've never been joined at the granularity of a single cycle."],
        ["Analysis · one person", "The answer lives in one engineer's spreadsheet", "Joining the two sides and judging “why this batch differs” relies on one senior process engineer exporting tables and comparing by experience."],
        ["Risk · single point", "They take leave, and root-cause stops", "This capability was never captured as a system. When they leave, get busy, or misremember, the plant can no longer say where the scrap came from."],
      ],
    },
    guar: {
      eyebrow: "Why trust it",
      h2a: "The two fears factories have about AI, we closed off in ",
      h2u: "code",
      h2b: ".",
      sub: "Not a promise — a mechanism. “It makes things up” and “it moves my machines” — both defused in the architecture.",
      cards: [
        { v: true, tag: "Number Grounding", h: "It won't make things up", pa: "", hl: "Every single number", pb: " in an answer must trace to a real query result; click it to see the original curve. If it can't find one, it says what's missing — instead of inventing a plausible-looking figure.", foot: "Numeric-normalized grounding, not substring matching" },
        { v: false, tag: "Read-Only by Design", h: "It won't touch equipment", pa: "It only reads your production data; ", hl: "it never writes to a PLC / CNC / robot", pb: ". A clean responsibility boundary — it passes a factory safety review without a special meeting.", foot: "Never in any real-time control loop" },
        { v: false, tag: "No Specialist Needed", h: "No expert required", pa: "Just ask in plain language. ", hl: "Analysis that used to need a senior process engineer", pb: " is now something anyone on the line can ask — root-cause is no longer locked in one person's head.", foot: "Natural language in, verifiable answers out" },
      ],
    },
    quest: {
      eyebrow: "Where the value is",
      h2a: "It doesn't do generic dashboards.",
      h2b: "It goes after the few expensive questions.",
      sub: "The value of process analysis is extremely uneven — on the same platform, different questions pay back one to two orders of magnitude apart. Ingot aims only at the few that convert to money. Almost every plant asks these, yet can't answer them systematically today:",
      rank: "The most expensive class",
      heroH: "Why did yield suddenly drop?",
      heroP: "From which batch, at which step, and moving together with which process parameter — turning “yield attribution” from a meeting-room guess into an evidence chain that pins it to a station and a batch. Every day it stays unclear is another day of scrap and rework.",
      dim1a: "Group · ", dim1b: "batch",
      dim2a: "Locate · ", dim2b: "station · parameter",
      small: [
        ["Is the tooling / cutter at end of life?", "Fix one tool, watch a key metric drift with usage count, predict when to service or replace. Too early wastes it; too late scraps parts.", "Trend · asset · usage count"],
        ["Same recipe — why is this machine different?", "Same parameters, systematic differences across machines. Compare stratified by asset to pin it to the specific one.", "Stratify · asset"],
      ],
    },
    how: {
      eyebrow: "How it works",
      h2: "Four steps, from one event to a verifiable answer.",
      steps: [
        { n: "01 · Ingest", h: "Standard events come in", pa: "Map any source to a standard ", code: "ProductionEvent", pb: " and submit in batches. The platform dedupes, orders, and threads by cycle." },
        { n: "02 · Assemble", h: "Into a production history", pa: "Process and quality from one run are joined by correlation id, reconstructed into one reviewable, comparable history.", code: "", pb: "" },
        { n: "03 · Ask", h: "In plain words", pa: "The model only understands and phrases language; the real querying, aggregation, and math run as deterministic code in the database.", code: "", pb: "" },
        { n: "04 · Verify", h: "A verifiable answer", pa: "Every number carries its source and drills down; missing data goes into ", code: "Limitations", pb: ". Conclusions go to an engineer to confirm — the platform never releases for you." },
      ],
    },
    bound: {
      eyebrow: "Boundary",
      h2: "The clearer the boundary, the lighter the install.",
      yesLbl: "Ingot does this",
      noLbl: "Ingot does not do this",
      yes: [
        "Bring equipment settings, inspection results, and business records into one production history",
        "Reconstruct a cycle, compare comparable cycles, and list possible causes",
        "Every conclusion links to the original record; state clearly when data is insufficient",
        "Hand the decision to your QMS (via webhook), instead of deciding for you",
      ],
      no: [
        "It never writes to a PLC / CNC / robot; no part in safety-related real-time control",
        "No scheduling, inventory, or logistics",
        "No quality release / block decisions — that's the QMS's job",
        "No changing field equipment, process settings, or existing records",
      ],
    },
    cta: {
      eyebrow: "Start",
      h2a: "Don't buy a platform first.",
      h2b: "Let us ",
      h2g: "answer one question",
      h2c: " first.",
      p: "Give us two weeks with your shop-floor data. Pick the one question you can least explain today, and run it end to end. See the value, then talk about the rest.",
      primary: "Book a root-cause session",
      secondary: "Read the docs",
    },
    foot: "Process root-cause engine · grounded in original records · ",
    footB: "Never invents · Never controls",
  },
} as const;

function AnswerBody({ locale }: { locale: Locale }) {
  if (locale === "en") {
    return (
      <>
        LOT-0716 first-pass yield <G src="check_data_quality · LOT-0716">96.2%</G>,{" "}
        <G src="compare_cycles · delta">2.9 pts</G> below the previous batch&apos;s{" "}
        <G src="compare_cycles · previous batch">99.1%</G>. The gap is concentrated at station 07: this
        batch&apos;s average cycle <G src="get_cycle_trace · station 07 · LOT-0716">51.3s</G> vs the previous{" "}
        <G src="get_cycle_trace · station 07 · prev">47.2s</G>,{" "}
        <G src="compare_cycles · cycle delta">4.1s</G> longer, with{" "}
        <G src="check_data_quality · over-limit count">12</G> parts crossing the process control limit. Other
        stations match the previous batch.
      </>
    );
  }
  return (
    <>
      LOT-0716 一次通过率 <G src="check_data_quality · LOT-0716">96.2%</G>,比上批{" "}
      <G src="compare_cycles · 上一批">99.1%</G> 低 <G src="compare_cycles · 差值">2.9 个点</G>
      。差异集中在工位 07:该批平均周期 <G src="get_cycle_trace · 工位07 · LOT-0716">51.3s</G>,较上批{" "}
      <G src="get_cycle_trace · 工位07 · 上批">47.2s</G> 长 <G src="compare_cycles · 周期差">4.1s</G>,同期{" "}
      <G src="check_data_quality · 越限计数">12</G> 件过程参数越过控制上限。其余工位与上批持平。
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

  return (
    <>
      <header className="nav">
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
          </nav>
        </div>
      </header>

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
          <div className="cols">
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
              <div className={`gcard${card.v ? " v" : ""}`} key={card.tag}>
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
            <article className="qhero">
              <span className="rank">{t.quest.rank}</span>
              <h3>{t.quest.heroH}</h3>
              <p>{t.quest.heroP}</p>
              <div className="dims">
                <span className="dim">{t.quest.dim1a}<b>{t.quest.dim1b}</b></span>
                <span className="dim">{t.quest.dim2a}<b>{t.quest.dim2b}</b></span>
              </div>
            </article>
            {t.quest.small.map((s) => (
              <article className="qsmall" key={s[0]}>
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
          <div className="steps">
            {t.how.steps.map((s) => (
              <div className="step" key={s.n}>
                <span className="s-n">{s.n}</span>
                <h4>{s.h}</h4>
                <p>
                  {s.pa}
                  {s.code ? (
                    s.code === "ProductionEvent" ? (
                      <a href={`${t.docs}/rfc-production-events`}>
                        <code>{s.code}</code>
                      </a>
                    ) : (
                      <code>{s.code}</code>
                    )
                  ) : null}
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
          <div className="split">
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
            <a className="btn btn-primary" href={`${t.docs}/`}>
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
