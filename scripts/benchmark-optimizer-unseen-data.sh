#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-${repo_root}/artifacts/optimizer-unseen-data.json}"

cd "${repo_root}"
uvx --from uv==0.11.32 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/benchmark_unseen.py \
  --output "${output}"
