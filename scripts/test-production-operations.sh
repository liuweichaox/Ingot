#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test_temp="$(mktemp -d)"
trap 'rm -rf -- "$test_temp"' EXIT

common_environment=(
  INGOT_SITE_ID=verification-site-0001
  INGOT_TARGET_RPO_MINUTES=15
  INGOT_TARGET_RTO_MINUTES=60
  INGOT_MAX_EDGE_OFFLINE_HOURS=24
  INGOT_MAX_BACKLOG_AGE_SECONDS=900
  INGOT_PEAK_EVENT_RATE_PER_SECOND=1000
  INGOT_PEAK_SAMPLE_POINTS_PER_SECOND=15000
  INGOT_CAPACITY_TEST_MULTIPLIER_PERCENT=200
  INGOT_REQUIRED_OBSERVATION_HOURS=168
  INGOT_MEASURED_RTO_MINUTES=45
  INGOT_MEASURED_EDGE_OFFLINE_HOURS=24
  INGOT_MEASURED_BACKLOG_AGE_SECONDS=600
  INGOT_MEASURED_CAPACITY_EVENT_RATE_PER_SECOND=2000
  INGOT_MEASURED_CAPACITY_SAMPLE_POINTS_PER_SECOND=30000
  INGOT_OBSERVED_CONTINUOUS_HOURS=168
  INGOT_BACKUP_EVIDENCE=verification-backup-001
  INGOT_PITR_DRILL_ID=verification-pitr-001
  INGOT_FAILURE_DRILL_ID=verification-failure-001
  INGOT_DATABASE_HA_EVIDENCE=verification-database-ha-001
  INGOT_FILE_RECOVERY_EVIDENCE=verification-file-recovery-001
  INGOT_EDGE_REPLAY_EVIDENCE=verification-edge-replay-001
  INGOT_DETERMINISM_EVIDENCE=verification-determinism-001
  INGOT_SITE_ISOLATION_EVIDENCE=verification-site-isolation-001
  INGOT_RUNBOOK_EVIDENCE=verification-runbook-001
  INGOT_MONITORING_EVIDENCE=http://prometheus:9090
  INGOT_ALERT_ROUTING_EVIDENCE=verification-alert-001
  INGOT_ACCEPTANCE_REVIEWER=ci-verifier
)

passed_artifact="$test_temp/passed.txt"
env "${common_environment[@]}" INGOT_MEASURED_RPO_MINUTES=0 \
  "$repo_root/scripts/verify-production-acceptance.sh" "$passed_artifact"
grep -Fqx 'result=passed' "$passed_artifact"
if command -v sha256sum >/dev/null 2>&1; then
  (cd "$test_temp" && sha256sum -c passed.txt.sha256 >/dev/null)
else
  (cd "$test_temp" && shasum -a 256 -c passed.txt.sha256 >/dev/null)
fi

failed_artifact="$test_temp/failed.txt"
if env "${common_environment[@]}" INGOT_MEASURED_RPO_MINUTES=16 \
  "$repo_root/scripts/verify-production-acceptance.sh" "$failed_artifact" \
  >"$test_temp/expected-acceptance-rejection.log" 2>&1; then
  echo "验收脚本错误地接受了超出 RPO 的结果。" >&2
  exit 1
fi
grep -Fqx 'result=failed' "$failed_artifact"
grep -Fq 'measured RPO exceeds target' "$test_temp/expected-acceptance-rejection.log"
if command -v sha256sum >/dev/null 2>&1; then
  (cd "$test_temp" && sha256sum -c failed.txt.sha256 >/dev/null)
else
  (cd "$test_temp" && shasum -a 256 -c failed.txt.sha256 >/dev/null)
fi

if env "${common_environment[@]}" INGOT_MEASURED_RPO_MINUTES=0 \
  "$repo_root/scripts/verify-production-acceptance.sh" "$passed_artifact" \
  >"$test_temp/expected-overwrite-rejection.log" 2>&1; then
  echo "验收脚本错误地覆盖了已有工件。" >&2
  exit 1
fi
grep -Fq '拒绝覆盖' "$test_temp/expected-overwrite-rejection.log"

drill_artifact="$test_temp/drill.txt"
if INGOT_DRILL_ENVIRONMENT=production \
  "$repo_root/scripts/drill-compose-failures.sh" --confirm-isolated-environment "$drill_artifact" \
  >"$test_temp/expected-drill-rejection.log" 2>&1; then
  echo "故障演练脚本错误地接受了非隔离环境。" >&2
  exit 1
fi
test ! -e "$drill_artifact"
grep -Fq '仅可在隔离环境' "$test_temp/expected-drill-rejection.log"

echo "生产运维脚本检查通过。"
