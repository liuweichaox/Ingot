#!/usr/bin/env bash
# 迁移一致性守卫（复审 §2.1 的地雷防护）。
# 不变式：每一张由 Store 启动初始化器 DDL 创建的表，都必须被某个迁移脚本覆盖。
# 迁移是 schema 的权威前向来源；若初始化器引入了迁移未覆盖的表，即视为漂移，构建失败。
# （过渡期：初始化器 DDL 仍在，作为无害幂等冗余；本守卫确保它们不会与迁移分叉。）
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
INFRA="$ROOT/src/platform/Ingot.Platform.Infrastructure"
MIG_DIR="$INFRA/Migrations/sql"

extract_tables() {
  # 从给定文件集中抽取 "CREATE TABLE IF NOT EXISTS <name>" 的表名，去重排序
  grep -rhoiE "CREATE TABLE IF NOT EXISTS[[:space:]]+[a-z_]+" "$@" 2>/dev/null \
    | awk '{print tolower($NF)}' | sort -u
}

migration_tables="$(extract_tables "$MIG_DIR")"

# 只统计 PostgreSQL 初始化器：仅取同时引用 Npgsql 的 .cs 文件里的 CREATE TABLE。
# 这样排除 SQLite 存储（EdgeRegistry 等，用 Microsoft.Data.Sqlite，不属于 Postgres 迁移范围）。
pg_ddl_files="$(grep -rlE "CREATE TABLE IF NOT EXISTS" --include='*.cs' "$INFRA" 2>/dev/null \
  | while read -r f; do grep -q "Npgsql" "$f" && echo "$f"; done)"
initializer_tables="$( { [ -n "$pg_ddl_files" ] && extract_tables $pg_ddl_files; } \
  | grep -vxE "schema_version" | sort -u)"  # schema_version 为迁移器自身账本表，非业务迁移

# 初始化器创建但迁移未覆盖的表 = 漂移
drift="$(comm -23 <(printf '%s\n' "$initializer_tables") <(printf '%s\n' "$migration_tables") || true)"

if [ -n "$drift" ]; then
  echo "ERROR: 以下表由 Store 初始化器创建，但没有对应迁移脚本覆盖（schema 漂移）：" >&2
  echo "$drift" | sed 's/^/  - /' >&2
  echo "请为这些表新增编号迁移脚本，使迁移成为 schema 的完整权威来源。" >&2
  exit 1
fi

mig_count="$(printf '%s\n' "$migration_tables" | grep -c . || true)"
init_count="$(printf '%s\n' "$initializer_tables" | grep -c . || true)"
echo "OK: 迁移覆盖 $mig_count 张表；初始化器 $init_count 张表全部被迁移覆盖，无漂移。"
