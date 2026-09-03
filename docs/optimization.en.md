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

## Admission and Stop Conditions

The system creates a recommendation only after at least three valid runs cover two distinct actual recipes. Runs require trustworthy identity, actual settings, required context, and quality outcomes. Incomplete data, poor comparability, inadequate coverage, conflicting constraints, or unmet model conditions stop the recommendation and state the reason.

## Observed Coverage Envelope

Safety boundaries state where the process is allowed to go, not where historical runs have been. Daily production runs cluster around the current recipe and move several settings together, so a surrogate fitted on that data reports small uncertainty inside the cluster while extrapolating freely into regions no run ever visited. A recommendation therefore stays inside the observed coverage of real runs as well as the safety boundaries, bounded by two independent gates:

- **Range gate**: every variable stays between its observed minimum and maximum across historical runs, widened by a margin. The margin is the larger of 10% of the observed spread and 2% of the declared range, so a variable that never moved keeps a small local step rather than being frozen or released across its full range.
- **Leverage gate**: a candidate's Mahalanobis distance from the observed centre stays within the largest distance among the observations themselves, widened by 10%. This is the hat-matrix extrapolation criterion from response surface work. It admits interpolation inside sparse data while rejecting points off the directions production actually varied, which a per-variable range cannot express.

Candidates are generated inside the envelope rather than sampled across the declared range and filtered, because the envelope is often a thin slice of that range when parameters are strongly correlated. When the envelope holds too few candidates, the system stops and reports that production runs have not covered enough of the parameter space.

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
