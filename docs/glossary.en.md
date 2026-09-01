# Glossary

> Document status: **current facts**. This page explains the recurring domain terms used across the public Ingot documentation. Specific numbers and maturity follow [current status](status.en.md), and product positioning follows the [brand guidelines](brand.en.md).

## Runs and recipes

- **Run**: The complete record of one real production execution that links actual conditions, process trajectory, production context, and quality outcomes through a single run identity.
- **Recipe**: A set of process settings for a specific product and equipment; the object the "next recipe" recommendation proposes.
- **Process specification**: Versioned configuration that defines variables, units, quality rules, and safety boundaries.
- **Operating region**: The validated parameter range and safety boundaries that optimization recommendations must not exceed.
- **Process trajectory**: The stage-by-stage characteristics of a run over time, used to locate where a deviation occurs.

## Evidence and admission

- **Run evidence**: A set of facts linked through one run identity, traceable to their source and independently reviewable.
- **Admission**: The completeness, comparability, and coverage checks that decide whether a real run may enter analysis; when they fail, recommendations stop and the reason is recorded.
- **Optimization observation**: A real recipe run plus its quality outcomes that passed quality and coverage admission, forming a reviewable optimization sample.
- **Comparability**: Whether different runs are sufficiently alike in material, equipment, tooling, time, and product to be compared.
- **Evidence gap**: A missing piece of information recorded explicitly when current facts cannot yet support a conclusion.

## Analysis and recommendations

- **Process diagnosis**: Comparing runs that satisfy comparability conditions to form candidate causes, counter-evidence, and evidence gaps, rather than asserting a root cause from history alone.
- **Next recipe**: One recipe recommended inside objectives, safety boundaries, and observed coverage, with candidate process settings, prediction intervals, risks, and rationale.
- **Candidate-coverage design (DOE)**: A statistical design method for understanding variable coverage and interaction; Ingot's current product loop admits only real runs and their quality outcomes.
- **Response surface**: A surrogate model that approximates the relationship between targets and parameters with an interpretable function, suited to limited samples.
- **Bayesian optimization**: A sequential optimization method for costly, stepwise decisions; it requires few variables, a quantifiable objective, and clear safety boundaries.
- **Surrogate model**: A statistical model, such as a Gaussian process, that approximates the real process response from limited samples.
- **Mechanism knowledge**: Reviewable knowledge from physical, chemical, or engineering principles used to constrain or explain recommendations without replacing real-run evidence.

## Deployment and responsibilities

- **Site**: A business scope with isolation boundaries; data, permissions, and tokens are assigned by site.
- **Edge ingestion token**: A separate, rotatable credential that a field connector uses to send events, configured independently per field.
- **Engineering decision**: Recommendations are never dispatched automatically; engineers define objectives and boundaries and review, adopt, or reject each recommendation.
