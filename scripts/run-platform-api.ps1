# 在 Windows 环境中验证配置并启动 Platform API 开发宿主。

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot "Data\platform-api"
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $runtimeRoot "inspection-attachments") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $runtimeRoot "process-knowledge") -Force | Out-Null

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Urls = "http://127.0.0.1:8000"
# 连接串优先使用调用方传入的值，其次读取本地 .env，避免开发脚本与 Compose 数据库密码漂移。
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Events)) {
    $postgresPassword = $env:INGOT_POSTGRES_PASSWORD
    $envFile = Join-Path $repoRoot ".env"
    if ([string]::IsNullOrWhiteSpace($postgresPassword) -and (Test-Path -LiteralPath $envFile)) {
        $passwordLine = Get-Content -LiteralPath $envFile |
            Where-Object { $_ -match '^\s*INGOT_POSTGRES_PASSWORD\s*=' } |
            Select-Object -First 1
        if ($passwordLine) {
            $postgresPassword = ($passwordLine -split '=', 2)[1].Trim().Trim('"', "'")
        }
    }
    if ([string]::IsNullOrWhiteSpace($postgresPassword)) {
        throw "缺少 PostgreSQL 密码。请设置 INGOT_POSTGRES_PASSWORD 或在 .env 中配置它。"
    }
    $env:ConnectionStrings__Events = "Host=127.0.0.1;Port=5432;Database=ingot;Username=ingot;Password=$postgresPassword"
}
$env:InspectionAttachments__RootPath = Join-Path $runtimeRoot "inspection-attachments"
$env:ProcessKnowledge__RootPath = Join-Path $runtimeRoot "process-knowledge"

$apiDll = Join-Path $repoRoot "src\platform\Ingot.Platform.Api\bin\Debug\net10.0\Ingot.Platform.Api.dll"
if (-not (Test-Path -LiteralPath $apiDll)) {
    dotnet build (Join-Path $repoRoot "src\platform\Ingot.Platform.Api\Ingot.Platform.Api.csproj")
}

Push-Location (Split-Path -Parent $apiDll)
try {
    dotnet $apiDll
}
finally {
    Pop-Location
}
