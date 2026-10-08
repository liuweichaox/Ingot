#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if grep -rEni --exclude-dir=dist --exclude-dir=node_modules \
  '/api/v1/agent|connector-workspaces|approve-package|AgentView|桌面 Agent|代码生成' \
  apps/platform/src; then
  echo "Platform Web must remain Chat-only." >&2
  exit 1
fi

if grep -rEni --exclude-dir=dist --exclude-dir=node_modules --exclude-dir=tests \
  '/api/v1/agent|connector-workspaces|approve-package|ConnectorBuilder|PackagingApprovers|Ingot Agent Desktop|desktop Agent' \
  src tests docker-compose.app.yml .github; then
  echo "Desktop code-generation surfaces are forbidden." >&2
  exit 1
fi

if grep -rEn --exclude-dir=dist --exclude-dir=node_modules \
  'Ingot\.Edge\.Agent|AgentDataAccess|EnableMultiAgent|MultiAgentEnabled|multiAgentEnabled|deepAnalysisEnabled' \
  src tests README.md README.en.md docs apps/website/app apps/docs-site/app docker-compose.app.yml .github; then
  echo "Legacy product names and compatibility configuration are forbidden." >&2
  exit 1
fi

# Production imports and site-specific mappings are deployment-controlled
# evidence, not repository assets. Deleted tracked files are ignored here so a
# cleanup commit can run the gate before it is created; CI checkouts contain any
# tracked file and will reject it.
sensitive_paths=()
while IFS= read -r path; do
  [[ -e "$path" ]] || continue
  case "$path" in
    tests/fixtures/synthetic/*|tools/*/examples/synthetic/*)
      ;;
    .ingot-import/*|mapping-*.json|*.csv|*.parquet|*.xlsx|*.xls|*.db)
      sensitive_paths+=("$path")
      ;;
  esac
done < <(git ls-files)

if (( ${#sensitive_paths[@]} > 0 )); then
  printf '%s\n' "${sensitive_paths[@]}"
  echo "Production data or site-specific import mappings must not be tracked. Synthetic data files are allowed only under tests/fixtures/synthetic or tools/*/examples/synthetic." >&2
  exit 1
fi

if grep -RInE --exclude='package-lock.json' --exclude='verify-product-scope.sh' \
  --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=.next \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=.venv \
  'IMPORT-REAL-DATA|measured_thickness_raw|vacuum_degree_kpa' \
  README.md README.en.md docs apps src tests tools scripts; then
  echo "Repository content contains site-specific production import markers." >&2
  exit 1
fi

# Retired parallel run-planning concepts stay out of current code, contracts,
# UI, tests, and documentation. Executions are the sole experiment records.
# Historical migrations stay immutable because deployed databases verify checksums.
retired_planning_pattern='IResearchExperimentRecordStore|ResearchExperimentRecordService|ResearchExperimentRecord|experiment-records|research_manual_experiment_records|Experiment.{0,20}(Plan|Planning|Workflow|Schedule)|Trial.{0,20}(Plan|Planning|Workflow|Schedule)|实验.{0,6}(计划|排程|工作流|状态机)|试验.{0,6}(计划|排程|工作流|状态机)'
if grep -RIniE \
  --exclude='package-lock.json' \
  --exclude='*.tsbuildinfo' \
  --exclude='verify-product-scope.sh' \
  --exclude='0001_baseline.sql' \
  --exclude='0024_standalone_experiment_records.sql' \
  --exclude='0025_remove_parallel_experiment_records.sql' \
  --exclude='0008_recipe_recommendations.sql' \
  --exclude='0013_research_evidence_integrity.sql' \
  --exclude='0015_retire_experiment_workflow.sql' \
  --exclude='0022_remove_legacy_workflow_artifacts.sql' \
  --exclude='0023_remove_retired_validation_artifacts.sql' \
  --exclude-dir=node_modules \
  --exclude-dir=dist \
  --exclude-dir=.next \
  --exclude-dir=out \
  --exclude-dir=bin \
  --exclude-dir=obj \
  --exclude-dir=.venv \
  --exclude-dir=.pytest_cache \
  "$retired_planning_pattern" \
  README.md README.en.md CONTRIBUTING.md CONTRIBUTING.en.md SECURITY.md CHANGELOG.md \
  docs apps optimizer src tests .github; then
  echo "Retired run-planning vocabulary is forbidden outside immutable cleanup migrations." >&2
  exit 1
fi

if grep -RInE \
  --exclude='verify-product-scope.sh' \
  --exclude='0001_baseline.sql' \
  --exclude='0024_standalone_experiment_records.sql' \
  --exclude='0025_remove_parallel_experiment_records.sql' \
  --exclude='0013_research_evidence_integrity.sql' \
  --exclude='0014_shadow_recommendation_execution_links.sql' \
  --exclude='0015_retire_experiment_workflow.sql' \
  --exclude='0022_remove_legacy_workflow_artifacts.sql' \
  --exclude='0023_remove_retired_validation_artifacts.sql' \
  --exclude-dir=bin \
  --exclude-dir=obj \
  '(^|[^[:alnum:]_])recommendation_knowledge_usage|research_shadow_recommendations|research_retired_workflow_records|research_transfer_assessments|research_rollback_drills|ResearchTransferAssessmentStatuses|ResearchTransferOutcomes|TransferAssessmentId|transfer-assessment' \
  src tests; then
  echo "Retired workflow storage names are forbidden outside immutable cleanup migrations." >&2
  exit 1
fi

echo "Platform Web product boundaries verified."
