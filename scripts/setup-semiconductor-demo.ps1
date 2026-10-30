# Provision a synthetic wafer-etch data model and acquisition task against the local demo simulator.
param(
    [switch]$WaitForBatch,
    [switch]$SkipBuild,
    [int]$WaitTimeoutSeconds = 390
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Read-DotEnv {
    $values = @{}
    if (Test-Path ".env") {
        Get-Content ".env" | ForEach-Object {
            if ($_ -match '^\s*([^#=]+?)\s*=\s*(.*)\s*$') {
                $key = $matches[1].Trim()
                $value = $matches[2].Trim()
                if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                    ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                    $value = $value.Substring(1, $value.Length - 2)
                }
                $values[$key] = $value
            }
        }
    }
    return $values
}

$envFile = Read-DotEnv
function Env-Value([string]$name, [string]$fallback = "") {
    $processValue = [Environment]::GetEnvironmentVariable($name, "Process")
    if ($processValue) { return $processValue }
    if ($envFile.ContainsKey($name)) { return [string]$envFile[$name] }
    return $fallback
}

$siteId = Env-Value "INGOT_SITE_ID" "SITE-LOCAL-001"
$edgeId = Env-Value "INGOT_EDGE_ID" "EDGE-LOCAL-001"
$adminUser = Env-Value "INGOT_ADMIN_USERNAME" "admin"
$adminPassword = Env-Value "INGOT_ADMIN_PASSWORD"
if ([string]::IsNullOrWhiteSpace($adminPassword)) {
    throw "INGOT_ADMIN_PASSWORD 未配置，无法创建演示配置。"
}

function Invoke-JsonApi {
    param(
        [ValidateSet("GET", "POST", "DELETE")][string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [switch]$Anonymous
    )
    $request = @{
        Method = $Method
        Uri = "http://127.0.0.1:8000$Path"
        ContentType = "application/json"
    }
    if (-not $Anonymous) { $request.Headers = $script:headers }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 30 -Compress) }
    try {
        return Invoke-RestMethod @request
    } catch {
        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
        throw "$Method $Path 失败：$detail"
    }
}

Write-Host "启动半导体晶圆刻蚀模拟服务和 Edge ConnectorHost..."
$composeArgs = @("compose", "-f", "docker-compose.app.yml")
$connectorHostPort = 8001
$simulatorHealthPort = 9100
if (Test-Path "docker-compose.demo.local.yml") {
    $composeArgs += @("-f", "docker-compose.demo.local.yml")
    $connectorHostPort = 8003
    $simulatorHealthPort = 9110
}
if (-not $SkipBuild) {
    & docker @composeArgs --profile semiconductor-demo up -d --build --no-deps semiconductor-etch-simulator
    if ($LASTEXITCODE -ne 0) { throw "半导体刻蚀模拟服务启动失败。" }
    & docker @composeArgs --profile connector-host up -d --build --no-deps connector-host
} else {
    & docker @composeArgs --profile semiconductor-demo up -d --no-deps semiconductor-etch-simulator
    & docker @composeArgs --profile connector-host up -d --no-deps connector-host
}
if ($LASTEXITCODE -ne 0) { throw "Edge ConnectorHost 启动失败。" }

for ($attempt = 0; $attempt -lt 60; $attempt++) {
    try {
        Invoke-RestMethod "http://127.0.0.1:8000/health" | Out-Null
        Invoke-RestMethod "http://127.0.0.1:$simulatorHealthPort/health" | Out-Null
        Invoke-RestMethod "http://127.0.0.1:$connectorHostPort/health" | Out-Null
        break
    } catch {
        if ($attempt -eq 59) { throw "平台、半导体过程模拟服务或 ConnectorHost 未在等待时间内就绪。" }
        Start-Sleep -Seconds 2
    }
}

$login = Invoke-JsonApi -Method POST -Path "/api/v1/auth/login" -Body @{
    username = $adminUser
    password = $adminPassword
} -Anonymous
$script:headers = @{ Authorization = "Bearer $($login.token)" }

# Protect an existing real production context before the seed script writes shared demo resources.
$existingContexts = Invoke-JsonApi -Method GET -Path "/api/v1/production-contexts?siteId=$siteId&equipmentId=ETCH-DEMO-01&activeOnly=true"
$activeEquipmentContexts = @($existingContexts.data | Where-Object { $_.equipmentId -eq "ETCH-DEMO-01" })
$demoContexts = @($activeEquipmentContexts | Where-Object {
    $_.productFamilyCode -eq "semiconductor-wafer" -and
    $_.productCode -eq "WAFER-ETCH-DEMO" -and
    $_.externalBatchRef -eq "WAFER-ETCH-DEMO-001"
})
if ($activeEquipmentContexts.Count -gt 0 -and $demoContexts.Count -eq 0) {
    throw "ETCH-DEMO-01 已有非半导体演示生产上下文；为避免覆盖现有数据，请先为演示配置另选设备编号。"
}
if ($demoContexts.Count -gt 1) {
    throw "ETCH-DEMO-01 存在多个有效演示生产上下文；请先清理重复上下文再运行演示脚本。"
}
$script:existingSemiconductorDemoContext = if ($demoContexts.Count -eq 1) { $demoContexts[0] } else { $null }

$processItems = @(
    @{ code = "chamber.pressure"; name = "腔室压力"; type = "double"; unit = "Pa"; category = "process"; address = 0; sourceType = "float32" },
    @{ code = "rf.source_power"; name = "射频源功率"; type = "double"; unit = "W"; category = "process"; address = 2; sourceType = "float32" },
    @{ code = "rf.bias_power"; name = "偏置功率"; type = "double"; unit = "W"; category = "process"; address = 4; sourceType = "float32" },
    @{ code = "rf.reflected_power"; name = "反射功率"; type = "double"; unit = "W"; category = "process"; address = 6; sourceType = "float32" },
    @{ code = "gas.cf4_flow"; name = "CF4 气体流量"; type = "double"; unit = "sccm"; category = "process"; address = 8; sourceType = "float32" },
    @{ code = "gas.o2_flow"; name = "O2 气体流量"; type = "double"; unit = "sccm"; category = "process"; address = 10; sourceType = "float32" },
    @{ code = "gas.ar_flow"; name = "Ar 气体流量"; type = "double"; unit = "sccm"; category = "process"; address = 12; sourceType = "float32" },
    @{ code = "wafer.temperature"; name = "晶圆温度"; type = "double"; unit = "°C"; category = "process"; address = 14; sourceType = "float32" },
    @{ code = "chamber.wall_temperature"; name = "腔壁温度"; type = "double"; unit = "°C"; category = "process"; address = 16; sourceType = "float32" },
    @{ code = "coolant.inlet_temperature"; name = "冷却液入口温度"; type = "double"; unit = "°C"; category = "process"; address = 18; sourceType = "float32" },
    @{ code = "coolant.outlet_temperature"; name = "冷却液出口温度"; type = "double"; unit = "°C"; category = "process"; address = 20; sourceType = "float32" },
    @{ code = "coolant.flow"; name = "冷却液流量"; type = "double"; unit = "L/min"; category = "process"; address = 22; sourceType = "float32" },
    @{ code = "plasma.voltage"; name = "等离子体电压"; type = "double"; unit = "V"; category = "process"; address = 24; sourceType = "float32" },
    @{ code = "plasma.current"; name = "等离子体电流"; type = "double"; unit = "A"; category = "process"; address = 26; sourceType = "float32" },
    @{ code = "endpoint.signal"; name = "终点检测信号"; type = "double"; unit = "ratio"; category = "process"; address = 28; sourceType = "float32" },
    @{ code = "wafer.rotation_speed"; name = "晶圆旋转速度"; type = "double"; unit = "rpm"; category = "process"; address = 30; sourceType = "float32" },
    @{ code = "vacuum.pump_current"; name = "真空泵电流"; type = "double"; unit = "A"; category = "process"; address = 32; sourceType = "float32" },
    @{ code = "environment.temperature"; name = "设备周边温度"; type = "double"; unit = "°C"; category = "process"; address = 34; sourceType = "float32" },
    @{ code = "process.stage"; name = "工艺阶段"; type = "integer"; unit = ""; category = "stage"; address = 82; sourceType = "int16" }
)

$recipeItems = @(
    @{ code = "etch.duration"; name = "刻蚀阶段时长"; unit = "s"; min = 120; max = 240; step = 1; value = 185; address = 200; sourceType = "int16" },
    @{ code = "chamber.pressure_target"; name = "腔室压力目标"; unit = "Pa"; min = 20; max = 100; step = 1; value = 35.0; address = 202; sourceType = "float32" },
    @{ code = "rf.source_power_target"; name = "射频源功率目标"; unit = "W"; min = 100; max = 800; step = 10; value = 600.0; address = 204; sourceType = "float32" },
    @{ code = "rf.bias_power_target"; name = "偏置功率目标"; unit = "W"; min = 0; max = 400; step = 5; value = 180.0; address = 206; sourceType = "float32" },
    @{ code = "gas.cf4_flow_target"; name = "CF4 流量目标"; unit = "sccm"; min = 0; max = 100; step = 1; value = 45.0; address = 208; sourceType = "float32" },
    @{ code = "gas.o2_flow_target"; name = "O2 流量目标"; unit = "sccm"; min = 0; max = 50; step = 1; value = 12.0; address = 210; sourceType = "float32" },
    @{ code = "gas.ar_flow_target"; name = "Ar 流量目标"; unit = "sccm"; min = 0; max = 150; step = 1; value = 80.0; address = 212; sourceType = "float32" },
    @{ code = "wafer.temperature_target"; name = "晶圆温度目标"; unit = "°C"; min = -50; max = 100; step = 1; value = 25.0; address = 214; sourceType = "float32" },
    @{ code = "wafer.rotation_target"; name = "晶圆旋转目标"; unit = "rpm"; min = 0; max = 120; step = 1; value = 60.0; address = 216; sourceType = "float32" },
    @{ code = "endpoint.signal_threshold"; name = "终点信号阈值"; unit = "ratio"; min = 0; max = 1; step = 0.01; value = 0.72; address = 218; sourceType = "float32" },
    @{ code = "endpoint.timeout"; name = "终点检测超时"; unit = "s"; min = 120; max = 300; step = 1; value = 210; address = 220; sourceType = "int16" },
    @{ code = "pumpdown.target_pressure"; name = "抽气阶段目标压力"; unit = "Pa"; min = 10; max = 150; step = 1; value = 50.0; address = 222; sourceType = "float32" },
    @{ code = "gas.stabilization_duration"; name = "气体稳定阶段时长"; unit = "s"; min = 5; max = 60; step = 1; value = 15; address = 224; sourceType = "int16" },
    @{ code = "plasma.ramp_duration"; name = "等离子体功率斜坡时长"; unit = "s"; min = 1; max = 30; step = 1; value = 10; address = 226; sourceType = "int16" },
    @{ code = "gas.purge_duration"; name = "气体清洗阶段时长"; unit = "s"; min = 10; max = 60; step = 1; value = 35; address = 228; sourceType = "int16" },
    @{ code = "coolant.flow_target"; name = "冷却液流量目标"; unit = "L/min"; min = 0; max = 20; step = 0.1; value = 6.0; address = 230; sourceType = "float32" },
    @{ code = "coolant.temperature_target"; name = "冷却液温度目标"; unit = "°C"; min = 0; max = 35; step = 0.5; value = 18.0; address = 232; sourceType = "float32" },
    @{ code = "rf.reflected_power_limit"; name = "反射功率演示阈值"; unit = "W"; min = 0; max = 100; step = 1; value = 40.0; address = 234; sourceType = "float32" },
    @{ code = "process.repeat_count"; name = "演示重复次数"; unit = "count"; min = 1; max = 3; step = 1; value = 1; address = 236; sourceType = "int16" },
    @{ code = "endpoint.mode_code"; name = "终点模式代码"; unit = "code"; min = 0; max = 2; step = 1; value = 1; address = 238; sourceType = "int16" }
)

function Recipe-DataType([string]$sourceType) {
    if ($sourceType -eq "int16") { return "integer" }
    return "double"
}

$model = [ordered]@{
    modelId = "semiconductor-wafer-etch"
    version = 1
    name = "半导体晶圆刻蚀过程数据模型（演示）"
    description = "仅用于软件演示的合成晶圆刻蚀监控数据，不代表真实工艺参数。"
    status = "published"
    acquisition = @{ dataItems = @($processItems | ForEach-Object {
        @{ code = $_.code; displayName = $_.name; dataType = $_.type; unit = $_.unit; category = $_.category; nullable = $false }
    }) }
    controlParameters = @($recipeItems | ForEach-Object {
        @{ code = $_.code; displayName = $_.name; dataType = (Recipe-DataType $_.sourceType); unit = $_.unit; nullable = $false; minimum = $_.min; maximum = $_.max; step = $_.step; changeAllowed = $true }
    })
}
$modelResult = Invoke-JsonApi -Method POST -Path "/api/v1/process-data-models" -Body $model
if ($modelResult.acquisition.dataItems.Count -lt 10) { throw "过程数据模型未达到 10 个参数。" }

$recipeValues = @($recipeItems | ForEach-Object { @{ code = $_.code; value = $_.value } })
$recipeDraft = [ordered]@{
    processSpecificationId = "wafer-etch-demo-v1"
    version = 1
    name = "晶圆刻蚀演示配方 v1（合成数据）"
    basedOnVersion = $null
    dataModelId = "semiconductor-wafer-etch"
    dataModelVersion = 1
    status = "draft"
    contextSelector = @{ product_family_code = "semiconductor-wafer"; product_code = "WAFER-ETCH-DEMO"; equipment_id = "ETCH-DEMO-01" }
    values = $recipeValues
    changeReason = $null
    mechanismNotes = "装载、稳定、刻蚀、清洗等阶段仅用于演示数据链路；参数均为合成值，不构成工艺窗口。"
    evidenceReferences = @()
}
$existingRecipes = Invoke-JsonApi -Method GET -Path "/api/v1/process-specifications"
$publishedRecipe = @($existingRecipes.data | Where-Object {
    $_.processSpecificationId -eq $recipeDraft.processSpecificationId -and
    $_.version -eq $recipeDraft.version -and
    $_.status -eq "published"
} | Select-Object -First 1)
if (@($publishedRecipe).Count -eq 0) {
    $draftResult = Invoke-JsonApi -Method POST -Path "/api/v1/process-specifications" -Body $recipeDraft
    $recipePublished = [ordered]@{}
    foreach ($entry in $recipeDraft.GetEnumerator()) {
        $recipePublished[$entry.Key] = $entry.Value
    }
    $recipePublished["status"] = "published"
    $recipePublished["changeReason"] = $null
    $publishedRecipe = Invoke-JsonApi -Method POST -Path "/api/v1/process-specifications" -Body $recipePublished
}
if ($publishedRecipe.values.Count -lt 15) { throw "配方未达到 15 个参数。" }

$analysisSignals = @($processItems | ForEach-Object {
    @{ dataItemCode = $_.code; includeTrace = $true; features = @("mean", "min", "max", "stddev", "slope") }
})
$analysisPlan = [ordered]@{
    planId = "semiconductor-wafer-etch-analysis"
    version = 1
    name = "晶圆刻蚀过程分析（演示）"
    description = "按模拟阶段查看腔室压力、射频功率、气体流量、晶圆温度和终点信号；不表示真实工艺结论。"
    status = "published"
    dataModelId = "semiconductor-wafer-etch"
    dataModelVersion = 1
    analysisScope = "production-execution"
    alignmentMode = "stage-relative"
    comparisonKeys = @("product_family_code")
    contextSelector = @{ product_family_code = "semiconductor-wafer"; product_code = "WAFER-ETCH-DEMO"; equipment_id = "ETCH-DEMO-01" }
    signals = $analysisSignals
}
$existingPlans = Invoke-JsonApi -Method GET -Path "/api/v1/process-analysis-plans"
$analysisPlanResult = @($existingPlans.data | Where-Object {
    $_.planId -eq $analysisPlan.planId -and $_.version -eq $analysisPlan.version -and $_.status -eq "published"
} | Select-Object -First 1)
if (@($analysisPlanResult).Count -eq 0) {
    $analysisPlanResult = Invoke-JsonApi -Method POST -Path "/api/v1/process-analysis-plans" -Body $analysisPlan
}
if (@($analysisPlanResult.signals).Count -lt 10) { throw "分析方案未达到 10 个信号。" }

$componentSpecs = @(
    @{ code = "etch-focus-ring"; name = "刻蚀焦点环（演示记录）"; id = "etch-kit-ring-demo"; serial = "ETCH-KIT-RING-DEMO"; attrs = @{ simulation = "true" } },
    @{ code = "etch-gas-distributor"; name = "气体分配组件（演示记录）"; id = "etch-kit-gas-demo"; serial = "ETCH-KIT-GAS-DEMO"; attrs = @{ simulation = "true" } },
    @{ code = "etch-electrode"; name = "电极组件（演示记录）"; id = "etch-kit-electrode-demo"; serial = "ETCH-KIT-ELECTRODE-DEMO"; attrs = @{ simulation = "true" } },
    @{ code = "wafer-chuck"; name = "晶圆承载台（演示记录）"; id = "etch-kit-chuck-demo"; serial = "ETCH-KIT-CHUCK-DEMO"; attrs = @{ simulation = "true" } },
    @{ code = "etch-seal-kit"; name = "腔体密封组件（演示记录）"; id = "etch-kit-seal-demo"; serial = "ETCH-KIT-SEAL-DEMO"; attrs = @{ simulation = "true" } },
    @{ code = "etch-temperature-monitor"; name = "温度监测组件（演示记录）"; id = "etch-kit-temp-demo"; serial = "ETCH-KIT-TEMP-DEMO"; attrs = @{ simulation = "true" } }
)
$existingComponentTypes = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-component-types"
$existingComponents = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-components"
foreach ($item in $componentSpecs) {
    if (@($existingComponentTypes.data | Where-Object { $_.componentTypeCode -eq $item.code }).Count -eq 0) {
        Invoke-JsonApi -Method POST -Path "/api/v1/tooling-component-types" -Body @{ componentTypeCode = $item.code; name = $item.name; status = "active"; attributes = @{} } | Out-Null
    }
    if (@($existingComponents.data | Where-Object { $_.componentId -eq $item.id }).Count -eq 0) {
        Invoke-JsonApi -Method POST -Path "/api/v1/tooling-components" -Body @{ componentId = $item.id; componentTypeCode = $item.code; serialNo = $item.serial; name = $item.name; status = "available"; attributes = $item.attrs } | Out-Null
    }
}
$roles = @($componentSpecs | ForEach-Object { @{ code = $_.code; name = $_.name; required = $true; sortOrder = 1; acceptedComponentTypeCodes = @($_.code) } })
$existingToolingTypes = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-types"
if (@($existingToolingTypes.data | Where-Object { $_.toolingTypeCode -eq "semiconductor-etch-kit" -and $_.version -eq 1 }).Count -eq 0) {
    Invoke-JsonApi -Method POST -Path "/api/v1/tooling-types" -Body @{ toolingTypeCode = "semiconductor-etch-kit"; version = 1; name = "晶圆刻蚀演示工艺套件"; status = "active"; roles = $roles } | Out-Null
}
$existingAssemblies = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-assemblies"
if (@($existingAssemblies.data | Where-Object { $_.toolingAssemblyId -eq "semiconductor-etch-kit-demo" }).Count -eq 0) {
    Invoke-JsonApi -Method POST -Path "/api/v1/tooling-assemblies" -Body @{ toolingAssemblyId = "semiconductor-etch-kit-demo"; toolingTypeCode = "semiconductor-etch-kit"; name = "晶圆刻蚀演示工艺套件"; status = "active" } | Out-Null
}

$revisions = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-assemblies/semiconductor-etch-kit-demo/revisions"
$revision = @($revisions.data | Where-Object { @($_.members).Count -ge 5 } | Select-Object -First 1)
if (@($revision).Count -eq 0) {
    $members = @($componentSpecs | ForEach-Object { @{ roleCode = $_.code; componentId = $_.id } })
    $revision = Invoke-JsonApi -Method POST -Path "/api/v1/tooling-assemblies/semiconductor-etch-kit-demo/revisions" -Body @{ toolingTypeVersion = 1; members = $members }
}
if (@($revision.members).Count -lt 5) { throw "工装总成未达到 5 个配件。" }

$activeInstallations = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-installations?siteId=$siteId&equipmentId=ETCH-DEMO-01&activeOnly=true"
$installation = @($activeInstallations.data | Where-Object { $_.assemblyRevisionId -eq $revision.assemblyRevisionId } | Select-Object -First 1)
if (@($installation).Count -eq 0) {
    if (@($activeInstallations.data).Count -gt 0 -and $null -eq $script:existingSemiconductorDemoContext) {
        throw "ETCH-DEMO-01 已安装其他工装且没有半导体演示上下文；为避免替换现场工装，请先为演示配置另选设备编号。"
    }
    $installation = Invoke-JsonApi -Method POST -Path "/api/v1/tooling-installations:replace" -Body @{
        siteId = $siteId; equipmentId = "ETCH-DEMO-01"; assemblyRevisionId = $revision.assemblyRevisionId
        installedAt = (Get-Date).ToUniversalTime().ToString("o"); source = "device"
    }
}

$context = $script:existingSemiconductorDemoContext
if ($null -eq $context -or $context.toolingInstallationId -ne $installation.installationId) {
    $context = Invoke-JsonApi -Method POST -Path "/api/v1/production-contexts:replace" -Body @{
        siteId = $siteId; equipmentId = "ETCH-DEMO-01"; productFamilyCode = "semiconductor-wafer"; productCode = "WAFER-ETCH-DEMO"
        processSpecificationId = "wafer-etch-demo-v1"; processSpecificationVersion = "1"
        toolingInstallationId = $installation.installationId; validFrom = (Get-Date).ToUniversalTime().ToString("o")
        source = "device"; externalOrderRef = "WAFER-ETCH-DEMO-ORDER"; externalBatchRef = "WAFER-ETCH-DEMO-001"
        materialLotRef = "SILICON-WAFER-DEMO-LOT"; materialSpecification = "Synthetic semiconductor wafer demo material"
    }
}

$valueMappings = @($processItems | ForEach-Object {
    $path = "input-register:$($_.address):$($_.sourceType):big-endian:high-low"
    @{ dataItemCode = $_.code; sourcePath = $path; required = $true; sourceDataType = $_.sourceType; sourceUnit = $_.unit }
})
$parameterMappings = @($recipeItems | ForEach-Object {
    @{ dataItemCode = $_.code; sourcePath = "holding-register:$($_.address):$($_.sourceType):big-endian:high-low"; required = $true; sourceDataType = $_.sourceType; sourceUnit = $_.unit }
})
$existingTasks = Invoke-JsonApi -Method GET -Path "/api/v1/ingestion-tasks"
$publishedTask = @($existingTasks.data | Where-Object {
    $_.taskId -eq "semiconductor-etch-simulator-001" -and $_.status -eq "published"
} | Sort-Object version -Descending | Select-Object -First 1)
$taskVersions = @($existingTasks.data | Where-Object { $_.taskId -eq "semiconductor-etch-simulator-001" })
$maxVersion = if ($taskVersions.Count -gt 0) { [int](($taskVersions | Measure-Object -Property version -Maximum).Maximum) } else { 0 }
$taskVersion = $maxVersion + 1
$task = [ordered]@{
    taskId = "semiconductor-etch-simulator-001"; version = $taskVersion; name = "晶圆刻蚀腔体采集（演示）"; status = "published"
    edgeId = $edgeId; protocol = "modbus-tcp"; dataModelId = "semiconductor-wafer-etch"; dataModelVersion = 1
    deviceProfile = @{ kind = "plc"; manufacturer = ""; model = "" }
    source = "equipment/semiconductor-etch-demo"; subjectType = "equipment"; subjectId = "ETCH-DEMO-01"
    modbusTcp = @{ host = "semiconductor-etch-simulator"; port = 1502; unitId = 1; addressBase = "zero-based"; pollIntervalMs = 500; maxMergeGap = 8 }
    execution = @{ timeoutMs = 5000; reconnectDelayMs = 2000; sourceIdentityStaleAfterMs = 60000; maximumFutureTimestampSkewMs = 300000 }
    timestampMode = "source"; timestampPath = "input-register:84:uint64:big-endian:high-low"; timestampEncoding = "unix-ms"
    sampleEventType = "process.sample"; staticContext = @{ simulation = "true"; material = "synthetic-semiconductor-wafer" }
    contextMappings = @(
        @{ contextKey = "run_active"; sourcePath = "input-register:80.0:boolean"; required = $true }
        @{ contextKey = "external_batch_ref"; sourcePath = "input-register:90:string:20"; required = $true }
    )
    valueMappings = $valueMappings
    processSpecification = @{ eventType = "process.specification.applied"; idPath = "holding-register:160:string:28"; versionPath = "holding-register:176:uint16:big-endian:high-low"; namePath = "holding-register:180:string:28"; parametersPath = "."; parameterMappings = $parameterMappings }
    lifecycle = @{ mode = "discrete"; activeContextKey = "run_active"; activeValue = "true"; startedEventType = "process.execution.started"; completedEventType = "process.execution.completed"; stepChangedEventType = "process.stage_changed" }
}
$taskResult = $null
if (@($publishedTask).Count -gt 0) {
    $currentTask = $publishedTask[0]
    $currentSignature = @(
        $currentTask.name, $currentTask.edgeId, $currentTask.protocol, $currentTask.dataModelId, [string]$currentTask.dataModelVersion,
        $currentTask.deviceProfile.kind, $currentTask.deviceProfile.manufacturer, $currentTask.deviceProfile.model,
        $currentTask.source, $currentTask.subjectType, $currentTask.subjectId,
        $currentTask.modbusTcp.host, [string]$currentTask.modbusTcp.port, [string]$currentTask.modbusTcp.unitId,
        $currentTask.modbusTcp.addressBase, [string]$currentTask.modbusTcp.pollIntervalMs, [string]$currentTask.modbusTcp.maxMergeGap,
        $currentTask.timestampMode, $currentTask.timestampPath, $currentTask.timestampEncoding,
        $currentTask.processSpecification.idPath, $currentTask.processSpecification.versionPath,
        $currentTask.processSpecification.namePath, $currentTask.processSpecification.parametersPath,
        $currentTask.lifecycle.mode, $currentTask.lifecycle.activeContextKey, $currentTask.lifecycle.activeValue
    ) -join "`t"
    $desiredSignature = @(
        $task.name, $task.edgeId, $task.protocol, $task.dataModelId, [string]$task.dataModelVersion,
        $task.deviceProfile.kind, $task.deviceProfile.manufacturer, $task.deviceProfile.model,
        $task.source, $task.subjectType, $task.subjectId,
        $task.modbusTcp.host, [string]$task.modbusTcp.port, [string]$task.modbusTcp.unitId,
        $task.modbusTcp.addressBase, [string]$task.modbusTcp.pollIntervalMs, [string]$task.modbusTcp.maxMergeGap,
        $task.timestampMode, $task.timestampPath, $task.timestampEncoding,
        $task.processSpecification.idPath, $task.processSpecification.versionPath,
        $task.processSpecification.namePath, $task.processSpecification.parametersPath,
        $task.lifecycle.mode, $task.lifecycle.activeContextKey, $task.lifecycle.activeValue
    ) -join "`t"
    $currentValueMappingSignature = @($currentTask.valueMappings | ForEach-Object { "$($_.dataItemCode)`t$($_.sourcePath)`t$($_.required)`t$($_.sourceDataType)`t$($_.sourceUnit)" } | Sort-Object) -join "`n"
    $desiredValueMappingSignature = @($valueMappings | ForEach-Object { "$($_.dataItemCode)`t$($_.sourcePath)`t$($_.required)`t$($_.sourceDataType)`t$($_.sourceUnit)" } | Sort-Object) -join "`n"
    $currentParameterMappingSignature = @($currentTask.processSpecification.parameterMappings | ForEach-Object { "$($_.dataItemCode)`t$($_.sourcePath)`t$($_.required)`t$($_.sourceDataType)`t$($_.sourceUnit)" } | Sort-Object) -join "`n"
    $desiredParameterMappingSignature = @($parameterMappings | ForEach-Object { "$($_.dataItemCode)`t$($_.sourcePath)`t$($_.required)`t$($_.sourceDataType)`t$($_.sourceUnit)" } | Sort-Object) -join "`n"
    $valueMappingsMatch = (@($currentTask.valueMappings).Count -eq $valueMappings.Count) -and ($currentValueMappingSignature -eq $desiredValueMappingSignature)
    $parameterMappingsMatch = (@($currentTask.processSpecification.parameterMappings).Count -eq $parameterMappings.Count) -and ($currentParameterMappingSignature -eq $desiredParameterMappingSignature)
    $hasAcquisitionLimits = @($currentTask.valueMappings | Where-Object { $null -ne $_.minimum -or $null -ne $_.maximum }).Count -gt 0

    if ($currentSignature -eq $desiredSignature -and $valueMappingsMatch -and $parameterMappingsMatch -and -not $hasAcquisitionLimits) {
        $taskResult = $currentTask
        Write-Host "  采集任务已与演示配置一致，保留现有已发布版本。"
    } else {
        $task.version = $taskVersion
        $taskResult = Invoke-JsonApi -Method POST -Path "/api/v1/ingestion-tasks" -Body $task
    }
} else {
    $task.version = $taskVersion
    $taskResult = Invoke-JsonApi -Method POST -Path "/api/v1/ingestion-tasks" -Body $task
}
if ($taskResult.valueMappings.Count -lt 10) { throw "采集任务未达到 10 个过程参数。" }
if ($taskResult.processSpecification.parameterMappings.Count -lt 15) { throw "采集任务未达到 15 个配方参数。" }

Write-Host "半导体晶圆刻蚀演示配置已完成："
Write-Host "  模拟端点: semiconductor-etch-simulator:1502 (Modbus TCP)"
Write-Host "  过程数据: $($taskResult.valueMappings.Count) 项"
Write-Host "  配方参数: $($taskResult.processSpecification.parameterMappings.Count) 项"
Write-Host "  工装配件: $(@($revision.members).Count) 件"
Write-Host "  合成批次: WAFER-ETCH-DEMO-001 / 300 秒"
Write-Host "  采集任务: $($taskResult.taskId)@$($taskResult.version) / $($taskResult.status)"

if ($WaitForBatch) {
    if ($WaitTimeoutSeconds -lt 1) { throw "WaitTimeoutSeconds 必须大于 0。" }
    $priorEvents = Invoke-JsonApi -Method GET -Path "/api/v1/events?siteId=$siteId&edgeId=$edgeId&subjectId=ETCH-DEMO-01&limit=1"
    $afterIngestId = if (@($priorEvents.data).Count -gt 0) { [long]$priorEvents.data[0].ingestId } else { 0L }
    $runRequestedAt = [DateTimeOffset]::UtcNow
    Write-Host "重启独立模拟器并等待一条新五分钟批次结束（最长 $WaitTimeoutSeconds 秒）..."
    & docker @composeArgs --profile semiconductor-demo restart semiconductor-etch-simulator
    if ($LASTEXITCODE -ne 0) { throw "无法重启半导体刻蚀模拟器，未开始等待新批次。" }
    $deadline = $runRequestedAt.AddSeconds($WaitTimeoutSeconds)
    $startedExecutionIds = @()
    $completedEvent = $null
    do {
        $events = Invoke-JsonApi -Method GET -Path "/api/v1/events?siteId=$siteId&edgeId=$edgeId&subjectId=ETCH-DEMO-01&afterIngestId=$afterIngestId&limit=200"
        foreach ($item in @($events.data)) {
            $afterIngestId = [Math]::Max([long]$afterIngestId, [long]$item.ingestId)
            $event = $item.event
            if (-not $event -or -not $event.executionId) { continue }
            try {
                $occurredAt = [DateTimeOffset]::Parse([string]$event.occurredAt, [Globalization.CultureInfo]::InvariantCulture)
            } catch {
                continue
            }
            if ($occurredAt.ToUniversalTime() -lt $runRequestedAt) { continue }
            if ($event.eventType -eq "process.execution.started") {
                if ($startedExecutionIds -notcontains [string]$event.executionId) {
                    $startedExecutionIds += [string]$event.executionId
                }
            } elseif ($event.eventType -eq "process.execution.completed" -and
                $startedExecutionIds -contains [string]$event.executionId) {
                $completedEvent = $item
            }
        }
        if (-not $completedEvent -and (Get-Date).ToUniversalTime() -lt $deadline) { Start-Sleep -Seconds 3 }
    } while (-not $completedEvent -and (Get-Date).ToUniversalTime() -lt $deadline)
    if (-not $completedEvent) { throw "模拟器重启后未观察到同一新运行的开始与完成事件。" }

    $executionId = [string]$completedEvent.event.executionId
    $execution = $null
    do {
        $executionResult = Invoke-JsonApi -Method GET -Path "/api/v1/process-executions?siteId=$siteId&executionId=$([Uri]::EscapeDataString($executionId))&limit=1"
        $execution = @($executionResult.data | Where-Object { $_.executionId -eq $executionId } | Select-Object -First 1)
        if ($execution.Count -gt 0) { $execution = $execution[0] } else { $execution = $null }
        if (-not $execution -and (Get-Date).ToUniversalTime() -lt $deadline) { Start-Sleep -Seconds 3 }
    } while (-not $execution -and (Get-Date).ToUniversalTime() -lt $deadline)
    if (-not $execution -or -not $execution.hasStarted -or -not $execution.hasCompleted -or $execution.sampleCount -lt 1) {
        throw "新运行边界未完整固化，或没有有效过程样本；请检查节点诊断和数据质量。"
    }
    $validSignalCount = @($execution.processDataQuality.signals | Where-Object { $_.validSampleCount -gt 0 }).Count
    if ($validSignalCount -lt 1) { throw "新运行没有通过质量筛选的有效过程信号；请检查点位映射和采样质量。" }
    Write-Host "演示链路验证通过：运行 $executionId，$($execution.sampleCount) 条样本，$validSignalCount 个信号含有效样本。"
    Write-Host "可在 /process-executions、/comparisons 查看本次运行曲线和阶段。"
}
