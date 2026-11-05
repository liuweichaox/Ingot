// 展示现场节点心跳与日志，帮助排查外部数据上报。
import { Link, useParams } from "react-router";
import { extractRows, useApi } from "../hooks/useApi";
import { useSiteScope } from "../hooks/useSiteScope";
import { Alert, Card, DataTable, Metric, Page, RequestError, StatusBadge } from "../ui/components";
import { formatTime, edgeStatus, countOnlineEdges, LoadingCard, SiteScopeField } from "./shared";

export function EdgesPage() {
  const siteScope = useSiteScope({ edgeInterval: 10000 });
  const { siteId, siteChoices, selectSite, invalidRequestedSite, edgeResponse } = siteScope;
  const { data, loading, error, reload } = edgeResponse;
  const rows = extractRows(data).filter(row => !siteId || row.siteId === siteId);
  const online = countOnlineEdges(rows);
  return (
    <Page title="现场节点">
      <div className="mb-5 max-w-md"><SiteScopeField siteId={siteId} siteChoices={siteChoices} onChange={selectSite} loading={siteScope.loading} label="现场范围（未选择时显示全部可访问节点）" /></div>
      {invalidRequestedSite && <Alert tone="warning">URL 中的站点不在当前账号可访问范围内。</Alert>}
      <RequestError error={siteScope.error || error} title="现场节点暂不可用" onRetry={siteScope.error ? siteScope.reload : reload} />
      {data && <div className="grid gap-4 sm:grid-cols-3">
        <Metric label="现场节点" value={rows.length} hint="已登记" />
        <Metric label="当前在线" value={online} hint="30 秒内有心跳" />
        <Metric label="需要处理" value={rows.filter(row => edgeStatus(row) !== "online").length} hint="离线或运行异常" />
      </div>}
      {loading && !data ? <LoadingCard /> : data ? <Card title="节点状态" description="现场节点接收外部数据并上报平台。">
        <DataTable rows={rows} keyField="edgeId" columns={[
          { key: "edgeId", label: "节点" },
          { key: "hostname", label: "名称" },
          { key: "_status", label: "状态", render: (_value, row) => <StatusBadge value={edgeStatus(row)} /> },
          { key: "lastSeen", label: "最后心跳", render: formatTime },
          { key: "lastError", label: "最近问题", render: value => value || "无" },
          { key: "version", label: "版本" },
          { key: "_action", label: "操作", render: (_value, row) => <Link className="font-medium text-blue-600 hover:text-blue-700" to={`/edges/${encodeURIComponent(row.edgeId)}${row.siteId ? `?siteId=${encodeURIComponent(row.siteId)}` : ""}`}>查看诊断</Link> },
        ]} />
      </Card> : null}
    </Page>
  );
}

export function EdgeDetailPage() {
  const { edgeId = "" } = useParams();
  const siteScope = useSiteScope({ edgeInterval: 10000 });
  const logs = useApi(`/api/edges/${encodeURIComponent(edgeId)}/logs?page=1&pageSize=50&audience=operator`, { interval: 10000 });
  const edge = extractRows(siteScope.edgeResponse.data).find(row => row.edgeId === edgeId);
  return <Page title="节点诊断">
    <RequestError error={siteScope.error || siteScope.edgeResponse.error || logs.error} onRetry={() => Promise.all([siteScope.reload(), logs.reload()])} />
    {siteScope.loading && !siteScope.edgeResponse.data ? <LoadingCard /> : edge ? <>
      <div className="grid gap-4 sm:grid-cols-3">
        <Metric label="节点" value={edge.edgeId} />
        <Metric label="状态" value={<StatusBadge value={edgeStatus(edge)} />} />
        <Metric label="最后心跳" value={formatTime(edge.lastSeen)} />
      </div>
      {edge.lastError && <Alert tone="warning">{edge.lastError}</Alert>}
      <Card title="最近日志"><DataTable rows={extractRows(logs.data)} keyField="id" columns={[
        { key: "timestamp", label: "时间", render: formatTime },
        { key: "level", label: "级别" },
        { key: "message", label: "内容" },
      ]} /></Card>
    </> : <Alert tone="warning">未找到节点或当前账号无权访问。</Alert>}
  </Page>;
}
