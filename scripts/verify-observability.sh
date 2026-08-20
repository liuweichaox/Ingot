#!/usr/bin/env bash
# 校验 Prometheus、告警和可观测性配置的有效性。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

docker run --rm --entrypoint /bin/promtool \
  -v "$repo_root/deploy/observability/prometheus.yml:/etc/prometheus/prometheus.yml:ro" \
  -v "$repo_root/deploy/observability/alerts.yml:/etc/prometheus/rules/alerts.yml:ro" \
  -v "$repo_root/deploy/observability/edge-targets.yml:/etc/prometheus/targets/edge-targets.yml:ro" \
  prom/prometheus:v3.12.0 \
  check config /etc/prometheus/prometheus.yml

docker run --rm --entrypoint /bin/amtool \
  -v "$repo_root/deploy/observability/alertmanager.yml:/etc/alertmanager/alertmanager.yml:ro" \
  prom/alertmanager:v0.32.1 \
  check-config /etc/alertmanager/alertmanager.yml

node -e '
  const fs = require("node:fs");
  JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
' "$repo_root/deploy/observability/grafana/dashboards/production-overview.json"

echo "可观测性配置检查通过。"
