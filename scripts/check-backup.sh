#!/usr/bin/env bash
# 校验备份文件的存在性、完整性和可恢复元数据。
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
backup_root="${1:-${INGOT_BACKUP_ROOT:-${repo_root}/deploy/backups}}"
maximum_age_hours="${2:-24}"

if [[ ! "${maximum_age_hours}" =~ ^[1-9][0-9]*$ ]]; then
  echo "最大备份年龄必须是正整数小时。" >&2
  exit 2
fi
if [[ ! -d "${backup_root}" ]]; then
  echo "备份根目录不存在：${backup_root}" >&2
  exit 1
fi

latest="$(find "${backup_root}" -mindepth 1 -maxdepth 1 -type d -name 'app-*' -print | sort | tail -n 1)"
if [[ -z "${latest}" || ! -f "${latest}/manifest.txt" || ! -f "${latest}/SHA256SUMS" ]]; then
  echo "没有找到完整的应用备份。" >&2
  exit 1
fi
if command -v sha256sum >/dev/null 2>&1; then
  (cd "${latest}" && sha256sum -c SHA256SUMS >/dev/null)
else
  (cd "${latest}" && shasum -a 256 -c SHA256SUMS >/dev/null)
fi

completed_at="$(sed -n 's/^completed_at=//p' "${latest}/manifest.txt")"
completed_epoch="$(date -u -d "${completed_at}" +%s 2>/dev/null ||
  date -j -u -f '%Y-%m-%dT%H:%M:%SZ' "${completed_at}" +%s 2>/dev/null || true)"
if [[ -z "${completed_epoch}" ]]; then
  echo "备份完成时间无效：${completed_at}" >&2
  exit 1
fi
now_epoch="$(date -u +%s)"
age_seconds="$((now_epoch - completed_epoch))"
maximum_age_seconds="$((maximum_age_hours * 3600))"
if (( age_seconds < 0 || age_seconds > maximum_age_seconds )); then
  echo "最近备份已过期：${latest}，完成于 ${completed_at}。" >&2
  exit 1
fi

echo "最近备份可用：${latest}，完成于 ${completed_at}。"
