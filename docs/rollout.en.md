# Rollout and Validation

An Ingot rollout begins with one real process-development project. The first objective is to establish a complete R&D loop and validate value through experiment count, development time, or resource cost.

## 1. Select the project

The first project needs:

- a defined product, material, and process;
- measurable target specifications;
- process parameters that experiments can adjust;
- clear equipment and safety boundaries;
- real execution and inspection conditions;
- a process engineer responsible for judging results.

Record the current baseline: experiments, calendar time, material consumption, equipment occupancy, and engineering effort normally required to reach specification.

## 2. Define objectives, variables, and constraints

The project records:

| Area | Required information |
|---|---|
| Objective | Metric, baseline, target, direction, tolerance, completion rule |
| Controllable variables | Name, unit, bounds, step, setting method |
| Process variables | Actual temperature, pressure, speed, position, time, and phase features |
| Outcome variables | Dimension, form, defect, strength, yield, efficiency, or cost |
| Context | Material, lot, equipment, tooling, recipe, environment, and people |
| Constraints | Equipment capability, safety, variable coupling, stop and rollback conditions |

## 3. Establish the data path

The delivery team implements the required protocol drivers and equipment adaptations, then maps site points into project variables. Validation covers connection, raw values, scaling, units, types, timestamps, cycle and experiment linkage, gaps, duplicates, recovery forwarding, and inspection linkage.

A small number of real experiments first confirm the data path and process semantics.

## 4. Collect historical evidence

Historical experiments, process data, inspections, process documents, physical mechanisms, and expert knowledge enter the same project. Source, time range, applicability, quality, and version are preserved in reproducible dataset snapshots.

## 5. Form the first development assessment

The system checks quality and experiment validity, aligns cycles and phases, computes features, analyzes sensitivity and interactions, compares history with targets, evaluates mechanism and expert evidence, and structures candidate hypotheses and limitations.

The process engineer reviews the result and confirms the objectives, variables, and boundaries used for the next experiments.

## 6. Design the next experiments

Recommendations consider:

- potential to approach target specifications;
- information gain for current hypotheses;
- explored and unexplored process regions;
- model uncertainty;
- material, equipment, and safety constraints;
- cost, duration, and feasibility.

Every candidate explains its rationale, parameters, expected outcome range, uncertainty, safety checks, and validation purpose. Engineers review candidates before execution.

## 7. Execute, inspect, and update

Each run separately preserves planned settings, actual settings, process traces, and inspection outcomes. The system assesses plan deviation, data completeness, and validity, then updates hypotheses, models, the process space, and next-step recommendations.

The loop continues until target specifications and repeatability criteria are met or project budget and stop conditions are reached.

## 8. Validate the process window

Controlled experiments confirm:

- target metrics meet their required range;
- critical process variables remain stable;
- safety metrics pass;
- independent repeats are consistent;
- material, equipment, tooling, and environmental applicability is explicit;
- every outcome traces to process data and computation evidence.

## 9. Preserve process knowledge

Reviewed conclusions become versioned process knowledge with the process law, parameter window, applicability, limitations, supporting and opposing evidence, dataset and model sources, risk, stop rules, revalidation requirements, reviewers, and validation date.

This knowledge provides a warm start for future products, materials, and equipment.

## 10. Measure the result

Compare the project with its baseline:

- experiments required to reach specification;
- development calendar time;
- ineffective-experiment ratio;
- material, equipment, and labor cost;
- adoption and effectiveness of recommended experiments;
- repeated validation of the process window;
- process knowledge created and reused.

Rollout is complete when real data and experiments have helped engineers finish a process-development objective faster.
