#!/usr/bin/env bash
# Verify fresh acceptance data and protocol integrity without running outcomes.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

uvx --from uv==0.12.5 uv run \
  --project optimizer \
  --locked \
  python tools/public-validation/benchmark_acceptance.py \
  --integrity-only
