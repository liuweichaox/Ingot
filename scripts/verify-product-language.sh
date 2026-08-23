#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

public_files=(
  README.md README.en.md CONTRIBUTING.md CONTRIBUTING.en.md SECURITY.md
  docs apps/website/app apps/docs-site/app
  apps/platform/src
)

# Production architecture is expressed as Ingot's own engineering contract.
# Do not name external database products as design authorities or alternatives.
forbidden_external_database_pattern='T[Dd][Ee][Nn][Gg][Ii][Nn][Ee]'
if grep -RInE \
  --exclude-dir=.git \
  --exclude-dir=node_modules \
  --exclude-dir=.next \
  --exclude-dir=dist \
  --exclude-dir=out \
  --exclude-dir=bin \
  --exclude-dir=obj \
  "$forbidden_external_database_pattern" .; then
  echo "Repository copy must not name the prohibited external database product." >&2
  exit 1
fi

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

check_entry_order() {
  local file="$1"
  shift
  local previous=0
  local entry line
  for entry in "$@"; do
    line=$(grep -nF -- "$entry" "$file" | head -n 1 | cut -d: -f1 || true)
    if [[ -z "$line" || "$line" -le "$previous" ]]; then
      echo "$file must keep the canonical primary navigation names and dependency order." >&2
      exit 1
    fi
    previous="$line"
  done
}

check_entry_order docs/design.md \
  '1. **工作台**' \
  '2. **现场接入**' \
  '3. **工艺配置**' \
  '4. **生产运行**' \
  '5. **质量管理**' \
  '6. **工艺追因**' \
  '7. **工艺研发**'

check_entry_order docs/design.en.md \
  '1. **Workbench**' \
  '2. **Field integration**' \
  '3. **Process configuration**' \
  '4. **Production runs**' \
  '5. **Quality management**' \
  '6. **Process diagnosis**' \
  '7. **Process R&D**'

canonical_nav_zh='现场接入 → 工艺配置 → 生产运行 → 质量管理 → 工艺追因 → 工艺研发'
canonical_nav_en='Field integration → Process configuration → Production runs → Quality management → Process diagnosis → Process R&D'
if ! grep -Fq "$canonical_nav_zh" docs/design.md ||
   ! grep -Fq "$canonical_nav_en" docs/design.en.md; then
  echo "System design navigation summaries must match the canonical product order." >&2
  exit 1
fi

canonical_zh='把每次真实运行变成可比较、可验证的工程证据，帮助工艺工程师减少无效实验，更快找到达到目标的工艺条件。'
canonical_en='Turn every real run into comparable, testable engineering evidence so process engineers can avoid unproductive experiments and reach target process conditions faster.'

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

# Real factory evidence remains access-controlled. Public documentation may
# describe protocols and conclusion boundaries, but must not promise disclosure
# of production data, project results, or full real-project evidence artifacts.
if grep -RInE \
  '公开报告至少包含数据范围|公开失败与限制|Public report includes data scope|publish failures and limits|publish separate reports for replay' \
  README.md README.en.md docs; then
  echo "Public documentation must not promise disclosure of confidential real-project evidence." >&2
  exit 1
fi

if ! grep -Fq '真实生产数据、项目与设备标识' docs/rollout.md ||
   ! grep -Fq 'Real production data, project and equipment identities' docs/rollout.en.md; then
  echo "Scenario validation documents must retain the real-production-data confidentiality boundary." >&2
  exit 1
fi

python3 scripts/verify-documentation-style.py
