# Ingot: AI Process R&D for Manufacturing

Ingot is an AI process R&D system for manufacturing. It fuses experimental data, real-time process data, physical mechanisms, and expert knowledge to help process engineers design experiments, discover patterns, optimize parameters, validate process windows, and shorten development cycles.

## The core problem

Process R&D is constrained by expensive experiments, interacting variables, limited samples, fragmented process and outcome data, and expert knowledge that is difficult to reuse. Engineers must find parameter ranges that meet target specifications with as few experiments as possible while respecting equipment, safety, material, and time constraints.

Ingot organizes every project into an evolving evidence chain:

1. define objectives, metrics, variables, and constraints;
2. acquire real data from experimental and production equipment and inspection instruments;
3. link process data, materials, tooling, recipes, and outcomes to individual experiments;
4. combine history, physical mechanisms, and expert knowledge into hypotheses;
5. analyze critical variables, phases, interactions, and uncertainty;
6. design the next experiments with higher information value;
7. update models, process windows, and understanding from new results;
8. validate conclusions and preserve traceable, reusable process knowledge.

## Product capabilities

### Native data acquisition

Ingot connects PLCs, controllers, sensors, experimental equipment, and inspection instruments through mainstream industrial protocols, with adaptations for actual models and site point maps. Edge performs sampling, quality assessment, offline persistence, and recovery forwarding.

### Process R&D management

Projects manage objectives, variables, constraints, hypotheses, experiments, outcomes, cost, progress, and review in one context.

### Intelligent experiment design

The system combines existing experiments, process constraints, model uncertainty, and information gain to help engineers choose the next experiments that are most worth running.

### Data and mechanism fusion

Time-series features, statistical evidence, data models, physical mechanisms, and expert rules work together to support explainable decisions under limited samples.

### Process-window validation

Candidate windows preserve applicable products, materials, equipment, tooling, and conditions, and are confirmed through controlled experiments for target outcomes, safety, stability, and reproducibility.

### Process knowledge

Reviewed conclusions preserve evidence, applicability, limitations, versions, and revalidation results for reuse across future products, materials, and equipment.

## One connected R&D path

```text
objective
  → variables and constraints
  → data and knowledge
  → hypotheses and analysis
  → experiment design
  → field execution and acquisition
  → inspection and outcomes
  → model and mechanism updates
  → process-window validation
  → process knowledge
```

## Suggested reading

- [Product and system design](design.en.md): product principles, the R&D loop, acquisition, data model, and intelligent R&D engine.
- [Rollout and validation](rollout.en.md): start with one real process-development project.
- [FAQ](faq.en.md): acquisition, experiments, AI, mechanisms, validation, and value.
