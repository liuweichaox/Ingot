#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="${INGOT_COMPOSE_FILE:-${repo_root}/docker-compose.app.yml}"
backup_root="${INGOT_BACKUP_ROOT:-${repo_root}/deploy/backups}"
destination="${1:-${backup_root}/app-$(date -u +%Y%m%dT%H%M%SZ)}"
compose=(docker compose -f "${compose_file}")
writers=()

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "缺少必要命令：$1" >&2
    exit 1
  }
}

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$@"
  else
    shasum -a 256 "$@"
  fi
}

restart_writers() {
  if (( ${#writers[@]} > 0 )); then
    "${compose[@]}" up -d "${writers[@]}" >/dev/null
  fi
}

archive_path() {
  local source="$1"
  local output="$2"
  "${compose[@]}" --profile maintenance run --rm --no-deps -T \
    --entrypoint tar app-maintenance -C "${source}" -czf - . > "${destination}/${output}"
  tar -tzf "${destination}/${output}" >/dev/null
}

require_command docker
require_command tar
if ! docker compose version >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
  echo "Docker daemon 或 Docker Compose v2 不可用。" >&2
  exit 1
fi
if [[ -e "${destination}" ]]; then
  echo "备份目录已经存在，拒绝覆盖：${destination}" >&2
  exit 1
fi

umask 077
mkdir -p "${destination}"
destination="$(cd "${destination}" && pwd)"
trap restart_writers EXIT

while IFS= read -r service; do
  case "${service}" in
    platform-api|platform-worker) writers+=("${service}") ;;
  esac
done < <("${compose[@]}" ps --status running --services)

# Stop both known database writers even if one is currently restarting and was
# therefore absent from the "running" snapshot above.
"${compose[@]}" stop platform-api platform-worker >/dev/null
if ! "${compose[@]}" ps --status running --services | grep -qx postgres; then
  echo "PostgreSQL 容器未运行，无法生成一致备份。" >&2
  exit 1
fi

"${compose[@]}" exec -T postgres \
  pg_dump -U ingot -d ingot --format=custom --no-owner --no-acl \
  > "${destination}/postgres.dump"
"${compose[@]}" exec -T postgres pg_restore --list \
  < "${destination}/postgres.dump" > "${destination}/postgres.contents"

archive_path /data/inspection-attachments inspection-attachments.tar.gz
archive_path /archive/inspection-attachments inspection-archive.tar.gz
archive_path /data/process-knowledge process-knowledge.tar.gz
archive_path /archive/process-knowledge process-knowledge-archive.tar.gz

cat > "${destination}/manifest.txt" <<EOF
format=ingot-app-backup-v1
completed_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
source_commit=$(git -C "${repo_root}" rev-parse HEAD 2>/dev/null || printf unknown)
database=ingot
assets=inspection-attachments,inspection-archive,process-knowledge,process-knowledge-archive
consistency=platform-api-and-platform-worker-stopped
EOF

(
  cd "${destination}"
  sha256_file manifest.txt postgres.dump postgres.contents \
    inspection-attachments.tar.gz inspection-archive.tar.gz \
    process-knowledge.tar.gz process-knowledge-archive.tar.gz > SHA256SUMS
)

echo "应用备份已完成并校验：${destination}"
