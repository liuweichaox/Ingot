#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

edge_port="${INGOT_V1_EDGE_PORT:-18581}"
simulator_port="${INGOT_V1_SIMULATOR_PORT:-15050}"
timeout_seconds="${INGOT_V1_TIMEOUT_SECONDS:-45}"
run_dir="$(mktemp -d -t ingot-v1.XXXXXX)"
config_dir="$run_dir/configs"
edge_log="$run_dir/edge.log"
simulator_log="$run_dir/simulator.log"
events_file="$run_dir/events.json"
edge_pid=""
simulator_pid=""
succeeded=false

mkdir -p "$config_dir"
cat >"$config_dir/legacy-conditional.json" <<JSON
{
  "SchemaVersion": 1,
  "IsEnabled": true,
  "PlcCode": "V1-SIM",
  "Driver": "melsec-a1e",
  "Host": "127.0.0.1",
  "Port": ${simulator_port},
  "ProtocolOptions": {
    "connect-timeout-ms": "5000",
    "receive-timeout-ms": "5000"
  },
  "HeartbeatMonitorRegister": "D100",
  "HeartbeatPollingInterval": 250,
  "Channels": [
    {
      "Measurement": "production",
      "ChannelCode": "LEGACY-CYCLE",
      "EnableBatchRead": false,
      "BatchSize": 1,
      "AcquisitionInterval": 0,
      "AcquisitionMode": "Conditional",
      "ConditionalAcquisition": {
        "Register": "D6006",
        "DataType": "short",
        "StartTriggerMode": "RisingEdge",
        "EndTriggerMode": "FallingEdge"
      },
      "Metrics": [
        {
          "MetricLabel": "recipe",
          "FieldName": "recipe_id",
          "Register": "D6100",
          "Index": 0,
          "DataType": "string",
          "StringByteLength": 16,
          "Encoding": "utf-8"
        }
      ]
    }
  ]
}
JSON

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
  "Events:DatabasePath=${run_dir}/events.db" \
  "Events:RetentionDays=0" \
  "Events:EnableInfluxProjection=false" \
  "Profiles:Directory=${repo_root}/profiles" \
  "Acquisition:DeviceConfigService:ConfigDirectory=${config_dir}" \
  "Acquisition:StateStore:DatabasePath=${run_dir}/acquisition-state.db" \
  "Acquisition:ChannelCollector:ConnectionCheckRetryDelayMs=50" \
  "Acquisition:ChannelCollector:TriggerWaitDelayMs=50" \
  "Acquisition:QueueService:FlushIntervalSeconds=1" \
  "Edge:EnableCentralReporting=false" \
  "Edge:EdgeId=V1-COMPATIBILITY" \
  "Edge:EnableEventShipping=false" \
  >"$edge_log" 2>&1 &
edge_pid=$!
wait_for_http "http://127.0.0.1:${edge_port}/health" "$edge_pid" "$edge_log"

dotnet run --project src/Ingot.Simulator --no-build -- \
  "Port=${simulator_port}" \
  "Mode=Scenario" \
  "ScenarioSpeed=1" \
  "UpdateIntervalMs=50" \
  "ConsoleIntervalMs=2000" \
  >"$simulator_log" 2>&1 &
simulator_pid=$!

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
  curl -fsS \
    "http://127.0.0.1:${edge_port}/api/v1/events?subjectType=equipment&subjectId=V1-SIM&limit=100" \
    -o "$events_file"
  completed="$(jq '[.data[] | select(.eventType == "cycle.completed")] | length' "$events_file")"
  if (( completed >= 1 )); then
    break
  fi
  sleep 1
done

if ! jq -e '
  [.data[] | select(.eventType == "cycle.started" or .eventType == "cycle.completed")]
  | sort_by(.seq)
  | . as $cycles
  | ($cycles | length) >= 2
  and ($cycles[0].eventType == "cycle.started")
  and ($cycles[1].eventType == "cycle.completed")
  and ($cycles[0].correlationId == $cycles[1].correlationId)
  and ($cycles[0].subject.type == "equipment")
  and ($cycles[0].subject.id == "V1-SIM")
  and ($cycles[0].source | contains("/V1-SIM/LEGACY-CYCLE"))
  and ($cycles[0].data.recipe_id == "R-POLISH-V3")
  and ($cycles[0].data.channel_code == "LEGACY-CYCLE")
  and ($cycles[0].context == {})
' "$events_file" >/dev/null; then
  echo "SchemaVersion 1 compatibility assertions failed." >&2
  jq '.' "$events_file" >&2
  tail -100 "$edge_log" >&2 || true
  exit 1
fi

correlation_id="$(jq -r \
  '[.data[] | select(.eventType == "cycle.started")] | sort_by(.seq) | .[0].correlationId' \
  "$events_file")"
echo "SchemaVersion 1 compatibility PASS"
echo "Legacy PlcCode/Register/ConditionalAcquisition config loaded without v2 fields."
echo "Cycle ${correlation_id} produced a paired immutable event chain and retained the legacy Metrics snapshot."
succeeded=true
