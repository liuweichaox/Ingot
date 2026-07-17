#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

rows="${INGOT_BENCHMARK_ROWS:-1000000}"
query_samples="${INGOT_BENCHMARK_QUERY_SAMPLES:-100}"
append_samples="${INGOT_BENCHMARK_APPEND_SAMPLES:-1000}"
memory_limit_mb="${INGOT_BENCHMARK_MEMORY_LIMIT_MB:-50}"

dotnet build tools/Ingot.EventBenchmarks/Ingot.EventBenchmarks.csproj --no-restore >/dev/null
dotnet run --project tools/Ingot.EventBenchmarks --no-build -- \
  --rows "$rows" \
  --query-samples "$query_samples" \
  --append-samples "$append_samples" \
  --memory-limit-mb "$memory_limit_mb" \
  --enforce
