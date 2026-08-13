# Frequently asked questions

> Status: **current product and technical boundaries**.

## What is the central problem Ingot solves?

Move process R&D from decisions without data support to decisions supported by real data, so computers genuinely help process engineers choose what to do next using effective methods selected for the problem. Acquisition, analysis, experiments, and optimization all serve that goal.

## Is Ingot a data-acquisition system?

Not only. Acquisition is necessary, but value appears when data become the conditions, trajectory, and quality evidence of a real run and can support comparison, judgment, and the next experiment.

## Does Ingot replace process engineers?

No. The system organizes facts, executes calculations, exposes uncertainty, and proposes actions. Engineers frame the problem, review data and constraints, judge field executability, approve experiments, and own the final decision.

## Can the system find root causes automatically?

History usually supports candidate causes, stable associations, confounded associations, or insufficient evidence. A definitive cause requires engineering judgment and appropriate controls, repetition, blocking, randomization, or intervention. Ingot narrows the field and designs validation without presenting correlation as causation.

## Why are Manufacturing, Process Executions, and Inspections necessary?

Process Executions define a real run, Manufacturing preserves its equipment, product, process specification, material, and tooling conditions, and Inspections preserve outcomes. Without any one of them, a computer may combine runs produced under different conditions.

## Why are planned settings insufficient?

Equipment may limit, bias, or dynamically deviate from a plan, and operators may intervene. Models need actual execution values. Missing explicitly mapped actual values exclude a run with a visible reason instead of being filled by plans.

## Why do process trajectories matter?

The same setting may produce different heating rates, overshoot, pressure hold, position, or cooling trajectories. Quality depends on the realized process, not only the process specification table. Stage features help engineers locate where deviation began.

## Why record material, tooling, and equipment context?

They may be important factors or merely traceability. The system checks coverage and overlap before estimating influence; it does not add a field to a model merely because the field exists.

## Why not always use the most complex AI?

The most effective method depends on the question and data. Simple comparison, robust statistics, or a well-designed controlled experiment may be more reliable. With insufficient or unidentifiable data, the system should request data or refuse to answer.

## What does the LLM do?

It understands questions, calls authorized tools, organizes records, and explains results. It does not generate numerical process settings directly, replace statistics, constraints, or experimental validation, or invent facts without sources.

## As foundation models and agents become more capable, what does Ingot become?

It does not become a general industrial chat product. The long-term direction is a trustworthy decision and validation operating system for manufacturing processes. Foundation models and agents can be replaced, while Ingot preserves run facts, evidence relationships, experiment state, permission and approval, execution receipts, and conclusion boundaries. The near term proves the historical evidence apparatus, the medium term opens model-independent agent capabilities, and the long term develops an open evidence and experiment specification for manufacturing intelligence. See the [Roadmap](project-plan.en.md).

## Does adding MCP make an agent safe to drive experiments?

No. MCP and similar protocols standardize tool discovery and invocation. Platform and field systems must still enforce project isolation, provenance, approval, idempotency, action allow lists, device confirmation, and rollback. Capabilities progress from read to propose, commit, and execute; an agent cannot self-approve or connect directly to equipment.

## When is Bayesian optimization appropriate?

It is useful when experiments are expensive, responses noisy, controlled dimensions limited, objectives measurable, boundaries explicit, and experiments selected sequentially. High-dimensional, strongly drifting, slowly measured, or heavily unobserved processes should reduce scope, improve data, or use another method first.

## How can a project start with little data?

Establish a safe baseline and a small set of informative initial experiments. Engineering knowledge, classical DOE, space filling, or exploration in an approved safe region may apply. Never let an algorithm guess arbitrary process specifications without safety evidence.

## Can the system recommend a batch of experiments?

Yes, when parallel equipment or batch efficiency justifies it, while incomplete conditions remain pending points. Field execution determines batch size; “more recommendations” is not a value measure.

## Are recommendations written automatically to controls?

Not by default. A recommendation becomes a formal experiment, reviewed by an engineer and executed manually or through an MES, process-specification management system, or controlled integration. Equipment interlocks and field safety remain independent of the model.

## Does an Optimizer or model-service outage stop acquisition?

No. Edge, Platform, process executions, and inspections continue; only new recommendations or natural-language explanations depending on that service pause.

## Why do examples use optical-lens molding and FX3U?

They are the first reproducible scenario for validating the data chain and algorithm path, not the product boundary. A new process supplies variables, mappings, objectives, constraints, context, and optional mechanism knowledge without rewriting the evidence spine or experiment state machine.

## Has the system proved shorter development cycles?

Not publicly yet. Real-project historical replay and prospective online validation remain incomplete. Code capability is not product benefit. Quantitative claims follow the [Scenario validation](rollout.en.md) protocol and must disclose data, baselines, failures, and limitations.

## Is the documentation now finalized?

Core value, product boundaries, evidence principles, and stable architecture form the v1 baseline. Algorithms, default experiment parameters, menus, implementation status, roadmap, and validation results continue to evolve without redefining the core value or bypassing evidence boundaries.
