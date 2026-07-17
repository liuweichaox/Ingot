#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

central_port="${INGOT_WEBHOOK_CENTRAL_PORT:-18280}"
receiver_port="${INGOT_WEBHOOK_RECEIVER_PORT:-18290}"
event_count="${INGOT_WEBHOOK_EVENT_COUNT:-50}"
delivery_timeout="${INGOT_WEBHOOK_DELIVERY_TIMEOUT_SECONDS:-120}"
edge_id="WEBHOOK-$(date +%s)-$$"
token="webhook-acceptance-token"
secret="webhook-acceptance-secret"
run_dir="$(mktemp -d -t ingot-webhook.XXXXXX)"
central_log="$run_dir/central.log"
receiver_log="$run_dir/receiver.log"
events_file="$run_dir/events.jsonl"
central_pid=""
receiver_pid=""
postgres_was_running=false
succeeded=false

stop_process() {
  local pid="${1:-}"
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
}

cleanup() {
  stop_process "$receiver_pid"
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

python3 tools/webhook_receiver.py \
  --port "$receiver_port" \
  --secret "$secret" \
  --output "$events_file" \
  >"$receiver_log" 2>&1 &
receiver_pid=$!
wait_for_http "http://127.0.0.1:${receiver_port}/health" "$receiver_pid" "$receiver_log"

dotnet run --project src/Ingot.Central.Api --no-build -- \
  "Urls=http://127.0.0.1:${central_port}" \
  "ConnectionStrings:Events=Host=localhost;Port=5432;Database=ingot;Username=ingot;Password=ingot" \
  "Central:DatabasePath=${run_dir}/central.db" \
  "EventIngest:RequireToken=true" \
  "EventIngest:EdgeTokens:${edge_id}=${token}" \
  "Webhook:Enabled=true" \
  "Webhook:PollIntervalMs=100" \
  "Webhook:BatchSize=100" \
  "Webhook:RequestTimeoutSeconds=5" \
  >"$central_log" 2>&1 &
central_pid=$!
wait_for_http "http://127.0.0.1:${central_port}/health" "$central_pid" "$central_log"

subscription_payload="$(jq -n \
  --arg endpoint "http://127.0.0.1:${receiver_port}/events" \
  --arg secret "$secret" \
  --arg acceptance_run "$edge_id" \
  '{
    name: "CloudEvents acceptance",
    endpoint: $endpoint,
    context: {acceptance_run: $acceptance_run},
    secret: $secret
  }')"
subscription="$(curl -fsS \
  -H "Content-Type: application/json" \
  -d "$subscription_payload" \
  "http://127.0.0.1:${central_port}/api/v1/subscriptions")"
subscription_id="$(jq -er '.subscriptionId' <<<"$subscription")"

dotnet run --project tools/Ingot.CentralBenchmarks --no-build -- \
  --central-url "http://127.0.0.1:${central_port}" \
  --edge-id "$edge_id" \
  --token "$token" \
  --events "$event_count" \
  --batch-size 50 \
  --minimum-rate 1 \
  --enforce

deadline=$((SECONDS + delivery_timeout))
while (( SECONDS < deadline )); do
  stats="$(curl -fsS "http://127.0.0.1:${receiver_port}/stats")"
  received="$(jq -r '.received' <<<"$stats")"
  unique="$(jq -r '.unique' <<<"$stats")"
  duplicates="$(jq -r '.duplicates' <<<"$stats")"
  invalid="$(jq -r '.invalid' <<<"$stats")"
  if (( received == event_count &&
        unique == event_count &&
        duplicates == 0 &&
        invalid == 0 )); then
    break
  fi
  sleep 1
done

stats="$(curl -fsS "http://127.0.0.1:${receiver_port}/stats")"
received="$(jq -r '.received' <<<"$stats")"
unique="$(jq -r '.unique' <<<"$stats")"
duplicates="$(jq -r '.duplicates' <<<"$stats")"
invalid="$(jq -r '.invalid' <<<"$stats")"
subscription_state="$(curl -fsS \
  "http://127.0.0.1:${central_port}/api/v1/subscriptions/${subscription_id}")"
last_success="$(jq -r '.lastSuccessAt // empty' <<<"$subscription_state")"
last_error="$(jq -r '.lastError // empty' <<<"$subscription_state")"
consecutive_failures="$(jq -r '.consecutiveFailures' <<<"$subscription_state")"

if (( received != event_count ||
      unique != event_count ||
      duplicates != 0 ||
      invalid != 0 ||
      consecutive_failures != 0 )) ||
   [[ -z "$last_success" || -n "$last_error" ]]; then
  echo "Webhook acceptance failed." >&2
  echo "stats=${stats}" >&2
  echo "subscription=${subscription_state}" >&2
  tail -100 "$central_log" >&2 || true
  exit 1
fi

line_count="$(wc -l < "$events_file" | tr -d ' ')"
if (( line_count != event_count )); then
  echo "Expected ${event_count} persisted receiver records, got ${line_count}." >&2
  exit 1
fi

echo "CloudEvents webhook PASS"
echo "Subscription: ${subscription_id}"
echo "Delivered: ${received}, unique=${unique}, duplicates=${duplicates}, invalid=${invalid}"
echo "HMAC-SHA256, CloudEvents 1.0 structured content, headers, and durable subscription cursor verified."
succeeded=true
