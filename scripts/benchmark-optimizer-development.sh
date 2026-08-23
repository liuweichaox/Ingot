#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-${repo_root}/artifacts/optimizer-development.json}"

cd "${repo_root}"
uvx --from uv==0.11.32 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/development/benchmark_candidate_v7.py \
  --episodes 25 \
  --bootstrap-samples 5000 \
  --output "${output}"
