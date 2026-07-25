#!/usr/bin/env bash
# Ingot 数据体检 + 证据定级 —— 真实场景演示（不依赖 .NET 平台编译）。
# 用产品的实际迁移 schema 与实际定级探针 SQL，处理现场形态的光学模压 CSV，产出体检+定级报告。
#
# 用法（需本机 psql 与一个可连的 PostgreSQL）：
#   bash scripts/demo/run-health-demo.sh
# 环境变量：
#   PGDATABASE(默认 ingot_demo) PGHOST PGPORT PGUSER PGPASSWORD  —— psql 标准变量
#   ADMIN_DB(默认 postgres)  —— 用于 createdb 的管理连接库
#
# 生产环境用 timescaledb 镜像；本演示为在普通 PostgreSQL 上可跑，自动跳过 timescaledb 扩展行
# （hypertable 是性能优化，不影响体检/定级的正确性）。
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
MIG="$DIR/../../src/platform/Ingot.Platform.Infrastructure/Migrations/sql"
DB="${PGDATABASE:-ingot_demo}"
ADMIN_DB="${ADMIN_DB:-postgres}"
CSV="$DIR/sample-optical-molding.csv"

echo ">> 目标库: $DB   CSV: $(basename "$CSV")"

# 1) 组装 schema（真实迁移 0001-0003，去 timescaledb 扩展；补 pgcrypto 供 gen_random_uuid）
SCHEMA="$(mktemp)"
echo "CREATE EXTENSION IF NOT EXISTS pgcrypto;" > "$SCHEMA"
for m in 0001_baseline 0002_problem_cases 0003_local_identity; do
  sed 's/CREATE EXTENSION IF NOT EXISTS timescaledb;/-- timescaledb skipped (plain-PG demo)/' \
      "$MIG/$m.sql" >> "$SCHEMA"
done

# 2) 重建演示库
psql -d "$ADMIN_DB" -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS $DB;" -c "CREATE DATABASE $DB;"
psql -d "$DB" -v ON_ERROR_STOP=1 -q -f "$SCHEMA"
echo ">> schema 就绪：$(psql -d "$DB" -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'") 张表"
rm -f "$SCHEMA"

# 3) 跑体检+定级（health-demo.sql 内含 \copy 导入 + 探针 + 报告）
#    \copy 为客户端读取，用绝对 CSV 路径以避免工作目录差异
DEMO="$(mktemp)"
sed "s#sample-optical-molding.csv#$CSV#" "$DIR/health-demo.sql" > "$DEMO"
psql -d "$DB" -v ON_ERROR_STOP=1 -q -f "$DEMO"
rm -f "$DEMO"

echo ""
echo ">> 完成。这是核心链路（历史 CSV → 生产事件 → 数据体检 → 证据定级）在真实 PostgreSQL 上的运行结果。"
echo ">> 生成 HTML 报告：见 scripts/demo/health-report.html（可直接发给现场/管理层）。"
