#!/usr/bin/env bash
# 校验生产验收声明与实测证据的一致性。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-}"
umask 077

if [[ -z "$output" ]]; then
  echo "用法：加载现场验收环境变量后，执行 $0 <验收工件路径>" >&2
  exit 2
fi
if [[ -e "$output" || -e "${output}.sha256" ]]; then
  echo "验收工件已经存在，拒绝覆盖：$output" >&2
  exit 1
fi

required_integer() {
  local name="$1"
  local value="${!name:-}"
  if [[ ! "$value" =~ ^[1-9][0-9]*$ ]]; then
    echo "$name 必须是正整数。" >&2
    exit 2
  fi
  if (( value > 1000000000 )); then
    echo "$name 超出允许范围（最大 1000000000）。" >&2
    exit 2
  fi
}

required_nonnegative_integer() {
  local name="$1"
  local value="${!name:-}"
  if [[ ! "$value" =~ ^(0|[1-9][0-9]*)$ ]]; then
    echo "$name 必须是非负整数。" >&2
    exit 2
  fi
  if (( value > 1000000000 )); then
    echo "$name 超出允许范围（最大 1000000000）。" >&2
    exit 2
  fi
}

required_evidence() {
  local name="$1"
  local value="${!name:-}"
  if [[ ! "$value" =~ ^[A-Za-z0-9][A-Za-z0-9._:/@+-]{2,255}$ ]]; then
    echo "$name 必须是 3-256 字符的稳定证据标识，且不能包含空白或控制字符。" >&2
    exit 2
  fi
}

for name in \
  INGOT_TARGET_RPO_MINUTES \
  INGOT_TARGET_RTO_MINUTES \
  INGOT_MAX_EDGE_OFFLINE_HOURS \
  INGOT_MAX_BACKLOG_AGE_SECONDS \
  INGOT_PEAK_EVENT_RATE_PER_SECOND \
  INGOT_PEAK_SAMPLE_POINTS_PER_SECOND \
  INGOT_CAPACITY_TEST_MULTIPLIER_PERCENT \
  INGOT_REQUIRED_OBSERVATION_HOURS; do
  required_integer "$name"
done

for name in \
  INGOT_MEASURED_RPO_MINUTES \
  INGOT_MEASURED_RTO_MINUTES \
  INGOT_MEASURED_EDGE_OFFLINE_HOURS \
  INGOT_MEASURED_BACKLOG_AGE_SECONDS \
  INGOT_MEASURED_CAPACITY_EVENT_RATE_PER_SECOND \
  INGOT_MEASURED_CAPACITY_SAMPLE_POINTS_PER_SECOND \
  INGOT_OBSERVED_CONTINUOUS_HOURS; do
  required_nonnegative_integer "$name"
done

if (( INGOT_CAPACITY_TEST_MULTIPLIER_PERCENT < 200 )); then
  echo "INGOT_CAPACITY_TEST_MULTIPLIER_PERCENT 不得低于 200。" >&2
  exit 2
fi

required_evidence INGOT_SITE_ID
required_evidence INGOT_BACKUP_EVIDENCE
required_evidence INGOT_PITR_DRILL_ID
required_evidence INGOT_FAILURE_DRILL_ID
required_evidence INGOT_DATABASE_HA_EVIDENCE
required_evidence INGOT_FILE_RECOVERY_EVIDENCE
required_evidence INGOT_EDGE_REPLAY_EVIDENCE
required_evidence INGOT_DETERMINISM_EVIDENCE
required_evidence INGOT_SITE_ISOLATION_EVIDENCE
required_evidence INGOT_RUNBOOK_EVIDENCE
required_evidence INGOT_MONITORING_EVIDENCE
required_evidence INGOT_ALERT_ROUTING_EVIDENCE
required_evidence INGOT_ACCEPTANCE_REVIEWER

failures=()
if (( INGOT_MEASURED_RPO_MINUTES > INGOT_TARGET_RPO_MINUTES )); then
  failures+=("measured RPO exceeds target")
fi
if (( INGOT_MEASURED_RTO_MINUTES > INGOT_TARGET_RTO_MINUTES )); then
  failures+=("measured RTO exceeds target")
fi
if (( INGOT_MEASURED_EDGE_OFFLINE_HOURS < INGOT_MAX_EDGE_OFFLINE_HOURS )); then
  failures+=("Edge offline/replay evidence is shorter than the declared window")
fi
if (( INGOT_MEASURED_BACKLOG_AGE_SECONDS > INGOT_MAX_BACKLOG_AGE_SECONDS )); then
  failures+=("measured backlog age exceeds the declared limit")
fi
if (( INGOT_MEASURED_CAPACITY_EVENT_RATE_PER_SECOND * 100 <
      INGOT_PEAK_EVENT_RATE_PER_SECOND * INGOT_CAPACITY_TEST_MULTIPLIER_PERCENT )); then
  failures+=("event-rate capacity evidence is below the required multiplier")
fi
if (( INGOT_MEASURED_CAPACITY_SAMPLE_POINTS_PER_SECOND * 100 <
      INGOT_PEAK_SAMPLE_POINTS_PER_SECOND * INGOT_CAPACITY_TEST_MULTIPLIER_PERCENT )); then
  failures+=("sample-rate capacity evidence is below the required multiplier")
fi
if (( INGOT_OBSERVED_CONTINUOUS_HOURS < INGOT_REQUIRED_OBSERVATION_HOURS )); then
  failures+=("continuous observation period is too short")
fi

result="passed"
if (( ${#failures[@]} > 0 )); then
  result="failed"
fi

output_dir="$(dirname "$output")"
mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"
output="${output_dir}/$(basename "$output")"
temporary="$(mktemp "${output}.tmp.XXXXXX")"
trap 'rm -f -- "$temporary"' EXIT

{
  echo "format=ingot-production-acceptance-v1"
  echo "result=$result"
  echo "generated_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "source_commit=$(git -C "$repo_root" rev-parse HEAD 2>/dev/null || printf unknown)"
  echo "site_id=$INGOT_SITE_ID"
  echo "target_rpo_minutes=$INGOT_TARGET_RPO_MINUTES"
  echo "measured_rpo_minutes=$INGOT_MEASURED_RPO_MINUTES"
  echo "target_rto_minutes=$INGOT_TARGET_RTO_MINUTES"
  echo "measured_rto_minutes=$INGOT_MEASURED_RTO_MINUTES"
  echo "maximum_edge_offline_hours=$INGOT_MAX_EDGE_OFFLINE_HOURS"
  echo "measured_edge_offline_hours=$INGOT_MEASURED_EDGE_OFFLINE_HOURS"
  echo "maximum_backlog_age_seconds=$INGOT_MAX_BACKLOG_AGE_SECONDS"
  echo "measured_backlog_age_seconds=$INGOT_MEASURED_BACKLOG_AGE_SECONDS"
  echo "peak_event_rate_per_second=$INGOT_PEAK_EVENT_RATE_PER_SECOND"
  echo "measured_capacity_event_rate_per_second=$INGOT_MEASURED_CAPACITY_EVENT_RATE_PER_SECOND"
  echo "peak_sample_points_per_second=$INGOT_PEAK_SAMPLE_POINTS_PER_SECOND"
  echo "measured_capacity_sample_points_per_second=$INGOT_MEASURED_CAPACITY_SAMPLE_POINTS_PER_SECOND"
  echo "capacity_test_multiplier_percent=$INGOT_CAPACITY_TEST_MULTIPLIER_PERCENT"
  echo "required_observation_hours=$INGOT_REQUIRED_OBSERVATION_HOURS"
  echo "observed_continuous_hours=$INGOT_OBSERVED_CONTINUOUS_HOURS"
  echo "backup_evidence=$INGOT_BACKUP_EVIDENCE"
  echo "pitr_drill_id=$INGOT_PITR_DRILL_ID"
  echo "failure_drill_id=$INGOT_FAILURE_DRILL_ID"
  echo "database_ha_evidence=$INGOT_DATABASE_HA_EVIDENCE"
  echo "file_recovery_evidence=$INGOT_FILE_RECOVERY_EVIDENCE"
  echo "edge_replay_evidence=$INGOT_EDGE_REPLAY_EVIDENCE"
  echo "determinism_evidence=$INGOT_DETERMINISM_EVIDENCE"
  echo "site_isolation_evidence=$INGOT_SITE_ISOLATION_EVIDENCE"
  echo "runbook_evidence=$INGOT_RUNBOOK_EVIDENCE"
  echo "monitoring_evidence=$INGOT_MONITORING_EVIDENCE"
  echo "alert_routing_evidence=$INGOT_ALERT_ROUTING_EVIDENCE"
  echo "reviewer=$INGOT_ACCEPTANCE_REVIEWER"
  echo "failure_count=${#failures[@]}"
  for index in "${!failures[@]}"; do
    echo "failure_$((index + 1))=${failures[$index]}"
  done
} > "$temporary"

mv "$temporary" "$output"
trap - EXIT
if command -v sha256sum >/dev/null 2>&1; then
  (cd "$output_dir" && sha256sum "$(basename "$output")" > "$(basename "$output").sha256")
else
  (cd "$output_dir" && shasum -a 256 "$(basename "$output")" > "$(basename "$output").sha256")
fi

if [[ "$result" != "passed" ]]; then
  printf '生产验收未通过，工件已保留：%s\n' "$output" >&2
  printf ' - %s\n' "${failures[@]}" >&2
  exit 1
fi

echo "生产验收声明与实测值通过：$output"
