# Scenario Evaluation Boundary

> Document status: **deployer evaluation guide**. This document explains how to evaluate the recommendation loop in a real project; it does not define a second product workflow.

Ingot has one formal record: a real production run forms evidence, the system produces a next-recipe recommendation, an engineer adopts, modifies, or rejects it with a reason, then links an actual run and freezes the quality outcome. Evaluation must not require users to create an additional plan, side path, or approval state.

## Evaluation Questions

Deployers may evaluate, inside their own controlled environment:

- whether runs, actual settings, process features, context, and inspection outcomes remain traceable;
- whether recommendations, engineer decisions, actual runs, and quality outcomes form a complete auditable chain;
- whether recommendations remain inside known safety boundaries and observed coverage; and
- whether adoption, modification, and rejection reasons and later quality outcomes support continued use of the method.

Frozen historical records may be used offline to check algorithm determinism, constraint compliance, and future-information isolation. They are not an engineer-facing workflow to create, approve, or execute, and they do not replace new real production outcomes.

## Conclusion Boundary

Repository demos, synthetic tests, and offline algorithm evaluation establish software contracts or method boundaries only; they do not promise benefit for a particular factory. Deployers set their own scope, comparison baseline, quality measures, cost accounting, and stop conditions. When evidence is insufficient, repair the data chain, use a simpler method, or pause recommendations rather than presenting association as a causal conclusion.

## Data Confidentiality

Real production data, project and equipment identities, process parameters, quality distributions, sequential run traces, and derived results do not enter the public repository. Deployers manage evaluation data, access, retention, export, backup, and deletion in their own controlled environment; Ingot does not aggregate or endorse quantified benefits for a particular scenario.
