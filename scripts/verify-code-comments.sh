#!/usr/bin/env bash
# 检查变更的 C# 公共接口和指定应用类的 XML 文档。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# Keep this repository check usable in minimal WSL/CI images that have grep but
# do not install ripgrep. The wrapper covers only the rg options used below.
if ! command -v rg >/dev/null 2>&1; then
  rg() {
    case "$1" in
      --files)
        git ls-files --cached --others --exclude-standard
        ;;
      *)
        grep -E -- "$1" "${@:2}"
        ;;
    esac
  }
fi

# 默认只检查变更，允许显式审计整个仓库。
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
  while IFS= read -r failure; do
    [[ -z "$failure" ]] || failures+=("$failure")
  done < <(
    awk '
      { sub(/\r$/, "") }
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
  [[ -f "$file" ]] || continue
  while IFS= read -r failure; do
    [[ -z "$failure" ]] || failures+=("$failure")
  done < <(
    awk '
      { sub(/\r$/, "") }
      /^public (sealed |abstract )*class / || /^public sealed partial class [^(]+\(/ {
        if (previous_nonblank !~ /^\/\/\/ <summary>.*<\/summary>$/ &&
            previous_nonblank !~ /^\/\/\/ <\/summary>$/)
          print FILENAME ":" FNR ": public application class 缺少类型级 XML summary"
      }
      $0 !~ /^[[:space:]]*$/ { previous_nonblank = $0 }
    ' "$file"
  )
done < <(files_matching '^(src/agent/Ingot\.Agent|src/edge/Ingot\.Edge\.Application|src/platform/Ingot\.Platform\.Application)/.*\.cs$')

if (( ${#failures[@]} > 0 )); then
  printf '%s\n' "${failures[@]}" >&2
  echo "C# 公共类型文档检查失败。" >&2
  exit 1
fi

echo "C# 公共类型文档检查通过。"
