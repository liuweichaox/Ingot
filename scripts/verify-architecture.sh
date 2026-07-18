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

check "domain" src/Ingot.Domain \
  'using (Npgsql|Microsoft\.(Data|Extensions|AspNetCore)|Serilog|Prometheus)' \
  "Domain 必须保持纯领域模型"

check "application" src/Ingot.Application \
  'using (Ingot\.Infrastructure|Ingot\.Central|Ingot\.Edge|Npgsql|Microsoft\.Data|Serilog|Prometheus)' \
  "Application 必须保持实现中立"

check "contracts" src/Ingot.Contracts \
  'using (Npgsql|Microsoft\.(Data|AspNetCore|Extensions)|Serilog|Prometheus|Ingot\.Infrastructure|Ingot\.Central|Ingot\.Edge|Ingot\.Application)' \
  "Contracts 只允许依赖 Domain"

check "connector-host" src/Ingot.Connector.Host \
  'using (Npgsql|Microsoft\.Data\.Sqlite)' \
  "Connector Host 必须保持组合根职责"

check "central-host" src/Ingot.Central.Api \
  'using (Npgsql|Microsoft\.Data\.Sqlite)|NpgsqlDataSource|SqliteConnection' \
  "Central API 必须保持组合根职责"

check "central-infrastructure" src/Ingot.Central.Infrastructure \
  'using Ingot\.Central\.Api' \
  "Central Infrastructure 必须独立于 API 宿主"

check "agent-core" src/Ingot.Agent \
  'using (Ingot\.Central|Npgsql|Microsoft\.Agents|OpenAI)' \
  "Agent 核心必须保持模型和存储中立"

check "agent-infrastructure" src/Ingot.Agent.Infrastructure \
  'using Ingot\.Central' \
  "Agent Infrastructure 必须独立于 Central 实现"

check "analysis-tools" src/Ingot.Central.Infrastructure/AgentTools \
  '(INSERT|UPDATE|DELETE|ExecuteNonQuery|Http(Post|Put|Patch|Delete)|WriteAsync)' \
  "事实分析工具必须保持查询职责"

check "edge-infrastructure" src/Ingot.Infrastructure \
  'using (Ingot\.Central|Ingot\.Connector\.Host)' \
  "边缘基础设施必须独立于宿主和中心实现"

check "connector-contract" src \
  'IPlc|Plc(Read|Write)|WriteRegister|Read(UShort|UInt|ULong|Short|Int|Long|Float|Double|String|Bool)Async' \
  "核心源码必须保持连接器协议中立"

echo "== 工程依赖 =="

project_check src/Ingot.Domain/Ingot.Domain.csproj \
  '<(PackageReference|ProjectReference)' \
  "Domain 必须保持零引用"

project_check src/Ingot.Central.Api/Ingot.Central.Api.csproj \
  'Npgsql|Microsoft\.Data\.Sqlite' \
  "Central API 的存储实现必须位于 Central Infrastructure"

project_check src/Ingot.Connector.Host/Ingot.Connector.Host.csproj \
  'Npgsql|Microsoft\.Data\.Sqlite' \
  "Connector Host 的存储实现必须位于 Infrastructure"

project_check src/Ingot.Agent/Ingot.Agent.csproj \
  'Ingot\.(Central|Agent\.Infrastructure|Connector\.Builder)|Npgsql|Microsoft\.Data\.Sqlite|Microsoft\.Agents|OpenAI' \
  "Agent 核心必须只依赖公共契约"

project_check src/Ingot.Agent.Infrastructure/Ingot.Agent.Infrastructure.csproj \
  'Ingot\.Central' \
  "Agent Infrastructure 必须独立于 Central 实现"

project_check src/Ingot.Central.Infrastructure/Ingot.Central.Infrastructure.csproj \
  'Ingot\.Central\.Api' \
  "Central Infrastructure 必须独立于 API 宿主"

if [[ "$fail" -ne 0 ]]; then
  echo "架构边界检查失败。"
  exit 1
fi

echo "架构边界检查通过。"
