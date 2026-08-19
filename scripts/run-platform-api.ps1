$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot "Data\platform-api"
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $runtimeRoot "inspection-attachments") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $runtimeRoot "process-knowledge") -Force | Out-Null

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Urls = "http://127.0.0.1:8000"
$env:ConnectionStrings__Events = "Host=127.0.0.1;Port=5432;Database=ingot;Username=ingot;Password=ingot-local-dev"
$env:Chat__DatabasePath = Join-Path $runtimeRoot "chat.db"
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
