import { useState } from "react";
import { Alert, Badge, Button, Card, EmptyState, Input, Select } from "../../ui/components";
import { PROBE_MODE } from "../protocolRegistry";

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
  onProbe,
  onMapPoint,
}) {
  const [search, setSearch] = useState("");
  const [selections, setSelections] = useState({});
  const readiness = descriptor.probeReadiness(form);
  const points = (probe?.points || []).filter(point =>
    !search.trim() || `${point.name} ${point.path} ${point.topic || ""}`.toLowerCase().includes(search.trim().toLowerCase()));
  const mappedPaths = new Set(form.valueMappings.map(item => item.sourcePath).filter(Boolean));

  return (
    <div className="grid gap-4">
      <Card
        title="验证连接"
        description={descriptor.probeMode === PROBE_MODE.discover
          ? `由所选采集节点真实连接设备，成功后显示${descriptor.probeViewLabel}。`
          : "该协议无法枚举地址空间，验证连接只会回读下方已配置的点位。"}
        actions={allowProbe ? (
          <Button variant="primary" disabled={probing || Boolean(readiness)} onClick={() => onProbe()}>
            {probing ? "读取中…" : "验证连接"}
          </Button>
        ) : undefined}
      >
        <div className="grid gap-3">
          {readiness && allowProbe && <Alert tone="warning">{readiness}</Alert>}
          {probeError && <Alert tone="danger">{probeError}</Alert>}
          {probe && (
            <Alert tone={probe.success && probe.mappingsValidated ? "success" : probe.success ? "info" : "warning"}>
              {probe.message}
            </Alert>
          )}
          {!probe && !probeError && !readiness && (
            <p className="text-sm leading-6 text-slate-500">
              验证会由现场节点真实连接设备一次，读取样本并校验每个点位的换算结果。发布前必须验证通过。
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
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-slate-50 text-slate-600">
                <tr>
                  <th className="px-3 py-2">设备点位</th>
                  <th className="px-3 py-2">原始值</th>
                  <th className="px-3 py-2">映射</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {points.map(point => (
                  <tr key={`${point.topic || ""}${point.path}`} className={mappedPaths.has(point.path) ? "bg-emerald-50/40" : undefined}>
                    <td className="px-3 py-2">
                      <p className="font-medium text-slate-700">{point.name}</p>
                      <code className="text-xs text-slate-500">{point.path}</code>
                      <p className="mt-0.5 flex flex-wrap gap-1">
                        <Badge tone="neutral">{point.dataType}</Badge>
                        {point.topic && <Badge tone="info">{point.topic}</Badge>}
                        {mappedPaths.has(point.path) && <Badge tone="success">已映射</Badge>}
                      </p>
                    </td>
                    <td className="px-3 py-2 text-slate-700">{point.rawValue ?? "—"}</td>
                    <td className="px-3 py-2">
                      {readOnly ? "—" : (
                        <div className="flex gap-2">
                          <Select
                            value={selections[point.path] || ""}
                            aria-label={`映射 ${point.path}`}
                            onChange={event => setSelections(value => ({ ...value, [point.path]: event.target.value }))}
                          >
                            <option value="">选择变量</option>
                            {dataItems.map(item => (
                              <option key={item.code} value={item.code}>
                                {item.displayName || item.code}{item.unit ? `（${item.unit}）` : ""}
                              </option>
                            ))}
                          </Select>
                          <Button
                            disabled={!selections[point.path]}
                            onClick={() => onMapPoint(point, selections[point.path])}
                          >
                            映射
                          </Button>
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
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
