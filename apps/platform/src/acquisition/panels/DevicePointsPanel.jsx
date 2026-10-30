// Present edge-side probe results and conversion evidence before a task can be published.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, Badge, Button, Card, EmptyState, Input, Select } from "../../ui/components";
import { PROBE_MODE } from "../protocolRegistry";

const metadataLabels = {
  category: "类别",
  type: "类型",
  subType: "子类型",
  componentPath: "组件路径",
  componentId: "组件 ID",
  supportReason: "不支持原因",
};

function visibleProbeMetadata(metadata) {
  return Object.entries(metadata || {}).filter(([key, value]) =>
    metadataLabels[key] && typeof value === "string" && value.trim());
}

function formatProbeTime(value) {
  return value ? new Date(value).toLocaleString("zh-CN") : "—";
}

export function DevicePointsPanel({
  descriptor,
  form,
  dataItems,
  probe,
  probeError,
  probing,
  readOnly,
  allowProbe = !readOnly,
  advisories,
  publishChecklist,
  focusPointRequest = null,
  onFocusPointComplete,
  onProbe,
  onMapPoint,
}) {
  const [search, setSearch] = useState("");
  const [selections, setSelections] = useState({});
  const [pendingFocusPoint, setPendingFocusPoint] = useState(null);
  const [focusedPointKey, setFocusedPointKey] = useState("");
  const readiness = descriptor.probeReadiness(form);
  const edgeMissing = !form.edgeId;
  const points = useMemo(() => (probe?.points || []).filter(point =>
    !search.trim() || `${point.name} ${point.path} ${point.topic || ""} ${Object.values(point.metadata || {}).join(" ")}`
      .toLowerCase().includes(search.trim().toLowerCase())), [probe?.points, search]);
  const pointKey = useCallback(point => `${descriptor.id === "mqtt" ? point.topic || "" : ""}\u001f${point.path}`, [descriptor.id]);
  useEffect(() => {
    if (!focusPointRequest) return;
    setSearch("");
    setPendingFocusPoint(focusPointRequest);
  }, [focusPointRequest]);

  useEffect(() => {
    if (!pendingFocusPoint) return undefined;
    const frame = window.requestAnimationFrame(() => {
      const key = pointKey(pendingFocusPoint);
      const row = document.getElementById(`acq-device-point-${encodeURIComponent(key)}`);
      if (row) {
        row.scrollIntoView({ block: "center", behavior: "smooth" });
        const field = row.querySelector("select");
        (field && !field.disabled ? field : row).focus({ preventScroll: true });
        setFocusedPointKey(key);
      }
      setPendingFocusPoint(null);
      onFocusPointComplete?.(pendingFocusPoint.revision);
    });
    return () => window.cancelAnimationFrame(frame);
  }, [onFocusPointComplete, pendingFocusPoint, pointKey, points]);

  useEffect(() => setFocusedPointKey(""), [search]);
  const mappedPaths = new Set(form.valueMappings
    .filter(item => item.sourcePath)
    .map(item => pointKey({ topic: item.topic, path: item.sourcePath })));
  const anyTopicMappedPaths = descriptor.id === "mqtt"
    ? new Set(form.valueMappings.filter(item => item.sourcePath && !(item.topic || "").trim()).map(item => item.sourcePath))
    : new Set();
  const isPointMapped = point => anyTopicMappedPaths.has(point.path) || mappedPaths.has(pointKey(point));
  const checkedMappings = probe?.mappings || [];
  const acceptedMappings = checkedMappings.filter(item => item.found && item.accepted).length;

  return (
    <div className="grid gap-4">
      <Card
        title={descriptor.probeActionLabel || "验证连接"}
        description={descriptor.probeMode === PROBE_MODE.discover
          ? `由所选现场节点真实连接设备，成功后显示${descriptor.probeViewLabel}。`
          : "该协议无法枚举地址空间，验证连接只会回读下方已配置的点位。"}
        actions={allowProbe ? (
          <Button variant="primary" disabled={probing || Boolean(readiness) || edgeMissing} onClick={() => onProbe()}>
            {probing ? "读取中…" : descriptor.probeActionLabel || "验证连接"}
          </Button>
        ) : undefined}
      >
        <div className="grid gap-3">
          {readiness && allowProbe && <Alert tone="warning">{readiness}</Alert>}
          {edgeMissing && allowProbe && <Alert tone="warning">请先选择能够访问设备网段的现场节点；连接探查会在该节点上执行。</Alert>}
          {probeError && <Alert tone="danger">{probeError}</Alert>}
          {probe && (
            <div className="grid gap-2">
              <Alert tone={probe.success && probe.mappingsValidated ? "success" : probe.success ? "info" : "warning"}>
                {probe.message}
              </Alert>
              <div className="flex flex-wrap gap-x-4 gap-y-1 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
                <span>最近探查：{formatProbeTime(probe.testedAt)}</span>
                <span>返回点位：{probe.points?.length || 0}</span>
                {checkedMappings.length > 0 && <span>换算校验：{acceptedMappings}/{checkedMappings.length} 通过</span>}
                {probe.scannedPointCount > 0 && <span>扫描点位：{probe.scannedPointCount}</span>}
              </div>
              {(probe.warnings || []).map((warning, index) => (
                <Alert key={`${index}:${warning}`} tone="warning">{warning}</Alert>
              ))}
              {checkedMappings.length > 0 && (
                <details className="rounded-lg border border-slate-200 bg-white">
                  <summary className="cursor-pointer px-3 py-2 text-xs font-semibold text-slate-700">
                    查看点位换算校验（{acceptedMappings}/{checkedMappings.length} 通过）
                  </summary>
                  <div className="max-h-56 overflow-auto border-t border-slate-100">
                    <table className="w-full text-left text-xs">
                      <thead className="sticky top-0 bg-slate-50 text-slate-600">
                        <tr><th className="px-3 py-2">工艺变量</th><th className="px-3 py-2">来源点位</th><th className="px-3 py-2">原始值 → 换算值</th><th className="px-3 py-2">结果</th></tr>
                      </thead>
                      <tbody className="divide-y divide-slate-100">
                        {checkedMappings.map((item, index) => {
                          const accepted = item.found && item.accepted;
                          return (
                            <tr key={`${item.dataItemCode}:${item.sourcePath}:${index}`}>
                              <td className="px-3 py-2 font-medium text-slate-800">{item.dataItemCode}</td>
                              <td className="px-3 py-2 break-all text-slate-600">{item.sourcePath}</td>
                              <td className="px-3 py-2 text-slate-600">{item.rawValue ?? "—"} → {item.convertedValue ?? "—"}</td>
                              <td className="px-3 py-2">
                                <Badge tone={accepted ? "success" : "danger"}>{accepted ? "通过" : item.error || "未找到或未接受"}</Badge>
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </details>
              )}
            </div>
          )}
          {!probe && !probeError && !readiness && (
            <p className="text-sm leading-6 text-slate-500">
              验证会由现场节点连接设备并读取样本，同时展示点位质量、来源时间和换算结果。发布前必须验证通过。
            </p>
          )}
        </div>
      </Card>

      {advisories.length > 0 && (
        <Card title="协议提示">
          <div className="grid gap-2">
            {advisories.map((item, index) => (
              <Alert key={index} tone={item.tone}>{item.message}</Alert>
            ))}
          </div>
        </Card>
      )}

      <Card
        title={descriptor.probeViewLabel}
        description={probe ? `共读取 ${probe.points?.length || 0} 个点位。` : "验证连接后显示设备返回的内容。"}
        actions={probe?.points?.length ? (
          <div className="flex flex-wrap gap-2">
            <Input className="max-w-[14rem]" value={search} placeholder="搜索路径或名称"
              onChange={event => setSearch(event.target.value)} />
            {allowProbe && <Button disabled={probing} onClick={() => onProbe({ search })}>从数据源筛选</Button>}
          </div>
        ) : undefined}
      >
        {!probe && <EmptyState title="尚未读取设备" description="填好连接参数后点击「验证连接」。" />}
        {probe && points.length === 0 && (
          <EmptyState title="没有匹配的点位" description="换一个关键词，或确认设备是否返回了预期内容。" />
        )}
        {probe && points.length > 0 && (
          <div className="max-h-[26rem] overflow-auto rounded-xl border border-slate-200">
            <table className="w-full min-w-[56rem] text-left text-sm">
              <thead className="sticky top-0 bg-slate-50 text-slate-600">
                <tr>
                  <th className="px-3 py-2">设备点位</th>
                  <th className="px-3 py-2">原始值</th>
                  <th className="px-3 py-2">质量 / 来源时间</th>
                  <th className="px-3 py-2">映射</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {points.map(point => {
                  const key = pointKey(point);
                  const focused = focusedPointKey === key;
                  return (
                  <tr key={key} id={`acq-device-point-${encodeURIComponent(key)}`} tabIndex={-1}
                    className={`${isPointMapped(point) ? "bg-emerald-50/40" : ""} ${focused ? "bg-blue-50 outline outline-2 outline-blue-400" : ""}`}>
                    <td className="px-3 py-2">
                      <p className="font-medium text-slate-700">{point.name}</p>
                      <code className="text-xs text-slate-500">{point.path}</code>
                      <p className="mt-0.5 flex flex-wrap gap-1">
                        <Badge tone="neutral">{point.dataType}</Badge>
                        {point.kind && <Badge tone="info">{point.kind}</Badge>}
                        {point.unit && <Badge tone="neutral">{point.unit}</Badge>}
                        {point.topic && <Badge tone="info">{point.topic}</Badge>}
                        {point.metadata?.supported === "false" && <Badge tone="danger">不支持</Badge>}
                        {isPointMapped(point) && <Badge tone="success">已映射</Badge>}
                      </p>
                      {visibleProbeMetadata(point.metadata).length > 0 && (
                        <p className="mt-1 break-all text-xs leading-5 text-slate-500">
                          {visibleProbeMetadata(point.metadata).map(([key, value]) => `${metadataLabels[key]}：${value}`).join(" · ")}
                        </p>
                      )}
                      {point.metadata?.supported === "false" && isPointMapped(point) && (
                        <p role="alert" className="mt-1 rounded-md bg-rose-50 px-2 py-1 text-xs leading-5 text-rose-800">
                          此点位已有映射，但当前驱动不支持采集。请移除此映射或改用受支持的 DataItem。
                        </p>
                      )}
                    </td>
                    <td className="px-3 py-2 text-slate-700">{point.rawValue ?? "—"}</td>
                    <td className="px-3 py-2">
                      <Badge tone={!point.quality ? "neutral" : /^Good/i.test(point.quality) ? "success" : "warning"}>
                        {point.quality || "未提供质量"}
                      </Badge>
                      <p className="mt-1 whitespace-nowrap text-xs text-slate-500">
                        {point.sourceTimestamp ? `来源：${formatProbeTime(point.sourceTimestamp)}` : "来源时间未提供"}
                      </p>
                    </td>
                    <td className="px-3 py-2">
                      {readOnly ? "—" : (
                        <div className="flex gap-2">
                          <Select
                            value={selections[key] || ""}
                            disabled={point.metadata?.supported === "false"}
                            aria-label={`映射 ${point.topic ? `${point.topic} / ` : ""}${point.path}`}
                            onChange={event => setSelections(value => ({ ...value, [key]: event.target.value }))}
                          >
                            <option value="">选择变量</option>
                            {dataItems.map(item => (
                              <option key={item.code} value={item.code}>
                                {item.displayName || item.code}{item.unit ? `（${item.unit}）` : ""}
                              </option>
                            ))}
                          </Select>
                          <Button
                            disabled={point.metadata?.supported === "false" || !selections[key]}
                            onClick={() => onMapPoint(point, selections[key])}
                          >
                            映射
                          </Button>
                        </div>
                      )}
                    </td>
                  </tr>
                );})}
              </tbody>
            </table>
          </div>
        )}
        {probe?.nextCursor && allowProbe && (
          <div className="mt-3 flex items-center justify-between gap-3 text-xs text-slate-500">
            <span>本次扫描 {probe.scannedPointCount || 0} 个点位，当前结果仍有下一页。</span>
            <Button disabled={probing} onClick={() => onProbe({ search, cursor: probe.nextCursor, append: true })}>
              继续加载
            </Button>
          </div>
        )}
        {probe?.scanLimitReached && (
          <Alert tone="warning">数据源点位超过单次扫描上限，请使用根路径、命名空间或正则条件缩小范围。</Alert>
        )}
      </Card>

      <Card title="发布检查" description="全部通过后才能把配置发布给现场节点。">
        <ul className="grid gap-2 text-sm">
          {publishChecklist.map(item => (
            <li key={item.label} className="flex items-start gap-2">
              <span className={`mt-0.5 inline-flex size-4 shrink-0 items-center justify-center rounded-full text-[10px] font-bold text-white ${item.done ? "bg-emerald-600" : "bg-slate-300"}`}>
                {item.done ? "✓" : ""}
              </span>
              <span className={item.done ? "text-slate-600" : "text-slate-800"}>
                {item.label}
                {item.detail && <span className="block text-xs text-slate-500">{item.detail}</span>}
              </span>
            </li>
          ))}
        </ul>
      </Card>

      <Card title="驱动能力" description="当前通信驱动支持的连接和读取方式。">
        <ul className="grid gap-1.5 text-sm text-slate-600">
          {descriptor.constraints.map(item => <li key={item}>· {item}</li>)}
        </ul>
      </Card>
    </div>
  );
}
