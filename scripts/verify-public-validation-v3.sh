#!/usr/bin/env bash
# 校验 v3 公开评估协议、固定数据和机理特征定义；草案阶段不运行结果评估。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$repo_root"
uvx --from uv==0.11.32 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/benchmark_v3.py \
  --integrity-only
