import { Dialog, DialogBackdrop, DialogPanel, DialogTitle } from "@headlessui/react";
import { XMarkIcon } from "@heroicons/react/24/outline";

export function cx(...values) {
  return values.filter(Boolean).join(" ");
}

export function Page({ title, description, actions, children }) {
  return (
    <div className="space-y-5">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-slate-950">{title}</h1>
          {description && <p className="mt-1 max-w-3xl text-sm leading-6 text-slate-500">{description}</p>}
        </div>
        {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
      </div>
      {children}
    </div>
  );
}

export function Card({ title, description, actions, className, children }) {
  return (
    <section className={cx("rounded-2xl border border-slate-200 bg-white shadow-sm", className)}>
      {(title || actions) && (
        <header className="flex flex-col gap-3 border-b border-slate-100 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            {title && <h2 className="font-semibold text-slate-900">{title}</h2>}
            {description && <p className="mt-0.5 text-sm text-slate-500">{description}</p>}
          </div>
          {actions}
        </header>
      )}
      <div className="p-5">{children}</div>
    </section>
  );
}

const buttonStyles = {
  primary: "bg-blue-600 text-white hover:bg-blue-700 focus-visible:outline-blue-600",
  secondary: "border border-slate-300 bg-white text-slate-700 hover:bg-slate-50 focus-visible:outline-blue-600",
  danger: "bg-rose-600 text-white hover:bg-rose-700 focus-visible:outline-rose-600",
  ghost: "text-slate-600 hover:bg-slate-100 focus-visible:outline-blue-600",
};

export function Button({ variant = "secondary", className, type = "button", children, ...props }) {
  return (
    <button
      type={type}
      className={cx(
        "inline-flex min-h-9 items-center justify-center gap-2 whitespace-nowrap rounded-lg px-3 py-2 text-sm font-medium transition focus-visible:outline-2 focus-visible:outline-offset-2 disabled:pointer-events-none disabled:opacity-50",
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
    neutral: "bg-slate-100 text-slate-700",
    success: "bg-emerald-50 text-emerald-700 ring-emerald-600/20",
    warning: "bg-amber-50 text-amber-700 ring-amber-600/20",
    danger: "bg-rose-50 text-rose-700 ring-rose-600/20",
    info: "bg-blue-50 text-blue-700 ring-blue-600/20",
  };
  return (
    <span className={cx("inline-flex whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset", tones[tone])}>
      {children}
    </span>
  );
}

export function StatusBadge({ value }) {
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
    confirmed: "已确认",
    approved: "已批准",
    selected: "已选择",
    fail: "不合格",
    failed: "不合格",
    offline: "离线",
    rejected: "已驳回",
    error: "异常",
    suspended: "已暂停",
    "rollback-required": "需要回滚",
    unavailable: "不可用",
    not_applicable: "无需质检",
    cancelled: "已取消",
    pending: "待处理",
    draft: "草稿",
    running: "运行中",
    uploaded: "已上传",
    dirty: "待更新",
    retired: "已停用",
    inconclusive: "待确认",
    degraded: "运行异常",
    in_progress: "进行中",
    review_pending: "待复核",
    reinspection_required: "需要重检",
    queued: "排队中",
    completed_with_errors: "完成但有异常",
    cancelling: "取消中",
    proposed: "待评估",
    investigating: "调查中",
    trialing: "试验中",
    planned: "已计划",
    open: "待处理",
    closed: "已关闭",
    concluded: "已形成结论",
    disabled: "已停用",
    withdrawn: "已撤回",
    "rolled-back": "已回退",
    information: "信息",
    warning: "警告",
    unknown: "待上报",
  };
  const tone = ["active", "ready", "online", "complete", "completed", "pass", "passed", "verified", "healthy", "published", "validated", "indexed", "reviewed", "available", "confirmed", "approved", "selected"].includes(normalized)
    ? "success"
    : ["fail", "failed", "offline", "rejected", "error", "suspended", "rollback-required", "unavailable", "cancelled"].includes(normalized)
      ? "danger"
      : ["pending", "draft", "running", "uploaded", "dirty", "degraded", "in_progress", "review_pending", "queued", "completed_with_errors", "cancelling", "proposed", "investigating", "trialing", "planned", "warning", "concluded", "withdrawn", "rolled-back"].includes(normalized)
        ? "warning"
        : "neutral";
  return <Badge tone={tone}>{labels[normalized] ?? String(value ?? "待上报")}</Badge>;
}

export function Field({ label, hint, error, className, children }) {
  return (
    <label className={cx("grid gap-1.5 text-sm font-medium text-slate-700", className)}>
      <span>{label}</span>
      {children}
      {hint && <span className="text-xs font-normal text-slate-500">{hint}</span>}
      {error && <span className="text-xs font-normal text-rose-600">{error}</span>}
    </label>
  );
}

export function Input({ className, ...props }) {
  return (
    <input
      className={cx(
        "min-h-10 w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-blue-500 focus:ring-3 focus:ring-blue-100",
        className,
      )}
      {...props}
    />
  );
}

export function Select({ className, children, ...props }) {
  return (
    <select
      className={cx(
        "min-h-10 w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-blue-500 focus:ring-3 focus:ring-blue-100",
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
        "min-h-28 w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-blue-500 focus:ring-3 focus:ring-blue-100",
        className,
      )}
      {...props}
    />
  );
}

export function EmptyState({ title = "暂无数据", description = "数据到达后会自动显示在这里。" }) {
  return (
    <div className="grid min-h-40 place-items-center rounded-xl border border-dashed border-slate-300 bg-slate-50/70 p-8 text-center">
      <div>
        <p className="font-medium text-slate-700">{title}</p>
        <p className="mt-1 text-sm text-slate-500">{description}</p>
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
    <div className={cx("rounded-xl border px-4 py-3 text-sm", tones[tone])} role={tone === "danger" ? "alert" : "status"}>
      {title && <p className="font-semibold">{title}</p>}
      {children && <div className={title ? "mt-1" : ""}>{children}</div>}
    </div>
  );
}

export function DataTable({ columns, rows, keyField = "id", getRowKey, onRowClick }) {
  if (!rows?.length) return <EmptyState />;
  return (
    <div className="-mx-5 overflow-x-auto">
      <table className="min-w-full divide-y divide-slate-200 text-left text-sm">
        <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            {columns.map((column, columnIndex) => (
              <th key={column.id ?? `${column.key}:${columnIndex}`} className="whitespace-nowrap px-5 py-3 font-semibold">{column.label}</th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {rows.map((row, index) => (
            <tr
              key={getRowKey ? getRowKey(row, index) : row[keyField] ?? index}
              className={cx("text-slate-700", onRowClick && "cursor-pointer hover:bg-blue-50/50")}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
            >
              {columns.map((column, columnIndex) => (
                <td key={column.id ?? `${column.key}:${columnIndex}`} className="max-w-sm px-5 py-3 align-top">
                  {column.render ? column.render(row[column.key], row) : displayValue(row[column.key])}
                </td>
              ))}
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

export function Metric({ label, value, hint }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
      <p className="text-sm text-slate-500">{label}</p>
      <p className="mt-2 text-3xl font-semibold tracking-tight text-slate-950">{value ?? "—"}</p>
      {hint && <p className="mt-1 text-xs text-slate-400">{hint}</p>}
    </div>
  );
}

export function Pagination({ page, pageSize, total, onPageChange, onPageSizeChange }) {
  const pageCount = Math.max(1, Math.ceil(Number(total || 0) / pageSize));
  if (!total) return null;
  return (
    <div className="mt-4 flex flex-col gap-3 border-t border-slate-100 pt-4 sm:flex-row sm:items-center sm:justify-between">
      <p className="text-sm text-slate-500">共 {total} 条 · 第 {page}/{pageCount} 页</p>
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
