#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

disconnect_seconds="${INGOT_DISCONNECT_SECONDS:-7200}"
warmup_seconds="${INGOT_DISCONNECT_WARMUP_SECONDS:-12}"
recovery_timeout="${INGOT_DISCONNECT_RECOVERY_TIMEOUT_SECONDS:-180}"
central_port="${INGOT_DISCONNECT_CENTRAL_PORT:-18180}"
edge_port="${INGOT_DISCONNECT_EDGE_PORT:-18181}"
simulator_port="${INGOT_DISCONNECT_SIMULATOR_PORT:-15020}"
scenario_speed="${INGOT_DISCONNECT_SCENARIO_SPEED:-4}"
edge_id="DISCONNECT-$(date +%s)-$$"
token="disconnect-acceptance-token"
run_dir="$(mktemp -d -t ingot-disconnect.XXXXXX)"
config_dir="$run_dir/configs"
events_db="$run_dir/events.db"
central_log="$run_dir/central.log"
edge_log="$run_dir/edge.log"
simulator_log="$run_dir/simulator.log"
central_pid=""
edge_pid=""
simulator_pid=""
postgres_was_running=false
succeeded=false

if [[ ! "$disconnect_seconds" =~ ^[0-9]+$ ]] || (( disconnect_seconds < 1 )); then
  echo "INGOT_DISCONNECT_SECONDS must be a positive integer." >&2
  exit 2
fi

mkdir -p "$config_dir"
sed "s/\"Port\": 502/\"Port\": ${simulator_port}/" \
  examples/scenarios/optical-polisher.v2.json \
  > "$config_dir/optical-polisher.v2.json"

stop_process() {
  local pid="${1:-}"
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
}

cleanup() {
  stop_process "$simulator_pid"
  stop_process "$edge_pid"
  stop_process "$central_pid"
  if [[ "$postgres_was_running" == false ]]; then
    docker compose -f docker-compose.events.yml stop postgres >/dev/null
  fi

  if [[ "$succeeded" == true && "${INGOT_KEEP_ACCEPTANCE_DATA:-false}" != true ]]; then
    rm -rf "$run_dir"
  else
    echo "Acceptance artifacts: $run_dir"
  fi
}
trap cleanup EXIT

wait_for_http() {
  local url="$1"
  local pid="$2"
  local log="$3"
  local attempts="${4:-60}"
  for _ in $(seq 1 "$attempts"); do
    if curl -fsS "$url" >/dev/null 2>&1; then
      return 0
    fi
    if ! kill -0 "$pid" 2>/dev/null; then
      cat "$log"
      return 1
    fi
    sleep 1
  done
  echo "Timed out waiting for $url" >&2
  tail -100 "$log" >&2 || true
  return 1
}

local_count() {
  sqlite3 "$events_db" "SELECT COUNT(*) FROM events;"
}

pending_count() {
  sqlite3 "$events_db" "SELECT COUNT(*) FROM events WHERE ship_state = 0;"
}

central_count() {
  docker exec ingot-postgres psql -U ingot -d ingot -At \
    -c "SELECT COUNT(*) FROM production_events WHERE edge_id = '${edge_id}';"
}

start_central() {
  dotnet run --project src/Ingot.Central.Api --no-build -- \
    "Urls=http://127.0.0.1:${central_port}" \
    "ConnectionStrings:Events=Host=localhost;Port=5432;Database=ingot;Username=ingot;Password=ingot" \
    "Central:DatabasePath=${run_dir}/central.db" \
    "EventIngest:RequireToken=true" \
    "EventIngest:EdgeTokens:${edge_id}=${token}" \
    "Webhook:Enabled=false" \
    >>"$central_log" 2>&1 &
  central_pid=$!
  wait_for_http "http://127.0.0.1:${central_port}/health" "$central_pid" "$central_log"
}

wait_for_sync() {
  local deadline=$((SECONDS + recovery_timeout))
  while (( SECONDS < deadline )); do
    local local_rows
    local pending_rows
    local central_rows
    local_rows="$(local_count)"
    pending_rows="$(pending_count)"
    central_rows="$(central_count)"
    if (( local_rows > 0 && pending_rows == 0 && central_rows == local_rows )); then
      echo "Synchronized: local=${local_rows}, central=${central_rows}, pending=0"
      return 0
    fi
    sleep 1
  done

  echo "Timed out waiting for event synchronization." >&2
  echo "Local=$(local_count), Central=$(central_count), Pending=$(pending_count)" >&2
  tail -100 "$edge_log" >&2 || true
  tail -100 "$central_log" >&2 || true
  return 1
}

if docker inspect -f '{{.State.Running}}' ingot-postgres 2>/dev/null | grep -q true; then
  postgres_was_running=true
else
  docker compose -f docker-compose.events.yml up -d postgres
fi

for _ in $(seq 1 60); do
  if docker exec ingot-postgres pg_isready -U ingot -d ingot >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec ingot-postgres pg_isready -U ingot -d ingot >/dev/null

dotnet build Ingot.sln --no-restore >/dev/null
start_central

dotnet run --project src/Ingot.Simulator --no-build -- \
  "Port=${simulator_port}" \
  "Mode=Scenario" \
  "ScenarioSpeed=${scenario_speed}" \
  "UpdateIntervalMs=50" \
  "ConsoleIntervalMs=2000" \
  >"$simulator_log" 2>&1 &
simulator_pid=$!

dotnet run --project src/Ingot.Edge.Agent --no-build -- \
  "Urls=http://127.0.0.1:${edge_port}" \
  "Logging:DatabasePath=${run_dir}/logs.db" \
  "Events:DatabasePath=${events_db}" \
  "Events:RetentionDays=0" \
  "Events:MaxBacklogRows=500000" \
  "Events:EnableInfluxProjection=false" \
  "Profiles:Directory=${repo_root}/profiles" \
  "Acquisition:DeviceConfigService:ConfigDirectory=${config_dir}" \
  "Acquisition:StateStore:DatabasePath=${run_dir}/acquisition-state.db" \
  "Acquisition:ChannelCollector:ConnectionCheckRetryDelayMs=50" \
  "Acquisition:ChannelCollector:TriggerWaitDelayMs=50" \
  "Edge:EnableCentralReporting=false" \
  "Edge:CentralApiBaseUrl=http://127.0.0.1:${central_port}" \
  "Edge:EdgeId=${edge_id}" \
  "Edge:EnableEventShipping=true" \
  "Edge:EventIngestToken=${token}" \
  "Edge:EventIdleDelayMs=100" \
  "Edge:EventRetryMaxSeconds=5" \
  >"$edge_log" 2>&1 &
edge_pid=$!
wait_for_http "http://127.0.0.1:${edge_port}/health" "$edge_pid" "$edge_log"

echo "Warming up scenario for ${warmup_seconds}s..."
sleep "$warmup_seconds"
wait_for_sync
before_local="$(local_count)"
before_central="$(central_count)"

echo "Disconnecting Central for ${disconnect_seconds}s..."
stop_process "$central_pid"
central_pid=""

remaining="$disconnect_seconds"
while (( remaining > 0 )); do
  step=60
  if (( remaining < step )); then
    step="$remaining"
  fi
  sleep "$step"
  remaining=$((remaining - step))
  echo "Central offline: $((disconnect_seconds - remaining))/${disconnect_seconds}s, local=$(local_count), pending=$(pending_count)"
done

stop_process "$simulator_pid"
simulator_pid=""
sleep 2

after_local="$(local_count)"
after_pending="$(pending_count)"
if (( after_local <= before_local )); then
  echo "No events were produced during the outage: before=${before_local}, after=${after_local}" >&2
  exit 1
fi
if (( after_pending <= 0 )); then
  echo "Expected a non-empty local outbox during the outage." >&2
  exit 1
fi

echo "Reconnecting Central: before=${before_local}, accumulated=${after_local}, pending=${after_pending}"
start_central
wait_for_sync

final_local="$(local_count)"
final_central="$(central_count)"
final_pending="$(pending_count)"
stats="$(docker exec ingot-postgres psql -U ingot -d ingot -At -F '|' -c "
  SELECT COUNT(*), COUNT(DISTINCT event_id), COUNT(DISTINCT seq), MIN(seq), MAX(seq)
  FROM production_events
  WHERE edge_id = '${edge_id}';")"
IFS='|' read -r rows distinct_ids distinct_sequences min_seq max_seq <<<"$stats"
gap_count="$(docker exec ingot-postgres psql -U ingot -d ingot -At -c "
  WITH ordered AS (
    SELECT seq, LAG(seq) OVER (ORDER BY seq) AS previous_seq
    FROM production_events
    WHERE edge_id = '${edge_id}'
  )
  SELECT COUNT(*) FROM ordered
  WHERE previous_seq IS NOT NULL AND seq <> previous_seq + 1;")"
orphan_count="$(docker exec ingot-postgres psql -U ingot -d ingot -At -c "
  SELECT COUNT(*)
  FROM event_ingest_keys AS keys
  LEFT JOIN production_events AS events ON events.event_id = keys.event_id
  WHERE keys.edge_id = '${edge_id}' AND events.event_id IS NULL;")"

if (( final_local != final_central ||
      final_pending != 0 ||
      rows != final_local ||
      distinct_ids != rows ||
      distinct_sequences != rows ||
      min_seq != 1 ||
      max_seq != rows ||
      gap_count != 0 ||
      orphan_count != 0 )); then
  echo "Disconnect recovery invariants failed." >&2
  echo "local=${final_local}, central=${final_central}, pending=${final_pending}" >&2
  echo "rows=${rows}, ids=${distinct_ids}, seqs=${distinct_sequences}, min=${min_seq}, max=${max_seq}" >&2
  echo "gaps=${gap_count}, orphanReservations=${orphan_count}" >&2
  exit 1
fi

echo "NFR7 PASS"
echo "Edge: ${edge_id}"
echo "Before outage: local=${before_local}, central=${before_central}"
echo "After outage: local=${after_local}, pending=${after_pending}"
echo "After recovery: local=${final_local}, central=${final_central}, pending=${final_pending}"
echo "Integrity: eventIds=${distinct_ids}, sequences=${distinct_sequences}, range=${min_seq}-${max_seq}, gaps=${gap_count}, orphanReservations=${orphan_count}"
succeeded=true
