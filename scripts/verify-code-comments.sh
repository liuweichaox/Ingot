#!/usr/bin/env bash
# 阻断缺少文件职责说明或公共边界文档的源码变更。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

failures=()
while IFS= read -r file; do
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
done < <(rg --files -g '*.cs')

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
done < <(
  rg --files \
    src/agent/Ingot.Agent \
    src/edge/Ingot.Edge.Application \
    src/platform/Ingot.Platform.Application \
    -g '*.cs'
)

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*(//|/\*)' "$file"; then
    failures+=("$file: JavaScript/TypeScript 文件缺少职责或边界说明")
  fi
done < <(
  rg --files apps \
    -g '*.js' \
    -g '*.jsx' \
    -g '*.mjs' \
    -g '*.ts' \
    -g '*.tsx'
)

while IFS= read -r file; do
  if ! rg -q '^#[[:space:]]' "$file"; then
    failures+=("$file: Shell 文件缺少 shebang 以外的职责或边界说明")
  fi
done < <(rg --files scripts deploy -g '*.sh')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*(--|/\*)' "$file"; then
    failures+=("$file: SQL 文件缺少用途或数据边界说明")
  fi
done < <(
  rg --files -g '*.sql' |
    rg -v '^src/platform/Ingot\.Platform\.Infrastructure/Migrations/sql/'
)

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*/\*' "$file"; then
    failures+=("$file: CSS 文件缺少样式职责说明")
  fi
done < <(rg --files apps -g '*.css' -g '*.scss')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*#' "$file"; then
    failures+=("$file: Dockerfile 缺少镜像职责说明")
  fi
done < <(rg --files -g 'Dockerfile' -g '*.Dockerfile')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*#' "$file"; then
    failures+=("$file: PowerShell 文件缺少操作范围说明")
  fi
done < <(rg --files -g '*.ps1')

while IFS= read -r file; do
  if ! rg -q '<!--' "$file"; then
    failures+=("$file: HTML 文件缺少页面职责说明")
  fi
done < <(rg --files -g '*.html')

while IFS= read -r file; do
  if ! rg -q '^[[:space:]]*[#;]' "$file"; then
    failures+=("$file: 配置文件缺少用途或边界说明")
  fi
done < <(rg --files -g '*.yml' -g '*.yaml' -g '*.toml' -g '*.conf')

if (( ${#failures[@]} > 0 )); then
  printf '%s\n' "${failures[@]}" >&2
  echo "源码注释完整性检查失败。" >&2
  exit 1
fi

echo "源码注释完整性检查通过。"
