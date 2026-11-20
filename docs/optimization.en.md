# Analysis and Optimization

> Document status: **current implementation and method boundary**. This page describes how real runs form next-recipe recommendations.

## One Business Loop

```text
real recipe run + actual settings + process context + valid quality outcome
                               ↓
                      optimization observation
                               ↓
             next-recipe recommendation from a checked candidate
                               ↓
                        engineer decision
       ├─ adoption or modification → actual-execution link → frozen quality outcome
       └─ rejection → record the reason and close the round
```

A recommendation is an append-only record with a frozen brief, input hash, prediction, uncertainty, and rationale. The engineer decides whether to adopt it. Engineer decisions, actual-execution links, and outcomes are also appended separately and never overwritten. Modification and rejection require a reason. Rejection is terminal and permits neither an execution link nor a frozen outcome.

The current product starts a next-run correction from a published recipe version. Adoption records the engineer's decision without creating or publishing a new version or sending equipment settings. The engineer confirms a run of the same version that started after the decision. Before freezing the outcome, the server checks the actual parameters against the decision. An outcome that did not follow the decision remains as evidence for traceability and does not count as validation of that decision. The actual parameters and admission rules determine whether that run can form a later observation.

When the engineer considers a change significant, the suggested settings open a revision draft for that version and follow the configuration publication process. The workbench disables generation when it finds an unfinished correction for the same site and recipe version. The generation API does not enforce a one-per-version limit on unfinished corrections. It passes adopted or modified decisions with the same frozen brief and no frozen outcome to the optimizer as pending points.

## Admission and Stop Conditions

Generation requires at least three valid runs and two distinct combinations of actual adjustable settings. Runs require trustworthy identity, actual settings, required context, and quality outcomes. The system stops with a reason when generation prerequisites fail, coverage is insufficient, or applicable hard constraints cannot be met. The minimum count is one prerequisite. How much the runs reveal also depends on comparability, variation in settings, and measurement quality.

## Handling Admission Failures

The admitted count from “Check data” describes readiness. Generation resolves published conditions and run evidence again, checking complete fields, distinct actual settings, and the optimizer response.

- **Execution incomplete or process data unavailable**: inspect the source run state, process signals, and published process analysis. Keep planned settings separate from actual values.
- **Missing readback, process features, or inspection values**: repair provenance in the relevant run. Inspection values must be finite. `INCONCLUSIVE` is not a definite outcome.
- **Insufficient context coverage or factor overlap**: check the frozen context policy against process configuration. Count only in-scope runs.
- **Only one combination of actual adjustable settings or fewer than three valid runs**: wait for new real runs. Copying a run does not create another observation.
- **Constraint, coverage, or optimizer snapshot mismatch**: retain the error and execution keys, then inspect configuration and service logs. Keep the configured range and envelope checks in place.

The readiness interface returns candidate, valid, and excluded counts, a truncation flag, and at most eight example exclusion reasons. When results are truncated or many runs are excluded, review source run records for the applicable reasons.

## Traceable Inputs and Outputs

Inputs comprise published conditions, actual run settings, quality objective values, outcome-constraint values, process features, and applicable knowledge and models. Platform deduplicates by execution key. The input hash covers the optimizer request, including its seed, as well as claim identities and model references. An identical input hash may return an existing recommendation for the same site and recipe. The recommendation stores the frozen brief, hashes, observation counts, model versions, and related details. The complete list of observations is not stored in the recommendation record.

The optimization service returns candidates with predictions, uncertainty, constraints, and coverage information. Platform checks the reported observation count, candidate count, and set of coverage variables. Review the run facts, objective and parameter units, parameter bounds, and the knowledge and model versions used. Later inspection provides the actual quality result.

## Observed Coverage Envelope

Configured bounds express allowed parameter settings. Historical runs show how settings have varied. When runs cluster around the current recipe or several settings vary together, a model can extrapolate when used far from those data.

In addition to parameter constraints, the optimizer uses two gates to limit candidate points to the observed coverage envelope.

- **Range gate**: every variable stays within its observed minimum and maximum plus a margin. The margin is the larger of 10% of the observed spread and 2% of the declared range. The configured range still limits the bounds.
- **Leverage gate**: a candidate's Mahalanobis distance from the observed centre stays within the largest distance among the observations themselves, widened by 10%. The distance uses a regularized covariance estimate to limit extrapolation beyond the joint variation in historical settings.

Automatic sampling draws candidates from the leverage ellipsoid and then checks the range gate and feasibility. When settings are strongly correlated, the usable region can be much smaller than the box defined by the individual variable ranges. The optimizer stops with a reason if too few candidates remain inside the envelope.

Recommendations use `reach-specification` only, aiming for predicted outcomes to reach specification. The optimizer applies both range and leverage gates to the original candidates it returns.

The optimization service returns the coverage envelope. Platform recomputes the range gate from the same runs and stops if the report is missing or the bounds disagree. Platform then snaps parameters to their published steps and checks parameter bounds, the range gate, and applicable hard mechanism constraints. Snapping can move a candidate, and Platform currently recomputes neither leverage nor model predictions. Joint coverage and the applicability of the original prediction therefore need separate verification for the aligned settings. Offline historical replay selects only runs that actually occurred.

## Numerical Methods

After Platform admission, the formal next-recipe recommendation path uses Gaussian processes and Bayesian optimization in BoTorch. The numerical service has a sequential optimization branch for fewer than three observations, which the Platform admission rules exclude from formal recommendations. Other methods, such as robust statistics and response surfaces, should be described in terms of the analyses or evaluations that use them.

## Mechanism knowledge and retrieval

- **Sources and scope**. Record the original location, usage permission, site, and content hash. Site access governs authorization, while recipe selectors govern applicability. Register the same content once per site. Re-uploading cannot change its scope. Review fragment units, conditions, and citations against the original.
- **Claims and review**. Record variables and units, version, scope, evidence level, expected effect, and falsification conditions. Link supporting or refuting evidence. Review sources and fragments separately from claims. Record both versions and the reason for a conflict, then retain the resolver and conclusion. Search matches locate material, and mechanism conclusions require separate verification.
- **Use in recommendations**. Applicable knowledge can supply hard constraints and forbidden combinations, and appropriate mechanism models can supply declarative features. Recommendations retain hashes and version references. The system excludes inactive, conflicting, or out-of-scope claims and stops if an applicable hard constraint leaves no feasible range. Platform has a soft-constraint ranking step. The formal path requests a single candidate (`top_k = 1`), so ranking currently leaves the choice unchanged.
- **Ingot Chat retrieval**. Search uses reviewed sources and human-reviewed fragments from authorized sites. An explicit recipe adds materials scoped to that recipe. Without a recipe, search uses site-wide material. Fragment citations retain the source, page or sheet, and hashes. The rebuildable semantic index is optional and disabled by default. When embeddings are unavailable, search falls back to keywords with the same authorization and review filters. See [Deployment](deployment.en.md) for configuration.

## Offline Evaluation Boundary

Offline algorithm evaluation may use frozen historical data, isolate future outcomes by time, and compare determinism, constraint compliance, and applicable baselines. It supports development and method review. Field benefit requires evidence from actual use.

## Outcomes and Knowledge

For an adopted or modified decision, the system freezes the outcome from source data once the linked run, parameter readbacks, process features, and required inspection results are complete. The decision and frozen outcome provide traceability for later observations and knowledge updates. Rejected recommendations retain only the decision and reason.

## Related Documents

- [Pilot guide](pilot.en.md)
- [Current status](status.en.md)
