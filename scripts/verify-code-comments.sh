#!/usr/bin/env bash
# 阻断缺少文件职责说明或公共边界文档的源码变更。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# 历史代码按增量基线治理；新建或修改的文件必须满足当前注释契约。
source_file_list="$(mktemp)"
trap 'rm -f -- "$source_file_list"' EXIT
if [[ "${INGOT_VERIFY_ALL_COMMENTS:-0}" == "1" ]]; then
  rg --files > "$source_file_list"
else
  base_ref="${INGOT_VERIFY_BASE_REF:-}"
  if [[ -n "$base_ref" ]] && ! git cat-file -e "$base_ref^{commit}" 2>/dev/null; then
    base_ref=""
  fi
  if [[ -z "$base_ref" ]] &&
     git diff --quiet && git diff --cached --quiet &&
     [[ -z "$(git ls-files --others --exclude-standard)" ]]; then
    base_ref="$(git rev-parse HEAD^ 2>/dev/null || true)"
  fi
  {
    {
      if [[ -n "$base_ref" ]]; then
        git diff --name-only --diff-filter=ACMR "$base_ref" HEAD
      fi
      git diff --name-only --diff-filter=ACMR
      git diff --cached --name-only --diff-filter=ACMR
      git ls-files --others --exclude-standard
    } | sort -u
  } > "$source_file_list"
fi

files_matching() {
  local pattern="$1"
  rg "$pattern" "$source_file_list" || true
}

failures=()
while IFS= read -r file; do
  [[ -f "$file" ]] || continue
  if ! rg -q '^[[:space:]]*(///|//|/\*)' "$file"; then
    failures+=("$file: C# 文件缺少职责或边界说明")
  fi
  while IFS= read -r failure; do
    [[ -z "$failure" ]] || failures+=("$failure")
  done < <(
    awk '
      /^public interface / {
        if (previous_nonblank !~ /^\/\/\/ <summary>.*<\/summary>$/ &&
            previous_nonblank !~ /^\/\/\/ <\/summary>$/)
          print FILENAME ":" FNR ": public interface 缺少类型级 XML summary"
      }
      $0 !~ /^[[:space:]]*$/ { previous_nonblank = $0 }
    ' "$file"
  )
done < <(files_matching '\.cs$')

while IFS= read -r file; do
  while IFS= read -r failure; do
    [[ -z "$failure" ]] || failures+=("$failure")
  done < <(
    awk '
      /^public (sealed |abstract )*class / || /^public sealed partial class [^(]+\(/ {
        if (previous_nonblank !~ /^\/\/\/ <summary>.*<\/summary>$/ &&
            previous_nonblank !~ /^\/\/\/ <\/summary>$/)
          print FILENAME ":" FNR ": public application class 缺少类型级 XML summary"
      }
      $0 !~ /^[[:space:]]*$/ { previous_nonblank = $0 }
    ' "$file"
  )
done < <(files_matching '^(src/agent/Ingot\.Agent|src/edge/Ingot\.Edge\.Application|src/platform/Ingot\.Platform\.Application)/.*\.cs$')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*(//|/\*)' "$file"; then
    failures+=("$file: JavaScript/TypeScript 文件缺少职责或边界说明")
  fi
done < <(files_matching '^apps/.*\.(js|jsx|mjs|ts|tsx)$')

while IFS= read -r file; do
  if ! rg -q '^#[[:space:]]' "$file"; then
    failures+=("$file: Shell 文件缺少 shebang 以外的职责或边界说明")
  fi
done < <(files_matching '^(scripts|deploy)/.*\.sh$')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*(--|/\*)' "$file"; then
    failures+=("$file: SQL 文件缺少用途或数据边界说明")
  fi
done < <(files_matching '\.sql$' | rg -v '^src/platform/Ingot\.Platform\.Infrastructure/Migrations/sql/' || true)

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*/\*' "$file"; then
    failures+=("$file: CSS 文件缺少样式职责说明")
  fi
done < <(files_matching '^apps/.*\.(css|scss)$')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*#' "$file"; then
    failures+=("$file: Dockerfile 缺少镜像职责说明")
  fi
done < <(files_matching '(^|/)(Dockerfile|[^/]+\.Dockerfile)$')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*#' "$file"; then
    failures+=("$file: PowerShell 文件缺少操作范围说明")
  fi
done < <(files_matching '\.ps1$')

while IFS= read -r file; do
  if ! rg -q '<!--' "$file"; then
    failures+=("$file: HTML 文件缺少页面职责说明")
  fi
done < <(files_matching '\.html$')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*[#;]' "$file"; then
    failures+=("$file: 配置文件缺少用途或边界说明")
  fi
done < <(files_matching '\.(yml|yaml|toml|conf)$')

if (( ${#failures[@]} > 0 )); then
  printf '%s\n' "${failures[@]}" >&2
  echo "源码注释完整性检查失败。" >&2
  exit 1
fi

echo "源码注释完整性检查通过。"
