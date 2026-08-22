#!/usr/bin/env bash
# 运行已冻结的 v3 公开物理实验评估；草案协议会被评估器拒绝。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-${repo_root}/artifacts/public-validation-v3.json}"

cd "$repo_root"
uvx --from uv==0.11.32 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/benchmark_v3.py \
  --output "$output"
