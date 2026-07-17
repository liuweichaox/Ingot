#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

edge_port="${INGOT_RESTART_EDGE_PORT:-18481}"
simulator_port="${INGOT_RESTART_SIMULATOR_PORT:-15040}"
scenario_speed="${INGOT_RESTART_SCENARIO_SPEED:-0.25}"
timeout_seconds="${INGOT_RESTART_TIMEOUT_SECONDS:-90}"
run_dir="$(mktemp -d -t ingot-restart.XXXXXX)"
config_dir="$run_dir/configs"
events_db="$run_dir/events.db"
state_db="$run_dir/acquisition-state.db"
edge_log="$run_dir/edge.log"
simulator_log="$run_dir/simulator.log"
edge_pid=""
simulator_pid=""
succeeded=false

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
  for _ in $(seq 1 60); do
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

hash_stream() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum | awk '{print $1}'
  else
    shasum -a 256 | awk '{print $1}'
  fi
}

start_edge() {
  dotnet src/Ingot.Edge.Agent/bin/Debug/net10.0/Ingot.Edge.Agent.dll \
    "Urls=http://127.0.0.1:${edge_port}" \
    "Logging:DatabasePath=${run_dir}/logs.db" \
    "Events:DatabasePath=${events_db}" \
    "Events:RetentionDays=0" \
    "Events:MaxBacklogRows=500000" \
    "Events:EnableInfluxProjection=false" \
    "InfluxDB:Url=http://127.0.0.1:1" \
    "InfluxDB:Token=acceptance-disabled" \
    "InfluxDB:Bucket=acceptance-disabled" \
    "InfluxDB:Org=acceptance-disabled" \
    "Profiles:Directory=${repo_root}/profiles" \
    "Acquisition:DeviceConfigService:ConfigDirectory=${config_dir}" \
    "Acquisition:StateStore:DatabasePath=${state_db}" \
    "Acquisition:ChannelCollector:ConnectionCheckRetryDelayMs=50" \
    "Acquisition:ChannelCollector:TriggerWaitDelayMs=50" \
    "Edge:EnableCentralReporting=false" \
    "Edge:EdgeId=RESTART-RECOVERY" \
    "Edge:EnableEventShipping=false" \
    >>"$edge_log" 2>&1 &
  edge_pid=$!
  wait_for_http "http://127.0.0.1:${edge_port}/health" "$edge_pid" "$edge_log"
}

query_count() {
  local event_type="$1"
  local correlation_id="$2"
  curl -fsSG \
    --data-urlencode "type=${event_type}" \
    --data-urlencode "correlationId=${correlation_id}" \
    --data-urlencode "limit=100" \
    "http://127.0.0.1:${edge_port}/api/v1/events" |
    jq '.count'
}

dotnet build Ingot.sln --no-restore >/dev/null
start_edge

dotnet src/Ingot.Simulator/bin/Debug/net10.0/Ingot.Simulator.dll \
  "Port=${simulator_port}" \
  "Mode=Scenario" \
  "ScenarioSpeed=${scenario_speed}" \
  "UpdateIntervalMs=50" \
  "ConsoleIntervalMs=2000" \
  >"$simulator_log" 2>&1 &
simulator_pid=$!

deadline=$((SECONDS + timeout_seconds))
cycle_id=""
while (( SECONDS < deadline )); do
  if [[ -f "$state_db" ]]; then
    cycle_id="$(sqlite3 "$state_db" "
      SELECT cycle_id
      FROM active_cycles
      WHERE channel_code = 'event-rule:polish-cycle'
      LIMIT 1;" 2>/dev/null || true)"
  fi
  if [[ -n "$cycle_id" ]] && (( $(query_count "cycle.started" "$cycle_id") == 1 )); then
    break
  fi
  sleep 0.1
done

if [[ -z "$cycle_id" ]]; then
  echo "Timed out waiting for an active persisted cycle." >&2
  tail -100 "$edge_log" >&2 || true
  tail -100 "$simulator_log" >&2 || true
  exit 1
fi

before_count="$(sqlite3 "$events_db" "SELECT COUNT(*) FROM events;")"
before_max_seq="$(sqlite3 "$events_db" "SELECT MAX(seq) FROM events;")"
before_hash="$(sqlite3 "$events_db" "
  SELECT seq || '|' || event_id || '|' || event_type
  FROM events
  WHERE seq <= ${before_max_seq}
  ORDER BY seq;" | hash_stream)"

echo "Killing Edge during active cycle ${cycle_id}; persisted events=${before_count}"
kill -9 "$edge_pid"
wait "$edge_pid" 2>/dev/null || true
edge_pid=""

start_edge

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
  recovered="$(query_count "diagnostic.cycle_recovered" "$cycle_id")"
  completed="$(query_count "cycle.completed" "$cycle_id")"
  if (( recovered >= 1 && completed == 1 )); then
    break
  fi
  sleep 0.2
done

started="$(query_count "cycle.started" "$cycle_id")"
recovered="$(query_count "diagnostic.cycle_recovered" "$cycle_id")"
completed="$(query_count "cycle.completed" "$cycle_id")"
after_prefix_count="$(sqlite3 "$events_db" "
  SELECT COUNT(*) FROM events WHERE seq <= ${before_max_seq};")"
after_hash="$(sqlite3 "$events_db" "
  SELECT seq || '|' || event_id || '|' || event_type
  FROM events
  WHERE seq <= ${before_max_seq}
  ORDER BY seq;" | hash_stream)"
active_remaining="$(sqlite3 "$state_db" "
  SELECT COUNT(*)
  FROM active_cycles
  WHERE cycle_id = '${cycle_id}';")"

if (( started != 1 ||
      recovered < 1 ||
      completed != 1 ||
      after_prefix_count != before_count ||
      active_remaining != 0 )) ||
   [[ "$before_hash" != "$after_hash" ]]; then
  echo "Restart recovery invariants failed." >&2
  echo "cycle=${cycle_id}, started=${started}, recovered=${recovered}, completed=${completed}" >&2
  echo "before=${before_count}, preserved=${after_prefix_count}, activeRemaining=${active_remaining}" >&2
  echo "hashBefore=${before_hash}, hashAfter=${after_hash}" >&2
  tail -120 "$edge_log" >&2 || true
  exit 1
fi

final_count="$(sqlite3 "$events_db" "SELECT COUNT(*) FROM events;")"
echo "Phase 1 restart recovery PASS"
echo "Cycle: ${cycle_id}"
echo "Pre-crash facts preserved byte-for-byte by identity: ${before_count}/${before_count}"
echo "Events after recovery: ${final_count}; started=1, recovered=${recovered}, completed=1"
echo "The completed event retained the persisted cycle CorrelationId and active state was cleared."
succeeded=true
