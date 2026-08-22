#!/usr/bin/env bash
# 使用固定公开数据快照运行离线优化基准；不会连接任何工厂或真实生产系统。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-${repo_root}/artifacts/public-validation.json}"

cd "$repo_root"
uvx --from uv==0.11.32 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/benchmark.py \
  --seeds 20 \
  --budget 12 \
  --output "$output"
