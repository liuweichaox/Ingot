# Public-data offline validation

This directory turns the public manufacturing-data evaluation into a reproducible benchmark. It validates data checks, categorical-context isolation, historical-pool replay, baseline comparison, and claim boundaries. It does not extrapolate a public-data result into proof of savings for a factory.

## Source

- Dataset: FDM 3D Printing Dataset, DOI `10.17632/zd6td6svd6.2`
- Source: https://data.mendeley.com/datasets/zd6td6svd6/2
- License: CC BY 4.0
- Repository snapshot: 162 complete DOE rows selected from the 500-run source, covering a closed printer, PLA+/PETG, three infill patterns, and 3×3×3 grids for layer thickness, infill density, and speed.

See [NOTICE](NOTICE.md) for citation, selection, and field transformations. Before every run, the loader verifies the fixed SHA-256, row count, sample identities, and DOE grid. It stops on mismatch rather than silently repairing or imputing outcomes.

Material and infill pattern are categorical process context and are modeled in separate campaigns. They are not encoded as continuous controls with a false distance relationship. Layer thickness, infill density, and speed are the continuous controllable variables in this benchmark.

## Current reference result

[latest-results.json](latest-results.json) is the committed complete reference snapshot. It uses six categorical contexts, 20 fixed seeds per context, three initial observations, and a 12-trial budget:

| Check | Current result |
|---|---|
| Workflow validation | Passed; 6/6 contexts completed |
| Categorical-context isolation | Passed |
| Optimizer mean success / capped mean trials | 100% / 7.59 |
| Random baseline mean success / capped mean trials | 71.67% / 8.72 |
| Response-surface baseline mean success / capped mean trials | 87.5% / 8.07 |
| Contexts where optimizer used fewer trials than random / response surface | 4/6 / 4/6 |
| Strict experiment-count reduction claim | **Not demonstrated** (`not-demonstrated`) |

The capped mean counts a run that does not succeed within budget as `budget + 1`, avoiding survivor bias from reporting successful runs only. Aggregate metrics are better, but the optimizer does not beat both baselines in every context, so the strict claim remains not demonstrated.

## Run

```bash
./scripts/benchmark-public-validation.sh
```

The default result path is `artifacts/public-validation.json`; pass another path as the script's first argument when needed.

Fast one-scenario check:

```bash
uvx --from uv==0.11.32 uv run --project optimizer --locked \
  python tools/public-validation/benchmark.py --seeds 1 --max-scenarios 1
```

## Automation layers

- Ordinary PR/CI runs `optimizer/tests/test_public_validation.py` to verify the fixture, categorical isolation, schema, claim boundary, and a fast one-scenario replay. It detects software regressions without requiring stochastic floating-point scores to be bit-identical across platforms.
- `.github/workflows/performance.yml` runs the complete 6×20 benchmark weekly or on demand and uploads `public-validation.json`, revealing performance changes after algorithm or dependency upgrades.
- `latest-results.json` is a reference evidence snapshot committed after human review, not a generated file rewritten by every PR.

## Interpretation

`workflow_validation` must be `passed`. `experiment_reduction_claim` is separate and passes only when every one of the six categorical contexts matches or exceeds both baselines' success rate while using fewer experiments. Better aggregate means do not pass when any context regresses, and an unfavorable benchmark is not rewritten as a favorable narrative.

Public data can validate software behavior, method comparison, and safety boundaries. Factory-specific benefit still requires local historical replay and a small controlled experiment without exporting factory data.

## Update policy

Update the reference snapshot only for an intentional change to source data, selection, algorithm, dependency lock, or decision policy:

1. A data change must update `NOTICE.md`, license information, SHA-256, row-count checks, and DOE-structure checks together. Do not replace source outcomes with imputed or synthetic outcomes without disclosure.
2. Run the complete benchmark after an algorithm or dependency change and retain every context and failure.
3. After review, refresh the reference snapshot with:

   ```bash
   ./scripts/benchmark-public-validation.sh tools/public-validation/latest-results.json
   ```

4. If `workflow_validation` or `experiment_reduction_claim` changes, update the Chinese and English root README, documentation home, FAQ, and optimization guide in the same change. A worse conclusion must be updated as well.
5. Exact floating-point scores are not the sole blocking condition for ordinary PRs. Data integrity, categorical isolation, allowed conclusion states, and safety claim boundaries remain blocking.
