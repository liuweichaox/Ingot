# 外部适配器接入示例

此示例展示向 Edge 提交一个合成的设备事件，不连接设备，也不代表完整工艺运行。不要把示例事件用于生产分析。

## 准备

部署 Edge，并在本机安全配置接入凭据。下面的 PowerShell 示例从环境变量读取凭据，避免把凭据写入文件。将地址改为部署者允许的 Edge 地址。

```powershell
$edgeUrl = 'http://localhost:8001'
$ingestToken = $env:INGOT_EDGE_INGEST_TOKEN
if ([string]::IsNullOrWhiteSpace($ingestToken)) { throw '请先设置 INGOT_EDGE_INGEST_TOKEN。' }
```

## 提交一个事件

事件 ID 必须是 UUIDv7。真实适配器应为每个不同事件生成并持久化唯一 ID；重试同一事件时保留原 ID 和内容。下例每次执行都会生成新事件，不适合作为重试逻辑。

```powershell
$eventId = [guid]::CreateVersion7().ToString()
$occurredAt = [DateTimeOffset]::UtcNow.ToString('O')
$events = @(@{
    eventId = $eventId
    eventType = 'equipment.heartbeat'
    occurredAt = $occurredAt
    recordedAt = $occurredAt
    source = 'adapter/example'
    subject = @{ type = 'equipment'; id = 'example-equipment' }
    context = @{}
    data = @{ status = 'example' }
})
$body = ConvertTo-Json -InputObject $events -Depth 10 -Compress
Invoke-RestMethod -Method Post -Uri "$edgeUrl/api/v1/connector-events" `
    -Headers @{ Authorization = "Bearer $ingestToken" } `
    -ContentType 'application/json' -Body $body
```

该示例需要支持 `[guid]::CreateVersion7()` 的 PowerShell/.NET 运行时。`202 Accepted` 只说明 Edge 已接受并本地持久化，并不表示 Platform 已接受或质量结果已验证。

## 用于实际工艺

真实适配器还必须提供可靠的设备和运行身份、来源发生时间、实际参数和过程数据，并把检验结果关联到同一运行。`process.sample` 的 `data.values` 与事件上下文必须匹配已发布的工艺模型；字段、单位、采样和触发由现场实现并验证。参见[数据接入](../../docs/data-connection.md)。

可以把现场协议资料、现有采集代码、已发布工艺模型和本示例交给 AI，请其生成独立适配器；在现场启用前应审查生成代码并验证身份、单位、断线、重复提交和设备安全边界。
