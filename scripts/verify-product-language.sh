#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

public_files=(
  README.md README.en.md CONTRIBUTING.md CONTRIBUTING.en.md SECURITY.md
  docs apps/website/app apps/docs-site/app
  apps/platform/src
)

forbidden_heating_character=$'\u7089'
if grep -RInF --exclude='package-lock.json' "$forbidden_heating_character" "${public_files[@]}"; then
  echo "Public copy contains prohibited heating-equipment language or imagery cues." >&2
  exit 1
fi

if grep -RIniE --exclude='package-lock.json' \
  '(^|[^[:alpha:]])(furnace|kiln|smelter|alchemy)([^[:alpha:]]|$)' "${public_files[@]}"; then
  echo "Public copy contains prohibited heating-equipment or alchemy language." >&2
  exit 1
fi

if grep -RInE \
  '^#{1,6} .*[0-9]+[[:space:]]*[–—-][[:space:]]*[0-9]+[[:space:]]*(天|周|月|年|days?|weeks?|months?|years?)' \
  docs; then
  echo "Documentation phase headings must use acceptance gates instead of calendar estimates." >&2
  exit 1
fi

# Guard the stable product baseline. Algorithms, interface labels, and roadmap
# phases may evolve; the core value and public claim boundaries do not drift with them.
if grep -RInE --exclude='package-lock.json' \
  '制造生产数据与工艺分析系统|生产事件平台|工艺改进工作台|候选解释|产品面|深度调查' "${public_files[@]}"; then
  echo "Public copy contains obsolete product terminology. Follow docs/design.md and docs/brand.md." >&2
  exit 1
fi

canonical_zh='让工艺研发从没有数据支撑走向有数据支撑，让计算机基于真实数据帮助工艺工程师抉择，并采用适合问题的有效计算方法分析数据。'
canonical_en='Move process R&D from decisions without data support to decisions supported by real data, so computers can genuinely help process engineers choose what to do next using the most effective computational methods for the problem.'

for file in README.md docs/brand.md docs/index.md docs/project-plan.md; do
  if ! grep -Fq "$canonical_zh" "$file"; then
    echo "$file must retain the canonical Chinese core value from docs/brand.md." >&2
    exit 1
  fi
done

for file in README.en.md docs/brand.en.md docs/index.en.md docs/project-plan.en.md; do
  if ! grep -Fq "$canonical_en" "$file"; then
    echo "$file must retain the canonical English core value from docs/brand.en.md." >&2
    exit 1
  fi
done

if grep -RIniE --exclude='package-lock.json' \
  'every experiment[^.]*closer to (the )?optimum|closed-loop process optimization|optimization brain' \
  README.md README.en.md apps/website/app apps/docs-site/app; then
  echo "Public copy has drifted back to an algorithm-first product narrative." >&2
  exit 1
fi

if grep -RIniE --exclude='package-lock.json' \
  'manufacturing production data and process analysis system|production event platform|process improvement workspace|candidate explanation|deep investigation' "${public_files[@]}"; then
  echo "Public copy contains obsolete English product terminology. Follow docs/design.en.md and docs/brand.en.md." >&2
  exit 1
fi
