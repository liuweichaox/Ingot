#!/usr/bin/env bash
# Run the current optimizer only against already inspected development fixtures.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-${repo_root}/artifacts/optimizer-development.json}"

cd "${repo_root}"
uvx --from uv==0.11.32 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/development/benchmark_development.py \
  --episodes 150 \
  --bootstrap-samples 5000 \
  --output "${output}"
