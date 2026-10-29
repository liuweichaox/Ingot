// Map probed device points to process-model items while keeping manual mapping available.

import { useEffect, useState } from "react";
import { Input } from "../ui/components";

const pointTransferType = "application/x-ingot-probe-point";

function pointIdentity(point) {
  return JSON.stringify([point.topic || "", point.path || ""]);
}

function isUnsupportedPoint(point) {
  return point?.metadata?.supported === "false";
}

function unsupportedReason(point) {
  return String(point?.metadata?.supportReason || "").trim();
}

function sourceAddress(row, descriptor) {
  if (descriptor.addressing === "modbus-register")
    return `${row.modbusArea || "holding-register"}:${row.modbusAddress ?? ""}${row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`}`;
  if (descriptor.addressing === "melsec-device")
    return `${row.melsecDevice || "D"}:${row.melsecAddress ?? ""}${row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`}`;
  return row.sourcePath || "";
}

export function ProbePointMappingCanvas({ descriptor, form, probe, dataItems, readOnly, onMapPoint }) {
  const [search, setSearch] = useState("");
  const [selectedPoint, setSelectedPoint] = useState("");
  const [selectedTarget, setSelectedTarget] = useState("");
  const points = probe?.points || [];
  const visiblePoints = points.filter(point => !search.trim() ||
    `${point.name} ${point.path} ${point.topic || ""}`.toLowerCase().includes(search.trim().toLowerCase()));
  const canMap = !readOnly && Boolean(onMapPoint);
  const dataItemFingerprint = dataItems.map(item => item.code).join("\u001f");
  const pointFingerprint = JSON.stringify(points.map(pointIdentity));

  useEffect(() => {
    setSelectedTarget("");
    setSelectedPoint("");
  }, [dataItemFingerprint, form.dataModel, pointFingerprint, probe?.sourceFingerprint, probe?.catalogCurrent]);

  function assign(point, targetCode) {
    const currentPoint = points.find(candidate => pointIdentity(candidate) === pointIdentity(point));
    const currentTarget = dataItems.find(item => item.code === targetCode);
    if (!currentPoint || !currentTarget || !canMap || isUnsupportedPoint(currentPoint)) return;
    onMapPoint(currentPoint, currentTarget.code);
    setSelectedPoint("");
    setSelectedTarget("");
  }

  function choosePoint(point) {
    if (isUnsupportedPoint(point)) return;
    const currentTarget = dataItems.find(item => item.code === selectedTarget);
    if (currentTarget) assign(point, currentTarget.code);
    else {
      setSelectedTarget("");
      setSelectedPoint(pointIdentity(point));
    }
  }

  function chooseTarget(item) {
    const point = points.find(candidate => pointIdentity(candidate) === selectedPoint);
    if (point) assign(point, item.code);
    else setSelectedTarget(item.code);
  }

  function dropPoint(event, item) {
    event.preventDefault();
    if (!canMap) return;
    try {
      const identity = event.dataTransfer?.getData(pointTransferType);
      const point = points.find(candidate => pointIdentity(candidate) === identity);
      assign(point, item.code);
    } catch {
      // Ignore payloads that were not produced by this point list.
    }
  }

  return (
    <section aria-label="点位拖放映射画布" className="grid gap-3 rounded-lg border border-slate-200 bg-white p-3">
      <div>
        <h4 className="text-sm font-semibold text-slate-900">设备点位 → 工艺变量</h4>
        <p className="mt-1 text-xs leading-5 text-slate-500">
          拖动已探查点位到目标变量；也可先选变量，再用键盘选择点位。映射会写入当前任务配置。
        </p>
      </div>
      {!points.length ? (
        <p className="rounded-md bg-slate-50 px-3 py-2 text-xs leading-5 text-slate-500">先验证连接读取设备点位，再在这里建立映射。</p>
      ) : (
        <>
          {probe.catalogCurrent === false && (
            <p role="status" className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-900">
              点位列表来自上一次探查，当前配置已变化；可继续辅助映射，但发布前需要重新探查。
            </p>
          )}
          <Input aria-label="画布 搜索设备点位" value={search} placeholder="搜索点位名称、路径或主题"
            onChange={event => setSearch(event.target.value)} />
          <div className="grid gap-3 md:grid-cols-2">
            <div>
              <p className="mb-1 text-[11px] font-semibold text-slate-600">可用设备点位 · {visiblePoints.length}/{points.length}</p>
              <div role="group" aria-label="画布 可用设备点位" className="max-h-56 space-y-1 overflow-auto rounded-md border border-slate-200 p-1">
                {visiblePoints.map(point => {
                  const identity = pointIdentity(point);
                  const selected = identity === selectedPoint;
                  const unsupported = isUnsupportedPoint(point);
                  const reason = unsupportedReason(point);
                  return (
                    <button key={identity} type="button" draggable={canMap && !unsupported}
                      aria-pressed={selected} disabled={!canMap || unsupported}
                      onClick={() => choosePoint(point)}
                      onDragStart={event => {
                        event.dataTransfer?.setData(pointTransferType, identity);
                        if (event.dataTransfer) event.dataTransfer.effectAllowed = "copy";
                      }}
                      className={`w-full rounded px-2 py-1.5 text-left ${unsupported
                        ? "cursor-not-allowed border border-rose-200 bg-rose-50/60"
                        : selected ? "bg-blue-100 ring-1 ring-blue-300" : "bg-slate-50 hover:bg-blue-50"}`}>
                      <span className="flex items-start justify-between gap-2">
                        <span className="break-all text-xs font-medium text-slate-800">{point.name || point.path}</span>
                        {unsupported && <span className="shrink-0 rounded bg-rose-100 px-1.5 py-0.5 text-[10px] font-semibold text-rose-800">不支持</span>}
                      </span>
                      <span className="block break-all text-[10px] leading-4 text-slate-500">{point.topic ? `${point.topic} · ` : ""}{point.path}</span>
                      <span className="mt-0.5 block text-[10px] text-slate-500">{point.dataType || "未知类型"}{point.unit ? ` · ${point.unit}` : ""}</span>
                      {reason && <span className="mt-1 block break-words text-[10px] leading-4 text-rose-800">{reason}</span>}
                    </button>
                  );
                })}
                {visiblePoints.length === 0 && <p className="p-2 text-xs text-slate-500">没有匹配点位。</p>}
              </div>
            </div>
            <div>
              <p className="mb-1 text-[11px] font-semibold text-slate-600">工艺变量 · {dataItems.length} 项</p>
              <div role="group" aria-label="画布 工艺变量目标" className="max-h-56 space-y-1 overflow-auto rounded-md border border-slate-200 p-1">
                {dataItems.map(item => {
                  const row = form.valueMappings.find(candidate => candidate.dataItemCode === item.code);
                  const selected = selectedTarget === item.code;
                  const mappedPoint = row && points.find(point =>
                    point.path === row.sourcePath && (!row.topic || point.topic === row.topic));
                  const unsupportedMapping = isUnsupportedPoint(mappedPoint);
                  const reason = unsupportedReason(mappedPoint);
                  const currentSource = row
                    ? [row.topic, sourceAddress(row, descriptor)].filter(Boolean).join(" · ") || "来源待设置"
                    : "尚未映射";
                  return (
                    <button key={item.code} type="button" disabled={!canMap}
                      aria-label={`画布目标 ${item.displayName || item.code}`} aria-pressed={selected}
                      onClick={() => chooseTarget(item)}
                      onDragOver={event => { if (canMap) event.preventDefault(); }}
                      onDrop={event => dropPoint(event, item)}
                      className={`w-full rounded border px-2 py-1.5 text-left ${selected
                        ? "border-blue-400 bg-blue-50 ring-1 ring-blue-200"
                        : "border-transparent bg-slate-50 hover:border-emerald-300 hover:bg-emerald-50"}`}>
                      <span className="flex items-center justify-between gap-2">
                        <span className="break-all text-xs font-medium text-slate-800">{item.displayName || item.code}</span>
                        <span className="shrink-0 text-[10px] text-slate-500">{item.nullable ? "可选" : "必需"}</span>
                      </span>
                      <span className={`mt-0.5 block break-all text-[10px] leading-4 ${row ? "text-emerald-700" : "text-slate-500"}`}>
                        {currentSource}{item.unit ? ` · ${item.unit}` : ""}
                      </span>
                      {unsupportedMapping && (
                        <span role="alert" className="mt-1 block break-words text-[10px] leading-4 text-rose-800">
                          当前映射来源不受支持{reason ? `：${reason}` : "；请移除此映射或更换来源点位。"}
                        </span>
                      )}
                    </button>
                  );
                })}
                {dataItems.length === 0 && <p className="p-2 text-xs text-slate-500">先选择已发布的工艺变量模型。</p>}
              </div>
            </div>
          </div>
          {selectedPoint && <p role="status" className="text-xs text-blue-700">已选择一个来源点位，请再选择目标变量完成映射。</p>}
          {selectedTarget && <p role="status" className="text-xs text-blue-700">已选择目标变量，请再选择来源点位完成映射。</p>}
        </>
      )}
    </section>
  );
}
