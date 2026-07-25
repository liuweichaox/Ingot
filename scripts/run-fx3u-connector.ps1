$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot "Data\connector-host-fx3u"
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Urls = "http://0.0.0.0:8001"
$env:Edge__EnablePlatformReporting = "true"
$env:Edge__PlatformApiBaseUrl = "http://127.0.0.1:8000"
$env:Edge__PublicBaseUrl = "http://127.0.0.1:8001"
$env:Edge__EdgeId = "EDGE-FX3U-SIM-001"
$env:Edge__EnableEventShipping = "true"
$env:Edge__EventIngestToken = "development-device-simulator-token-0001"
$env:ConnectorHost__IngestToken = "connector-host-ingest-token-0001"
$env:ConnectorHost__LocalApiToken = "development-device-simulator-token-0001"
$env:Events__DatabasePath = Join-Path $runtimeRoot "events.db"
$env:Logging__DatabasePath = Join-Path $runtimeRoot "logs.db"
$env:Context__DatabasePath = Join-Path $runtimeRoot "context.db"

$connectorDll = Join-Path $repoRoot "src\edge\Ingot.Edge.ConnectorHost\bin\Debug\net10.0\Ingot.Edge.ConnectorHost.dll"
if (-not (Test-Path -LiteralPath $connectorDll)) {
    dotnet build (Join-Path $repoRoot "src\edge\Ingot.Edge.ConnectorHost\Ingot.Edge.ConnectorHost.csproj")
}

dotnet $connectorDll
