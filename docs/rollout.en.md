# Scenario Evaluation Boundary

> Document status: **deployer evaluation guide**. This document explains how to evaluate the recommendation loop in a real project; it does not define a second product workflow.

Ingot has one formal record: a real production run forms evidence, the system produces a next-recipe recommendation, an engineer adopts, modifies, or rejects it. Modification and rejection require a reason; adoption may include one. Adoption or modification then links an actual run and freezes its quality outcome; rejection requires no linked run. Evaluation must not require users to create an additional plan, side path, or approval state.

## Evaluation Questions

Deployers may evaluate, inside their own controlled environment:

- whether runs, actual settings, process features, context, and inspection outcomes remain traceable;
- whether recommendations, engineer decisions, actual runs, and quality outcomes form a complete auditable chain;
- whether recommendations remain inside known safety boundaries and observed coverage; and
- whether adoption, modification, and rejection reasons and later quality outcomes support continued use of the method.

Frozen historical records may be used offline to check algorithm determinism, constraint compliance, and future-information isolation. They are not an engineer-facing workflow to create, approve, or execute, and they do not replace new real production outcomes.

## Fix the Evaluation Protocol

Before comparing results, record the product and process scope, recipe version, quality specification, time window, inclusion and exclusion rules, cost accounting, and comparison baseline in the controlled environment. A baseline may be existing operation in the same scope or an applicable simple method. Explain its selection and do not change the protocol because an individual result is unfavorable.

Separate workflow usability from method effectiveness. Check evidence completeness, decision traceability, and constraint compliance before comparing runs-to-specification, quality outcomes, or cost. Explain incomplete, excluded, and rejected records separately rather than selecting only successful adopted recommendations. When scope, specification, or method changes, record the change and reassess comparability.

## Prevent Future-Information Leakage

Freeze the runs, inspections, and knowledge versions visible at each step in chronological historical evaluation. Candidate selection, feature computation, missing-value handling, and tuning may use only information available at that time. Future inspection outcomes and later knowledge revisions must not enter earlier recommendation inputs.

Fix the data snapshot, random seed, algorithm configuration, and software version, retaining inclusion and exclusion lists. Separate tuning data from final comparison data; document the limitation when they overlap. Historical replay selects only executions that happened, so explain how that restriction differs from a real field decision.

## Stopping and Recording Conclusions

Declare stop conditions before observing results, including safety-boundary anomalies, unverifiable quality facts, loss of context comparability, conflicting constraints, and insufficient evidence. When triggered, pause recommendations or repair data and record the time, related execution keys, and evidence for resuming. Expected benefit does not justify bypassing a stop condition.

Retain at least scope and versions, data window, baseline, inclusion and exclusion rules, decision distribution, actual outcomes, missing data, and limitations, with links to supporting runs and recommendations. A conclusion may establish workflow usability, require more data, or find the method unsuitable for now; insufficient evidence does not require a positive benefit claim. These are deployer evaluation records, not additional product business states.

## Conclusion Boundary

Synthetic replays, automated tests, and offline algorithm evaluation establish software contracts or method boundaries only; they do not promise benefit for a particular factory. Deployers set their own scope, comparison baseline, quality measures, cost accounting, and stop conditions. When evidence is insufficient, repair the data chain, use a simpler method, or pause recommendations rather than presenting association as a causal conclusion.

## Data Confidentiality

Real production data, project and equipment identities, process parameters, quality distributions, sequential run traces, and derived results do not enter the public repository. Deployers manage evaluation data, access, retention, export, backup, and deletion in their own controlled environment; Ingot does not aggregate or endorse quantified benefits for a particular scenario.
