# Ingot Documentation

Ingot is an open-source process diagnosis and optimization system for expensive, small-data manufacturing experiments. It connects control systems, instruments, vision, inspection, and business data into traceable evidence for real runs, uses that evidence to explain which variable or trajectory segment made a run miss specification, and recommends the next experiment with constrained Bayesian optimization.

## Start here

| Goal | Read |
|---|---|
| Run the complete stack locally | [Install and run a first experiment](getting-started.en.md) |
| Understand components and boundaries | [Architecture](design.en.md) |
| Understand GPs, qLogNEI/qLogNEHVI, and constraints | [Optimizer](optimization.en.md) |
| Connect control systems, instruments, inspections, and run data | [Equipment and data wiring](data-connection.en.md) |
| Prove whether the system reduces experiments | [Real-world validation](rollout.en.md) |
| Review long-term phases, priorities, and acceptance gates | [Long-term project plan](project-plan.en.md) |
| Deploy inside a factory network | [Deployment and operations](deployment.en.md) |
| Find common answers | [FAQ](faq.en.md) |

## Six-step product loop

```text
define process → connect equipment → collect production data → close the data loop → diagnose → process optimization
       ↑                                                                                         ↓
       └──────── validated recipes, process windows, and knowledge return to production ─────────┘
```

1. **Define the process**: define equipment, products, process variables, standard units, recipe parameters, and quality objectives.
2. **Connect equipment**: select a communication driver, test the live connection, and map equipment points to stable process variables.
3. **Collect production data**: form runs from actual start and end signals while recording trajectories, stages, actual recipes, and context.
4. **Close the data loop**: check timestamps, gaps, and anomalies, then associate inspections with the same production run.
5. **Diagnose the process**: compare passing and failing runs and retain source, controllability, and confounding boundaries for every candidate cause.
6. **Process optimization**: verify safe candidates through controlled experiments and publish validated recipes, process windows, and reusable knowledge.

Diagnosis and optimization share one loop: the evidence read to explain a past result is the evidence read to choose the next experiment. Acquisition, inspection, features, experiments, and knowledge all serve this loop. A new process usually changes variables, mappings, objectives, safety limits, features, and optional physical priors—not the system architecture.

## Current status

The current implementation consists of Edge ConnectorHost, a modular Platform API monolith, embedded Agent capabilities, a separate Optimizer, and a separate Web frontend. The code-level loop is implemented and covered by automated tests. A real optical-molding historical replay has not yet been published. Ingot distinguishes “the software runs” from “the software has proven fewer factory experiments”; the latter requires run-by-run replay and prospective evidence.

## Public commitments

- Never present planned settings as actual run values.
- Never present a model mean as certainty.
- Never use an LLM to invent numerical recipes.
- Never claim real-process benefit from simulation alone.
- Retain snapshot, model version, intervals, and provenance for every recommendation.

Chinese documentation starts at [index.md](index.md).
