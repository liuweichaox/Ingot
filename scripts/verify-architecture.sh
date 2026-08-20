#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

fail=0

check() {
  local name="$1" path="$2" pattern="$3" message="$4"
  local hits
  if [[ ! -e "$path" ]]; then
    echo "✗ [$name] 检查目标不存在: $path"
    fail=1
    return
  fi
  hits=$(grep -rnE "$pattern" "$path" --include='*.cs' --exclude-dir=bin --exclude-dir=obj 2>/dev/null || true)
  if [[ -n "$hits" ]]; then
    echo "✗ [$name] $message"
    echo "$hits" | sed 's/^/    /'
    fail=1
  else
    echo "✓ [$name]"
  fi
}

project_check() {
  local file="$1" pattern="$2" message="$3"
  if [[ ! -f "$file" ]]; then
    echo "✗ [csproj] 检查目标不存在: $file"
    fail=1
    return
  fi
  if grep -qE "$pattern" "$file"; then
    echo "✗ [csproj] $message ($file)"
    fail=1
  else
    echo "✓ [csproj] $(basename "$file")"
  fi
}

echo "== 源码依赖 =="

check "domain" src/shared/Ingot.Domain \
  'using (Npgsql|Microsoft\.(Data|Extensions|AspNetCore)|Serilog|Prometheus)' \
  "Domain 必须保持纯领域模型"

check "application" src/edge/Ingot.Edge.Application \
  'using (Ingot\.Edge\.(Infrastructure|ConnectorHost)|Ingot\.Platform|Npgsql|Microsoft\.Data|Serilog|Prometheus)' \
  "Application 必须保持实现中立"

check "platform-application" src/platform/Ingot.Platform.Application \
  'using (Ingot\.Platform\.Infrastructure|Npgsql|Microsoft\.(Data|Extensions|AspNetCore)|Serilog|Prometheus)|System\.Net\.Http\.Json' \
  "Platform Application 必须保持实现中立"

check "contracts" src/shared/Ingot.Contracts \
  'using (Npgsql|Microsoft\.(Data|AspNetCore|Extensions)|Serilog|Prometheus|Ingot\.(Platform|Edge|Agent))' \
  "Contracts 只允许依赖 Domain"

check "agent-contracts" src/shared/Ingot.Agent.Contracts \
  'using (Npgsql|Microsoft\.(Data|AspNetCore|Extensions)|Serilog|Prometheus|Ingot\.)' \
  "Agent Contracts 必须保持零依赖"

check "connector-host" src/edge/Ingot.Edge.ConnectorHost \
  'using (Npgsql|Microsoft\.Data\.Sqlite)' \
  "Connector Host 必须保持组合根职责"

check "platform-host" src/platform/Ingot.Platform.Api \
  'using (Npgsql|Microsoft\.Data\.Sqlite)|NpgsqlDataSource|SqliteConnection' \
  "Platform API 必须保持组合根职责"

check "platform-worker-host" src/platform/Ingot.Platform.Worker \
  'using (Npgsql|Microsoft\.Data\.Sqlite)|NpgsqlDataSource|SqliteConnection' \
  "Platform Worker 必须保持组合根职责"

check "platform-migrator-host" src/platform/Ingot.Platform.Migrator \
  'using (Npgsql|Microsoft\.Data\.Sqlite)|NpgsqlDataSource|SqliteConnection' \
  "Platform Migrator 必须保持组合根职责"

check "platform-infrastructure" src/platform/Ingot.Platform.Infrastructure \
  'using Ingot\.Platform\.Api' \
  "Platform Infrastructure 必须独立于 API 宿主"

check "inspection-infrastructure" src/platform/Ingot.Platform.Infrastructure/Inspections \
  'using Ingot\.Platform\.Api|namespace Ingot\.Platform\.Inspections\.Infrastructure' \
  "检验基础设施必须归属统一 Platform Infrastructure，且独立于 API 宿主"

legacy_inspection_files=$(find src/platform/Ingot.Platform.Inspections.Infrastructure \
  -maxdepth 1 -type f \( -name '*.cs' -o -name '*.csproj' \) -print 2>/dev/null || true)
if [[ -n "$legacy_inspection_files" ]]; then
  echo "✗ [inspection-module-ownership] 不得恢复独立检验基础设施程序集"
  echo "$legacy_inspection_files" | sed 's/^/    /'
  fail=1
else
  echo "✓ [inspection-module-ownership]"
fi

check "inspection-api-ports" src/platform/Ingot.Platform.Api/Controllers \
  'using Ingot\.Platform\.Infrastructure\.Inspections' \
  "检验 API 只能依赖 Application 端口，不能依赖检验基础设施命名空间"

check "api-application-boundary" src/platform/Ingot.Platform.Api/Controllers \
  '\bI[A-Z][A-Za-z0-9]*Store\b|\b(Postgres|Sqlite)[A-Za-z0-9]*Store\b' \
  "Controller 不得直接依赖存储端口或数据库 Store；必须通过 Platform Application 用例"

inspection_controller_writes=$(grep -rnE \
  '\b(store|records|reviews|attachments|masterData|workflow)\.(Create|Upsert|Delete|Save|LogAccess)[A-Za-z]*Async' \
  src/platform/Ingot.Platform.Api/Controllers/Inspection*.cs 2>/dev/null || true)
if [[ -n "$inspection_controller_writes" ]]; then
  echo "✗ [inspection-application-boundary] 检验写用例必须由 Platform Application 编排"
  echo "$inspection_controller_writes" | sed 's/^/    /'
  fail=1
else
  echo "✓ [inspection-application-boundary]"
fi

api_error_compat_hits=$(grep -rnE \
  'ApiProblemDetailsResultFilter|new \{[^}]*\berror\s*=|\b(BadRequest|Unauthorized|Conflict|NotFound)\(new' \
  src/platform/Ingot.Platform.Api --include='*.cs' --exclude-dir=bin --exclude-dir=obj 2>/dev/null || true)
if [[ -n "$api_error_compat_hits" ]]; then
  echo "✗ [typed-api-errors] API 错误必须直接返回类型化 Problem Details，不得恢复匿名错误兼容转换"
  echo "$api_error_compat_hits" | sed 's/^/    /'
  fail=1
else
  echo "✓ [typed-api-errors]"
fi

compatibility_hits=$(grep -rnE \
  'SqliteAgentStore|LegacySqliteAgentRunImporter|IAgentRunImportStore|ImportLegacySqlite|Chat:DatabasePath|IBatchedEventLog|CompatibleDateTimeOffsetConverter|ResearchExperimentCommandException|ResearchExperimentPlanValidationException|ApiProblemDetailsResultFilter' \
  src apps/platform/src docker-compose.app.yml scripts/run-platform-api.ps1 \
  --include='*.cs' --include='*.csproj' --include='*.json' --include='*.js' \
  --include='*.jsx' --include='*.yml' --include='*.ps1' \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules 2>/dev/null || true)
if [[ -n "$compatibility_hits" ]]; then
  echo "✗ [current-product-only] 新项目不得恢复开发期兼容路径"
  echo "$compatibility_hits" | sed 's/^/    /'
  fail=1
else
  echo "✓ [current-product-only]"
fi

baseline_upgrade_hits=$(grep -nE \
  '\b(RENAME|DROP (TABLE|VIEW|COLUMN))\b|to_regclass|legacy_' \
  src/platform/Ingot.Platform.Infrastructure/Migrations/sql/0001_baseline.sql 2>/dev/null || true)
if [[ -n "$baseline_upgrade_hits" ]]; then
  echo "✗ [fresh-schema-baseline] 新装基线不得包含旧 schema 的识别、改名或删除逻辑"
  echo "$baseline_upgrade_hits" | sed 's/^/    /'
  fail=1
else
  echo "✓ [fresh-schema-baseline]"
fi

research_inspection_hits=$(grep -rnE \
  'using (Ingot\.Contracts\.Inspections|Ingot\.Platform\.Infrastructure\.Inspections)' \
  src/platform/Ingot.Platform.Infrastructure/ProcessResearch \
  --include='*.cs' --exclude='ResearchObservationAssembler.cs' 2>/dev/null || true)
if [[ -n "$research_inspection_hits" ]]; then
  echo "✗ [process-research-context-matrix] 研究规则不得直接读取检验上下文；请通过已登记适配器装配证据"
  echo "$research_inspection_hits" | sed 's/^/    /'
  fail=1
else
  echo "✓ [process-research-context-matrix]"
fi

check "process-research-application-rules" src/platform/Ingot.Platform.Application/ProcessResearch \
  'using (Ingot\.Contracts\.Inspections|Ingot\.Platform\.Infrastructure\.Inspections)' \
  "Application 研究规则不得直接读取检验上下文"

check "application-context-direction" src/platform/Ingot.Platform.Application/ResearchAssets \
  'using Ingot\.Platform\.Application\.ProcessResearch' \
  "ResearchAssets 不得反向依赖 ProcessResearch；项目上下文必须通过窄端口读取"

unexpected_research_infrastructure=$(find src/platform/Ingot.Platform.Infrastructure/ProcessResearch \
  -type f -name '*.cs' \
  ! -name 'PostgresProcessResearchStore.cs' \
  ! -name 'ProcessOptimizerCircuitBreakerHandler.cs' \
  ! -name 'ProcessOptimizerClient.cs' \
  ! -name 'ResearchExperimentAutomationHostedService.cs' \
  ! -name 'ResearchObservationAssembler.cs' \
  ! -name 'ProcessResearchModuleServiceCollectionExtensions.cs' \
  -print)
if [[ -n "$unexpected_research_infrastructure" ]]; then
  echo "✗ [process-research-module-ownership] 研究规则与存储端口必须归属 Platform Application"
  echo "$unexpected_research_infrastructure" | sed 's/^/    /'
  fail=1
else
  echo "✓ [process-research-module-ownership]"
fi

unexpected_inspection_infrastructure=$(find src/platform/Ingot.Platform.Infrastructure/Inspections \
  -type f -name '*.cs' \
  ! -name 'InspectionProductionEventReader.cs' \
  ! -name 'InspectionAttachmentOptions.cs' \
  ! -name 'InspectionModuleServiceCollectionExtensions.cs' \
  ! -name 'InspectionStoreInitializerHostedService.cs' \
  ! -name 'PostgresInspectionAttachmentStore.cs' \
  ! -name 'PostgresInspectionMasterDataStore.cs' \
  ! -name 'PostgresInspectionRecordStore.cs' \
  ! -name 'PostgresInspectionReviewStore.cs' \
  -print)
if [[ -n "$unexpected_inspection_infrastructure" ]]; then
  echo "✗ [inspection-workflow-ownership] 检验规则与工作流必须归属 Platform Application"
  echo "$unexpected_inspection_infrastructure" | sed 's/^/    /'
  fail=1
else
  echo "✓ [inspection-workflow-ownership]"
fi

process_execution_port_leaks=$(grep -rnE \
  'public interface (IExecutionComparisonService|IProcessExecutionService|ITimeWindowComparisonService|ITimeSeriesStore|IProcessExecutionAnalysisOperationsStore)' \
  src/platform/Ingot.Platform.Infrastructure --include='*.cs' 2>/dev/null || true)
if [[ -n "$process_execution_port_leaks" ]]; then
  echo "✗ [process-execution-port-ownership] 运行查询与比较端口必须归属 Platform Application"
  echo "$process_execution_port_leaks" | sed 's/^/    /'
  fail=1
else
  echo "✓ [process-execution-port-ownership]"
fi

process_execution_api_store_leaks=$(grep -rnE \
  '\b(PostgresExecutionBoundaryStore|IProcessExecutionAnalysisMaterializationStore|ITimeSeriesStore)\b' \
  src/platform/Ingot.Platform.Api/Controllers --include='*.cs' 2>/dev/null || true)
if [[ -n "$process_execution_api_store_leaks" ]]; then
  echo "✗ [process-execution-api-boundary] 运行 API 必须通过 Application 用例，不能直接注入存储实现"
  echo "$process_execution_api_store_leaks" | sed 's/^/    /'
  fail=1
else
  echo "✓ [process-execution-api-boundary]"
fi

research_asset_rule_leaks=$(grep -rnE \
  'public sealed class (ResearchAssetWorkflow|MechanismModelService|MechanismKnowledgeService)' \
  src/platform/Ingot.Platform.Infrastructure --include='*.cs' 2>/dev/null || true)
if [[ -n "$research_asset_rule_leaks" ]]; then
  echo "✗ [research-assets-application-boundary] 研究资产规则与工作流必须归属 Platform Application"
  echo "$research_asset_rule_leaks" | sed 's/^/    /'
  fail=1
else
  echo "✓ [research-assets-application-boundary]"
fi

application_port_leaks=$(grep -rnE \
  'public interface (IPlatformEventStore|IIngestionTaskStore|IIngestionConfigurationStore|IAcquisitionProbeTaskStore|IManufacturingContextStore|IQualityAnalysisService|IGoldenQuestionStore|ILocalUserStore)' \
  src/platform/Ingot.Platform.Infrastructure --include='*.cs' 2>/dev/null || true)
if [[ -n "$application_port_leaks" ]]; then
  echo "✗ [platform-application-port-ownership] 数据库无关的 Platform 存储端口必须归属 Application"
  echo "$application_port_leaks" | sed 's/^/    /'
  fail=1
else
  echo "✓ [platform-application-port-ownership]"
fi

application_rule_leaks=$(grep -rnE \
  'public (sealed |static )?class (ProcessAnalysisResolver|ProcessExecutionAnalysisEngine|ExecutionDiagnosisEngine|ExecutionInvestigationReportBuilder|BuiltInFeatureDefinitionRegistry|GoldenQuestionEvaluator|AcquisitionProbeTaskCoordinator)' \
  src/platform/Ingot.Platform.Infrastructure --include='*.cs' 2>/dev/null || true)
if [[ -n "$application_rule_leaks" ]]; then
  echo "✗ [platform-application-rule-ownership] 可脱库测试的应用规则必须归属 Application"
  echo "$application_rule_leaks" | sed 's/^/    /'
  fail=1
else
  echo "✓ [platform-application-rule-ownership]"
fi

acquisition_controller_writes=$(grep -nE \
  '\b(store|taskStore|processStore|probeTasks)\.(Upsert|Publish|Save|Delete|QueueAndWait)[A-Za-z]*Async' \
  src/platform/Ingot.Platform.Api/Controllers/IngestionConfigurationController.cs 2>/dev/null || true)
if [[ -n "$acquisition_controller_writes" ]]; then
  echo "✗ [acquisition-application-boundary] 采集配置写用例必须由 Application 工作流编排"
  echo "$acquisition_controller_writes" | sed 's/^/    /'
  fail=1
else
  echo "✓ [acquisition-application-boundary]"
fi

api_module_implementation_leaks=$(grep -rnE \
  '^using Ingot\.Platform\.Infrastructure\.(Acquisition|Analytics|Manufacturing|Insight|ProcessConfiguration|ProcessExecutions|ProcessResearch);' \
  src/platform/Ingot.Platform.Api/Controllers --include='*.cs' 2>/dev/null || true)
if [[ -n "$api_module_implementation_leaks" ]]; then
  echo "✗ [platform-api-application-boundary] 业务 Controller 必须通过 Application 用例或端口，不得直接依赖模块实现"
  echo "$api_module_implementation_leaks" | sed 's/^/    /'
  fail=1
else
  echo "✓ [platform-api-application-boundary]"
fi

api_worker_hits=$(grep -nE \
  'AddIngotPlatformWorkers|AddIngotLocalIdentityMaintenance|AdminSeederHostedService|SessionPruneHostedService' \
  src/platform/Ingot.Platform.Api/Program.cs 2>/dev/null || true)
if [[ -n "$api_worker_hits" ]]; then
  echo "✗ [stateless-api-host] Platform API 不得注册持久化后台任务或部署引导任务"
  echo "$api_worker_hits" | sed 's/^/    /'
  fail=1
else
  echo "✓ [stateless-api-host]"
fi

if grep -q 'AdminSeederHostedService' \
  src/platform/Ingot.Platform.Infrastructure/Identity/LocalIdentityHostedServices.cs 2>/dev/null; then
  echo "✗ [identity-bootstrap-ownership] 首用户引导必须归属 Migrator，不得恢复为 API HostedService"
  fail=1
else
  echo "✓ [identity-bootstrap-ownership]"
fi

store_schema_ddl=$(grep -rnE \
  '(CREATE TABLE|CREATE (UNIQUE )?INDEX|ALTER TABLE|create_hypertable|add_[a-z_]*policy)' \
  src/platform/Ingot.Platform.Infrastructure \
  --include='*.cs' --exclude-dir=Migrations --exclude-dir=bin --exclude-dir=obj 2>/dev/null || true)
if [[ -n "$store_schema_ddl" ]]; then
  echo "✗ [postgres-schema-ownership] PostgreSQL user schema 只能由版本化迁移定义"
  echo "$store_schema_ddl" | sed 's/^/    /'
  fail=1
else
  echo "✓ [postgres-schema-ownership]"
fi

check "agent-core" src/agent/Ingot.Agent \
  'using (Ingot\.Platform|Npgsql|Microsoft\.Agents|OpenAI)' \
  "Agent 核心必须保持模型和存储中立"

check "agent-providers" src/agent/Ingot.Agent.Providers \
  'using Ingot\.Platform' \
  "Agent Providers 必须独立于 Platform 实现"

check "analysis-tools" src/platform/Ingot.Platform.Infrastructure/AgentTools \
  '(INSERT|UPDATE|DELETE|ExecuteNonQuery|Http(Post|Put|Patch|Delete)|WriteAsync)' \
  "记录分析工具必须保持查询职责"

check "edge-infrastructure" src/edge/Ingot.Edge.Infrastructure \
  'using (Ingot\.Platform|Ingot\.Edge\.ConnectorHost)' \
  "边缘基础设施必须独立于宿主和 Platform 实现"

check "connector-contract" src \
  'IPlc|Plc(Read|Write)|WriteRegister|Read(UShort|UInt|ULong|Short|Int|Long|Float|Double|String|Bool)Async' \
  "核心源码必须保持连接器协议中立"

echo "== 工程依赖 =="

project_check src/shared/Ingot.Domain/Ingot.Domain.csproj \
  '<(PackageReference|ProjectReference)' \
  "Domain 必须保持零引用"

project_check src/shared/Ingot.Agent.Contracts/Ingot.Agent.Contracts.csproj \
  '<(PackageReference|ProjectReference)' \
  "Agent Contracts 必须保持零引用"

project_check src/platform/Ingot.Platform.Application/Ingot.Platform.Application.csproj \
  'Ingot\.Platform\.Infrastructure|Npgsql|Microsoft\.Data\.Sqlite|Serilog|Prometheus' \
  "Platform Application 必须独立于基础设施实现"

project_check src/platform/Ingot.Platform.Api/Ingot.Platform.Api.csproj \
  'Npgsql|Microsoft\.Data\.Sqlite' \
  "Platform API 的存储实现必须位于 Platform Infrastructure"

project_check src/platform/Ingot.Platform.Worker/Ingot.Platform.Worker.csproj \
  'Npgsql|Microsoft\.Data\.Sqlite' \
  "Platform Worker 的存储实现必须位于 Platform Infrastructure"

project_check src/platform/Ingot.Platform.Migrator/Ingot.Platform.Migrator.csproj \
  'Npgsql|Microsoft\.Data\.Sqlite' \
  "Platform Migrator 的存储实现必须位于 Platform Infrastructure"

project_check src/edge/Ingot.Edge.ConnectorHost/Ingot.Edge.ConnectorHost.csproj \
  'Npgsql|Microsoft\.Data\.Sqlite' \
  "Connector Host 的存储实现必须位于 Infrastructure"

project_check src/agent/Ingot.Agent/Ingot.Agent.csproj \
  'Ingot\.(Contracts|Platform|Agent\.Providers|Edge\.ConnectorHost)|Npgsql|Microsoft\.Data\.Sqlite|Microsoft\.Agents|OpenAI' \
  "Agent 核心必须只依赖 Agent Contracts"

project_check src/agent/Ingot.Agent.Providers/Ingot.Agent.Providers.csproj \
  'Ingot\.Platform' \
  "Agent Providers 必须独立于 Platform 实现"

project_check src/platform/Ingot.Platform.Infrastructure/Ingot.Platform.Infrastructure.csproj \
  'Ingot\.Platform\.Api' \
  "Platform Infrastructure 必须独立于 API 宿主"

if [[ "$fail" -ne 0 ]]; then
  echo "架构边界检查失败。"
  exit 1
fi

echo "架构边界检查通过。"
