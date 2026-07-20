"use client";

import Image from "next/image";
import dynamic from "next/dynamic";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import {
  FACTORY_STAGE_DATA,
  FACTORY_STAGE_MS,
  FACTORY_STAGES,
  type FactoryDatum,
  type FactoryView,
} from "./factoryStages";

type Locale = "zh" | "en";
type PlatformView = "overview" | "review" | "analysis";
type ResolvedStageDatum = FactoryDatum & {
  capture: "automatic" | "hybrid";
  code: string;
  dataLabel: string;
  detail: string;
  label: string;
  operation: string;
  stored: boolean;
  view: FactoryView;
};
const PLATFORM_VIEW_KEYS = ["overview", "review", "analysis"] as const satisfies readonly PlatformView[];

const factoryViews = FACTORY_STAGES.map((stage) => stage.view);

const FactoryScene3D = dynamic(() => import("./FactoryScene3D"), {
  ssr: false,
});

const stageMeta = FACTORY_STAGES;

const messages = {
  zh: {
    meta: {
      title: "Ingot — 让生产事实可验证、可追问、可调查",
      description:
        "Ingot 汇集生产现场的重要记录；工程师通过 Ingot Chat 查问题、看证据，并在需要时深入调查。",
    },
    languageLabel: "语言",
    nav: {
      label: "主导航",
      home: "Ingot 首页",
      product: "产品",
      data: "生产记录",
      analytics: "Ingot Chat",
      deployment: "为什么可信",
      docs: "文档",
    },
    hero: {
      eyebrow: "INGOT · PRODUCTION INTELLIGENCE",
      heading: ["让生产事实可验证、", "可追问、可调查。"],
      lead:
        "Ingot 把来自设备、检测和业务系统的记录放在同一条可追溯的生产履历中。工程师通过 Ingot Chat 提问、查看证据；遇到复杂问题时，可启动多角色深入调查。它不会改变现场设备或已有记录。",
      analysis: "认识 Ingot Chat",
      github: "了解 Ingot 平台",
      pause: "暂停动画",
      play: "继续动画",
      proofLabel: "产品特性",
      proof: ["连接多种生产来源", "保存可追溯记录", "Ingot Chat 日常问答", "深入调查与证据回链"],
      viewLabel: "三维工厂视角",
      views: ["机器人上料", "CNC 加工", "机器人下料", "视觉质检", "人工检测"],
      dataKinds: ["设备状态", "加工事件", "设备状态", "检测结果", "人工检测记录"],
      captureAuto: "示例生产事件",
      captureGenerated: "示例上下文与关联 ID",
      captureHybrid: "示例检测记录",
    },
    stageData: [
      {
        label: "机器人上料状态",
        active: "取毛坯 · 进入机床",
        activeDetail: "机械臂从机旁料盘取件，正沿门口水平方向伸入机罩并放入固定夹具",
        settled: "工件已装夹 · 机械臂退出",
        settledDetail: "夹具已夹紧 · 机械臂已回安全位 · 主轴保持 0 rpm",
      },
      {
        label: "CNC 核心加工事件",
        active: "加工开始",
        activeDetail: "工件已夹紧，程序 O1207 开始执行",
        settled: "加工结束",
        settledDetail: "程序 O1207 正常结束 · 用时 42.8 s · 工件 ING-0718-0127",
      },
      {
        label: "机器人运行状态",
        active: "机床取件 · 下料",
        activeDetail: "主轴已退回安全位、机床门打开，机械臂正水平伸入固定夹具取件并送往视觉入口",
        settled: "机器人已回待机位",
        settledDetail: "工件位于视觉检测位 · 姿态 90° · 抓手已释放",
      },
      {
        label: "视觉检测数据",
        active: "相机采集中",
        activeDetail: "工件已停稳，三相机正在采集尺寸与表面数据",
        settled: "检测结果已记录",
        settledDetail: "结果 PASS · 得分 97.8 · 孔距 32.047 mm · 图像 IMG-182347",
      },
      {
        label: "人工检测记录",
        active: "正在检测表面粗糙度",
        activeDetail: "QE-018 已扫码确认工件，正在使用粗糙度仪检测 Ra 值；外部检测客户端将示例结果提交到检测 API",
        settled: "人工检测结果已保存",
        settledDetail: "表面粗糙度 Ra 0.82 μm · 合格 · 仪器 ROUGHNESS-01 · 检测员 QE-018",
      },
    ],
    stages: [
      { label: "机器人上料", detail: "机械臂从机旁料盘取毛坯，经打开的正面自动门水平伸入固定夹具；完全退出后机床才允许关门加工", data: "自动：料盘到位 · 抓手双通道 · 门/主轴互锁 · 夹紧确认" },
      { label: "CNC 加工", detail: "工件已定位，O1207 加工程序正在执行", data: "自动：程序/刀具 · 主轴/进给 · 周期/报警" },
      { label: "机器人下料", detail: "加工结束、主轴退回且门打开后，机械臂水平伸入固定夹具取件，再放到独立的视觉输送入口", data: "自动：主轴安全位 · TCP/速度 · 抓手/保护停机 · 放件确认" },
      { label: "视觉质检", detail: "工件已停稳，三相机正在采集尺寸与表面数据", data: "自动：测量值 · 图像 ID · PASS/FAIL · 得分" },
      { label: "人工检测", detail: "示例检测客户端记录工件、测量值、单位、仪器和检测员，并提交检测 API", data: "示例事实：工件 ID · 粗糙度 Ra · 仪器 · 检测员" },
    ],
    factory: {
      railLabel: "示例生产工序",
      heading: ["每一次生产，", "都有一条可追溯的履历。"],
      lead:
        "这是一个示例生产过程。从上料、加工到检测，Ingot 将关键记录整理成同一件工件的完整履历，方便回看、比对和调查。",
      provenanceTitle: "设备、检测与业务记录，汇成一条生产履历",
      provenanceLead:
        "每条记录都保留来源、时间、对象和关联关系；当有人提出问题时，可以回到对应的原始记录。",
      provenance: [
        ["接入现场记录", "设备、仪器和现有系统可以按照现场情况接入 Ingot，保留各自的工作方式。"],
        ["汇集检测结果", "自动或人工检测的结果与工件、批次和生产过程放在一起，形成完整上下文。"],
        ["保留事实依据", "每个结论都可以回到相应的生产记录；数据不完整时，Ingot 会明确提示。"],
      ],
    },
    platform: {
      heading: ["从生产记录，", "到可以行动的问题调查。"],
      lead:
        "Ingot 是平台；Ingot Chat 是工程师最常使用的入口。先快速了解发生了什么，再在需要时把不同视角放在一起深入审查。",
      workspace: "INGOT 示例生产工作台",
      site: "华东一厂 / CELL-A",
      connected: "示例数据",
      tabsLabel: "平台能力视图",
      tabs: ["生产总览", "人工检测", "Ingot Chat"],
      overview: "生产总览",
      viewKickers: ["FACTORY / OVERVIEW", "HUMAN / MANUAL INSPECTION", "CHAT / FACT QUERY"],
      range: "当前班次",
      metrics: [
        ["在线工位", "5 / 5", "全部数据源健康"],
        ["当前批次", "LOT-0716", "AL-6061"],
        ["一次通过率", "99.2%", "+0.4% 本班次"],
        ["平均周期", "47.2s", "目标 50.0s"],
      ],
      lineTitle: "本次生产履历",
      factsTitle: "当前工位记录",
      factsHint: "加工开始/结束、设备状态和检测结果分别记录，名称与现场工序一致",
      envelope: "工程字段",
      envelopeShow: "查看工程字段",
      envelopeHide: "收起工程字段",
      healthTitle: "事件接入状态",
      health: [
        ["自动数据源", "5 / 5 在线"],
        ["待处理检测任务", "2"],
        ["工件信息完整率", "99.8%"],
        ["生产履历缺失", "0"],
      ],
      dataStatus: "数据已保存 · 已归入工件履历",
      pendingDataStatus: "人工检测进行中 · 结果尚未保存",
      status: {
        run: "数据更新中",
        pass: "已记录",
        ready: "待采集",
        sign: "检测中",
        submitted: "已提交",
        onDemand: "按需触发",
      },
      review: {
        title: "示例流程：机旁人工检测",
        lead: "该示例展示外部检测客户端可提交的检测事实：工件、结果、测量值、单位、仪器、检测员和时间。质量判定与放行由企业现有流程负责。",
        taskLabel: "当前检测任务",
        task: "MI-LOT-0716-001",
        state: "等待测量",
        evidenceTitle: "本次检测要求",
        evidence: [
          ["工件", "ING-0718-0127"],
          ["检测项目", "表面粗糙度 Ra"],
          ["规格上限", "1.60 μm"],
          ["检测结果", "0.82 μm · 合格"],
          ["仪器与人员", "ROUGHNESS-01 · QE-018"],
        ],
        stepsTitle: "现场检测步骤",
        steps: [
          ["01", "确认人员和工件", "检测员刷卡，固定扫码器读取工件码，避免测错工件或批次。"],
          ["02", "确认仪器可用", "企业检测客户端确认粗糙度仪、校准状态、检测项目和规格范围。"],
          ["03", "完成测量", "检测员把探头放到指定表面；客户端取得示例测量值 Ra 0.82 μm。"],
          ["04", "提交事实", "客户端核对测量值和结果，通过 InspectionRecord API 保存检测事实。"],
        ],
        boundary: "Ingot 保存客户端提交的检测事实；人员认证、仪器连接、校准判断、返工、报废和首件放行由企业现有系统负责。",
        toAnalysis: "在 Chat 中查看周期事件与数据质量",
      },
      analysis: {
        title: "Ingot Chat · 日常问答与深入调查",
        lead: "先用日常问答快速查清事实；遇到复杂问题，再邀请多个专业视角共同审查现有证据。",
        chat: {
          title: "Ingot Chat · 示例调查",
          note: "这是示例数据。Chat 只读取已有记录，显示依据和限制，不会改变现场或写入生产数据。",
          modes: ["日常问答", "深入调查"],
          question: "这个周期发生了什么，数据是否完整？",
          standard: ["check_data_quality · 已完成", "get_cycle_trace · 已完成"],
          roles: ["工艺视角：检查过程变化", "质量视角：检查检测结果与样本", "反证视角：寻找遗漏与其他解释"],
          outcome: ["数据质量通过；周期事实链已回链到原始事件。", "形成待工程师确认的候选解释，并同时呈现支持依据、反证和数据限制。"],
          boundary: "深入调查只基于已验证的记录；它给出候选解释，不替代工程师判断，也不控制设备。",
        },
      },
    },
    trust: {
      heading: "让每个回答都有依据",
      lead: "Ingot 不是用猜测回答问题。它把现场记录保留为可核对的事实，让每个结论都能回到来源。",
      cards: [
        ["记录来自现场", "设备、检测和业务系统仍由企业按自己的方式运行；Ingot 接收它们的重要生产记录。"],
        ["结论回到依据", "Chat 的重要数字和结论都会附上对应记录；查不到足够依据时会直接说明。"],
        ["不同来源放在一起", "同一件工件、同一个批次和同一次加工可以串成完整的生产履历，而不是分散在不同系统中。"],
        ["不触碰现场控制", "Ingot 只帮助理解已经发生的事情；它不改变设备、工艺设置、检测结果或业务流程。"],
      ],
    },
    boundary: {
      heading: ["一个标准事件 API，", "适配多种数据来源。"],
      lead:
        "设备、仪器和业务系统由使用方适配。Platform 接收标准 ProductionEvent；Connector Host 可作为用户自管的可选本地入口与 outbox。",
      cards: [
        [
          "INDEPENDENT BY DEFAULT",
          "可选本地事件入口",
          "Connector Host 由使用方部署时可先写入本地 SQLite。outbox 默认最多保留 500,000 条未确认事件；达到上限时丢弃最旧记录并写入 diagnostic.backlog_dropped 和指标。",
        ],
        [
          "OPTIONAL SYSTEM CONNECTION",
          "协议留在适配程序",
          "MES、ERP、设备或仪器协议由使用方处理，Ingot 核心只接收统一事件契约。",
        ],
        [
          "CLEAR SCOPE",
          "边界清晰，实施更轻",
          "Ingot 保存生产事件和检测事实并提供 Platform Web Chat 查询；排产、库存、物流、质量处置和设备控制由现场业务系统负责。",
        ],
      ],
    },
    language: {
      telemetryHeading: ["生产事件回答", "现场记录了什么。"],
      eventHeading: ["Chat 回答", "周期事实是否完整。"],
      nodes: [
        ["SOURCE ADAPTER", "source payload → ProductionEvent"],
        ["PRODUCTION FACTS", "event + subject + context + correlation"],
        ["CENTRAL WEB CHAT", "data quality + cycle trace + evidence"],
      ],
      journey: [
        "使用方适配程序把源数据转换为带时间、来源、对象和上下文的标准生产事件。",
        "CorrelationId 将同一周期的事件组成可排序时间线，Context 保留适配程序提交的业务标识。",
        "Chat 检查周期配对、上下文空缺、来源序号间断和事件新鲜度，并返回证据链接。",
      ],
    },
    planes: {
      heading: ["标准事件与检测事实，", "分别保存、清晰引用。"],
      lead:
        "当前平台保存离散 ProductionEvent 和独立 InspectionRecord。Chat 的周期工具基于生产事件事实工作。",
      telemetryTitle: "适配程序提交了什么",
      telemetryFoot: ["标准事件", "直接批次", "可选本地 outbox"],
      eventTitle: "Platform 保存了什么",
      eventFoot: ["加工事件", "自动检测结果", "人工检测结果"],
    },
    anatomy: {
      heading: "加工开始与结束，是完整的生产事件。",
      lead:
        "只有核心加工使用 cycle.started 与 cycle.completed。上料和机器人记录设备状态，视觉与人工检测保存检测结果，不为每个动作重复增加开始/结束事件。",
      tabsLabel: "加工事件五元组",
      immutable: "不可变",
      fields: [
        ["TYPE", "发生了什么", "cycle.completed"],
        ["TIME", "何时发生", "2026-07-17 14:32:08.429Z"],
        ["SUBJECT", "发生在谁身上", "equipment / POL-03"],
        ["CONTEXT", "当时的业务环境", "lot · tooling · recipe"],
        ["DATA", "这件事的细节", "duration · good_count"],
      ],
    },
    architecture: {
      heading: ["使用方负责数据源协议，", "Ingot 负责标准事实。"],
      lead:
        "使用方适配设备、仪器或业务系统并生成 ProductionEvent。适配程序可直接提交 Platform，或通过可选 Connector Host 使用本地 outbox；Platform 提供事件、检测、Ingot Chat 和 Webhook API。",
      sources: [
        ["设备 / 业务系统", "使用方适配"],
        ["VISION", "结果推送"],
        ["量仪", "串口 / 文件"],
        ["人工检测", "扫码 / 表单"],
      ],
      stream: "设备数据流",
      engine: "可选 Connector Host",
      engineItems: ["本地事件入口", "契约与 Token 校验", "SQLite outbox", "向 Platform 批量上报"],
      status: "用户自管 · 可选",
      retry: "重试 · ACK · 去重",
      consumers: ["INGOT 分析平台", "报表 / BI", "AI / 质量应用", "现有业务系统（可选）"],
    },
    trace: {
      heading: ["按关联 ID，", "还原周期事件时间线。"],
      lead:
        "事件可按对象、上下文、CorrelationId 和时间查询。周期工具按发生时间排序同一 CorrelationId 的事件，并明确标记缺少完成事件等限制。以下内容均为示例事实。",
      filter: "筛选",
      live: "示例追溯",
      events: "7 条 ProductionEvent",
      steps: [
        ["14:02:11", "context.updated", "批次 LOT-0716 · 工件 ING-0718-0127", "LINE-02"],
        ["14:05:43", "context.updated", "模具 MOLD-A17 · 程序 O1207", "POL-03"],
        ["14:06:02", "equipment.state", "夹具已夹紧 · 机床门已关闭", "POL-03"],
        ["14:06:11", "cycle.started", "加工开始 · O1207", "POL-03"],
        ["14:07:04", "equipment.state", "主轴 8,200 rpm · 进给 480 mm/min", "POL-03"],
        ["14:08:09", "cycle.completed", "加工结束 · 118.4 s", "POL-03"],
        ["14:08:15", "equipment.state", "工件已卸载 · 主轴安全位", "POL-03"],
      ],
    },
    edge: {
      heading: ["轻量部署在现场，", "有界保存标准事件。"],
      metrics: [
        "可选本地入口单批上限",
        "默认未确认事件硬上限",
        "现场事件与 outbox 存储",
        "有界离线积压，按 ACK 顺序重试",
      ],
      principles: [
        [
          "SOURCE NEUTRAL",
          "资产与来源彻底分离",
          "一个资产可以关联设备、视觉和业务系统；连接器统一输出标准生产事件。",
        ],
        [
          "CONFIG DRIVEN",
          "事件契约定义生产语言",
          "使用方适配程序识别源系统语义；Platform 对统一事件类型、时间、来源、对象和上下文进行校验。",
        ],
        [
          "OPEN BY DESIGN",
          "按需连接，不设前置条件",
          "统一查询、SSE 实时订阅与 CloudEvents 映射，让报表、AI 与现有业务系统按需接入。",
        ],
      ],
    },
    cta: {
      kicker: "INGOT PLATFORM / INGOT CHAT",
      heading: ["把生产记录变成", "每个人都能追问的事实。"],
      button: "查看文档",
      footer: "Ingot 平台 · Ingot Chat · 有依据的生产调查。",
    },
  },
  en: {
    meta: {
      title: "Ingot — Production facts you can verify, question, and investigate",
      description:
        "Ingot brings important production records together. Engineers use Ingot Chat to ask questions, inspect evidence, and investigate complex problems.",
    },
    languageLabel: "Language",
    nav: {
      label: "Main navigation",
      home: "Ingot home",
      product: "Product",
      data: "Production Records",
      analytics: "Ingot Chat",
      deployment: "Why it is trusted",
      docs: "Docs",
    },
    hero: {
      eyebrow: "INGOT · PRODUCTION INTELLIGENCE",
      heading: ["Production facts you can verify,", "question, and investigate."],
      lead:
        "Ingot brings records from equipment, inspections, and business systems into one traceable production history. Engineers ask questions and inspect evidence in Ingot Chat, then start a multi-perspective investigation when a problem needs a closer look. It never changes equipment or production records.",
      analysis: "Explore Ingot Chat",
      github: "Explore the Ingot platform",
      pause: "Pause animation",
      play: "Resume animation",
      proofLabel: "Product capabilities",
      proof: [
        "Connect production sources",
        "Keep a traceable history",
        "Everyday Ingot Chat",
        "Evidence-led investigation",
      ],
      viewLabel: "3D factory views",
      views: ["Robot loading", "CNC machining", "Robot unloading", "Vision inspection", "Manual inspection"],
      dataKinds: ["Equipment state", "Machining event", "Equipment state", "Inspection result", "Manual inspection record"],
      captureAuto: "SAMPLE PRODUCTION EVENT",
      captureGenerated: "SAMPLE CONTEXT + CORRELATION ID",
      captureHybrid: "SAMPLE INSPECTION RECORD",
    },
    stageData: [
      {
        label: "Robot loading state",
        active: "Pick blank · enter machine",
        activeDetail: "The robot takes a blank from the machine-side tray and inserts horizontally through the open door into the fixed fixture",
        settled: "Part clamped · robot clear",
        settledDetail: "Fixture clamped · robot at safe position · spindle remains at 0 rpm",
      },
      {
        label: "Core CNC machining events",
        active: "Machining started",
        activeDetail: "Part clamped; program O1207 has started",
        settled: "Machining ended",
        settledDetail: "Program O1207 ended normally · 42.8 s · part ING-0718-0127",
      },
      {
        label: "Robot operating state",
        active: "Machine unload · transfer",
        activeDetail: "The spindle is at its safe position and the door is open; the robot inserts horizontally to remove the part from the fixed fixture",
        settled: "Robot at home position",
        settledDetail: "Part at vision station · orientation 90° · gripper released",
      },
      {
        label: "Vision inspection data",
        active: "Cameras acquiring",
        activeDetail: "Part stopped; three cameras are acquiring dimensions and surfaces",
        settled: "Inspection result recorded",
        settledDetail: "Result PASS · score 97.8 · hole pitch 32.047 mm · image IMG-182347",
      },
      {
        label: "Manual inspection record",
        active: "Measuring surface roughness",
        activeDetail: "QE-018 scanned the part and is measuring Ra; an external inspection client submits this sample result to the inspection API",
        settled: "Manual inspection result saved",
        settledDetail: "Surface roughness Ra 0.82 μm · PASS · instrument ROUGHNESS-01 · inspector QE-018",
      },
    ],
    stages: [
      { label: "Robot Loading", detail: "The robot picks from a machine-side tray and inserts horizontally through the open front door into the fixed fixture; machining waits until the robot clears", data: "AUTO · tray present · dual-channel grip · door/spindle interlock · clamp confirmation" },
      { label: "CNC Machining", detail: "Part located; machining program O1207 is running", data: "AUTO · program/tool · spindle/feed · cycle/alarms" },
      { label: "Robot Unloading", detail: "After the spindle retracts and the door opens, the robot inserts horizontally into the fixed fixture, removes the part, and places it at the separate vision infeed", data: "AUTO · spindle safe position · TCP/speed · gripper/stops · place confirmation" },
      { label: "Vision Inspection", detail: "Part stopped; three cameras are acquiring dimensional and surface data", data: "AUTO · measurements · image ID · PASS/FAIL · score" },
      { label: "Manual Inspection", detail: "A sample inspection client records the part, measurement, unit, instrument, and inspector and submits the inspection API", data: "SAMPLE FACT · part ID · roughness Ra · instrument · inspector" },
    ],
    factory: {
      railLabel: "Sample production stages",
      heading: ["Every production run.", "One traceable history."],
      lead:
        "This sample process follows a part from loading through machining and inspection. Ingot brings the important records together, making them easy to review, compare, and investigate.",
      provenanceTitle: "EQUIPMENT, INSPECTION, AND BUSINESS RECORDS BECOME ONE HISTORY",
      provenanceLead:
        "Each record keeps its source, time, subject, and connection to the work. When a question is raised, people can return to the original record.",
      provenance: [
        ["Bring in shop-floor records", "Equipment, instruments, and existing systems can connect to Ingot in a way that fits the plant and preserves how they already work."],
        ["Bring inspection results together", "Automatic and manual inspection results sit beside the part, lot, and production process that give them meaning."],
        ["Keep the evidence", "Every conclusion can link back to the records behind it. When information is incomplete, Ingot says so clearly."],
      ],
    },
    platform: {
      heading: ["From production records", "to investigations people can act on."],
      lead:
        "Ingot is the platform. Ingot Chat is the main place engineers use it: first to understand what happened, then to bring multiple perspectives into a deeper review when needed.",
      workspace: "INGOT SAMPLE PRODUCTION WORKSPACE",
      site: "EAST PLANT / CELL-A",
      connected: "SAMPLE DATA",
      tabsLabel: "Platform capability views",
      tabs: ["Production Overview", "Manual Inspection", "Ingot Chat"],
      overview: "Production Overview",
      viewKickers: ["FACTORY / OVERVIEW", "HUMAN / MANUAL INSPECTION", "CHAT / FACT QUERY"],
      range: "CURRENT SHIFT",
      metrics: [
        ["Stations online", "5 / 5", "All sources healthy"],
        ["Active lot", "LOT-0716", "AL-6061"],
        ["First-pass yield", "99.2%", "+0.4% this shift"],
        ["Average cycle", "47.2s", "Target 50.0s"],
      ],
      lineTitle: "Current production history",
      factsTitle: "Current station record",
      factsHint: "Machining start/end, equipment state, and inspection results use names that match the shop-floor operation",
      envelope: "ENGINEERING FIELDS",
      envelopeShow: "View engineering fields",
      envelopeHide: "Hide engineering fields",
      healthTitle: "Event ingress status",
      health: [
        ["Automatic sources", "5 / 5 online"],
        ["Open inspection tasks", "2"],
        ["Part information complete", "99.8%"],
        ["Missing production records", "0"],
      ],
      dataStatus: "DATA SAVED · ADDED TO PART HISTORY",
      pendingDataStatus: "MANUAL INSPECTION IN PROGRESS · RESULT NOT SAVED",
      status: {
        run: "DATA UPDATING",
        pass: "RECORDED",
        ready: "AWAITING DATA",
        sign: "INSPECTING",
        submitted: "SUBMITTED",
        onDemand: "ON DEMAND",
      },
      review: {
        title: "Sample workflow: machine-side inspection",
        lead: "This sample shows inspection facts an external client can submit: part, outcome, measurement, unit, instrument, inspector, and time. Quality decisions and release remain in the plant's existing workflow.",
        taskLabel: "Current inspection task",
        task: "MI-LOT-0716-001",
        state: "AWAITING MEASUREMENT",
        evidenceTitle: "Inspection requirement",
        evidence: [
          ["Part", "ING-0718-0127"],
          ["Check item", "Surface roughness Ra"],
          ["Upper limit", "1.60 μm"],
          ["Inspection result", "0.82 μm · PASS"],
          ["Instrument and inspector", "ROUGHNESS-01 · QE-018"],
        ],
        stepsTitle: "Shop-floor inspection steps",
        steps: [
          ["01", "Confirm person and part", "The inspector badges in and the fixed scanner reads the part code, preventing a part or lot mix-up."],
          ["02", "Confirm the instrument", "The plant inspection client confirms the tester, calibration state, inspection item, and specification range."],
          ["03", "Perform the measurement", "The inspector places the probe on the specified surface; the client obtains sample value Ra 0.82 μm."],
          ["04", "Submit the fact", "The client verifies the value and outcome and saves the fact through the InspectionRecord API."],
        ],
        boundary: "Ingot stores inspection facts submitted by a client. Identity, instrument connectivity, calibration decisions, rework, scrap, and first-article release remain in the plant's systems.",
        toAnalysis: "Open cycle events and data quality in Chat",
      },
      analysis: {
        title: "Ingot Chat · Everyday questions and deeper investigation",
        lead: "Use everyday questions to establish the facts quickly. For complex issues, bring several professional perspectives together to review the same evidence.",
        chat: {
          title: "Ingot Chat · Sample investigation",
          note: "This uses sample data. Chat reads existing records, shows evidence and limitations, and never changes production records or field equipment.",
          modes: ["Everyday question", "Deeper investigation"],
          question: "What happened in this cycle, and is its data complete?",
          standard: ["check_data_quality · completed", "get_cycle_trace · completed"],
          roles: ["Process view: review changes in the work", "Quality view: review inspection results and samples", "Challenge view: look for gaps and alternative explanations"],
          outcome: ["Data quality passed; cycle evidence links to source events.", "A candidate explanation is ready for an engineer to review, with supporting evidence, challenges, and data limitations."],
          boundary: "The deeper investigation uses only verified records. It offers candidate explanations, not a substitute for engineering judgement or equipment control.",
        },
      },
    },
    trust: {
      heading: "Give every answer a basis",
      lead: "Ingot does not answer from guesswork. It keeps shop-floor records as facts that can be checked, so every important conclusion can lead back to its source.",
      cards: [
        ["Records come from the plant", "Equipment, inspection, and business systems continue to run in the ways a business chooses. Ingot receives their important production records."],
        ["Conclusions lead to evidence", "Important numbers and conclusions in Chat include the records behind them. If there is not enough evidence, Chat says so."],
        ["Different sources, one context", "The workpiece, lot, and production run can be connected into one history instead of being scattered across systems."],
        ["It never touches control", "Ingot helps people understand what already happened. It does not change equipment, process settings, inspection outcomes, or business workflows."],
      ],
    },
    boundary: {
      heading: ["One standard event API.", "Many source boundaries."],
      lead:
        "Teams adapt equipment, instruments, and business systems. Platform accepts standard ProductionEvent records; Connector Host is a team-operated optional local ingress and outbox.",
      cards: [
        [
          "INDEPENDENT BY DEFAULT",
          "Optional local event ingress",
          "When team-operated, Connector Host commits events to local SQLite first. The outbox retains at most 500,000 unacknowledged events by default; at capacity, it drops the oldest records and emits diagnostic.backlog_dropped plus a metric.",
        ],
        [
          "OPTIONAL SYSTEM CONNECTION",
          "Keep protocols in adapters",
          "MES, ERP, equipment, and instrument protocols remain in team-owned adaptation. The Ingot core accepts one normalized event contract.",
        ],
        [
          "CLEAR SCOPE",
          "A clear scope keeps deployment light",
          "Ingot stores production events and inspection facts and exposes Platform Web Chat queries. Plant business systems own scheduling, inventory, logistics, quality disposition, and equipment control.",
        ],
      ],
    },
    language: {
      telemetryHeading: ["Production events show", "what the floor recorded."],
      eventHeading: ["Chat answers", "whether cycle facts are complete."],
      nodes: [
        ["SOURCE ADAPTER", "source payload → ProductionEvent"],
        ["PRODUCTION FACTS", "event + subject + context + correlation"],
        ["CENTRAL WEB CHAT", "data quality + cycle trace + evidence"],
      ],
      journey: [
        "Team-owned adapters translate source data into standard production events with time, source, subject, and context.",
        "CorrelationId groups events into an ordered cycle timeline; Context retains the business identifiers submitted by the adapter.",
        "Chat checks cycle pairing, empty context, source sequence gaps, and freshness and returns evidence links.",
      ],
    },
    planes: {
      heading: ["Standard events and inspection facts.", "Stored separately and cited clearly."],
      lead:
        "The current platform stores discrete ProductionEvent and InspectionRecord facts. Current cycle tools use production-event facts.",
      telemetryTitle: "What did the adapter submit?",
      telemetryFoot: ["Normalized events", "Direct batch", "Optional local outbox"],
      eventTitle: "What did Platform persist?",
      eventFoot: ["Machining events", "Automatic inspection", "Manual inspection"],
    },
    anatomy: {
      heading: "Machining start and end are complete production events.",
      lead:
        "Only core machining uses cycle.started and cycle.completed. Loading and robot motion are equipment state, while vision and manual inspection store inspection results—without adding artificial start/end events to every action.",
      tabsLabel: "Five dimensions of a machining event",
      immutable: "IMMUTABLE",
      fields: [
        ["TYPE", "What happened", "cycle.completed"],
        ["TIME", "When it happened", "2026-07-17 14:32:08.429Z"],
        ["SUBJECT", "The asset involved", "equipment / POL-03"],
        ["CONTEXT", "Business context at the time", "lot · tooling · recipe"],
        ["DATA", "Event details", "duration · good_count"],
      ],
    },
    architecture: {
      heading: ["Teams own source protocols.", "Ingot owns normalized facts."],
      lead:
        "Teams adapt equipment, instruments, or business systems and emit ProductionEvent records. An adapter can submit directly to Platform or use optional Connector Host for a local outbox; Platform provides event, inspection, Ingot Chat, and webhook APIs.",
      sources: [
        ["Equipment / business systems", "Team-owned adaptation"],
        ["VISION", "Result push"],
        ["METROLOGY", "Serial / File"],
        ["HUMAN QA", "Scan / Form"],
      ],
      stream: "EQUIPMENT DATA STREAM",
      engine: "OPTIONAL CONNECTOR HOST",
      engineItems: ["Local Event Ingress", "Contract + Token Validation", "SQLite Outbox", "Batch to Platform"],
      status: "TEAM-OPERATED · OPTIONAL",
      retry: "RETRY · ACK · DEDUPE",
      consumers: ["INGOT ANALYTICS", "REPORTING / BI", "AI / QUALITY APPS", "EXISTING SYSTEMS (OPTIONAL)"],
    },
    trace: {
      heading: ["Use CorrelationId", "to reconstruct a cycle timeline."],
      lead:
        "Query events by subject, context, CorrelationId, or time. The cycle tool orders events sharing one CorrelationId and reports limitations such as a missing completion event. Every record below is a sample fact.",
      filter: "FILTER",
      live: "SAMPLE TRACE",
      events: "7 PRODUCTION EVENTS",
      steps: [
        ["14:02:11", "context.updated", "Lot LOT-0716 · part ING-0718-0127", "LINE-02"],
        ["14:05:43", "context.updated", "Tooling MOLD-A17 · program O1207", "POL-03"],
        ["14:06:02", "equipment.state", "Fixture clamped · machine door closed", "POL-03"],
        ["14:06:11", "cycle.started", "Machining started · O1207", "POL-03"],
        ["14:07:04", "equipment.state", "Spindle 8,200 rpm · feed 480 mm/min", "POL-03"],
        ["14:08:09", "cycle.completed", "Machining ended · 118.4 s", "POL-03"],
        ["14:08:15", "equipment.state", "Part unloaded · spindle at safe position", "POL-03"],
      ],
    },
    edge: {
      heading: ["Lightweight at the edge.", "Bounded persistence for normalized events."],
      metrics: [
        "Optional local-ingress limit per request",
        "Default hard limit for unacknowledged events",
        "Shop-floor event and outbox storage",
        "Bounded offline backlog with ACK-ordered retries",
      ],
      principles: [
        [
          "SOURCE NEUTRAL",
          "Keep assets independent from data sources",
          "An asset can combine equipment, vision, and business-system facts through normalized production events.",
        ],
        [
          "CONFIG DRIVEN",
          "Define production language through the event contract",
          "Team-owned adaptation interprets source semantics. Platform validates normalized event type, time, source, subject, and context.",
        ],
        [
          "OPEN BY DESIGN",
          "Connect by choice, never by prerequisite",
          "Unified queries, live SSE subscriptions, and CloudEvents mappings let reporting, AI, and existing business systems connect when useful.",
        ],
      ],
    },
    cta: {
      kicker: "INGOT PLATFORM / INGOT CHAT",
      heading: ["Turn production records into", "facts that everyone can question."],
      button: "Read the docs",
      footer: "Ingot platform · Ingot Chat · evidence-led production investigation.",
    },
  },
} as const;

function FactoryCanvas({ activeStage, cycleId, locale }: { activeStage: number; cycleId: number; locale: Locale }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const activeStageRef = useRef(activeStage);

  useEffect(() => {
    activeStageRef.current = activeStage;
  }, [activeStage]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const context = canvas.getContext("2d");
    if (!context) return;

    let width = 0;
    let height = 0;
    let animationId = 0;
    const startedAt = performance.now();
    let isVisible = true;
    const reduceMotion = window.matchMedia(
      "(prefers-reduced-motion: reduce)",
    ).matches;

    const stationLabels = ["RAW NEST", "CNC-07", "ROBOT-02", "VISION-03"];
    const motes = Array.from({ length: 48 }, () => ({
      x: Math.random(),
      y: Math.random(),
      r: 0.4 + Math.random() * 1.5,
      phase: Math.random() * Math.PI * 2,
    }));

    const resize = () => {
      const ratio = Math.min(window.devicePixelRatio || 1, 2);
      const rect = canvas.getBoundingClientRect();
      width = rect.width;
      height = rect.height;
      canvas.width = Math.round(width * ratio);
      canvas.height = Math.round(height * ratio);
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
      if (reduceMotion) window.requestAnimationFrame(() => draw(performance.now()));
    };

    const roundedRect = (
      x: number,
      y: number,
      rectWidth: number,
      rectHeight: number,
      radius: number,
    ) => {
      const r = Math.min(radius, rectWidth / 2, rectHeight / 2);
      context.beginPath();
      context.moveTo(x + r, y);
      context.lineTo(x + rectWidth - r, y);
      context.quadraticCurveTo(x + rectWidth, y, x + rectWidth, y + r);
      context.lineTo(x + rectWidth, y + rectHeight - r);
      context.quadraticCurveTo(
        x + rectWidth,
        y + rectHeight,
        x + rectWidth - r,
        y + rectHeight,
      );
      context.lineTo(x + r, y + rectHeight);
      context.quadraticCurveTo(x, y + rectHeight, x, y + rectHeight - r);
      context.lineTo(x, y + r);
      context.quadraticCurveTo(x, y, x + r, y);
      context.closePath();
    };

    const drawLabel = (text: string, x: number, y: number, active: boolean) => {
      context.font = "600 9px SFMono-Regular, Consolas, monospace";
      context.textAlign = "center";
      context.fillStyle = active ? "rgba(245, 203, 115, .95)" : "rgba(168, 173, 163, .5)";
      context.fillText(text, x, y);
    };

    const drawFeeder = (x: number, y: number, scale: number, active: boolean, t: number) => {
      context.save();
      context.translate(x, y);
      context.scale(scale, scale);
      context.strokeStyle = active ? "rgba(242, 205, 121, .92)" : "rgba(154, 163, 154, .42)";
      context.fillStyle = "rgba(28, 32, 28, .96)";
      context.lineWidth = 1.4;
      context.beginPath();
      context.moveTo(-34, -88);
      context.lineTo(34, -88);
      context.lineTo(22, -38);
      context.lineTo(-22, -38);
      context.closePath();
      context.fill();
      context.stroke();
      roundedRect(-28, -38, 56, 42, 4);
      context.fill();
      context.stroke();
      context.fillStyle = active ? "rgba(112, 217, 208, .82)" : "rgba(112, 217, 208, .28)";
      for (let i = 0; i < 5; i += 1) {
        const px = -21 + i * 10;
        const py = -71 + Math.sin(t * 2.2 + i) * 3;
        context.fillRect(px, py, 5, 5);
      }
      context.restore();
    };

    const drawCnc = (x: number, y: number, scale: number, active: boolean, t: number) => {
      context.save();
      context.translate(x, y);
      context.scale(scale, scale);
      context.fillStyle = "rgba(24, 28, 25, .98)";
      context.strokeStyle = active ? "rgba(242, 205, 121, .95)" : "rgba(164, 170, 161, .45)";
      context.lineWidth = 1.4;
      roundedRect(-46, -104, 92, 108, 7);
      context.fill();
      context.stroke();
      context.fillStyle = "rgba(5, 10, 10, .9)";
      roundedRect(-31, -83, 62, 49, 3);
      context.fill();
      context.strokeStyle = "rgba(112, 217, 208, .35)";
      context.stroke();
      context.strokeStyle = active ? "rgba(112, 217, 208, .95)" : "rgba(112, 217, 208, .32)";
      context.beginPath();
      context.moveTo(0, -78);
      context.lineTo(0, -55);
      context.stroke();
      context.save();
      context.translate(0, -50);
      context.rotate(t * 5);
      context.beginPath();
      context.moveTo(-12, 0);
      context.lineTo(12, 0);
      context.moveTo(0, -12);
      context.lineTo(0, 12);
      context.stroke();
      context.restore();
      context.fillStyle = active ? "rgba(221, 168, 74, .85)" : "rgba(221, 168, 74, .25)";
      context.fillRect(-27, -20, 54, 6);
      context.restore();
    };

    const drawRobot = (x: number, y: number, scale: number, active: boolean, t: number) => {
      context.save();
      context.translate(x, y);
      context.scale(scale, scale);
      const a1 = -1.12 + Math.sin(t * 1.35) * 0.2;
      const a2 = 0.9 + Math.sin(t * 1.35 + 1.2) * 0.3;
      const p1 = { x: Math.cos(a1) * 49, y: -26 + Math.sin(a1) * 49 };
      const p2 = {
        x: p1.x + Math.cos(a1 + a2) * 42,
        y: p1.y + Math.sin(a1 + a2) * 42,
      };
      context.lineCap = "round";
      context.lineWidth = 14;
      context.strokeStyle = active ? "rgba(229, 171, 66, .96)" : "rgba(127, 100, 48, .72)";
      context.beginPath();
      context.moveTo(0, -26);
      context.lineTo(p1.x, p1.y);
      context.lineTo(p2.x, p2.y);
      context.stroke();
      context.lineWidth = 2;
      context.strokeStyle = "rgba(255, 222, 151, .7)";
      for (const point of [{ x: 0, y: -26 }, p1, p2]) {
        context.beginPath();
        context.arc(point.x, point.y, 8, 0, Math.PI * 2);
        context.fillStyle = "rgba(24, 25, 20, 1)";
        context.fill();
        context.stroke();
      }
      context.fillStyle = "rgba(29, 31, 26, 1)";
      roundedRect(-26, -19, 52, 23, 4);
      context.fill();
      context.stroke();
      context.beginPath();
      context.moveTo(p2.x - 8, p2.y + 3);
      context.lineTo(p2.x - 13, p2.y + 14);
      context.moveTo(p2.x + 8, p2.y + 3);
      context.lineTo(p2.x + 13, p2.y + 14);
      context.stroke();
      context.restore();
    };

    const drawVision = (x: number, y: number, scale: number, active: boolean, t: number) => {
      context.save();
      context.translate(x, y);
      context.scale(scale, scale);
      context.strokeStyle = active ? "rgba(112, 217, 208, .95)" : "rgba(112, 217, 208, .38)";
      context.fillStyle = "rgba(21, 27, 26, .96)";
      context.lineWidth = 1.5;
      context.fillRect(-38, -87, 12, 91);
      context.fillRect(26, -87, 12, 91);
      roundedRect(-42, -101, 84, 20, 4);
      context.fill();
      context.stroke();
      context.fillStyle = active ? "rgba(112, 217, 208, .9)" : "rgba(112, 217, 208, .3)";
      context.beginPath();
      context.arc(0, -91, 5, 0, Math.PI * 2);
      context.fill();
      const scanY = -72 + ((t * 28) % 62);
      const beam = context.createLinearGradient(-25, scanY, 25, scanY);
      beam.addColorStop(0, "rgba(112, 217, 208, 0)");
      beam.addColorStop(0.5, active ? "rgba(112, 217, 208, .9)" : "rgba(112, 217, 208, .2)");
      beam.addColorStop(1, "rgba(112, 217, 208, 0)");
      context.fillStyle = beam;
      context.fillRect(-25, scanY, 50, 2);
      context.restore();
    };

    const drawManualQa = (x: number, y: number, scale: number, active: boolean, t: number) => {
      context.save();
      context.translate(x, y);
      context.scale(scale, scale);

      const screenGlow = active ? 0.76 + Math.sin(t * 4.2) * 0.12 : 0.28;
      context.fillStyle = "rgba(22, 27, 24, .98)";
      context.strokeStyle = active ? "rgba(242, 205, 121, .92)" : "rgba(146, 154, 146, .42)";
      context.lineWidth = 1.4;
      roundedRect(-68, -126, 58, 58, 4);
      context.fill();
      context.stroke();
      context.fillStyle = `rgba(112, 217, 208, ${screenGlow * 0.16})`;
      context.fillRect(-61, -118, 44, 39);
      context.strokeStyle = `rgba(112, 217, 208, ${screenGlow})`;
      context.strokeRect(-61, -118, 44, 39);
      context.fillStyle = `rgba(168, 223, 189, ${screenGlow})`;
      context.fillRect(-56, -110, 31, 2);
      context.fillRect(-56, -103, 23, 2);
      context.fillStyle = active ? "rgba(242, 205, 121, .9)" : "rgba(242, 205, 121, .28)";
      context.fillRect(-56, -90, active ? 28 + Math.sin(t * 2.1) * 5 : 15, 3);
      context.strokeStyle = "rgba(148, 155, 146, .46)";
      context.beginPath();
      context.moveTo(-39, -68);
      context.lineTo(-39, -29);
      context.moveTo(-57, -29);
      context.lineTo(-21, -29);
      context.stroke();

      const gesture = active ? Math.max(0, Math.sin(t * 1.8)) : 0;
      context.fillStyle = "rgba(205, 178, 142, .9)";
      context.beginPath();
      context.arc(26, -105, 10, 0, Math.PI * 2);
      context.fill();
      context.strokeStyle = active ? "rgba(242, 205, 121, .78)" : "rgba(136, 142, 134, .5)";
      context.fillStyle = "rgba(38, 47, 43, .98)";
      roundedRect(14, -92, 25, 50, 7);
      context.fill();
      context.stroke();
      context.lineCap = "round";
      context.lineWidth = 6;
      context.beginPath();
      context.moveTo(17, -79);
      context.lineTo(-5 - gesture * 9, -91 - gesture * 4);
      context.stroke();
      context.lineWidth = 5;
      context.beginPath();
      context.moveTo(20, -43);
      context.lineTo(16, -8);
      context.moveTo(34, -43);
      context.lineTo(39, -8);
      context.stroke();

      context.textAlign = "center";
      context.font = "600 8px SFMono-Regular, Consolas, monospace";
      context.fillStyle = active ? "rgba(242, 205, 121, .94)" : "rgba(156, 163, 154, .48)";
      context.fillText("QA-HMI-01", -8, 8);
      context.font = "500 6px SFMono-Regular, Consolas, monospace";
      context.fillStyle = active ? "rgba(168, 223, 189, .88)" : "rgba(135, 145, 136, .42)";
      context.fillText(
        locale === "zh" ? "扫码工件 · 人工检测" : "PART SCAN · MANUAL INSPECTION",
        -8,
        19,
      );
      context.restore();
    };

    const draw = (now: number) => {
      context.clearRect(0, 0, width, height);
      const t = reduceMotion ? 0 : (now - startedAt) / 1000;
      const active = activeStageRef.current;
      animationId = 0;
      const sceneBlend = Math.max(0, Math.min(1, (width - 840) / 420));
      const sceneLeft = width * (0.08 + 0.325 * sceneBlend);
      const sceneRight = Math.min(
        width * (0.92 + 0.045 * sceneBlend),
        sceneLeft + Math.min(1000, width * 0.84),
      );
      const sceneWidth = sceneRight - sceneLeft;
      const baseY = height * 0.69;
      const scale = Math.max(0.62, Math.min(1.08, sceneWidth / 760));
      const physicalActive = active < 4 ? active : -1;
      const manualActive = active === 4;
      const qaX = sceneLeft + sceneWidth * 0.58;
      const qaY = baseY + 128 * scale;
      const qaDataY = qaY - 106 * scale;

      const background = context.createLinearGradient(0, 0, 0, height);
      background.addColorStop(0, "#080a08");
      background.addColorStop(0.55, "#0b0e0b");
      background.addColorStop(1, "#050605");
      context.fillStyle = background;
      context.fillRect(0, 0, width, height);

      const ambient = context.createRadialGradient(
        width * 0.76,
        height * 0.34,
        0,
        width * 0.76,
        height * 0.34,
        Math.max(width, height) * 0.55,
      );
      ambient.addColorStop(0, "rgba(202, 137, 42, .12)");
      ambient.addColorStop(0.35, "rgba(26, 66, 62, .08)");
      ambient.addColorStop(1, "rgba(0, 0, 0, 0)");
      context.fillStyle = ambient;
      context.fillRect(0, 0, width, height);

      motes.forEach((mote) => {
        const px = sceneLeft + mote.x * sceneWidth;
        const py = height * 0.13 + mote.y * height * 0.58 + Math.sin(t * 0.4 + mote.phase) * 5;
        context.fillStyle = `rgba(112, 217, 208, ${0.05 + mote.r * 0.035})`;
        context.beginPath();
        context.arc(px, py, mote.r, 0, Math.PI * 2);
        context.fill();
      });

      const vanishX = width * 0.73;
      const horizonY = height * 0.37;
      context.lineWidth = 0.7;
      context.strokeStyle = "rgba(171, 177, 166, .085)";
      for (let i = -8; i <= 8; i += 1) {
        context.beginPath();
        context.moveTo(vanishX, horizonY);
        context.lineTo(vanishX + i * width * 0.105, height);
        context.stroke();
      }
      for (let i = 0; i < 11; i += 1) {
        const p = i / 10;
        const gy = horizonY + Math.pow(p, 1.75) * (height - horizonY);
        context.beginPath();
        context.moveTo(sceneLeft * 0.82, gy);
        context.lineTo(width, gy);
        context.stroke();
      }

      const hubX = width * 0.79;
      const hubY = height * 0.235;
      const activeX = manualActive ? qaX : sceneLeft + (sceneWidth * Math.max(physicalActive, 0)) / 4;
      const activeY = manualActive ? qaDataY : baseY - 112 * scale;
      const pulse = 0.5 + Math.sin(t * 3.2) * 0.5;
      const hubGlow = context.createRadialGradient(hubX, hubY, 0, hubX, hubY, 110 + pulse * 18);
      hubGlow.addColorStop(0, "rgba(244, 196, 96, .28)");
      hubGlow.addColorStop(0.32, "rgba(221, 168, 74, .08)");
      hubGlow.addColorStop(1, "rgba(221, 168, 74, 0)");
      context.fillStyle = hubGlow;
      context.fillRect(hubX - 150, hubY - 150, 300, 300);

      context.strokeStyle = "rgba(112, 217, 208, .22)";
      context.lineWidth = 1;
      for (let i = 0; i < 5; i += 1) {
        const stationX = sceneLeft + (sceneWidth * i) / 4;
        context.beginPath();
        context.moveTo(stationX, baseY - 112 * scale);
        context.bezierCurveTo(stationX, hubY + 80, hubX - 80, hubY + 60, hubX, hubY);
        context.stroke();
      }
      context.beginPath();
      context.moveTo(qaX, qaDataY);
      context.bezierCurveTo(qaX, hubY + 112, hubX - 42, hubY + 78, hubX, hubY);
      context.stroke();
      context.strokeStyle = "rgba(242, 205, 121, .68)";
      context.beginPath();
      context.moveTo(activeX, activeY);
      context.bezierCurveTo(activeX, hubY + 70, hubX - 65, hubY + 45, hubX, hubY);
      context.stroke();
      for (let i = 0; i < 9; i += 1) {
        const p = (t * 0.32 + i / 9) % 1;
        const inv = 1 - p;
        const bx =
          inv * inv * inv * activeX +
          3 * inv * inv * p * activeX +
          3 * inv * p * p * (hubX - 65) +
          p * p * p * hubX;
        const by =
          inv * inv * inv * activeY +
          3 * inv * inv * p * (hubY + 70) +
          3 * inv * p * p * (hubY + 45) +
          p * p * p * hubY;
        context.fillStyle = i % 3 === 0 ? "rgba(242, 205, 121, .95)" : "rgba(112, 217, 208, .7)";
        context.beginPath();
        context.arc(bx, by, i % 3 === 0 ? 2.2 : 1.3, 0, Math.PI * 2);
        context.fill();
      }

      context.save();
      context.translate(hubX, hubY);
      const hubSize = Math.max(26, Math.min(42, width * 0.027));
      context.beginPath();
      for (let i = 0; i < 6; i += 1) {
        const angle = -Math.PI / 2 + (i * Math.PI) / 3;
        const px = Math.cos(angle) * hubSize;
        const py = Math.sin(angle) * hubSize;
        if (i === 0) context.moveTo(px, py);
        else context.lineTo(px, py);
      }
      context.closePath();
      context.fillStyle = "rgba(13, 13, 9, .96)";
      context.fill();
      context.lineWidth = 2;
      context.strokeStyle = "rgba(242, 205, 121, .9)";
      context.stroke();
      const logoScale = hubSize / 42;
      context.scale(logoScale, logoScale);
      context.fillStyle = "#f5b93e";
      context.beginPath();
      context.moveTo(-8, -16);
      context.lineTo(8, -16);
      context.lineTo(11, -3);
      context.lineTo(-11, -3);
      context.closePath();
      context.fill();
      context.fillStyle = "#526476";
      context.beginPath();
      context.moveTo(-18, 1);
      context.lineTo(-2, 1);
      context.lineTo(1, 14);
      context.lineTo(-21, 14);
      context.closePath();
      context.fill();
      context.beginPath();
      context.moveTo(2, 1);
      context.lineTo(18, 1);
      context.lineTo(21, 14);
      context.lineTo(-1, 14);
      context.closePath();
      context.fill();
      context.restore();
      context.font = "600 8px SFMono-Regular, Consolas, monospace";
      context.textAlign = "center";
      context.fillStyle = "rgba(242, 205, 121, .72)";
      context.fillText("INGOT DATA CORE", hubX, hubY + hubSize + 19);

      const beltGradient = context.createLinearGradient(sceneLeft, 0, sceneRight, 0);
      beltGradient.addColorStop(0, "rgba(76, 83, 77, .5)");
      beltGradient.addColorStop(0.5, "rgba(118, 123, 113, .78)");
      beltGradient.addColorStop(1, "rgba(76, 83, 77, .5)");
      context.fillStyle = "rgba(17, 20, 17, .96)";
      roundedRect(sceneLeft - 28, baseY - 9, sceneWidth + 56, 30, 7);
      context.fill();
      context.strokeStyle = beltGradient;
      context.stroke();
      context.fillStyle = beltGradient;
      for (let x = sceneLeft - 14; x < sceneRight + 24; x += 24) {
        context.beginPath();
        context.arc(x, baseY + 6, 5, 0, Math.PI * 2);
        context.fill();
      }

      for (let i = 0; i < 6; i += 1) {
        const progress = (t / 13 + i / 6) % 1;
        const partX = sceneLeft - 12 + progress * (sceneWidth + 24);
        const highlightedPosition = manualActive ? 3 : physicalActive;
        const isHot = Math.abs(progress * 3 - highlightedPosition) < 0.42;
        context.shadowColor = isHot ? "rgba(242, 205, 121, .8)" : "transparent";
        context.shadowBlur = isHot ? 12 : 0;
        context.fillStyle = isHot ? "rgba(232, 178, 74, .95)" : "rgba(176, 184, 176, .76)";
        roundedRect(partX - 10, baseY - 17, 20, 13, 3);
        context.fill();
        context.shadowBlur = 0;
      }

      const stationXs = Array.from({ length: 4 }, (_, index) => sceneLeft + (sceneWidth * index) / 3);
      const drawStation = [drawFeeder, drawCnc, drawRobot, drawVision];
      stationXs.forEach((stationX, index) => {
        const isActive = index === physicalActive;
        if (isActive) {
          const stationGlow = context.createRadialGradient(
            stationX,
            baseY - 55,
            0,
            stationX,
            baseY - 55,
            95 * scale,
          );
          stationGlow.addColorStop(0, "rgba(221, 168, 74, .18)");
          stationGlow.addColorStop(1, "rgba(221, 168, 74, 0)");
          context.fillStyle = stationGlow;
          context.fillRect(stationX - 110, baseY - 170, 220, 190);
        }
        drawStation[index](stationX, baseY - 21, scale, isActive, isActive ? t : 0);
        drawLabel(stationLabels[index], stationX, baseY + 48, isActive);
      });
      drawManualQa(qaX, qaY, Math.max(0.58, scale * 0.76), manualActive, manualActive ? t : 0);

      context.textAlign = "left";
      context.font = "500 8px SFMono-Regular, Consolas, monospace";
      context.fillStyle = "rgba(163, 169, 159, .42)";
      context.fillText(
        locale === "zh" ? "生产线 / CELL-A" : "PRODUCTION LINE / CELL-A",
        sceneLeft - 24,
        baseY + 76,
      );
      context.textAlign = "right";
      context.fillText(
        `${locale === "zh" ? "循环周期" : "LOOP CYCLE"} 02:14.018`,
        sceneRight + 24,
        baseY + 76,
      );
      context.shadowBlur = 0;
      if (!reduceMotion && isVisible && document.visibilityState === "visible") {
        animationId = window.requestAnimationFrame(draw);
      }
    };

    const observer = new IntersectionObserver(
      ([entry]) => {
        isVisible = entry.isIntersecting;
        if (!isVisible && animationId) {
          window.cancelAnimationFrame(animationId);
          animationId = 0;
        } else if (isVisible && !reduceMotion && !animationId) {
          animationId = window.requestAnimationFrame(draw);
        }
      },
      { threshold: 0.02 },
    );

    const handleVisibility = () => {
      if (document.visibilityState === "visible" && isVisible && !reduceMotion && !animationId) {
        animationId = window.requestAnimationFrame(draw);
      } else if (document.visibilityState !== "visible" && animationId) {
        window.cancelAnimationFrame(animationId);
        animationId = 0;
      }
    };

    resize();
    window.addEventListener("resize", resize);
    document.addEventListener("visibilitychange", handleVisibility);
    observer.observe(canvas);
    if (reduceMotion) draw(performance.now());
    else animationId = window.requestAnimationFrame(draw);
    return () => {
      window.cancelAnimationFrame(animationId);
      window.removeEventListener("resize", resize);
      document.removeEventListener("visibilitychange", handleVisibility);
      observer.disconnect();
    };
  }, [cycleId, locale]);

  return <canvas ref={canvasRef} className="factory-canvas" aria-hidden="true" />;
}

function BrandMark() {
  return (
    <Image
      className="brand-mark"
      src="/brand/ingot-mark-dark.svg"
      width={32}
      height={32}
      alt=""
      aria-hidden="true"
      unoptimized
    />
  );
}

function BrandLockup() {
  return (
    <Image
      className="brand-lockup"
      src="/brand/ingot-lockup-dark.svg"
      width={94}
      height={35}
      alt=""
      aria-hidden="true"
      unoptimized
    />
  );
}

function CaptureBadges({
  capture,
  autoLabel,
  generatedLabel,
  hybridLabel,
}: {
  capture: "automatic" | "hybrid";
  autoLabel: string;
  generatedLabel: string;
  hybridLabel: string;
}) {
  return (
    <span className="capture-badges">
      <span className={`capture-badge ${capture}`}>
        {capture === "hybrid" ? hybridLabel : autoLabel}
      </span>
      <span className="capture-badge generated">{generatedLabel}</span>
    </span>
  );
}

export default function IngotSite({ initialLocale = "zh" }: { initialLocale?: Locale }) {
  const [liveIndex, setLiveIndex] = useState(0);
  const [stageRevision, setStageRevision] = useState(0);
  const [settledRevision, setSettledRevision] = useState<number | null>(null);
  const [activeAnatomy, setActiveAnatomy] = useState(0);
  const [locale] = useState<Locale>(initialLocale);
  const [platformFocus, setPlatformFocus] = useState<number | null>(null);
  const [platformView, setPlatformView] = useState<PlatformView>("overview");
  const [chatMode, setChatMode] = useState(0);
  const [showEnvelope, setShowEnvelope] = useState(false);
  const [factoryPlaying, setFactoryPlaying] = useState(true);

  const copy = messages[locale];
  const stageStream = stageMeta.map((stage, index) => ({
    ...stage,
    ...copy.stages[index],
  }));
  const anatomy = copy.anatomy.fields;
  const traceSteps = copy.trace.steps;

  useEffect(() => {
    const frame = window.requestAnimationFrame(() => {
      if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) setFactoryPlaying(false);
    });
    return () => window.cancelAnimationFrame(frame);
  }, []);

  useEffect(() => {
    document.documentElement.lang = locale === "zh" ? "zh-CN" : "en";
    document.title = copy.meta.title;
    const description = document.querySelector<HTMLMetaElement>('meta[name="description"]');
    if (description) description.content = copy.meta.description;
  }, [copy.meta.description, copy.meta.title, locale]);

  useEffect(() => {
    if (!factoryPlaying || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const timeout = window.setTimeout(() => {
      setLiveIndex((liveIndex + 1) % stageMeta.length);
      setStageRevision((current) => current + 1);
    }, FACTORY_STAGE_MS);
    return () => window.clearTimeout(timeout);
  }, [stageRevision, factoryPlaying, liveIndex]);

  useEffect(() => {
    if (!factoryPlaying || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const stageData = FACTORY_STAGE_DATA[liveIndex] ?? FACTORY_STAGE_DATA[0];
    const timeout = window.setTimeout(() => {
      setSettledRevision(stageRevision);
    }, FACTORY_STAGE_MS * stageData.settleAt);
    return () => window.clearTimeout(timeout);
  }, [stageRevision, factoryPlaying, liveIndex]);

  const stageSettled = settledRevision === stageRevision;
  const resolveStageDatum = (index: number, settled: boolean): ResolvedStageDatum => {
    const stage = stageStream[index] ?? stageStream[0];
    const stageData = FACTORY_STAGE_DATA[index] ?? FACTORY_STAGE_DATA[0];
    const labels = copy.stageData[index] ?? copy.stageData[0];
    const datum = stageData.kind === "machining"
      ? settled ? stageData.completed : stageData.started
      : settled ? stageData.settled : stageData.active;
    return {
      ...stage,
      ...datum,
      detail: settled ? labels.settledDetail : labels.activeDetail,
      dataLabel: settled ? labels.settled : labels.active,
      stored: !(index === 4 && !settled),
    };
  };
  const liveDatum = resolveStageDatum(liveIndex, stageSettled);
  const liveStageData = FACTORY_STAGE_DATA[liveIndex] ?? FACTORY_STAGE_DATA[0];
  const liveDataCopy = copy.stageData[liveIndex] ?? copy.stageData[0];
  const factoryView = factoryViews[liveIndex];
  const platformDatum = platformFocus === null || platformFocus === liveIndex
    ? liveDatum
    : resolveStageDatum(platformFocus, true);
  const liveDatumData = liveDatum.data;
  const platformDatumData = platformDatum.data;
  const platformCorrelation = platformDatum.correlationId;
  const platformViewIndex = platformView === "overview" ? 0 : platformView === "review" ? 1 : 2;
  const stageDataTypes = FACTORY_STAGE_DATA.map((stageData) =>
    stageData.kind === "machining"
      ? [stageData.started.eventType, stageData.completed.eventType]
      : Array.from(new Set([stageData.active.recordType, stageData.settled.recordType])),
  );

  const selectLiveStage = (index: number) => {
    setLiveIndex(index);
    setStageRevision((current) => current + 1);
    setFactoryPlaying(false);
  };

  const selectPlatformView = (view: PlatformView) => {
    setPlatformView(view);
    if (view !== "overview") setShowEnvelope(false);
  };

  return (
    <main lang={locale === "zh" ? "zh-CN" : "en"}>
      <nav className="nav shell" aria-label={copy.nav.label}>
        <a className="brand" href="#top" aria-label={copy.nav.home}>
          <BrandLockup />
        </a>
        <div className="nav-links">
          <a href="#top">{copy.nav.product}</a>
          <a href="#factory">{copy.nav.data}</a>
          <a href="#platform" onClick={() => selectPlatformView("analysis")}>{copy.nav.analytics}</a>
          <a href="#trust">{copy.nav.deployment}</a>
          <a href="https://docs.ingotstack.com" target="_blank" rel="noreferrer">{copy.nav.docs}</a>
        </div>
        <div className="nav-actions">
          <div className="language-switch" aria-label={copy.languageLabel}>
            <Link
              className={locale === "zh" ? "active" : ""}
              aria-current={locale === "zh" ? "page" : undefined}
              href="/"
            >
              中
            </Link>
            <Link
              className={locale === "en" ? "active" : ""}
              aria-current={locale === "en" ? "page" : undefined}
              href="/en/"
            >
              EN
            </Link>
          </div>
          <a
            className="nav-cta"
            href="https://github.com/liuweichaox/Ingot"
            target="_blank"
            rel="noreferrer"
          >
            GitHub <span>↗</span>
          </a>
        </div>
      </nav>

      <section className="factory-hero" id="top">
        <FactoryScene3D
          activeStage={liveIndex}
          stageSettled={stageSettled}
          stageRevision={stageRevision}
          paused={!factoryPlaying}
          view={factoryView}
          fallback={<FactoryCanvas activeStage={liveIndex} cycleId={stageRevision} locale={locale} />}
        />
        <div className="factory-haze" aria-hidden="true" />
        <div className="factory-scan" aria-hidden="true" />

        <div className="factory-view-switch" role="group" aria-label={copy.hero.viewLabel}>
          <div className="factory-view-toolbar">
            <span>3D CAMERA</span>
            <button
              type="button"
              className="factory-playback"
              aria-pressed={!factoryPlaying}
              onClick={() => setFactoryPlaying((current) => !current)}
            >
              <i aria-hidden="true">{factoryPlaying ? "Ⅱ" : "▶"}</i>
              {factoryPlaying ? copy.hero.pause : copy.hero.play}
            </button>
          </div>
          <div>
            {factoryViews.map((view, index) => (
              <button
                type="button"
                className={liveIndex === index ? "active" : ""}
                aria-pressed={liveIndex === index}
                onClick={() => selectLiveStage(index)}
                key={view}
              >
                <i>0{index + 1}</i>
                {copy.hero.views[index]}
              </button>
            ))}
          </div>
        </div>

        <div className="factory-hero-shell shell">
          <div className="hero-copy factory-copy">
            <div className="eyebrow">
              <span className="live-dot" />
              {copy.hero.eyebrow}
            </div>
            <h1>
              {copy.hero.heading[0]}
              <br />
              <span>{copy.hero.heading[1]}</span>
            </h1>
            <p className="hero-lead">{copy.hero.lead}</p>
            <div className="hero-actions">
              <a
                className="button button-primary"
                href="#platform"
                onClick={() => selectPlatformView("analysis")}
              >
                {copy.hero.analysis} <span>↓</span>
              </a>
              <a
                className="button button-ghost"
                href={`https://docs.ingotstack.com/${locale}/rfc-production-events`}
                target="_blank"
                rel="noreferrer"
              >
                {copy.hero.github} <span>↗</span>
              </a>
            </div>
            <div className="hero-proof" aria-label={copy.hero.proofLabel}>
              {copy.hero.proof.map((item) => (
                <span key={item}>{item}</span>
              ))}
            </div>
          </div>

          <aside
            className="factory-live-card"
            key={`${liveDatum.recordName}-${liveIndex}-${stageRevision}`}
            aria-live="polite"
          >
            <div className="factory-card-head">
              <span className={`event-pip ${liveDatum.tone}`} />
              <span>{liveStageData.kind === "machining" ? (locale === "zh" ? "加工事件" : "MACHINING EVENT") : (locale === "zh" ? "示例事实" : "SAMPLE FACT")}</span>
              <span className="factory-card-sync">↔ CAM 0{liveIndex + 1}</span>
              <time>{liveDatum.time}</time>
            </div>
            <div className="factory-card-machine">
              <span>
                {liveDatum.label}
                <small>{liveDatum.dataLabel}</small>
              </span>
              <strong>{liveDatum.code}</strong>
            </div>
            <CaptureBadges
              capture={liveDatum.capture}
              autoLabel={copy.hero.captureAuto}
              generatedLabel={copy.hero.captureGenerated}
              hybridLabel={copy.hero.captureHybrid}
            />
            {liveStageData.kind === "machining" ? (
              <ol className="factory-event-pair" aria-label={liveDataCopy.label}>
                <li className={stageSettled ? "done" : "active"}>
                  <span><i />{liveDataCopy.active}</span>
                  <code>{liveStageData.started.eventType}</code>
                  <time>{liveStageData.started.time}</time>
                </li>
                <li className={stageSettled ? "active" : "pending"}>
                  <span><i />{liveDataCopy.settled}</span>
                  <code>{liveStageData.completed.eventType}</code>
                  <time>{stageSettled ? liveStageData.completed.time : "—"}</time>
                </li>
              </ol>
            ) : (
              <ol className="factory-event-pair point-event" aria-label={liveDataCopy.label}>
                <li className="active">
                  <span><i />{liveDatum.dataLabel}</span>
                  <code>{copy.hero.dataKinds[liveIndex]}</code>
                  <time>{liveDatum.time}</time>
                </li>
              </ol>
            )}
            <code>{liveDatum.eventType ?? copy.hero.dataKinds[liveIndex]}</code>
            <p>{liveDatum.detail}</p>
            {liveDatumData && <small className="factory-event-payload">{liveDatumData}</small>}
            <div className="factory-card-foot">
              <span>{liveDatum.context}</span>
              <span>{liveDatum.stored ? `#${String(liveDatum.sequence).padStart(6, "0")}` : (locale === "zh" ? "尚未保存" : "NOT SAVED")}</span>
            </div>
          </aside>
        </div>

        <ol className="factory-stage-rail shell" aria-label={copy.factory.railLabel}>
          {stageStream.map((stage, index) => (
            <li
              className={liveIndex === index ? "active" : ""}
              key={`${stage.code}-${liveIndex === index ? stageRevision : "idle"}`}
            >
              <button type="button" onClick={() => selectLiveStage(index)} aria-pressed={liveIndex === index}>
                <span>0{index + 1}</span>
                <div>
                  <strong>{stage.label}</strong>
                  <small>{stage.code}</small>
                </div>
              </button>
              <i aria-hidden="true" style={{ animationDuration: `${FACTORY_STAGE_MS}ms` }} />
            </li>
          ))}
        </ol>
      </section>

      <section className="factory-loop-section shell reveal-section" id="factory">
        <div className="factory-loop-intro">
          <div>
            <p className="section-index">01 / THE RUNNING FACTORY</p>
            <h2>
              {copy.factory.heading[0]}
              <br />
              <span>{copy.factory.heading[1]}</span>
            </h2>
          </div>
          <p>{copy.factory.lead}</p>
        </div>

        <div className="capture-model" aria-labelledby="capture-model-title">
          <header>
            <p className="section-index" id="capture-model-title">DATA PROVENANCE</p>
            <strong>{copy.factory.provenanceTitle}</strong>
            <span>{copy.factory.provenanceLead}</span>
          </header>
          <div>
            {copy.factory.provenance.map(([label, detail], index) => (
              <article className={`capture-model-${index + 1}`} key={label}>
                <span>0{index + 1}</span>
                <strong>{label}</strong>
                <p>{detail}</p>
              </article>
            ))}
          </div>
        </div>

        <div className="factory-flow-cards">
          {stageStream.map((stage, index) => (
            <article className={liveIndex === index ? "active" : ""} key={stage.code}>
              <div className="factory-node-head">
                <span>0{index + 1}</span>
                <small>{stage.source}</small>
              </div>
              <div className={`factory-machine-icon machine-${index + 1}`} aria-hidden="true">
                <i />
                <i />
                <i />
              </div>
              <CaptureBadges
                capture={stage.capture}
                autoLabel={copy.hero.captureAuto}
                generatedLabel={copy.hero.captureGenerated}
                hybridLabel={copy.hero.captureHybrid}
              />
              <strong>{stage.label}</strong>
              <b>{stage.code}</b>
              <div className="factory-card-event-types">
                {index === 1
                  ? stageDataTypes[index].map((type) => <code key={type}>{type}</code>)
                  : <code>{copy.hero.dataKinds[index]}</code>}
              </div>
              <p>{stage.detail}</p>
              <small className={`factory-data-fields ${stage.capture}`}>{stage.data}</small>
              <div className="factory-node-link" aria-hidden="true">
                <span />
                {index < stageStream.length - 1 && <i>›</i>}
              </div>
            </article>
          ))}
        </div>
      </section>

      <section className="platform-section" id="platform">
        <div className="shell">
          <div className="section-heading platform-heading">
            <div>
            <p className="section-index">02 / DATA TO PROCESS INSIGHT</p>
              <h2>
                {copy.platform.heading[0]}
                <br />
                <span>{copy.platform.heading[1]}</span>
              </h2>
            </div>
            <p>{copy.platform.lead}</p>
          </div>

          <div className="platform-console">
            <div className="platform-console-bar">
              <div className="console-dots" aria-hidden="true"><i /><i /><i /></div>
              <span>{copy.platform.workspace}</span>
              <div><i /> {copy.platform.connected}</div>
            </div>

            <aside className="platform-sidebar">
              <div className="platform-sidebar-brand">
                <BrandMark />
                <div><strong>INGOT</strong><small>PLATFORM</small></div>
              </div>
              <nav aria-label={copy.platform.tabsLabel}>
                {copy.platform.tabs.map((item, index) => (
                  <button
                    type="button"
                    className={platformViewIndex === index ? "active" : ""}
                    aria-pressed={platformViewIndex === index}
                    onClick={() => selectPlatformView(PLATFORM_VIEW_KEYS[index])}
                    key={item}
                  >
                    <span>0{index + 1}</span>
                    {item}
                  </button>
                ))}
              </nav>
              <div className="platform-sidebar-site">
                <span className="live-dot" />
                <div><small>SITE</small><strong>{copy.platform.site}</strong></div>
              </div>
            </aside>

            <div className="platform-workspace">
              <div className="platform-mobile-tabs" role="group" aria-label={copy.platform.tabsLabel}>
                {copy.platform.tabs.map((item, index) => (
                  <button
                    type="button"
                    className={platformViewIndex === index ? "active" : ""}
                    aria-pressed={platformViewIndex === index}
                    onClick={() => selectPlatformView(PLATFORM_VIEW_KEYS[index])}
                    key={item}
                  >
                    {item}
                  </button>
                ))}
              </div>
              <header className="platform-workspace-head">
                <div>
                  <small>{copy.platform.viewKickers[platformViewIndex]}</small>
                  <h3>
                    {platformView === "overview"
                      ? copy.platform.overview
                      : platformView === "review"
                        ? copy.platform.review.title
                        : copy.platform.analysis.title}
                  </h3>
                </div>
                <span className="platform-range"><span className="live-dot" /> {copy.platform.range}</span>
              </header>

              {platformView === "overview" && (
                <div className="platform-view-panel" key="overview">
                  <div className="platform-metrics">
                    {copy.platform.metrics.map(([label, value, note], index) => (
                      <article key={label}>
                        <div><span>0{index + 1}</span><i className={index === 2 ? "gold" : ""} /></div>
                        <small>{label}</small>
                        <strong>{value}</strong>
                        <p>{note}</p>
                      </article>
                    ))}
                  </div>

                  <div className="platform-grid">
                    <article className="platform-line-card">
                      <header>
                        <div><span className="live-dot" /><strong>{copy.platform.lineTitle}</strong></div>
                        <small>CELL-A · {liveDatum.time}</small>
                      </header>
                      <div className="platform-line-flow">
                        {stageStream.map((stage, index) => (
                          <button
                            type="button"
                            className={`${liveIndex === index ? "active" : ""} ${platformFocus === index ? "selected" : ""}`}
                            onClick={() => setPlatformFocus(index === liveIndex ? null : index)}
                            key={stage.code}
                          >
                            <span><i />0{index + 1}</span>
                            <strong>{stage.label}</strong>
                            <small>{stage.code}</small>
                            <em className={`capture-badge capture-inline ${stage.capture}`}>
                              {stage.capture === "hybrid" ? copy.hero.captureHybrid : copy.hero.captureAuto}
                            </em>
                            <b>
                              {stage.capture === "hybrid"
                                ? liveIndex === index
                                  ? stageSettled
                                    ? copy.platform.status.submitted
                                    : copy.platform.status.sign
                                  : index < liveIndex
                                    ? copy.platform.status.submitted
                                    : copy.platform.status.onDemand
                                : liveIndex === index
                                  ? stageSettled
                                    ? copy.platform.status.pass
                                    : copy.platform.status.run
                                  : index < liveIndex
                                    ? copy.platform.status.pass
                                    : copy.platform.status.ready}
                            </b>
                          </button>
                        ))}
                      </div>
                      <div className="platform-throughput" aria-hidden="true">
                        <span>60</span>
                        <div>{Array.from({ length: 38 }).map((_, index) => <i key={index} style={{ height: `${18 + ((index * 23) % 70)}%` }} />)}</div>
                        <span>0</span>
                      </div>
                    </article>

                    <article className="platform-fact-card" key={`platform-${platformFocus ?? liveIndex}`}>
                      <header>
                        <div><span className={`event-pip ${platformDatum.tone}`} /><strong>{copy.platform.factsTitle}</strong></div>
                        <time>{platformDatum.time}</time>
                      </header>
                      <small>{copy.platform.factsHint}</small>
                      <div className="platform-fact-human">
                        <span>{platformDatum.dataLabel}</span>
                        <h4>{platformDatum.detail}</h4>
                        <p>{platformDatum.context}</p>
                      </div>
                      <CaptureBadges
                        capture={platformDatum.capture}
                        autoLabel={copy.hero.captureAuto}
                        generatedLabel={copy.hero.captureGenerated}
                        hybridLabel={copy.hero.captureHybrid}
                      />
                      <button
                        type="button"
                        className="platform-envelope-toggle"
                        aria-expanded={showEnvelope}
                        onClick={() => setShowEnvelope((current) => !current)}
                      >
                        <span>{showEnvelope ? "−" : "+"}</span>
                        {showEnvelope ? copy.platform.envelopeHide : copy.platform.envelopeShow}
                      </button>
                      {showEnvelope && (
                        <div className="platform-envelope-mini">
                          <span>{copy.platform.envelope}</span>
                          <dl>
                            <div><dt>record_type</dt><dd>{platformDatum.recordType}</dd></div>
                            {platformDatum.eventType && <div><dt>event_type</dt><dd>{platformDatum.eventType}</dd></div>}
                            <div><dt>name</dt><dd>{platformDatum.recordName}</dd></div>
                            <div><dt>source</dt><dd>{platformDatum.source}</dd></div>
                            <div><dt>subject</dt><dd>{platformDatum.subject}</dd></div>
                            {platformCorrelation && <div><dt>correlation</dt><dd>{platformCorrelation}</dd></div>}
                            {platformDatumData && <div><dt>data</dt><dd>{platformDatumData}</dd></div>}
                            <div><dt>seq</dt><dd>{platformDatum.stored ? `#${String(platformDatum.sequence).padStart(6, "0")}` : "—"}</dd></div>
                          </dl>
                        </div>
                      )}
                      <footer><span>{platformDatum.stored ? "✓" : "○"}</span>{platformDatum.stored ? copy.platform.dataStatus : copy.platform.pendingDataStatus}</footer>
                    </article>
                  </div>

                  <section className="platform-health" aria-label={copy.platform.healthTitle}>
                    <header><strong>{copy.platform.healthTitle}</strong><span>EDGE → CENTRAL</span></header>
                    <div>
                      {copy.platform.health.map(([label, value]) => (
                        <p key={label}><span><i />{label}</span><strong>{value}</strong></p>
                      ))}
                    </div>
                  </section>
                </div>
              )}

              {platformView === "review" && (
                <div className="platform-view-panel review-view" key="review">
                  <div className="review-summary">
                    <div>
                      <small>{copy.platform.review.taskLabel}</small>
                      <strong>{copy.platform.review.task}</strong>
                    </div>
                    <span><i />{copy.platform.review.state}</span>
                    <p>{copy.platform.review.lead}</p>
                  </div>
                  <div className="review-grid">
                    <article className="review-evidence-card">
                      <header><span className="event-pip cyan" /><strong>{copy.platform.review.evidenceTitle}</strong></header>
                      <dl>
                        {copy.platform.review.evidence.map(([label, value]) => (
                          <div key={label}><dt>{label}</dt><dd>{value}</dd></div>
                        ))}
                      </dl>
                    </article>
                    <article className="review-steps-card">
                      <header><span className="event-pip gold" /><strong>{copy.platform.review.stepsTitle}</strong></header>
                      <ol>
                        {copy.platform.review.steps.map(([number, title, detail]) => (
                          <li key={number}>
                            <span>{number}</span>
                            <div><strong>{title}</strong><p>{detail}</p></div>
                          </li>
                        ))}
                      </ol>
                    </article>
                  </div>
                  <div className="review-boundary-note">
                    <span>↗</span>
                    <p>{copy.platform.review.boundary}</p>
                    <button
                      type="button"
                      onClick={() => {
                        setPlatformView("analysis");
                      }}
                    >
                      {copy.platform.review.toAnalysis} →
                    </button>
                  </div>
                </div>
              )}

              {platformView === "analysis" && (
                <div className="platform-view-panel analysis-view" key="analysis">
                  <section className="chat-capability" aria-label={copy.platform.analysis.chat.title}>
                    <header><div><small>CENTRAL WEB · READ ONLY</small><strong>{copy.platform.analysis.chat.title}</strong></div><span>{copy.platform.analysis.chat.note}</span></header>
                    <div className="chat-mode-switch" role="group">
                      {copy.platform.analysis.chat.modes.map((mode, index) => <button type="button" className={chatMode === index ? "active" : ""} aria-pressed={chatMode === index} onClick={() => setChatMode(index)} key={mode}>{mode}</button>)}
                    </div>
                    <blockquote>{copy.platform.analysis.chat.question}</blockquote>
                    <div className="chat-capability-flow">
                      {(chatMode === 0 ? copy.platform.analysis.chat.standard : copy.platform.analysis.chat.roles).map((item) => <span key={item}>✓ {item}</span>)}
                    </div>
                    <p><b>EVIDENCE VERIFIED</b>{copy.platform.analysis.chat.outcome[chatMode]}</p>
                    <footer>↳ {copy.platform.analysis.chat.boundary}</footer>
                  </section>
                </div>
              )}
            </div>
          </div>
        </div>
      </section>

      <section className="trust-section shell reveal-section" id="trust">
        <div className="section-heading compact"><div><p className="section-index">03 / EVENT INGESTION</p><h2>{copy.trust.heading}</h2></div><p>{copy.trust.lead}</p></div>
        <div className="trust-grid">{copy.trust.cards.map(([title, detail], index) => <article key={title}><span>0{index + 1}</span><h3>{title}</h3><p>{detail}</p></article>)}</div>
      </section>

      <details className="technical-details shell">
        <summary>{locale === "zh" ? "面向技术团队的实施资料" : "Implementation details for technical teams"}</summary>
        <p>{locale === "zh"
          ? "接入方式、数据结构、部署边界和开放接口请查看文档。"
          : "See the documentation for integration, data structure, deployment boundaries, and public interfaces."}</p>
        <div className="technical-details-content">
      <section className="boundary-section shell reveal-section" id="boundary">
        <div className="section-heading compact">
          <div>
            <p className="section-index">04 / INGESTION OPTIONS</p>
            <h2>{copy.boundary.heading[0]}<br />{copy.boundary.heading[1]}</h2>
          </div>
          <p>{copy.boundary.lead}</p>
        </div>
        <div className="edge-principles boundary-grid">
          {copy.boundary.cards.map(([overline, title, detail], index) => (
            <article key={title}>
              <span>0{index + 1}</span>
              <small>{overline}</small>
              <h3>{title}</h3>
              <p>{detail}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="statement shell reveal-section" id="language">
        <p className="section-index">05 / DATA TO PROCESS INSIGHT</p>
        <div className="statement-grid">
          <h2>
            {copy.language.telemetryHeading[0]}
            <br />
            <span>{copy.language.telemetryHeading[1]}</span>
          </h2>
          <h2>
            {copy.language.eventHeading[0]}
            <br />
            <em>{copy.language.eventHeading[1]}</em>
          </h2>
        </div>
        <div className="value-journey">
          <article>
            <span className="journey-number">01</span>
            <small>{copy.language.nodes[0][0]}</small>
            <strong>{copy.language.nodes[0][1]}</strong>
            <p>{copy.language.journey[0]}</p>
          </article>
          <div className="journey-arrow">→</div>
          <article>
            <span className="journey-number">02</span>
            <small>{copy.language.nodes[1][0]}</small>
            <strong>{copy.language.nodes[1][1]}</strong>
            <p>{copy.language.journey[1]}</p>
          </article>
          <div className="journey-arrow">→</div>
          <article className="journey-highlight">
            <span className="journey-number">03</span>
            <small>{copy.language.nodes[2][0]}</small>
            <strong>{copy.language.nodes[2][1]}</strong>
            <p>{copy.language.journey[2]}</p>
          </article>
        </div>
      </section>

      <section className="dual-plane">
        <div className="shell">
          <div className="section-heading">
            <div>
              <p className="section-index">06 / STANDARD EVENTS + CENTRAL FACTS</p>
              <h2>{copy.planes.heading[0]}<br />{copy.planes.heading[1]}</h2>
            </div>
            <p>{copy.planes.lead}</p>
          </div>

          <div className="planes">
            <article className="plane telemetry-plane">
              <div className="plane-head">
                <span className="plane-icon">≈</span>
                <div>
                  <small>SOURCE EVENTS</small>
                  <h3>{copy.planes.telemetryTitle}</h3>
                </div>
                    <b>≤ 1000 ev/batch</b>
              </div>
              <div className="waveform" aria-hidden="true">
                {Array.from({ length: 48 }).map((_, index) => (
                  <i
                    key={index}
                    style={{
                      height: `${22 + ((index * 17) % 57)}%`,
                      opacity: 0.22 + ((index * 13) % 60) / 100,
                    }}
                  />
                ))}
              </div>
              <div className="plane-foot">
                {copy.planes.telemetryFoot.map((item) => (
                  <span key={item}>{item}</span>
                ))}
              </div>
            </article>

            <article className="plane event-plane">
              <div className="plane-head">
                <span className="plane-icon">◆</span>
                <div>
                  <small>CENTRAL FACTS</small>
                  <h3>{copy.planes.eventTitle}</h3>
                </div>
                    <b>≤ 500 ev/batch</b>
              </div>
              <div className="event-rail" aria-hidden="true">
                <span />
                {["LOT", "CYCLE", "ALARM", "CYCLE", "PARAM"].map(
                  (label, index) => (
                    <i key={label + index} style={{ left: `${8 + index * 21}%` }}>
                      <em>{label}</em>
                    </i>
                  ),
                )}
              </div>
              <div className="plane-foot">
                {copy.planes.eventFoot.map((item) => (
                  <span key={item}>{item}</span>
                ))}
              </div>
            </article>
          </div>
        </div>
      </section>

      <section className="anatomy shell reveal-section">
        <div className="section-heading compact">
          <div>
            <p className="section-index">07 / MACHINING EVENT</p>
            <h2>{copy.anatomy.heading}</h2>
          </div>
          <p>{copy.anatomy.lead}</p>
        </div>

        <div className="anatomy-board">
          <div className="anatomy-tabs" role="tablist" aria-label={copy.anatomy.tabsLabel}>
            {anatomy.map(([key, label], index) => (
              <button
                key={key}
                className={activeAnatomy === index ? "active" : ""}
                onMouseEnter={() => setActiveAnatomy(index)}
                onFocus={() => setActiveAnatomy(index)}
                onClick={() => setActiveAnatomy(index)}
                role="tab"
                aria-selected={activeAnatomy === index}
              >
                <span>0{index + 1}</span>
                <strong>{key}</strong>
                <small>{label}</small>
              </button>
            ))}
          </div>
          <div className="event-envelope">
            <div className="envelope-top">
              <span>ProductionEvent</span>
              <span className="immutable-badge">{copy.anatomy.immutable}</span>
            </div>
            <div className="envelope-code">
              {anatomy.map(([key, , value], index) => (
                <div
                  key={key}
                  className={activeAnatomy === index ? "active" : ""}
                  onMouseEnter={() => setActiveAnatomy(index)}
                >
                  <span>{key.toLowerCase()}</span>
                  <strong>{value}</strong>
                </div>
              ))}
            </div>
            <div className="envelope-focus">
              <small>{anatomy[activeAnatomy][0]}</small>
              <p>{anatomy[activeAnatomy][1]}</p>
              <strong>{anatomy[activeAnatomy][2]}</strong>
            </div>
          </div>
        </div>
      </section>

      <section className="architecture" id="architecture">
        <div className="shell">
          <div className="section-heading">
            <div>
              <p className="section-index">08 / EDGE TO ENTERPRISE</p>
              <h2>{copy.architecture.heading[0]}<br />{copy.architecture.heading[1]}</h2>
            </div>
            <p>{copy.architecture.lead}</p>
          </div>

          <div className="architecture-flow">
            <div className="arch-sources arch-column">
              <small>01 · SOURCES</small>
              {copy.architecture.sources.map(([name, mode]) => (
                <div className="source-chip" key={name}>
                  <span>{name.slice(0, 2)}</span>
                  <strong>{name}</strong>
                  <small>{mode}</small>
                </div>
              ))}
            </div>

            <div className="flow-connector" aria-hidden="true">
              <span>{copy.architecture.stream}</span>
              <i />
            </div>

            <div className="edge-node arch-column">
              <small>02 · OPTIONAL LOCAL INGRESS</small>
              <div className="node-core">
                <div className="node-brand">
                  <BrandMark />
                  <strong>{copy.architecture.engine}</strong>
                </div>
                {copy.architecture.engineItems.map((item) => (
                  <span key={item}>{item}</span>
                ))}
              </div>
              <div className="edge-status">
                <span />
                {copy.architecture.status}
              </div>
            </div>

            <div className="flow-connector uplink" aria-hidden="true">
              <span>{copy.architecture.retry}</span>
              <i />
            </div>

            <div className="arch-consumers arch-column">
              <small>03 · PLATFORM + ECOSYSTEM</small>
              {copy.architecture.consumers.map(
                (item, index) => (
                  <div className="consumer-chip" key={item}>
                    <span>0{index + 1}</span>
                    <strong>{item}</strong>
                  </div>
                ),
              )}
            </div>
          </div>
        </div>
      </section>

      <section className="trace shell reveal-section" id="trace">
        <div className="trace-copy">
          <p className="section-index">09 / TRACEABILITY BY DESIGN</p>
          <h2>{copy.trace.heading[0]}<br />{copy.trace.heading[1]}</h2>
          <p>{copy.trace.lead}</p>
          <div className="query-chip">
            <small>{copy.trace.filter}</small>
            <code>ctx.material_lot = &quot;LOT-0716&quot;</code>
          </div>
        </div>
        <div className="trace-panel">
          <div className="trace-head">
            <div>
              <span className="live-dot" />
              {copy.trace.live}
            </div>
            <strong>LOT-0716</strong>
            <span>{copy.trace.events}</span>
          </div>
          <div className="trace-list">
            {traceSteps.map(([time, type, detail, asset], index) => (
              <div className="trace-row" key={time + type}>
                <time>{time}</time>
                <div className="trace-marker">
                  <i className={type.includes("alarm") ? "alert" : ""} />
                  {index < traceSteps.length - 1 && <span />}
                </div>
                <div>
                  <strong>{type}</strong>
                  <p>{detail}</p>
                </div>
                <small>{asset}</small>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="edge-section" id="edge">
        <div className="shell">
          <div className="edge-intro">
            <p className="section-index">10 / BUILT FOR THE EDGE</p>
            <h2>{copy.edge.heading[0]}<br />{copy.edge.heading[1]}</h2>
          </div>
          <div className="edge-metrics">
            <article>
              <strong>≤ 1000<span> events</span></strong>
              <p>{copy.edge.metrics[0]}</p>
            </article>
            <article>
              <strong>500k<span> rows</span></strong>
              <p>{copy.edge.metrics[1]}</p>
            </article>
            <article>
              <strong>SQLite<span> WAL</span></strong>
              <p>{copy.edge.metrics[2]}</p>
            </article>
            <article>
              <strong>ACK<span> seq</span></strong>
              <p>{copy.edge.metrics[3]}</p>
            </article>
          </div>
          <div className="edge-principles">
            {copy.edge.principles.map(([overline, title, principleCopy], index) => (
              <article key={title}>
                <span>0{index + 1}</span>
                <small>{overline}</small>
                <h3>{title}</h3>
                <p>{principleCopy}</p>
              </article>
            ))}
          </div>
        </div>
      </section>
        </div>
      </details>

      <section className="final-cta shell">
        <div className="cta-glow" aria-hidden="true" />
        <BrandMark />
        <p>{copy.cta.kicker}</p>
        <h2>{copy.cta.heading[0]}<br />{copy.cta.heading[1]}</h2>
        <a
          className="button button-primary"
          href={`https://docs.ingotstack.com/${locale}`}
          target="_blank"
          rel="noreferrer"
        >
          {copy.cta.button} <span>↑</span>
        </a>
      </section>

      <footer className="footer shell">
        <a className="brand" href="#top" aria-label={copy.nav.home}>
          <BrandLockup />
        </a>
        <p>{copy.cta.footer}</p>
        <div>
          <a
            href="https://github.com/liuweichaox/Ingot"
            target="_blank"
            rel="noreferrer"
          >
            GitHub ↗
          </a>
          <a
            href="https://github.com/liuweichaox/Ingot/issues"
            target="_blank"
            rel="noreferrer"
          >
            Issues ↗
          </a>
        </div>
      </footer>
    </main>
  );
}
