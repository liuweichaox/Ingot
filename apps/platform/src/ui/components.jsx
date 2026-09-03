// 提供平台统一、可访问且具备稳定列表标识的基础界面组件。
import { Dialog, DialogBackdrop, DialogPanel, DialogTitle } from "@headlessui/react";
import { CalendarDaysIcon, ChevronDownIcon, ChevronUpDownIcon, ChevronUpIcon, ClockIcon, ShieldCheckIcon, XMarkIcon } from "@heroicons/react/24/outline";
import {
  createSortedRowModel,
  rowSortingFeature,
  sortFn_alphanumeric,
  sortFn_datetime,
  sortFn_text,
  tableFeatures,
  useTable,
} from "@tanstack/react-table";
import { useEffect, useMemo, useRef, useState } from "react";
import { formatLocalDateTime } from "./dateTime";

const dataTableFeatures = tableFeatures({
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
  sortFns: {
    alphanumeric: sortFn_alphanumeric,
    datetime: sortFn_datetime,
    text: sortFn_text,
  },
});

export function cx(...values) {
  return values.filter(Boolean).join(" ");
}

export function Page({ title, description, actions, className, children }) {
  return (
    <div className={cx("space-y-6", className)}>
      <div className="flex flex-col gap-4 border-b border-slate-200/80 pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div className="min-w-0">
          <p className="data-label mb-2 text-trajectory-700">Engineering workspace</p>
          <h1 className="text-[1.75rem] font-semibold leading-tight tracking-[-0.035em] text-slate-950 sm:text-[2rem]">{title}</h1>
          {description && <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-500">{description}</p>}
        </div>
        {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
      </div>
      {children}
    </div>
  );
}

export function Card({ title, description, actions, className, children }) {
  return (
    <section className={cx("product-panel min-w-0 overflow-hidden rounded-xl", className)}>
      {(title || actions) && (
        <header className="flex flex-col gap-2 border-b border-slate-200/80 px-5 py-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            {title && <h2 className="text-sm font-semibold tracking-[-0.01em] text-slate-900">{title}</h2>}
            {description && <p className="mt-1 text-[13px] leading-5 text-slate-500">{description}</p>}
          </div>
          {actions && <div className="shrink-0">{actions}</div>}
        </header>
      )}
      <div className="p-5">{children}</div>
    </section>
  );
}

const buttonStyles = {
  primary: "border border-evidence-500 bg-evidence-500 text-coal-950 shadow-sm hover:border-evidence-400 hover:bg-evidence-400 focus-visible:outline-evidence-600",
  secondary: "border border-slate-300 bg-white text-slate-700 shadow-sm hover:border-slate-400 hover:bg-slate-50 focus-visible:outline-blue-600",
  danger: "bg-rose-600 text-white hover:bg-rose-700 focus-visible:outline-rose-600",
  ghost: "text-slate-600 hover:bg-slate-100 focus-visible:outline-blue-600",
};

export function Button({ variant = "secondary", className, type = "button", children, ...props }) {
  return (
    <button
      type={type}
      className={cx(
        "inline-flex min-h-10 items-center justify-center gap-2 whitespace-nowrap rounded-lg px-3.5 py-2 text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-offset-2 disabled:pointer-events-none disabled:opacity-50",
        buttonStyles[variant],
        className,
      )}
      {...props}
    >
      {children}
    </button>
  );
}

export function Badge({ tone = "neutral", children }) {
  const tones = {
    neutral: "bg-slate-100 text-slate-700 ring-slate-600/20",
    success: "bg-emerald-50 text-emerald-700 ring-emerald-600/20",
    warning: "bg-amber-50 text-amber-700 ring-amber-600/20",
    danger: "bg-rose-50 text-rose-700 ring-rose-600/20",
    info: "bg-trajectory-50 text-trajectory-700 ring-trajectory-600/20",
  };
  return (
    <span className={cx("inline-flex whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-semibold ring-1 ring-inset", tones[tone])}>
      {children}
    </span>
  );
}

export function StatusBadge({ value, label }) {
  const normalized = String(value ?? "unknown").toLowerCase();
  const labels = {
    active: "已启用",
    ready: "就绪",
    online: "在线",
    complete: "已完成",
    completed: "已完成",
    pass: "合格",
    passed: "合格",
    verified: "已确认",
    healthy: "正常",
    published: "已发布",
    validated: "已验证",
    indexed: "已索引",
    reviewed: "已复核",
    available: "可用",
    inactive: "已停用",
    maintenance: "维护中",
    missing: "缺少组件",
    valid: "有效",
    invalid: "无效",
    connected: "已连接",
    disconnected: "未连接",
    "query-time": "查询时计算",
    archived: "已归档",
    supported: "已支持",
    candidate: "候选",
    dispatched: "已下发",
    accepted: "已接受",
    modified: "已修改",
    recorded: "待复核",
    confirmed: "已确认",
    approved: "已批准",
    selected: "已选择",
    applied: "已应用",
    synchronized: "已同步",
    fail: "不合格",
    failed: "不合格",
    offline: "离线",
    rejected: "已驳回",
    falsified: "已反证",
    error: "异常",
    suspended: "已暂停",
    "rollback-required": "需要回滚",
    unavailable: "不可用",
    not_applicable: "无需质检",
    cancelled: "已取消",
    pending: "待处理",
    buffering: "本地缓存中",
    validating: "验证中",
    "waiting-execution-boundary": "等待过程执行边界",
    applying: "应用中",
    rollback: "已保留旧版本",
    draft: "草稿",
    running: "运行中",
    uploaded: "已上传",
    dirty: "待更新",
    retired: "已停用",
    inconclusive: "待确认",
    degraded: "运行异常",
    collecting: "采集中",
    blocked: "禁止正式分析",
    forbidden: "禁止分析",
    not_analyzable: "禁止分析",
    in_progress: "进行中",
    review_pending: "待复核",
    reinspection_required: "需要重检",
    queued: "排队中",
    completed_with_errors: "完成但有异常",
    incomplete: "边界不完整",
    cancelling: "取消中",
    proposed: "待评估",
    investigating: "调查中",
    trialing: "试验中",
    planned: "已计划",
    open: "待处理",
    closed: "已关闭",
    concluded: "已形成结论",
    disabled: "已停用",
    starting: "启动中",
    withdrawn: "已撤回",
    "rolled-back": "已回退",
    information: "信息",
    warning: "警告",
    released: "已发布",
    removed: "已卸下",
    unknown: "待上报",
  };
  const tone = ["active", "ready", "online", "complete", "completed", "pass", "passed", "verified", "healthy", "published", "validated", "indexed", "reviewed", "available", "valid", "connected", "confirmed", "approved", "selected", "applied", "synchronized", "supported", "dispatched", "accepted"].includes(normalized)
    ? "success"
    : ["fail", "failed", "offline", "rejected", "falsified", "error", "suspended", "rollback-required", "unavailable", "cancelled", "missing", "invalid", "disconnected", "blocked", "forbidden", "not_analyzable"].includes(normalized)
      ? "danger"
      : ["pending", "buffering", "validating", "waiting-execution-boundary", "applying", "rollback", "draft", "starting", "running", "uploaded", "dirty", "degraded", "collecting", "in_progress", "review_pending", "queued", "completed_with_errors", "incomplete", "cancelling", "proposed", "investigating", "trialing", "planned", "warning", "concluded", "withdrawn", "rolled-back", "maintenance", "candidate", "modified", "recorded"].includes(normalized)
        ? "warning"
        : "neutral";
  return <Badge tone={tone}>{label ?? labels[normalized] ?? String(value ?? "待上报")}</Badge>;
}

const evidenceLevels = {
  insufficient: { label: "证据不足", strength: 1, activeClassName: "bg-slate-400", labelClassName: "text-slate-600" },
  screening: { label: "仅稳健筛选", strength: 1, activeClassName: "bg-slate-400", labelClassName: "text-slate-600" },
  limited: { label: "证据有限", strength: 2, activeClassName: "bg-amber-500", labelClassName: "text-amber-700" },
  exploratory: { label: "探索性证据", strength: 2, activeClassName: "bg-amber-500", labelClassName: "text-amber-700" },
  stable: { label: "证据稳定", strength: 3, activeClassName: "bg-teal-500", labelClassName: "text-teal-700" },
  sufficient: { label: "证据充分", strength: 4, activeClassName: "bg-emerald-600", labelClassName: "text-emerald-700" },
};

function evidenceLevelLabel(value) {
  const normalized = String(value ?? "insufficient").toLowerCase();
  return evidenceLevels[normalized]?.label ?? String(value ?? "证据不足");
}

export function EvidenceLevel({ value, label, size = "default", className }) {
  const normalized = String(value ?? "insufficient").toLowerCase();
  const definition = evidenceLevels[normalized] || evidenceLevels.insufficient;
  const displayLabel = label || evidenceLevelLabel(value);
  const large = size === "large";
  return (
    <span
      className={cx("inline-flex max-w-full items-center gap-2 whitespace-nowrap", large ? "text-sm" : "text-[13px]", className)}
      role="img"
      aria-label={`证据等级：${displayLabel}，4 段中 ${definition.strength} 段`}
      title={`证据等级：${displayLabel}`}
    >
      <span className="flex shrink-0 gap-0.5" aria-hidden="true">
        {[1, 2, 3, 4].map(segment => (
          <span
            key={segment}
            className={cx(
              "block rounded-sm",
              large ? "h-2 w-5" : "h-1.5 w-3.5",
              segment <= definition.strength ? definition.activeClassName : "bg-slate-200",
            )}
          />
        ))}
      </span>
      <strong className={cx("truncate font-semibold", definition.labelClassName)}>{displayLabel}</strong>
    </span>
  );
}

export function ConclusionBoundary({ title = "结论边界", children, className }) {
  return (
    <aside
      className={cx("flex min-w-0 items-start gap-2 rounded-lg border border-dashed border-slate-300 bg-slate-50 px-3 py-2 text-[13px] leading-5 text-slate-600", className)}
      aria-label={title}
    >
      <ShieldCheckIcon className="mt-0.5 size-4 shrink-0 text-slate-500" aria-hidden="true" />
      <div className="min-w-0">
        <strong className="font-semibold text-slate-700">{title}</strong>
        <div>{children}</div>
      </div>
    </aside>
  );
}

export function WorkflowGuide({ title = "按步骤完成", description, steps, compact = false }) {
  return (
    <section className="overflow-hidden rounded-lg border border-slate-200 bg-white">
      <div className="border-b border-slate-200 px-4 py-3">
        <p className="text-sm font-semibold text-slate-900">{title}</p>
        {description && <p className="mt-0.5 text-[13px] leading-5 text-slate-500">{description}</p>}
      </div>
      <ol className={cx("grid divide-y divide-slate-200", !compact && "md:grid-cols-3 md:divide-x md:divide-y-0")}>
        {steps.map((step, index) => {
          const state = step.state || "upcoming";
          return (
            <li
              key={step.title}
              className="flex gap-3 px-4 py-3"
            >
              <span className={cx(
                "grid size-6 shrink-0 place-items-center rounded text-xs font-semibold",

                state === "done" ? "bg-emerald-700 text-white" :
                  state === "current" ? "bg-blue-600 text-white" :
                    "bg-slate-200 text-slate-600",
              )}>
                {state === "done" ? "✓" : index + 1}
              </span>
              <div>
                <p className="text-sm font-semibold text-slate-900">{step.title}</p>
                <p className="mt-0.5 text-[13px] leading-5 text-slate-500">{step.description}</p>
              </div>
            </li>
          );
        })}
      </ol>
    </section>
  );
}

export function notify(message, tone = "success") {
  window.dispatchEvent(new CustomEvent("ingot:notice", { detail: { message, tone } }));
}

export function ToastHost() {
  const [notices, setNotices] = useState([]);
  const nextNoticeId = useRef(0);
  const notice = notices[0];
  useEffect(() => {
    function handleNotice(event) {
      const nextNotice = { ...event.detail, id: ++nextNoticeId.current };
      setNotices(current => [...current, nextNotice]);
    }
    window.addEventListener("ingot:notice", handleNotice);
    return () => window.removeEventListener("ingot:notice", handleNotice);
  }, []);
  useEffect(() => {
    if (!notice) return undefined;
    const timer = window.setTimeout(() => {
      setNotices(current => current[0]?.id === notice.id ? current.slice(1) : current);
    }, 3500);
    return () => window.clearTimeout(timer);
  }, [notice]);
  if (!notice) return null;
  const tone = notice.tone === "danger"
    ? "border-rose-200 bg-rose-50 text-rose-800"
    : "border-emerald-200 bg-emerald-50 text-emerald-800";
  return (
    <div
      key={notice.id}
      className={cx("fixed inset-x-4 bottom-4 z-200 flex max-h-[calc(100dvh-2rem)] items-start gap-3 overflow-y-auto rounded-xl border px-4 py-3 text-sm font-medium shadow-xl sm:bottom-5 sm:left-auto sm:right-5 sm:w-full sm:max-w-sm", tone)}
      role="status"
      aria-atomic="true"
    >
      <span className="min-w-0 flex-1 break-words">{notice.message}</span>
      <button
        type="button"
        aria-label="关闭通知"
        className="-mr-1 -mt-1 flex size-8 shrink-0 items-center justify-center rounded-md hover:bg-black/5 focus-visible:outline-2 focus-visible:outline-offset-2"
        onClick={() => setNotices(current => current[0]?.id === notice.id ? current.slice(1) : current)}
      >
        <XMarkIcon className="size-4" aria-hidden="true" />
      </button>
    </div>
  );
}

export function Field({ label, hint, hintVisible = false, error, className, children }) {
  return (
    <label className={cx("grid min-w-0 content-start gap-1 self-start text-[13px] font-semibold text-slate-700", className)}>
      {label !== undefined && label !== null && <span className="min-w-0 leading-5">{label}</span>}
      {children}
      {hint && <span className={hintVisible ? "min-w-0 break-words text-[13px] font-normal leading-5 text-slate-500" : "sr-only"}>{hint}</span>}
      {error && <span className="min-w-0 text-[13px] font-normal leading-5 text-rose-600" role="alert">{error}</span>}
    </label>
  );
}

export function Input({ className, ...props }) {
  return (
    <input
      className={cx(
        "h-9 min-w-0 w-full rounded-md border border-slate-300 bg-white px-2.5 py-1.5 text-[13px] text-slate-900 shadow-[0_1px_2px_rgba(7,16,14,.025)] outline-none transition placeholder:text-slate-400 focus:border-trajectory-500 focus-visible:ring-2 focus-visible:ring-trajectory-500/20 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-600 disabled:shadow-none read-only:bg-slate-50 read-only:text-slate-600",
        className,
      )}
      {...props}
    />
  );
}

function toLocalDateTimeValue(value) {
  return formatLocalDateTime(value);
}

// Retains native calendar accessibility while presenting date and time as one field.
export function DateTimeField({ value, onChange, disabled = false, required = false, className, ...props }) {
  const localValue = toLocalDateTimeValue(value);
  const [date = "", time = ""] = localValue.split("T");
  const emit = (nextDate, nextTime) => onChange?.(nextDate ? `${nextDate}T${nextTime || "00:00"}` : "");
  return (
    <div className={cx("grid min-w-0 grid-cols-[minmax(0,1fr)_minmax(0,.72fr)] overflow-hidden rounded-md border border-slate-300 bg-white shadow-[0_1px_2px_rgba(7,16,14,.025)] transition focus-within:border-trajectory-500 focus-within:ring-2 focus-within:ring-trajectory-500/20 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50", className)}>
      <div className="flex min-w-0 items-center border-r border-slate-200 px-2.5">
        <CalendarDaysIcon className="mr-2 size-4 shrink-0 text-slate-400" aria-hidden="true" />
        <input
          {...props}
          aria-label={props["aria-label"] ? `${props["aria-label"]}日期` : "日期"}
          type="date"
          required={required}
          value={date}
          disabled={disabled}
          onChange={event => emit(event.target.value, time)}
          className="h-9 min-w-0 w-full bg-transparent text-[13px] text-slate-900 outline-none disabled:cursor-not-allowed disabled:text-slate-600"
        />
      </div>
      <div className="flex min-w-0 items-center px-2.5">
        <ClockIcon className="mr-2 size-4 shrink-0 text-slate-400" aria-hidden="true" />
        <input
          aria-label={props["aria-label"] ? `${props["aria-label"]}时间` : "时间"}
          type="time"
          value={time}
          disabled={disabled || !date}
          onChange={event => emit(date, event.target.value)}
          className="h-9 min-w-0 w-full bg-transparent text-[13px] text-slate-900 outline-none disabled:cursor-not-allowed disabled:text-slate-600"
        />
      </div>
    </div>
  );
}

export function Select({ className, children, ...props }) {
  return (
    <select
      className={cx(
        "h-9 min-w-0 w-full rounded-md border border-slate-300 bg-white px-2.5 pr-8 py-1.5 text-[13px] text-slate-900 shadow-[0_1px_2px_rgba(7,16,14,.025)] outline-none transition focus:border-trajectory-500 focus-visible:ring-2 focus-visible:ring-trajectory-500/20 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-600 disabled:shadow-none",
        className,
      )}
      {...props}
    >
      {children}
    </select>
  );
}

export function Textarea({ className, ...props }) {
  return (
    <textarea
      className={cx(
        "min-h-20 min-w-0 w-full resize-y rounded-md border border-slate-300 bg-white px-2.5 py-2 text-[13px] leading-5 text-slate-900 shadow-[0_1px_2px_rgba(7,16,14,.025)] outline-none transition placeholder:text-slate-400 focus:border-trajectory-500 focus-visible:ring-2 focus-visible:ring-trajectory-500/20 disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-600 disabled:shadow-none read-only:bg-slate-50 read-only:text-slate-600",
        className,
      )}
      {...props}
    />
  );
}

export function EmptyState({ title = "暂无数据", description = "数据到达后会自动显示在这里。", actions, details }) {
  return (
    <div className="grid min-h-40 place-items-center rounded-lg border border-dashed border-slate-300 bg-slate-50/75 p-8 text-center">
      <div className="max-w-xl">
        <p className="font-semibold text-slate-800">{title}</p>
        <p className="mt-1 text-sm leading-6 text-slate-600">{description}</p>
        {details && <div className="mt-4 text-left text-sm leading-6 text-slate-600">{details}</div>}
        {actions && <div className="mt-5 flex flex-wrap justify-center gap-2">{actions}</div>}
      </div>
    </div>
  );
}

export function Alert({ tone = "info", title, children }) {
  const tones = {
    info: "border-blue-200 bg-blue-50 text-blue-800",
    danger: "border-rose-200 bg-rose-50 text-rose-800",
    warning: "border-amber-200 bg-amber-50 text-amber-800",
    success: "border-emerald-200 bg-emerald-50 text-emerald-800",
  };
  return (
    <div className={cx("rounded-md border px-4 py-3 text-sm", tones[tone])} role={tone === "danger" ? "alert" : "status"}>
      {title && <p className="font-semibold">{title}</p>}
      {children && <div className={title ? "mt-1" : ""}>{children}</div>}
    </div>
  );
}

export function RequestError({ error, onRetry, title }) {
  if (!error) return null;
  const message = typeof error === "string" ? error : error.message || String(error);
  const effectiveTitle = title || (/无权|权限不足/.test(message)
    ? "当前岗位无权读取这些数据"
    : "数据暂时无法读取");
  return (
    <Alert tone="danger" title={effectiveTitle}>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <span className="min-w-0 break-words">{message}</span>
        {onRetry && <Button className="shrink-0" onClick={onRetry}>重试</Button>}
      </div>
    </Alert>
  );
}

export function DataTable({ columns, rows, keyField = "id", getRowKey, onRowClick }) {
  const safeColumns = columns || [];
  const safeRows = rows || [];
  const minimumWidth = safeColumns.length >= 8 ? "min-w-[1080px]" : safeColumns.length >= 6 ? "min-w-[840px]" : safeColumns.length >= 5 ? "min-w-[760px]" : safeColumns.length >= 4 ? "min-w-[640px]" : "min-w-full";
  const rowIds = useMemo(() => {
    const rowKeys = safeRows.map((row, index) => getRowKey ? getRowKey(row, index) : row[keyField] ?? index);
    const rowKeyCounts = new Map();
    rowKeys.forEach(key => rowKeyCounts.set(key, (rowKeyCounts.get(key) || 0) + 1));
    return rowKeys.map((key, index) => String(rowKeyCounts.get(key) > 1 ? `${key}:${index}` : key));
  }, [getRowKey, keyField, safeRows]);
  const tableColumns = useMemo(() => safeColumns.map((column, columnIndex) => ({
    id: String(column.id ?? `${column.key}:${columnIndex}`),
    accessorFn: row => row[column.key],
    header: column.label,
    cell: info => column.render ? column.render(info.getValue(), info.row.original) : displayValue(info.getValue()),
    enableSorting: column.sortable !== false && column.label !== "操作",
    sortDescFirst: column.sortDescFirst,
    sortFn: column.sortFn,
    sortUndefined: column.sortUndefined ?? "last",
    meta: { ingotColumn: column, columnIndex },
  })), [safeColumns]);
  const table = useTable({
    features: dataTableFeatures,
    columns: tableColumns,
    data: safeRows,
    enableMultiSort: true,
    enableSortingRemoval: true,
    getRowId: (_, index) => rowIds[index],
  });

  if (!safeRows.length) return <EmptyState />;
  return (
    <div className="relative overflow-x-auto rounded-lg border border-slate-200 bg-white scrollbar-thin" role="region" aria-label="可横向滚动的数据表" tabIndex={safeColumns.length >= 4 ? 0 : undefined}>
      {safeColumns.length >= 4 && <p className="sticky left-0 top-0 z-20 border-b border-slate-200 bg-slate-50 px-3 py-1.5 text-[13px] text-slate-600 sm:hidden">左右滑动查看全部字段</p>}
      <table className={cx("w-full divide-y divide-slate-200 text-left text-sm tabular-nums", minimumWidth)}>
        <thead className="bg-slate-50/95 text-xs tracking-[0.02em] text-slate-600">
          {table.getHeaderGroups().map(headerGroup => (
            <tr key={headerGroup.id}>
              {headerGroup.headers.map((header, columnIndex) => {
                const column = header.column.columnDef.meta.ingotColumn;
                const canSort = header.column.getCanSort();
                const sorted = header.column.getIsSorted();
                const sortLabel = sorted === "asc" ? "升序" : sorted === "desc" ? "降序" : "未排序";
                return (
                  <th
                    key={column.id ?? `${column.key}:${columnIndex}`}
                    scope="col"
                    aria-sort={sorted === "asc" ? "ascending" : sorted === "desc" ? "descending" : "none"}
                    className={cx(
                      "whitespace-nowrap px-3 py-3 font-semibold sm:px-4",
                      column.label === "操作" && "sticky right-0 z-10 w-px border-l border-slate-200 bg-slate-50 shadow-[-8px_0_12px_-12px_rgba(15,23,42,.45)]",
                      column.align === "right" && "text-right",
                    )}
                  >
                    {header.isPlaceholder ? null : canSort ? (
                      <button
                        type="button"
                        className={cx(
                          "group inline-flex min-h-7 items-center gap-1 rounded px-1.5 py-1 transition hover:bg-slate-200/70 hover:text-slate-900 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-trajectory-600",
                          column.align === "right" && "ml-auto",
                        )}
                        onClick={header.column.getToggleSortingHandler()}
                        aria-label={`${column.label}：${sortLabel}，点击切换排序`}
                        title="点击排序，Shift + 点击可按多列排序"
                      >
                        <table.FlexRender header={header} />
                        {sorted === "asc" ? <ChevronUpIcon className="size-3.5 text-trajectory-700" /> : sorted === "desc" ? <ChevronDownIcon className="size-3.5 text-trajectory-700" /> : <ChevronUpDownIcon className="size-3.5 text-slate-400 transition group-hover:text-slate-600" />}
                      </button>
                    ) : <table.FlexRender header={header} />}
                  </th>
                );
              })}
            </tr>
          ))}
        </thead>
        <tbody className="divide-y divide-slate-100">
          {table.getRowModel().rows.map(row => (
            <tr
              key={row.id}
              className={cx(
                "text-slate-700 transition-colors hover:bg-trajectory-50/60",
                onRowClick && "cursor-pointer hover:bg-blue-50/50 focus-visible:bg-blue-50 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-blue-600",
              )}
              onClick={onRowClick ? () => onRowClick(row.original) : undefined}

              tabIndex={onRowClick ? 0 : undefined}
              onKeyDown={onRowClick ? event => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  onRowClick(row.original);
                }
              } : undefined}
            >
              {row.getAllCells().map((cell, columnIndex) => {
                const column = cell.column.columnDef.meta.ingotColumn;
                return (
                  <td
                    key={column.id ?? `${column.key}:${columnIndex}`}
                    className={cx(
                      "max-w-sm px-3 py-3 align-middle leading-5 sm:px-4",
                      (column.primary || columnIndex === 0) && "font-medium text-slate-900",
                      column.label === "操作" && "sticky right-0 z-10 w-px min-w-max whitespace-nowrap border-l border-slate-100 bg-white shadow-[-8px_0_12px_-12px_rgba(15,23,42,.45)] [&_*]:whitespace-nowrap [&>div]:flex-nowrap",
                      column.align === "right" && "text-right",
                    )}
                  >
                    <table.FlexRender cell={cell} />
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Drawer({ open, onClose, title, description, children, footer, size = "lg", closeOnBackdrop = true }) {
  const sizes = { md: "max-w-xl", lg: "max-w-3xl", xl: "max-w-5xl" };
  return (
    <Dialog open={open} onClose={closeOnBackdrop ? onClose : () => {}} className="relative z-100">
      <DialogBackdrop transition className="fixed inset-0 bg-slate-950/30 backdrop-blur-sm transition data-closed:opacity-0" />
      <div className="fixed inset-0 overflow-hidden">
        <div className="absolute inset-0 overflow-hidden">
          <div className="pointer-events-none fixed inset-y-0 right-0 flex max-w-full pl-8 sm:pl-16">
            <DialogPanel
              transition
              className={cx(
                "pointer-events-auto flex w-screen flex-col bg-white shadow-2xl transition duration-300 data-closed:translate-x-full",
                sizes[size],
              )}
            >
              <header className="flex items-start justify-between border-b border-slate-200 px-5 py-4">
                <div>
                  <DialogTitle className="text-lg font-semibold text-slate-950">{title}</DialogTitle>
                  {description && <p className="mt-1 text-sm text-slate-500">{description}</p>}
                </div>
                <Button variant="ghost" className="-mr-2 p-2" onClick={onClose} aria-label="关闭">
                  <XMarkIcon className="size-5" />
                </Button>
              </header>
              <div className="min-h-0 flex-1 overflow-y-auto p-5">{children}</div>
              {footer && <footer className="flex justify-end gap-2 border-t border-slate-200 px-5 py-4">{footer}</footer>}
            </DialogPanel>
          </div>
        </div>
      </div>
    </Dialog>
  );
}

export function Metric({ label, value, hint, className, valueClassName }) {
  return (
    <div className={cx("product-panel min-w-0 rounded-xl p-5", className)}>
      <p className="data-label">{label}</p>
      <p className={cx("data-value mt-2 min-w-0 break-words text-[1.75rem] font-semibold leading-tight text-slate-950", valueClassName)}>
        {value ?? "—"}
      </p>
      {hint && <p className="mt-1 break-words text-[13px] leading-5 text-slate-500">{hint}</p>}
    </div>
  );
}

export function ConfirmDialog({ open, title, description, confirmLabel = "确认", tone = "danger", onCancel, onConfirm }) {
  return (
    <Dialog open={open} onClose={onCancel} className="relative z-150">
      <DialogBackdrop transition className="fixed inset-0 bg-slate-950/35 backdrop-blur-sm transition data-closed:opacity-0" />
      <div className="fixed inset-0 grid place-items-center overflow-y-auto p-4">
        <DialogPanel
          transition
          className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-5 shadow-2xl transition data-closed:scale-95 data-closed:opacity-0"
        >
          <DialogTitle className="text-lg font-semibold text-slate-950">{title}</DialogTitle>
          {description && <p className="mt-2 text-sm leading-6 text-slate-600">{description}</p>}
          <div className="mt-6 flex justify-end gap-2">
            <Button onClick={onCancel}>取消</Button>
            <Button variant={tone === "danger" ? "danger" : "primary"} onClick={onConfirm}>{confirmLabel}</Button>
          </div>
        </DialogPanel>
      </div>
    </Dialog>
  );
}

export function useConfirmDialog() {
  const [options, setOptions] = useState(null);
  const resolver = useRef(null);

  function settle(value) {
    resolver.current?.(value);
    resolver.current = null;
    setOptions(null);
  }

  function confirm(nextOptions) {
    resolver.current?.(false);
    return new Promise(resolve => {
      resolver.current = resolve;
      setOptions(nextOptions);
    });
  }

  return {
    confirm,
    confirmationDialog: (
      <ConfirmDialog
        open={Boolean(options)}
        title={options?.title || "确认操作"}
        description={options?.description}
        confirmLabel={options?.confirmLabel}
        tone={options?.tone}
        onCancel={() => settle(false)}
        onConfirm={() => settle(true)}
      />
    ),
  };
}

export function Pagination({ page, pageSize, total, onPageChange, onPageSizeChange }) {
  const pageCount = Math.max(1, Math.ceil(Number(total || 0) / pageSize));
  if (!total || total <= 20) return null;
  return (
    <div className="mt-4 flex flex-col gap-3 border-t border-slate-100 pt-4 sm:flex-row sm:items-center sm:justify-between">
      <p className="text-sm text-slate-500 tabular-nums">共 {total} 条 · 第 {page}/{pageCount} 页</p>
      <div className="flex items-center gap-2">
        {onPageSizeChange && (
          <Select className="w-24" value={pageSize} onChange={event => onPageSizeChange(Number(event.target.value))} aria-label="每页数量">
            {[20, 50, 100].map(value => <option key={value} value={value}>{value} 条</option>)}
          </Select>
        )}
        <Button disabled={page <= 1} onClick={() => onPageChange(page - 1)}>上一页</Button>
        <Button disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}>下一页</Button>
      </div>
    </div>
  );
}

function displayValue(value) {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "boolean") return value ? "是" : "否";
  if (Array.isArray(value)) {
    if (!value.length) return "—";
    return value.map(item => typeof item === "object" ? `${Object.keys(item || {}).length} 项信息` : displayValue(item)).join("、");
  }
  if (typeof value === "object") {
    const entries = Object.entries(value);
    if (!entries.length) return "—";
    return entries.map(([key, item]) => `${key}：${typeof item === "object" ? `${Object.keys(item || {}).length} 项` : displayValue(item)}`).join("；");
  }
  return String(value);
}
