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
  'using (Ingot\.Platform\.Infrastructure|Npgsql|Microsoft\.Data|Serilog|Prometheus)' \
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

compatibility_hits=$(grep -rnE \
  'SqliteAgentStore|LegacySqliteAgentRunImporter|IAgentRunImportStore|ImportLegacySqlite|Chat:DatabasePath|IBatchedEventLog|CompatibleDateTimeOffsetConverter|ResearchExperimentCommandException|ResearchExperimentPlanValidationException' \
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
