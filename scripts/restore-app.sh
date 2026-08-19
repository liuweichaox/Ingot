#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="${INGOT_COMPOSE_FILE:-${repo_root}/docker-compose.app.yml}"
compose=(docker compose -f "${compose_file}")
confirmation="${1:-}"
backup_dir="${2:-}"
writers=()
restore_succeeded=false

usage() {
  echo "用法：$0 --confirm-replace-all-data <备份目录>" >&2
}

verify_checksums() {
  if command -v sha256sum >/dev/null 2>&1; then
    (cd "${backup_dir}" && sha256sum -c SHA256SUMS)
  else
    (cd "${backup_dir}" && shasum -a 256 -c SHA256SUMS)
  fi
}

restart_writers() {
  if [[ "${restore_succeeded}" == true ]] && (( ${#writers[@]} > 0 )); then
    "${compose[@]}" up -d "${writers[@]}" >/dev/null
  elif [[ "${restore_succeeded}" != true ]]; then
    echo "恢复未完成；写入服务保持停止，请排查后重新恢复。" >&2
  fi
}

restore_path() {
  local target="$1"
  local archive="$2"
  case "${target}" in
    /data/inspection-attachments|/archive/inspection-attachments|/data/process-knowledge|/archive/process-knowledge) ;;
    *) echo "拒绝清理未注册的恢复目录：${target}" >&2; exit 1 ;;
  esac
  "${compose[@]}" --profile maintenance run --rm --no-deps -T \
    -v "${backup_dir}:/backup:ro" app-maintenance -euc '
      target="$1"
      archive="$2"
      find "$target" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
      tar -C "$target" -xzf "/backup/$archive"
    ' -- "${target}" "${archive}"
}

if [[ "${confirmation}" != "--confirm-replace-all-data" || -z "${backup_dir}" ]]; then
  usage
  exit 2
fi
if [[ ! -d "${backup_dir}" ]]; then
  echo "备份目录不存在：${backup_dir}" >&2
  exit 1
fi
backup_dir="$(cd "${backup_dir}" && pwd)"
for required in manifest.txt SHA256SUMS postgres.dump postgres.contents \
  inspection-attachments.tar.gz inspection-archive.tar.gz \
  process-knowledge.tar.gz process-knowledge-archive.tar.gz; do
  [[ -f "${backup_dir}/${required}" ]] || {
    echo "备份缺少文件：${required}" >&2
    exit 1
  }
done
grep -qx 'format=ingot-app-backup-v1' "${backup_dir}/manifest.txt" || {
  echo "不支持的备份格式。" >&2
  exit 1
}
verify_checksums

while IFS= read -r service; do
  case "${service}" in
    platform-api|platform-worker) writers+=("${service}") ;;
  esac
done < <("${compose[@]}" ps --status running --services)
trap restart_writers EXIT
# Stop both known database writers even if one is currently restarting and was
# therefore absent from the "running" snapshot above.
"${compose[@]}" stop platform-api platform-worker >/dev/null
if ! "${compose[@]}" ps --status running --services | grep -qx postgres; then
  echo "PostgreSQL 容器未运行。" >&2
  exit 1
fi

"${compose[@]}" exec -T postgres dropdb --if-exists --force -U ingot ingot
"${compose[@]}" exec -T postgres createdb -U ingot -O ingot ingot
"${compose[@]}" exec -T postgres \
  pg_restore --exit-on-error --no-owner --no-acl -U ingot -d ingot \
  < "${backup_dir}/postgres.dump"

restore_path /data/inspection-attachments inspection-attachments.tar.gz
restore_path /archive/inspection-attachments inspection-archive.tar.gz
restore_path /data/process-knowledge process-knowledge.tar.gz
restore_path /archive/process-knowledge process-knowledge-archive.tar.gz

"${compose[@]}" run --rm platform-migrate
"${compose[@]}" exec -T postgres psql -U ingot -d ingot -v ON_ERROR_STOP=1 -Atc \
  "SELECT 'migration_count=' || count(*) FROM schema_version;"
restore_succeeded=true
echo "恢复完成并通过迁移检查：${backup_dir}"
