/**
 * apps/platform/src/navigation.js
 *
 * Ingot 前端信息架构 v2.0 —— 唯一事实来源。
 *
 * 一级导航按「工程师在回答哪个问题」组织，不按数据表或部门组织：
 *   总览 · 课题 · 证据 · 追因 · 配置
 * 课题是主线（问题 → 假设 → 实验 → 结论 → 工艺窗口），排在第二位；
 * 证据是事实层（运行 + 检测结果 + 事件 + 现场上下文），对应标识下方两块钢锭；
 * 追因把证据变成候选，课题把候选变成结论 —— 对应顶部金锭。
 * 系统管理不占一级导航，挂在顶栏齿轮下；平台健康信息提到常驻状态栏。
 *
 * 菜单命名规则见《Ingot 前端重构设计规格》§3.2：
 *   名词短语 2–5 字 · 不用"管理" · 不用"工业/智能/AI" · 父子不重名 · 能力边界写进名字。
 *
 * 术语：模压场景中「周期」与「运行」已确认为同一件事，UI 全站只用「运行」。
 * 运行号 OperationRunId 是唯一主标识；CorrelationId / RunKey 只在证据面板等处作技术别名出现。
 */
import {
  AdjustmentsHorizontalIcon,
  BeakerIcon,
  MagnifyingGlassCircleIcon,
  Square3Stack3DIcon,
  Squares2X2Icon,
} from "@heroicons/react/24/outline";

/**
 * 一级模块。desc 用于图标轨悬停提示与命令面板描述。
 * pending 表示"有几件事等你决定"，在图标轨上以 Evidence Gold 角标显示。
 */
export const sections = [
  {
    id: "overview", label: "总览", desc: "现在怎么样，我该先做什么",
    icon: Squares2X2Icon, path: "/overview",
    groups: [{ items: [
      ["/overview", "决策工作台"],
      ["/overview/live", "实时运行"],
      ["/overview/tasks", "我的任务"],
    ]}],
  },
  {
    // 主线放第二位：产品的价值就是把一个工艺问题推到有适用范围的工艺窗口
    id: "topic", label: "课题", desc: "主线：问题 → 假设 → 实验 → 结论 → 工艺窗口",
    icon: BeakerIcon, path: "/research/projects",
    groups: [
      { label: "推进中", items: [
        ["/research/projects", "课题列表"],
        ["/research/experiments", "实验排程"],
      ]},
      { label: "结论沉淀", items: [
        ["/research/windows", "工艺窗口"],
        ["/research/assets", "研发资产"],
      ]},
    ],
  },
  {
    // 事实层：一次运行的条件、轨迹、结果与上下文本来就是一体的，不该拆成两个模块
    id: "evidence", label: "证据", desc: "看清这次运行：条件、轨迹、结果、上下文",
    icon: Square3Stack3DIcon, path: "/runs",
    groups: [
      { label: "运行与结果", items: [
        ["/runs", "运行记录"],
        ["/runs/inspections", "检测任务"],
        ["/runs/events", "运行事件"],
      ]},
      { label: "现场上下文", items: [
        ["/runs/setup", "运行准备"],
        ["/runs/tooling", "工装装卸"],
      ]},
    ],
  },
  {
    id: "diagnose", label: "追因", desc: "差异出在哪，证据够不够",
    icon: MagnifyingGlassCircleIcon, path: "/diagnose/compare",
    groups: [
      { label: "证据准备", items: [
        ["/diagnose/data-health", "数据可信度"],
        ["/diagnose/basket", "对比暂存区"],
      ]},
      { label: "差异分析", items: [
        ["/diagnose/compare", "运行对比"],
        ["/diagnose/distribution", "结果分布"],
        ["/diagnose/candidates", "候选原因"],
      ]},
      { label: "提问与核对", items: [
        ["/diagnose/assistant", "分析助手"],
        ["/diagnose/checkset", "回答核对集"],
      ]},
    ],
  },
  {
    id: "configure", label: "配置", desc: "系统应该理解哪些变量、规则和设备",
    icon: AdjustmentsHorizontalIcon, path: "/configure/objects",
    groups: [
      { label: "工艺定义", items: [
        ["/configure/objects", "对象台账"],
        ["/configure/scenarios", "场景包"],
        ["/configure/process-models", "工艺模型"],
        ["/configure/recipes", "配方版本"],
        ["/configure/analysis-plans", "分析模型"],
      ]},
      { label: "检测规则", items: [
        ["/configure/inspection-definitions", "检测定义"],
        ["/configure/quality-plans", "检测方案"],
      ]},
      { label: "工装与资产", items: [
        ["/configure/tooling-types", "装配模板"],
        ["/configure/component-types", "组件分类"],
        ["/configure/components", "组件资产"],
        ["/configure/tooling-assemblies", "模具资产"],
      ]},
      { label: "现场接入", items: [
        ["/configure/edges", "现场节点"],
        ["/configure/acquisition", "设备接入"],
      ]},
    ],
  },
];

/** 系统管理：顶栏齿轮，不占一级导航。 */
export const systemSection = {
  id: "system", label: "系统管理", desc: "平台自身的账号、健康与留痕",
  path: "/system/users",
  groups: [{ items: [
    ["/system/users", "用户与权限"],
    ["/system/health", "平台状态"],
    ["/system/logs", "运行日志"],
    ["/system/audit", "审计追踪"],
  ]}],
};

/** 页面标题与副标题。副标题说明"这一页帮你回答什么"，不复述菜单名。 */
export const pageDetails = {
  "/overview": ["决策工作台", "按“需要你决定什么”排序，而不是按数据表排序"],
  "/overview/live": ["实时运行", "正在跑的运行、设备占用与现场节点心跳"],
  "/overview/tasks": ["我的任务", "等着你批准、复核或确认的事"],

  "/runs": ["运行记录", "一次真实运行的条件、轨迹与结果"],
  "/runs/inspections": ["检测任务", "录入检测值、复核判定与原图核对"],
  "/runs/events": ["运行事件", "设备与工艺事件，可关联到运行上下文"],
  "/runs/setup": ["运行准备", "声明接下来的运行用哪台设备、哪个产品、哪版配方与哪套工装"],
  "/runs/tooling": ["工装装卸", "记录工装组合版本在设备上的装入与卸下区间"],

  "/diagnose/data-health": ["数据可信度", "这些运行够不够格进入正式分析"],
  "/diagnose/basket": ["对比暂存区", "跨页面攒下的待比较运行"],
  "/diagnose/compare": ["运行对比", "比较可比运行，定位首次偏离与差异变量"],
  "/diagnose/distribution": ["结果分布", "按产品、配方与上下文查看结果分布与分层差异"],
  "/diagnose/candidates": ["候选原因", "所有待验证的原因及其证据强度"],
  "/diagnose/assistant": ["分析助手", "用只读工具回答问题并给出引用，不生成数值配方"],
  "/diagnose/checkset": ["回答核对集", "用真实问题持续核对事实、引用与正确拒绝"],

  "/research/projects": ["课题列表", "一个工艺问题从证据、假设、实验推进到工艺窗口"],
  "/research/experiments": ["实验排程", "跨课题的待批准、待执行与执行中实验"],
  "/research/windows": ["工艺窗口", "已验证结论、适用范围与失效条件 —— 系统的最终产出"],
  "/research/assets": ["研发资产", "可复用的数据集、模型、机理与知识"],

  "/configure/objects": ["对象台账", "工业对象的身份、层级与分析单元定义"],
  "/configure/scenarios": ["场景包", "声明这个工艺场景需要哪些变量、上下文字段与规则"],
  "/configure/process-models": ["工艺模型", "定义工艺变量、阶段号与配方参数，供设备点位统一映射"],
  "/configure/recipes": ["配方版本", "维护引用工艺模型的完整配方有效值"],
  "/configure/analysis-plans": ["分析模型", "版本化定义分析范围、对齐方式、分组与数据项"],
  "/configure/inspection-definitions": ["检测定义", "要检测的特性、录入类型与判定规则"],
  "/configure/quality-plans": ["检测方案", "产品适用的检测项目与复核规则"],
  "/configure/tooling-types": ["装配模板", "模具结构、装配位置与各位置允许的组件分类"],
  "/configure/component-types": ["组件分类", "维护模芯、模架等物理资产类别"],
  "/configure/components": ["组件资产", "具有资产编号与序列号的可更换物理组件"],
  "/configure/tooling-assemblies": ["模具资产", "模具身份、不可变配置版本与每个位置的实际成员"],
  "/configure/edges": ["现场节点", "负责连接设备、仪器、系统并上报数据的现场节点"],
  "/configure/acquisition": ["设备接入", "采集节点、通信驱动与点位到工艺变量的映射"],

  "/system/users": ["用户与权限", "本地账户、岗位权限、密码与启停状态"],
  "/system/health": ["平台状态", "中心服务、现场节点与数据上行是否正常"],
  "/system/logs": ["运行日志", "平台运行记录查询"],
  "/system/audit": ["审计追踪", "谁在什么时候批准了什么、改了什么"],
};

/** 动态路径的标题（前缀匹配）。 */
export const dynamicPageDetails = [
  ["/research/projects/", ["课题工作区", "围绕当前问题推进假设、实验、验证与知识复用"]],
  ["/configure/edges/", ["节点诊断", "查看现场节点的连接、采集、上行与最近日志"]],
  ["/configure/acquisition/", ["接入配置", "选择采集节点与通信驱动，将设备点位映射到工艺变量"]],
  ["/runs/", ["运行详情", "查看单次运行的条件、过程轨迹、检测结果与上下文快照"]],
];

/**
 * v1 旧路由 → v2.1 新路由。收藏夹与外链不会断。
 * 注意：/runs/:runId 与 /runs/events 等静态兄弟路径共存，
 * React Router 静态段优先于动态段，运行号（OR-…）不会与它们冲突。
 */
export const legacyRedirects = {
  "/": "/overview",
  "/workbench": "/overview",
  "/cycles": "/runs",
  "/events": "/runs/events",
  "/inspections": "/runs/inspections",
  "/production/changeover": "/runs/setup",
  "/production/tooling-installations": "/runs/tooling",
  "/production-setup": "/runs/setup",
  "/quality-analysis": "/diagnose/distribution",
  "/quality-plans": "/configure/quality-plans",
  "/comparisons": "/diagnose/compare",
  "/data-quality": "/diagnose/data-health",
  "/chat": "/diagnose/assistant",
  "/golden-questions": "/diagnose/checkset",
  "/research-projects": "/research/projects",
  "/process-improvement": "/research/projects",
  "/research-assets": "/research/assets",
  "/explorer": "/configure/objects",
  "/profiles": "/configure/process-models",
  "/configuration/scenario-packages": "/configure/scenarios",
  "/configuration/process-data-models": "/configure/process-models",
  "/configuration/recipe-versions": "/configure/recipes",
  "/configuration/process-analysis-plans": "/configure/analysis-plans",
  "/configuration/inspection-definitions": "/configure/inspection-definitions",
  "/configuration/quality-plans": "/configure/quality-plans",
  "/configuration/tooling-types": "/configure/tooling-types",
  "/configuration/component-types": "/configure/component-types",
  "/configuration/components": "/configure/components",
  "/configuration/tooling-assemblies": "/configure/tooling-assemblies",
  "/configuration/acquisition-profiles": "/configure/acquisition",
  "/edges": "/configure/edges",
  "/identity/users": "/system/users",
  "/users": "/system/users",
  "/platform-metrics": "/system/health",
  "/logs": "/system/logs",
};

export const sectionItems = section => section.groups.flatMap(group => group.items);
export const allSections = [...sections, systemSection];

export function findSection(pathname) {
  return (
    allSections.find(section =>
      sectionItems(section).some(([path]) => pathname === path || pathname.startsWith(`${path}/`)),
    ) ?? sections[0]
  );
}

export function findPageDetail(pathname) {
  if (pageDetails[pathname]) return pageDetails[pathname];
  const dynamic = dynamicPageDetails.find(([prefix]) => pathname.startsWith(prefix));
  return dynamic ? dynamic[1] : ["Ingot", "工艺追因与优化"];
}

/** 命令面板的页面条目。 */
export const searchEntries = allSections.flatMap(section =>
  section.groups.flatMap(group =>
    group.items.map(([path, label]) => ({
      path, label,
      section: section.label,
      group: group.label ?? "",
      description: pageDetails[path]?.[1] ?? "打开功能页面",
    })),
  ),
);
