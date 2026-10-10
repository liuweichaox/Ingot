# Analysis and Optimization

> Document status: **current implementation and method boundary**. This page describes how real runs form next-recipe recommendations.

## One Business Loop

```text
real recipe run + actual settings + process context + valid quality outcome
                              ↓
                     optimization observation
                              ↓
 next-recipe recommendation inside safety boundaries and observed coverage
                              ↓
 engineer adoption / modification / rejection, with a recorded reason
                              ↓
             actual-execution link → frozen quality outcome
```

A recommendation is not an equipment command. It is an append-only record containing its input snapshot, prediction, uncertainty, constraints, evidence scope, and rationale. Engineer decisions, actual-execution links, and outcomes are also appended separately and never overwritten.

The current product starts a next-run correction from a published recipe version. Adoption records the engineer's decision; it does not create or publish a new version or download equipment settings. The next qualifying real run of that version supplies the execution link and frozen outcome. When the engineer considers a change significant, the suggested settings open a revision draft for that version and follow the configuration publication process. A recipe version cannot open another correction while one remains unfrozen.

## Admission and Stop Conditions

The system creates a recommendation only after at least three valid runs cover two distinct actual recipes. Runs require trustworthy identity, actual settings, required context, and quality outcomes. Incomplete data, poor comparability, inadequate coverage, conflicting constraints, or unmet model conditions stop the recommendation and state the reason.

## Handling Admission Failures

The admitted count from “Check data” describes readiness; it does not guarantee that the next request will generate a recommendation. Generation resolves published conditions and run evidence again, checking complete fields, distinct actual settings, and the optimizer response.

- **Execution incomplete or process data unavailable**: inspect the source run state, process signals, and published process analysis; do not substitute planned settings for actual values.
- **Missing readback, process features, or inspection values**: repair provenance in the relevant run. Inspection values must be finite; `INCONCLUSIVE` is not a definite outcome.
- **Insufficient context coverage or factor overlap**: check the frozen context policy against process configuration; do not add out-of-scope runs to meet a count.
- **Only one actual recipe or fewer than three valid runs**: wait for new real runs; copying a run does not create another observation.
- **Constraint, coverage, or optimizer snapshot mismatch**: retain the error and execution keys, then inspect configuration and service logs; do not widen ranges to bypass coverage.

The readiness interface returns candidate, valid, and excluded counts, a truncation flag, and at most eight example exclusion reasons. When results are truncated or many runs are excluded, review source run records; examples are not the complete exclusion list.

## Traceable Inputs and Outputs

Inputs comprise published conditions, actual run settings, quality objective values, outcome-constraint values, process features, and applicable knowledge and models. Platform deduplicates by execution key and records the request seed and input hash. An identical input hash may return an existing recommendation; repeated requests are not independent experiments.

The output is one recommendation with predictions, uncertainty, constraints, and coverage information. Platform checks the optimizer's observation and recommendation counts and independently recomputes the coverage envelope. Review input execution keys, objective and parameter units, bounds, and the knowledge and model versions used. Predictions do not replace later real inspection.

## Observed Coverage Envelope

Safety boundaries state where the process is allowed to go, not where historical runs have been. Daily production runs cluster around the current recipe and move several settings together, so a surrogate fitted on that data reports small uncertainty inside the cluster while extrapolating freely into regions no run ever visited. A recommendation therefore stays inside the observed coverage of real runs as well as the safety boundaries, bounded by two independent gates:

- **Range gate**: every variable stays between its observed minimum and maximum across historical runs, widened by a margin. The margin is the larger of 10% of the observed spread and 2% of the declared range, so a variable that never moved keeps a small local step rather than being frozen or released across its full range.
- **Leverage gate**: a candidate's Mahalanobis distance from the observed centre stays within the largest distance among the observations themselves, widened by 10%. This is the hat-matrix extrapolation criterion from response surface work. It admits interpolation inside sparse data while rejecting points off the directions production actually varied, which a per-variable range cannot express.

Candidates are generated inside the envelope rather than sampled across the declared range and filtered, because the envelope is often a thin slice of that range when parameters are strongly correlated. When the envelope holds too few candidates, the system stops and reports that production runs have not covered enough of the parameter space.

Recommendations use `reach-specification` only and always stay inside the observed coverage envelope.

The optimization service returns the envelope it applied and Platform recomputes the same envelope from the same runs. Platform stops the recommendation when the two disagree or when any suggestion falls outside it. The gate does not apply to offline algorithm evaluation: historical replay may only select runs that actually happened, so it is not extrapolation.

## Numerical Methods

The system selects robust statistics, linear or quadratic response surfaces, Gaussian processes, and Bayesian optimization according to the data. Every method follows the same admission, constraint, and traceability requirements; a more complex model is not more trustworthy merely because it is complex.

Mechanism knowledge may enter as hard constraints, soft ranking, or declarative feature input. Each recommendation freezes the exact knowledge, model, and input versions used; knowledge conflicts, staleness, or scope mismatch cause degradation or a stop.

## Offline Evaluation Boundary

Offline algorithm evaluation may use frozen historical data, isolate future outcomes by time, and compare determinism, constraint compliance, and applicable baselines. It supports development and method review; it is not a business workflow that an engineer creates, approves, or executes, and it cannot prove field benefit.

## Outcomes and Knowledge

When actual execution, parameter readback, and inspection facts are complete, the system freezes a recommendation outcome from source data exactly once. Adoption, modification, and rejection reasons together with quality outcomes become traceable evidence for the next observation and later knowledge updates.

## Related Documents

- [Pilot guide](pilot.en.md)
- [Mechanism knowledge design](mechanism-knowledge.en.md)
- [Current status](status.en.md)
