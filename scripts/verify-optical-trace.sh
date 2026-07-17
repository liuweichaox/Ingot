#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

edge_port="${INGOT_OPTICAL_EDGE_PORT:-18381}"
simulator_port="${INGOT_OPTICAL_SIMULATOR_PORT:-15030}"
scenario_speed="${INGOT_OPTICAL_SCENARIO_SPEED:-1}"
timeout_seconds="${INGOT_OPTICAL_TIMEOUT_SECONDS:-60}"
material_lot="${INGOT_OPTICAL_MATERIAL_LOT:-LOT-001}"
expected_tooling="${INGOT_OPTICAL_TOOLING:-TOOL-A}"
run_dir="$(mktemp -d -t ingot-optical.XXXXXX)"
config_dir="$run_dir/configs"
events_db="$run_dir/events.db"
edge_log="$run_dir/edge.log"
simulator_log="$run_dir/simulator.log"
response_file="$run_dir/trace.json"
context_file="$run_dir/context.json"
stream_file="$run_dir/edge-stream.txt"
stream_events_file="$run_dir/edge-stream-events.json"
edge_pid=""
simulator_pid=""
succeeded=false

mkdir -p "$config_dir"
sed \
  -e "s/\"Port\": 502/\"Port\": ${simulator_port}/" \
  -e 's/"HeartbeatPollingInterval": 1000/"HeartbeatPollingInterval": 250/' \
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

dotnet build Ingot.sln --no-restore >/dev/null

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
  "Edge:EdgeId=OPTICAL-TRACE" \
  "Edge:EnableEventShipping=false" \
  >"$edge_log" 2>&1 &
edge_pid=$!
wait_for_http "http://127.0.0.1:${edge_port}/health" "$edge_pid" "$edge_log"

dotnet run --project src/Ingot.Simulator --no-build -- \
  "Port=${simulator_port}" \
  "Mode=Scenario" \
  "ScenarioSpeed=${scenario_speed}" \
  "UpdateIntervalMs=50" \
  "ConsoleIntervalMs=2000" \
  >"$simulator_log" 2>&1 &
simulator_pid=$!

trace_url="http://127.0.0.1:${edge_port}/api/v1/events?ctx.material_lot=${material_lot}&limit=100"
deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
  if curl -fsS "$trace_url" -o "$response_file"; then
    completed_cycles="$(jq \
      '[.data[] | select(.eventType == "cycle.completed")] | length' \
      "$response_file")"
    cleared_alarms="$(jq \
      '[.data[] | select(.eventType == "alarm.cleared")] | length' \
      "$response_file")"
    if (( completed_cycles >= 3 && cleared_alarms >= 1 )); then
      break
    fi
  fi
  sleep 1
done

curl -fsS "$trace_url" -o "$response_file"
curl -fsS \
  "http://127.0.0.1:${edge_port}/api/v1/context/polishing-machine/POL-03?keys=material_lot&keys=tooling" \
  -o "$context_file"

if ! jq -e \
  --arg lot "$material_lot" \
  --arg tooling "$expected_tooling" '
  .data as $events
  | ($events | map(select(.eventType == "material.lot_changed")) | length) >= 1
  and ($events | map(select(.eventType == "tooling.changed")) | length) >= 1
  and ($events | map(select(.eventType == "cycle.started")) | length) == 3
  and ($events | map(select(.eventType == "cycle.completed")) | length) == 3
  and ($events | map(select(.eventType == "alarm.raised")) | length) == 1
  and ($events | map(select(.eventType == "alarm.cleared")) | length) == 1
  and (
    $events
    | map(select(.eventType == "cycle.started" or .eventType == "cycle.completed"))
    | all(.context.material_lot == $lot and .context.tooling == $tooling)
  )
  and (
    $events
    | map(select(.eventType == "cycle.started"))
    | all(.data.recipe_id == "R-POLISH-V3")
  )
  and (
    $events
    | map(select(.eventType == "cycle.completed"))
    | all(.data.good_count != null)
  )
  and (
    $events
    | map(select(.eventType == "cycle.started" or .eventType == "cycle.completed"))
    | group_by(.correlationId)
    | length == 3
  )
  and (
    $events
    | map(select(.eventType == "cycle.started" or .eventType == "cycle.completed"))
    | group_by(.correlationId)
    | all(length == 2 and (map(.eventType) | sort) == ["cycle.completed", "cycle.started"])
  )
  and (
    $events
    | sort_by(.seq)
    | map(.eventType)
    | index("material.lot_changed") < index("tooling.changed")
  )
' "$response_file" >/dev/null; then
  echo "Optical trace assertions failed for ${material_lot}." >&2
  jq '.' "$response_file" >&2
  exit 1
fi

if ! jq -e \
  --arg lot "$material_lot" \
  --arg tooling "$expected_tooling" \
  '.values.material_lot == $lot and .values.tooling == $tooling' \
  "$context_file" >/dev/null; then
  echo "Persisted context assertions failed." >&2
  jq '.' "$context_file" >&2
  exit 1
fi

event_count="$(jq '.data | length' "$response_file")"
first_seq="$(jq '[.data[].seq] | min' "$response_file")"
last_seq="$(jq '[.data[].seq] | max' "$response_file")"
cycle_count="$(jq \
  '[.data[] | select(.eventType == "cycle.completed")] | length' \
  "$response_file")"

set +e
curl -sN --max-time 15 \
  -H "Last-Event-ID: ${last_seq}" \
  "http://127.0.0.1:${edge_port}/api/v1/events/stream?ctx.material_lot=LOT-002" \
  >"$stream_file"
stream_status=$?
set -e
if (( stream_status != 0 && stream_status != 28 )); then
  echo "Edge SSE request failed with curl status ${stream_status}." >&2
  exit 1
fi

sed -n 's/^data: //p' "$stream_file" | jq -s '.' > "$stream_events_file"
stream_count="$(jq 'length' "$stream_events_file")"
minimum_stream_id="$(sed -n 's/^id: //p' "$stream_file" | sort -n | head -1)"
if (( stream_count < 1 )) ||
   [[ -z "$minimum_stream_id" ]] ||
   (( minimum_stream_id <= last_seq )); then
  echo "Edge SSE did not resume after Last-Event-ID=${last_seq}." >&2
  cat "$stream_file" >&2
  exit 1
fi
if ! jq -e '
  length > 0
  and all(.seq > 0)
  and all(.context.material_lot == "LOT-002")
' "$stream_events_file" >/dev/null; then
  echo "Edge SSE filtering assertions failed." >&2
  jq '.' "$stream_events_file" >&2
  exit 1
fi

echo "Phase 2 optical trace PASS"
echo "Material lot: ${material_lot}, tooling: ${expected_tooling}"
echo "Events returned: ${event_count}, sequence range: ${first_seq}-${last_seq}"
echo "Complete cycles: ${cycle_count}; lot/tooling changes and alarm raise/clear verified."
echo "All cycle pairs share three distinct CorrelationIds and carry automatic context snapshots."
echo "Edge SSE resumed strictly after Last-Event-ID=${last_seq} and applied ctx.material_lot filtering."
succeeded=true
