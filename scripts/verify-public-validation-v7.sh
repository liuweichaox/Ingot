#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

uvx --from uv==0.11.32 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/benchmark_v7.py \
  --integrity-only
