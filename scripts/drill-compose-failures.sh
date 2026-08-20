#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
confirmation="${1:-}"
output="${2:-}"
umask 077

if [[ "$confirmation" != "--confirm-isolated-environment" || "${INGOT_DRILL_ENVIRONMENT:-}" != "isolated" ]]; then
  echo "拒绝执行：仅可在隔离环境中设置 INGOT_DRILL_ENVIRONMENT=isolated，并传入 --confirm-isolated-environment。" >&2
  exit 2
fi
if [[ -z "$output" ]]; then
  echo "用法：INGOT_DRILL_ENVIRONMENT=isolated $0 --confirm-isolated-environment <演练工件路径>" >&2
  exit 2
fi
if [[ -e "$output" || -e "${output}.sha256" ]]; then
  echo "演练工件已经存在，拒绝覆盖：$output" >&2
  exit 1
fi
for command in docker curl; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "缺少演练依赖：$command" >&2
    exit 2
  fi
done

cd "$repo_root"
compose=(docker compose -f docker-compose.app.yml)
required_services=(optimizer platform-api platform-worker connector-host)
pending_restore=()
results=()
failure_count=0
started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

is_running() {
  "${compose[@]}" ps --status running --services 2>/dev/null | grep -Fqx "$1"
}

wait_http() {
  local url="$1"
  local attempts="$2"
  local attempt
  for ((attempt = 1; attempt <= attempts; attempt++)); do
    if curl --fail --silent --show-error --max-time 2 "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  return 1
}

remove_pending() {
  local target="$1"
  local retained=()
  local service
  for service in "${pending_restore[@]}"; do
    if [[ "$service" != "$target" ]]; then
      retained+=("$service")
    fi
  done
  pending_restore=("${retained[@]}")
}

restore_pending() {
  local service
  for service in "${pending_restore[@]}"; do
    "${compose[@]}" start "$service" >/dev/null 2>&1 || true
  done
}
trap restore_pending EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

for service in "${required_services[@]}"; do
  if ! is_running "$service"; then
    echo "演练前置条件不满足：$service 未运行。请先启动 connector-host profile 的隔离栈。" >&2
    exit 2
  fi
done

run_case() {
  local name="$1"
  local stopped_service="$2"
  local survival_url="$3"
  local recovery_url="$4"
  local case_started
  local case_failure=""
  local recovery_seconds
  case_started="$(date +%s)"
  pending_restore+=("$stopped_service")

  if ! "${compose[@]}" stop --timeout 20 "$stopped_service" >/dev/null; then
    case_failure="stop-command"
  elif ! wait_http "$survival_url" 30; then
    case_failure="survival-check"
  fi

  if ! "${compose[@]}" start "$stopped_service" >/dev/null; then
    case_failure="${case_failure:+${case_failure}+}restart-command"
  elif ! wait_http "$recovery_url" 120; then
    case_failure="${case_failure:+${case_failure}+}recovery-check"
  fi
  if is_running "$stopped_service"; then
    remove_pending "$stopped_service"
  else
    case_failure="${case_failure:+${case_failure}+}service-not-running"
  fi

  if [[ -z "$case_failure" ]]; then
    recovery_seconds=$(($(date +%s) - case_started))
    results+=("$name=passed:${recovery_seconds}s")
  else
    results+=("$name=failed:${case_failure}")
    failure_count=$((failure_count + 1))
  fi
}

run_case optimizer_outage optimizer http://127.0.0.1:8000/health http://127.0.0.1:8100/ready
run_case worker_outage platform-worker http://127.0.0.1:8000/health http://127.0.0.1:8000/health
run_case api_outage platform-api http://127.0.0.1:8001/health http://127.0.0.1:8000/health
restore_pending

result=passed
if (( failure_count > 0 )); then
  result=failed
fi

output_dir="$(dirname "$output")"
mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"
output="${output_dir}/$(basename "$output")"
temporary="$(mktemp "${output}.tmp.XXXXXX")"
trap 'restore_pending; rm -f -- "$temporary"' EXIT
{
  echo "format=ingot-compose-failure-drill-v1"
  echo "result=$result"
  echo "started_at=$started_at"
  echo "completed_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "source_commit=$(git rev-parse HEAD 2>/dev/null || printf unknown)"
  echo "environment=isolated"
  echo "failure_count=$failure_count"
  for index in "${!results[@]}"; do
    echo "case_$((index + 1))=${results[$index]}"
  done
} > "$temporary"
mv "$temporary" "$output"
trap restore_pending EXIT

if command -v sha256sum >/dev/null 2>&1; then
  (cd "$output_dir" && sha256sum "$(basename "$output")" > "$(basename "$output").sha256")
else
  (cd "$output_dir" && shasum -a 256 "$(basename "$output")" > "$(basename "$output").sha256")
fi

if [[ "$result" != "passed" ]]; then
  echo "故障演练未通过，工件已保留：$output" >&2
  exit 1
fi

echo "隔离环境故障演练通过：$output"
