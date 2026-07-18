#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

port="${INGOT_BENCHMARK_PORT:-18080}"
events="${INGOT_BENCHMARK_EVENTS:-10000}"
edge_id="BENCH-$(date +%s)-$$"
token="benchmark-token"
central_log="$(mktemp -t ingot-central-benchmark.XXXXXX.log)"
central_pid=""
postgres_was_running=false
compose_file="docker-compose.app.yml"
postgres_password="${INGOT_BENCHMARK_POSTGRES_PASSWORD:-ingot}"

compose() {
  INGOT_POSTGRES_PASSWORD="$postgres_password" \
  INGOT_EDGE_TOKEN="benchmark-edge-token" \
  INGOT_OPERATOR_TOKEN="benchmark-operator-token" \
  INGOT_CONNECTOR_TOKEN="benchmark-connector-token" \
    docker compose -f "$compose_file" "$@"
}

cleanup() {
  if [[ -n "$central_pid" ]] && kill -0 "$central_pid" 2>/dev/null; then
    kill "$central_pid" 2>/dev/null || true
    wait "$central_pid" 2>/dev/null || true
  fi
  if [[ "$postgres_was_running" == false ]]; then
    compose stop postgres >/dev/null
  fi
  rm -f "$central_log"
}
trap cleanup EXIT

if docker inspect -f '{{.State.Running}}' ingot-postgres 2>/dev/null | grep -q true; then
  postgres_was_running=true
else
  compose up -d postgres
fi

for _ in $(seq 1 30); do
  if docker exec ingot-postgres pg_isready -U ingot -d ingot >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

dotnet build Ingot.sln --no-restore >/dev/null
dotnet run --project src/Ingot.Central.Api --no-build -- \
  "Urls=http://127.0.0.1:${port}" \
  "ConnectionStrings:Events=Host=localhost;Port=5432;Database=ingot;Username=ingot;Password=${postgres_password}" \
  "Central:DatabasePath=/tmp/ingot-central-benchmark.db" \
  "EventIngest:EdgeTokens:${edge_id}=${token}" \
  >"$central_log" 2>&1 &
central_pid=$!

for _ in $(seq 1 30); do
  if curl -fsS "http://127.0.0.1:${port}/health" >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$central_pid" 2>/dev/null; then
    cat "$central_log"
    exit 1
  fi
  sleep 1
done

curl -fsS "http://127.0.0.1:${port}/health" >/dev/null
dotnet run --project tools/Ingot.CentralBenchmarks --no-build -- \
  --central-url "http://127.0.0.1:${port}" \
  --edge-id "$edge_id" \
  --token "$token" \
  --events "$events" \
  --batch-size 500 \
  --minimum-rate 500 \
  --enforce

docker exec -i ingot-postgres psql -U ingot -d ingot \
  -v edge_id="$edge_id" < scripts/verify-event-integrity.sql

first_ingest_id="$(curl -fsSG \
  --data-urlencode "edgeId=${edge_id}" \
  --data-urlencode "afterIngestId=0" \
  --data-urlencode "limit=1" \
  "http://127.0.0.1:${port}/api/v1/events" |
  jq -er '.data[0].ingestId')"
sse_output="$(mktemp -t ingot-central-sse.XXXXXX)"
set +e
curl -sN --max-time 3 \
  -H "Last-Event-ID: ${first_ingest_id}" \
  --get \
  --data-urlencode "edgeId=${edge_id}" \
  "http://127.0.0.1:${port}/api/v1/events/stream" \
  >"$sse_output"
sse_status=$?
set -e
if (( sse_status != 0 && sse_status != 28 )); then
  echo "Central SSE request failed with curl status ${sse_status}." >&2
  rm -f "$sse_output"
  exit 1
fi

next_ingest_id="$(awk '/^id: / { sub(/^id: /, ""); print; exit }' "$sse_output")"
first_sse_event="$(awk '/^data: / { sub(/^data: /, ""); print; exit }' "$sse_output")"
if [[ -z "$next_ingest_id" || -z "$first_sse_event" ]] ||
   (( next_ingest_id <= first_ingest_id )); then
  echo "Central SSE did not resume after Last-Event-ID=${first_ingest_id}." >&2
  cat "$sse_output" >&2
  rm -f "$sse_output"
  exit 1
fi
jq -e --arg edge_id "$edge_id" '.edgeId == $edge_id' \
  <<<"$first_sse_event" >/dev/null
rm -f "$sse_output"
echo "Central SSE resume: PASS (cursor ${first_ingest_id} -> ${next_ingest_id})"
