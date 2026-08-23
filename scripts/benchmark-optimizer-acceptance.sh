#!/usr/bin/env bash
# Run the frozen fresh-data acceptance and retain its complete paired traces.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-${repo_root}/tools/public-validation/acceptance-results.json}"

cd "${repo_root}"
uvx --from uv==0.11.32 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/benchmark_acceptance.py \
  --output "${output}"
