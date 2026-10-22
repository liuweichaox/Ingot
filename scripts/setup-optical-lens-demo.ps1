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

Write-Host "启动光学镜片 PLC 模拟服务和 Edge ConnectorHost..."
$composeArgs = @("compose", "-f", "docker-compose.app.yml")
if (-not $SkipBuild) {
    & docker @composeArgs --profile optical-lens-demo up -d --build optical-lens-plc
    if ($LASTEXITCODE -ne 0) { throw "光学镜片 PLC 模拟服务启动失败。" }
    & docker @composeArgs --profile connector-host up -d --build connector-host
} else {
    & docker @composeArgs --profile optical-lens-demo up -d optical-lens-plc
    & docker @composeArgs --profile connector-host up -d connector-host
}
if ($LASTEXITCODE -ne 0) { throw "Edge ConnectorHost 启动失败。" }

for ($attempt = 0; $attempt -lt 60; $attempt++) {
    try {
        Invoke-RestMethod "http://127.0.0.1:8000/health" | Out-Null
        Invoke-RestMethod "http://127.0.0.1:9100/health" | Out-Null
        Invoke-RestMethod "http://127.0.0.1:8001/health" | Out-Null
        break
    } catch {
        if ($attempt -eq 59) { throw "平台、PLC 模拟服务或 ConnectorHost 未在等待时间内就绪。" }
        Start-Sleep -Seconds 2
    }
}

$login = Invoke-JsonApi -Method POST -Path "/api/v1/auth/login" -Body @{
    username = $adminUser
    password = $adminPassword
} -Anonymous
$script:headers = @{ Authorization = "Bearer $($login.token)" }

$processItems = @(
    @{ code = "mold.upper_temp"; name = "上模温度"; type = "double"; unit = "°C"; category = "process"; address = 0; sourceType = "float32"; min = 20; max = 190 },
    @{ code = "mold.lower_temp"; name = "下模温度"; type = "double"; unit = "°C"; category = "process"; address = 2; sourceType = "float32"; min = 20; max = 190 },
    @{ code = "cavity.pressure"; name = "型腔压力"; type = "double"; unit = "MPa"; category = "process"; address = 4; sourceType = "float32"; min = 0; max = 12 },
    @{ code = "press.force"; name = "合模力"; type = "double"; unit = "kN"; category = "process"; address = 6; sourceType = "float32"; min = 0; max = 50 },
    @{ code = "vacuum.pressure"; name = "型腔真空度"; type = "double"; unit = "kPa(g)"; category = "process"; address = 8; sourceType = "float32"; min = -101; max = 0 },
    @{ code = "plunger.position"; name = "压头位置"; type = "double"; unit = "mm"; category = "process"; address = 10; sourceType = "float32"; min = 3.8; max = 4.8 },
    @{ code = "plunger.speed"; name = "压头速度"; type = "double"; unit = "mm/s"; category = "process"; address = 12; sourceType = "float32"; min = 0; max = 5 },
    @{ code = "coolant.inlet_temp"; name = "冷却水入口温度"; type = "double"; unit = "°C"; category = "process"; address = 14; sourceType = "float32"; min = 15; max = 35 },
    @{ code = "coolant.outlet_temp"; name = "冷却水出口温度"; type = "double"; unit = "°C"; category = "process"; address = 16; sourceType = "float32"; min = 15; max = 50 },
    @{ code = "coolant.flow"; name = "冷却水流量"; type = "double"; unit = "L/min"; category = "process"; address = 18; sourceType = "float32"; min = 0; max = 12 },
    @{ code = "heater.upper_output"; name = "上模加热输出"; type = "double"; unit = "%"; category = "process"; address = 20; sourceType = "float32"; min = 0; max = 100 },
    @{ code = "heater.lower_output"; name = "下模加热输出"; type = "double"; unit = "%"; category = "process"; address = 22; sourceType = "float32"; min = 0; max = 100 },
    @{ code = "preform.temperature"; name = "预制坯温度"; type = "double"; unit = "°C"; category = "process"; address = 24; sourceType = "float32"; min = 20; max = 90 },
    @{ code = "environment.temperature"; name = "洁净室温度"; type = "double"; unit = "°C"; category = "process"; address = 26; sourceType = "float32"; min = 18; max = 30 },
    @{ code = "environment.humidity"; name = "洁净室湿度"; type = "double"; unit = "%RH"; category = "process"; address = 28; sourceType = "float32"; min = 20; max = 50 },
    @{ code = "ejector.force"; name = "脱模力"; type = "double"; unit = "kN"; category = "process"; address = 30; sourceType = "float32"; min = 0; max = 10 },
    @{ code = "mold.gap"; name = "模腔间隙"; type = "double"; unit = "mm"; category = "process"; address = 32; sourceType = "float32"; min = 3.8; max = 4.8 },
    @{ code = "vacuum.leak_rate"; name = "真空泄漏率"; type = "double"; unit = "kPa/min"; category = "process"; address = 34; sourceType = "float32"; min = 0; max = 8 },
    @{ code = "process.stage"; name = "工艺阶段"; type = "integer"; unit = ""; category = "stage"; address = 82; sourceType = "int16"; min = 0; max = 4 }
)

$recipeItems = @(
    @{ code = "mold.upper_setpoint"; name = "上模恒温设定"; unit = "°C"; min = 165; max = 185; step = 0.5; value = 175.0; address = 200; sourceType = "float32" },
    @{ code = "mold.lower_setpoint"; name = "下模恒温设定"; unit = "°C"; min = 165; max = 185; step = 0.5; value = 173.0; address = 202; sourceType = "float32" },
    @{ code = "heating.ramp_rate"; name = "升温速率"; unit = "°C/s"; min = 0.8; max = 2.5; step = 0.1; value = 1.8; address = 204; sourceType = "float32" },
    @{ code = "vacuum.setpoint"; name = "真空设定"; unit = "kPa(g)"; min = -95; max = -70; step = 1; value = -85.0; address = 206; sourceType = "float32" },
    @{ code = "vacuum.pull_time"; name = "抽真空时间"; unit = "s"; min = 20; max = 60; step = 1; value = 30; address = 208; sourceType = "int16" },
    @{ code = "forming.pressure"; name = "成形压力"; unit = "MPa"; min = 6; max = 10; step = 0.1; value = 8.0; address = 210; sourceType = "float32" },
    @{ code = "forming.pressure_ramp"; name = "加压速率"; unit = "MPa/s"; min = 0.1; max = 1; step = 0.05; value = 0.4; address = 212; sourceType = "float32" },
    @{ code = "forming.hold_time"; name = "保压时间"; unit = "s"; min = 90; max = 180; step = 1; value = 135; address = 214; sourceType = "int16" },
    @{ code = "clamp.force"; name = "合模力设定"; unit = "kN"; min = 25; max = 45; step = 0.5; value = 35.0; address = 216; sourceType = "float32" },
    @{ code = "plunger.approach_speed"; name = "合模接近速度"; unit = "mm/s"; min = 0.2; max = 1.5; step = 0.1; value = 0.8; address = 218; sourceType = "float32" },
    @{ code = "mold.final_gap"; name = "最终模腔间隙"; unit = "mm"; min = 4.1; max = 4.3; step = 0.01; value = 4.2; address = 220; sourceType = "float32" },
    @{ code = "cooling.start_temp"; name = "冷却启动温度"; unit = "°C"; min = 165; max = 180; step = 0.5; value = 175.0; address = 222; sourceType = "float32" },
    @{ code = "cooling.target_temp"; name = "脱模温度"; unit = "°C"; min = 50; max = 80; step = 0.5; value = 65.0; address = 224; sourceType = "float32" },
    @{ code = "cooling.duration"; name = "冷却时间"; unit = "s"; min = 45; max = 90; step = 1; value = 75; address = 226; sourceType = "int16" },
    @{ code = "cooling.flow"; name = "冷却水流量设定"; unit = "L/min"; min = 4; max = 8; step = 0.1; value = 6.0; address = 228; sourceType = "float32" },
    @{ code = "pressure.release_temp"; name = "释压温度"; unit = "°C"; min = 60; max = 100; step = 0.5; value = 80.0; address = 230; sourceType = "float32" },
    @{ code = "preform.temperature"; name = "预制坯预热温度"; unit = "°C"; min = 40; max = 80; step = 0.5; value = 60.0; address = 232; sourceType = "float32" },
    @{ code = "preform.mass"; name = "预制坯质量"; unit = "g"; min = 3.8; max = 4.6; step = 0.01; value = 4.2; address = 234; sourceType = "float32" },
    @{ code = "vent.delay"; name = "排气延时"; unit = "s"; min = 5; max = 30; step = 1; value = 12; address = 236; sourceType = "int16" },
    @{ code = "ejector.force_limit"; name = "脱模力上限"; unit = "kN"; min = 1; max = 4; step = 0.1; value = 2.0; address = 238; sourceType = "float32" }
)

function Recipe-DataType([string]$sourceType) {
    if ($sourceType -eq "int16") { return "integer" }
    return "double"
}

$model = [ordered]@{
    modelId = "optical-lens-pmma"
    version = 1
    name = "PMMA 光学镜片热压工艺数据模型"
    description = "演示用：光学级 PMMA 非球面镜片 300 秒热压成型"
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
    processSpecificationId = "pmma-lens-4p2"
    version = 1
    name = "PMMA 镜片 4.2mm 标准热压配方"
    basedOnVersion = $null
    dataModelId = "optical-lens-pmma"
    dataModelVersion = 1
    status = "draft"
    contextSelector = @{ product_family_code = "optical-lens"; product_code = "OL25-420"; equipment_id = "OLP-01" }
    values = $recipeValues
    changeReason = $null
    mechanismNotes = "升温、恒温保压、降温脱模三阶段；演示数据，不代表通用生产窗口。"
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
    planId = "optical-lens-analysis"
    version = 1
    name = "光学镜片热压过程分析"
    description = "演示用：按阶段分析 PMMA 镜片热压过程的温度、压力、真空和成形状态。"
    status = "published"
    dataModelId = "optical-lens-pmma"
    dataModelVersion = 1
    analysisScope = "production-execution"
    alignmentMode = "stage-relative"
    comparisonKeys = @("product_family_code")
    contextSelector = @{ product_family_code = "optical-lens"; product_code = "OL25-420"; equipment_id = "OLP-01" }
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
    @{ code = "upper-optical-insert"; name = "上模光学镶件"; id = "olm-u-001"; serial = "OLM-U-001"; attrs = @{ material = "S136"; opticalDiameter = "25.00mm"; roughness = "<=10nm" } },
    @{ code = "lower-optical-insert"; name = "下模光学镶件"; id = "olm-l-001"; serial = "OLM-L-001"; attrs = @{ material = "S136"; opticalDiameter = "25.00mm" } },
    @{ code = "gap-spacer"; name = "定厚间隔环"; id = "olm-s-001"; serial = "OLM-S-001"; attrs = @{ thickness = "4.20mm"; tolerance = "+/-0.01mm" } },
    @{ code = "vacuum-seal"; name = "真空密封圈"; id = "olm-v-001"; serial = "OLM-V-001"; attrs = @{ material = "FKM"; temperature = "200C"; life = "2000 cycles" } },
    @{ code = "ejector"; name = "顶针组件"; id = "olm-e-001"; serial = "OLM-E-001"; attrs = @{ material = "SKD11"; forceLimit = "2kN" } },
    @{ code = "temperature-sensor"; name = "模具温度传感器"; id = "olm-t-001"; serial = "OLM-T-001"; attrs = @{ type = "K"; calibrationValidUntil = "2026-12-01" } }
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
if (@($existingToolingTypes.data | Where-Object { $_.toolingTypeCode -eq "optical-lens-mold" -and $_.version -eq 1 }).Count -eq 0) {
    Invoke-JsonApi -Method POST -Path "/api/v1/tooling-types" -Body @{ toolingTypeCode = "optical-lens-mold"; version = 1; name = "25mm PMMA 镜片模具"; status = "active"; roles = $roles } | Out-Null
}
$existingAssemblies = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-assemblies"
if (@($existingAssemblies.data | Where-Object { $_.toolingAssemblyId -eq "olm-25-420-a" }).Count -eq 0) {
    Invoke-JsonApi -Method POST -Path "/api/v1/tooling-assemblies" -Body @{ toolingAssemblyId = "olm-25-420-a"; toolingTypeCode = "optical-lens-mold"; name = "OLM-25-420-A 光学镜片模具总成"; status = "active" } | Out-Null
}

$revisions = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-assemblies/olm-25-420-a/revisions"
$revision = @($revisions.data | Where-Object { @($_.members).Count -ge 5 } | Select-Object -First 1)
if (@($revision).Count -eq 0) {
    $members = @($componentSpecs | ForEach-Object { @{ roleCode = $_.code; componentId = $_.id } })
    $revision = Invoke-JsonApi -Method POST -Path "/api/v1/tooling-assemblies/olm-25-420-a/revisions" -Body @{ toolingTypeVersion = 1; members = $members }
}
if (@($revision.members).Count -lt 5) { throw "工装总成未达到 5 个配件。" }

$activeInstallations = Invoke-JsonApi -Method GET -Path "/api/v1/tooling-installations?siteId=$siteId&equipmentId=OLP-01&activeOnly=true"
$installation = @($activeInstallations.data | Where-Object { $_.assemblyRevisionId -eq $revision.assemblyRevisionId } | Select-Object -First 1)
if (@($installation).Count -eq 0) {
    $installation = Invoke-JsonApi -Method POST -Path "/api/v1/tooling-installations:replace" -Body @{
        siteId = $siteId; equipmentId = "OLP-01"; assemblyRevisionId = $revision.assemblyRevisionId
        installedAt = (Get-Date).ToUniversalTime().ToString("o"); source = "device"
    }
}

$context = Invoke-JsonApi -Method POST -Path "/api/v1/production-contexts:replace" -Body @{
    siteId = $siteId; equipmentId = "OLP-01"; productFamilyCode = "optical-lens"; productCode = "OL25-420"
    processSpecificationId = "pmma-lens-4p2"; processSpecificationVersion = "1"
    toolingInstallationId = $installation.installationId; validFrom = (Get-Date).ToUniversalTime().ToString("o")
    source = "device"; externalOrderRef = "DEMO-ORDER-001"; externalBatchRef = "OL25-420-DEMO-001"
    materialLotRef = "PMMA-LOT-DEMO-001"; materialSpecification = "Optical grade PMMA"
}

$valueMappings = @($processItems | ForEach-Object {
    $path = "input-register:$($_.address):$($_.sourceType):big-endian:high-low"
    @{ dataItemCode = $_.code; sourcePath = $path; required = $true; sourceDataType = $_.sourceType; sourceUnit = $_.unit; minimum = $_.min; maximum = $_.max; outOfRangeBehavior = "reject" }
})
$parameterMappings = @($recipeItems | ForEach-Object {
    @{ dataItemCode = $_.code; sourcePath = "holding-register:$($_.address):$($_.sourceType):big-endian:high-low"; required = $true; sourceDataType = $_.sourceType; sourceUnit = $_.unit }
})
$existingTasks = Invoke-JsonApi -Method GET -Path "/api/v1/ingestion-tasks"
$publishedTask = @($existingTasks.data | Where-Object {
    $_.taskId -eq "optical-lens-plc-001" -and $_.status -eq "published"
} | Sort-Object version -Descending | Select-Object -First 1)
$taskVersion = 1
if (@($publishedTask).Count -gt 0) {
    $missingParameterUnits = @($publishedTask.processSpecification.parameterMappings | Where-Object {
        [string]::IsNullOrWhiteSpace($_.sourceUnit)
    }).Count
    $taskVersion = [int]$publishedTask.version
    if ($missingParameterUnits -gt 0) {
        $maxVersion = @($existingTasks.data | Where-Object { $_.taskId -eq "optical-lens-plc-001" } | Measure-Object -Property version -Maximum).Maximum
        $taskVersion = [int]$maxVersion + 1
    }
}
$task = [ordered]@{
    taskId = "optical-lens-plc-001"; version = $taskVersion; name = "光学镜片热压 PLC 采集"; status = "published"
    edgeId = $edgeId; protocol = "modbus-tcp"; dataModelId = "optical-lens-pmma"; dataModelVersion = 1
    source = "plc/optical-lens-press"; subjectType = "equipment"; subjectId = "OLP-01"
    modbusTcp = @{ host = "optical-lens-plc"; port = 1502; unitId = 1; addressBase = "zero-based"; pollIntervalMs = 500; maxMergeGap = 8 }
    execution = @{ timeoutMs = 5000; reconnectDelayMs = 2000; sourceIdentityStaleAfterMs = 60000; maximumFutureTimestampSkewMs = 300000 }
    timestampMode = "source"; timestampPath = "input-register:84:uint64:big-endian:high-low"; timestampEncoding = "unix-ms"
    sampleEventType = "process.sample"; staticContext = @{ simulation = "true"; material = "optical-grade-pmma" }
    contextMappings = @(
        @{ contextKey = "run_active"; sourcePath = "input-register:80.0:boolean"; required = $true }
        @{ contextKey = "external_batch_ref"; sourcePath = "input-register:90:string:20"; required = $true }
    )
    valueMappings = $valueMappings
    processSpecification = @{ eventType = "process.specification.applied"; idPath = "holding-register:160:string:28"; versionPath = "holding-register:176:uint16:big-endian:high-low"; namePath = "holding-register:180:string:28"; parametersPath = "."; parameterMappings = $parameterMappings }
    lifecycle = @{ mode = "discrete"; activeContextKey = "run_active"; activeValue = "true"; startedEventType = "process.execution.started"; completedEventType = "process.execution.completed"; stepChangedEventType = "process.stage_changed" }
}
$taskResult = Invoke-JsonApi -Method POST -Path "/api/v1/ingestion-tasks" -Body $task
if ($taskResult.valueMappings.Count -lt 10) { throw "采集任务未达到 10 个过程参数。" }
if ($taskResult.processSpecification.parameterMappings.Count -lt 15) { throw "采集任务未达到 15 个配方参数。" }

Write-Host "光学镜片演示配置已完成："
Write-Host "  PLC: optical-lens-plc:1502 (Modbus TCP)"
Write-Host "  过程数据: $($taskResult.valueMappings.Count) 项"
Write-Host "  配方参数: $($taskResult.processSpecification.parameterMappings.Count) 项"
Write-Host "  工装配件: $(@($revision.members).Count) 件"
Write-Host "  生产批次: OL25-420-DEMO-001 / 300 秒"
Write-Host "  采集任务: $($taskResult.taskId)@$($taskResult.version) / $($taskResult.status)"

if ($WaitForBatch) {
    Write-Host "等待一条完整五分钟批次结束（最长 $WaitTimeoutSeconds 秒）..."
    Start-Sleep -Seconds ([Math]::Min(12, [Math]::Max(1, [int]$envFile["INGOT_DEMO_PLC_IDLE_SECONDS"])))
    $deadline = (Get-Date).ToUniversalTime().AddSeconds($WaitTimeoutSeconds)
    do {
        Start-Sleep -Seconds 10
        $events = Invoke-JsonApi -Method GET -Path "/api/v1/events?siteId=$siteId&edgeId=$edgeId&subjectId=OLP-01&limit=5"
        $completed = @($events.data | Where-Object { $_.eventType -eq "process.execution.completed" })
    } while ($completed.Count -eq 0 -and (Get-Date).ToUniversalTime() -lt $deadline)
    if ($completed.Count -eq 0) { throw "五分钟批次未在等待时间内产生完成事件。" }
    Write-Host "已收到过程完成事件；可在 /process-executions、/comparisons 查看曲线和阶段。"
}
