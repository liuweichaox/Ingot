#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

port="${INGOT_SAMPLE_BENCHMARK_PORT:-18081}"
events="${INGOT_SAMPLE_BENCHMARK_EVENTS:-1000}"
batch_size="${INGOT_SAMPLE_BENCHMARK_BATCH_SIZE:-100}"
signals="${INGOT_SAMPLE_BENCHMARK_SIGNALS:-15}"
samples_per_execution="${INGOT_SAMPLE_BENCHMARK_SAMPLES_PER_EXECUTION:-1000}"
edge_id="SAMPLE-BENCH-$(date +%s)-$$"
lifecycle_edge_id="LIFECYCLE-BENCH-$(date +%s)-$$"
site_id="SITE-BENCHMARK"
token="sample-benchmark-token"
database="ingot_sample_benchmark_$(date +%s)_$$"
database_role="ingot_sample_bench_$(date +%s)_$$"
database_password="sample_bench_$(date +%s)_$$_password"
platform_container="ingot-platform-sample-benchmark-$$"
database_created=false
database_role_created=false
postgres_was_running=false
compose_file="docker-compose.app.yml"
dotnet_command="${INGOT_DOTNET_COMMAND:-dotnet}"

if [[ ! "$database" =~ ^ingot_sample_benchmark_[0-9]+_[0-9]+$ ||
      ! "$database_role" =~ ^ingot_sample_bench_[0-9]+_[0-9]+$ ||
      ! "$database_password" =~ ^sample_bench_[0-9]+_[0-9]+_password$ ]]; then
  echo "Unsafe benchmark database identity." >&2
  exit 1
fi

compose() {
  INGOT_POSTGRES_PASSWORD="sample-benchmark-compose-placeholder" \
  INGOT_SITE_ID="$site_id" \
  INGOT_EDGE_ID="sample-benchmark-edge" \
  INGOT_EDGE_TOKEN="sample-benchmark-edge-token" \
  INGOT_CONNECTOR_TOKEN="sample-benchmark-connector-token" \
    docker compose -f "$compose_file" "$@"
}

cleanup() {
  docker rm -f "$platform_container" >/dev/null 2>&1 || true
  if [[ "$database_created" == true ]]; then
    docker exec ingot-postgres dropdb -U ingot --if-exists --force "$database" >/dev/null 2>&1 || true
  fi
  if [[ "$database_role_created" == true ]]; then
    docker exec ingot-postgres psql -U ingot -d postgres \
      -c "DROP ROLE IF EXISTS ${database_role};" >/dev/null 2>&1 || true
  fi
  if [[ "$postgres_was_running" == false ]]; then
    compose stop postgres >/dev/null
  fi
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

docker exec ingot-postgres psql -U ingot -d postgres \
  -c "CREATE ROLE ${database_role} LOGIN PASSWORD '${database_password}';" >/dev/null
database_role_created=true
docker exec ingot-postgres createdb -U ingot -O "$database_role" "$database"
database_created=true
docker exec ingot-postgres psql -U ingot -d "$database" \
  -c 'CREATE EXTENSION IF NOT EXISTS timescaledb;' >/dev/null

if ! command -v "$dotnet_command" >/dev/null 2>&1; then
  windows_dotnet="/mnt/c/Program Files/dotnet/dotnet.exe"
  if [[ -x "$windows_dotnet" ]]; then
    dotnet_command="$windows_dotnet"
  else
    echo "dotnet SDK was not found. Set INGOT_DOTNET_COMMAND to an executable SDK path." >&2
    exit 1
  fi
fi

"$dotnet_command" build tools/Ingot.PlatformBenchmarks/Ingot.PlatformBenchmarks.csproj --no-restore >/dev/null
compose build platform-api >/dev/null
platform_network="$(docker inspect -f '{{range $key, $value := .NetworkSettings.Networks}}{{$key}}{{end}}' ingot-postgres)"

docker run -d \
  --name "$platform_container" \
  --network "$platform_network" \
  -p "127.0.0.1:${port}:8000" \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e Urls=http://+:8000 \
  -e "ConnectionStrings__Events=Host=ingot-postgres;Port=5432;Database=${database};Username=${database_role};Password=${database_password}" \
  -e "EventIngest__EdgeTokens__${edge_id}=${token}" \
  -e "EventIngest__EdgeTokens__${lifecycle_edge_id}=${token}" \
  -e "EventIngest__EdgeSites__${edge_id}=${site_id}" \
  -e "EventIngest__EdgeSites__${lifecycle_edge_id}=${site_id}" \
  -e Authentication__Mode=Disabled \
  ingot-platform-api:latest >/dev/null

for _ in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:${port}/health" >/dev/null 2>&1; then
    break
  fi
  if ! docker inspect -f '{{.State.Running}}' "$platform_container" 2>/dev/null | grep -q true; then
    docker logs "$platform_container" 2>&1 || true
    exit 1
  fi
  sleep 1
done

curl -fsS "http://127.0.0.1:${port}/health" >/dev/null

echo "Lifecycle baseline:"
"$dotnet_command" run --project tools/Ingot.PlatformBenchmarks --no-build -- \
  --platform-url "http://127.0.0.1:${port}" \
  --site-id "$site_id" \
  --edge-id "$lifecycle_edge_id" \
  --token "$token" \
  --events "$events" \
  --batch-size "$batch_size" \
  --shape lifecycle \
  --minimum-rate 1

echo "Process-sample projection:"
wal_before="$(docker exec ingot-postgres psql -U ingot -d "$database" -Atc 'SELECT pg_current_wal_lsn();')"
size_before="$(docker exec ingot-postgres psql -U ingot -d "$database" -Atc 'SELECT pg_database_size(current_database());')"
if [[ ! "$edge_id" =~ ^SAMPLE-BENCH-[0-9]+-[0-9]+$ ||
      ! "$wal_before" =~ ^[0-9A-F]+/[0-9A-F]+$ ]]; then
  echo "Unsafe benchmark query identity." >&2
  exit 1
fi

"$dotnet_command" run --project tools/Ingot.PlatformBenchmarks --no-build -- \
  --platform-url "http://127.0.0.1:${port}" \
  --site-id "$site_id" \
  --edge-id "$edge_id" \
  --token "$token" \
  --events "$events" \
  --batch-size "$batch_size" \
  --shape process-sample \
  --signals "$signals" \
  --samples-per-execution "$samples_per_execution" \
  --minimum-rate 1

expected_signal_rows=$((events * signals))
IFS='|' read -r event_count execution_count signal_rows signal_count <<<"$(
  docker exec ingot-postgres psql -U ingot -d "$database" -At \
    -c "SELECT
          (SELECT count(*) FROM production_events WHERE site_id = '${site_id}' AND edge_id = '${edge_id}'),
          (SELECT count(DISTINCT execution_id) FROM production_events WHERE site_id = '${site_id}' AND edge_id = '${edge_id}'),
          (SELECT count(*) FROM time_series_samples WHERE site_id = '${site_id}' AND edge_id = '${edge_id}'),
          (SELECT count(DISTINCT signal_code) FROM time_series_samples WHERE site_id = '${site_id}' AND edge_id = '${edge_id}');"
)"

wal_bytes="$(docker exec ingot-postgres psql -U ingot -d "$database" -At \
  -c "SELECT pg_wal_lsn_diff(pg_current_wal_lsn(), '${wal_before}')::bigint;")"
size_after="$(docker exec ingot-postgres psql -U ingot -d "$database" -Atc 'SELECT pg_database_size(current_database());')"

echo "Integrity:"
echo "  production_events: ${event_count}/${events}"
echo "  executions: ${execution_count}"
echo "  time_series_samples: ${signal_rows}/${expected_signal_rows}"
echo "  distinct signals: ${signal_count}/${signals}"
echo "Storage:"
echo "  database growth bytes: $((size_after - size_before))"
echo "  WAL growth bytes: ${wal_bytes}"

if [[ "$event_count" -ne "$events" ||
      "$signal_rows" -ne "$expected_signal_rows" ||
      "$signal_count" -ne "$signals" ]]; then
  echo "Process-sample benchmark integrity check failed." >&2
  echo "Platform log:" >&2
  docker logs --tail 100 "$platform_container" >&2
  exit 1
fi

echo "Process-sample projection integrity: PASS"
