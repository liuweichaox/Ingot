# Ingot Documentation

Ingot is an open-source process-optimization system for expensive, small-data manufacturing experiments. It connects control systems, instruments, vision, inspection, and business data into observations of real runs, then recommends the next experiment with constrained Bayesian optimization.

## Start here

| Goal | Read |
|---|---|
| Run the complete stack locally | [Install and run a first experiment](getting-started.en.md) |
| Understand components and boundaries | [Architecture](design.en.md) |
| Understand GPs, qLogNEI/qLogNEHVI, and constraints | [Optimizer](optimization.en.md) |
| Connect control systems, instruments, inspections, and run data | [Equipment and data wiring](data-connection.en.md) |
| Prove whether the system reduces experiments | [Real-world validation](rollout.en.md) |
| Deploy inside a factory network | [Deployment and operations](deployment.en.md) |
| Find common answers | [FAQ](faq.en.md) |

## One product loop

```text
define controls, objectives, and safety constraints
→ acquire actual recipes and cycle trajectories
→ join inspection outcomes
→ assemble experimental observations
→ diagnose candidate variables and propose an R&D hypothesis
→ fit trajectory and quality surrogates
→ recommend and validate the next settings
→ engineer review and execution
→ update from new evidence
```

Acquisition, inspection, features, experiments, and knowledge all serve this loop. A new process usually changes variables, mappings, objectives, safety limits, features, and optional physical priors—not the system architecture.

## Current status

The current implementation consists of Edge ConnectorHost, a modular Platform API monolith, embedded Agent capabilities, a separate Optimizer, and a separate Web frontend. The code-level loop is implemented and covered by automated tests. A real optical-molding historical replay has not yet been published. Ingot distinguishes “the software runs” from “the software has proven fewer factory experiments”; the latter requires run-by-run replay and prospective evidence.

## Public commitments

- Never present planned settings as actual run values.
- Never present a model mean as certainty.
- Never use an LLM to invent numerical recipes.
- Never claim real-process benefit from simulation alone.
- Retain snapshot, model version, intervals, and provenance for every recommendation.

Chinese documentation starts at [index.md](index.md).
