<div align="center">
  <a href="https://ingotstack.com/en/">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="apps/website/public/brand/ingot-lockup-dark.svg">
      <img src="apps/website/public/brand/ingot-lockup-dark.svg" alt="Ingot" width="360">
    </picture>
  </a>

  <h3>AI Process R&amp;D for Manufacturing</h3>

  <p>
    Fuse experimental data, real-time process data, physical mechanisms, and expert knowledge<br>
    to help process engineers design experiments, discover patterns, optimize parameters, validate process windows, and shorten development cycles.
  </p>

  <p>
    <a href="https://ingotstack.com/en/"><strong>Website</strong></a>
    ·
    <a href="https://docs.ingotstack.com/en"><strong>Documentation</strong></a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/issues">Feedback</a>
  </p>

  <p>English · <a href="README.md">简体中文</a></p>
</div>

## What is Ingot?

Ingot helps process engineers answer three critical questions within limited time and experiment budgets:

1. which process variables and phases truly affect the target outcome;
2. which next experiment will create the highest information value;
3. when the evidence is sufficient to confirm a process window and preserve reusable knowledge.

Ingot organizes objectives, variables, experiments, equipment processes, inspection outcomes, models, mechanisms, and expert judgment into a continuously updated evidence loop.

## Core capabilities

| Capability | R&D outcome |
|---|---|
| Native data acquisition | Acquire real process data through mainstream industrial protocols and equipment-specific adaptations |
| R&D project management | Manage objectives, variables, constraints, experiments, cost, and progress together |
| Process semantics | Connect signals, phases, materials, tooling, and quality metrics to development meaning |
| Experiment design | Design high-value next experiments from existing evidence, constraints, and uncertainty |
| Intelligent analysis | Identify critical variables, phases, interactions, and candidate process laws |
| Mechanism fusion | Combine physical mechanisms, data models, and expert knowledge for sample-efficient development |
| Process-window validation | Validate parameter ranges, expected outcomes, safety constraints, and applicability |
| Process knowledge | Preserve reviewed conclusions as traceable, reusable, continuously verifiable knowledge |

## Process R&D loop

```text
define the development objective
    ↓
establish variables, metrics, and constraints
    ↓
combine historical experiments, real-time process data, mechanisms, and expert knowledge
    ↓
discover patterns and form hypotheses
    ↓
design and review the next experiments
    ↓
execute experiments, acquire processes, and link inspections
    ↓
update models, process windows, and next-step recommendations
    ↓
validate conclusions and preserve process knowledge
```

Every experiment increases process understanding and becomes evidence that future projects can reuse.

## Product components

### Edge

Edge runs near equipment and experiments. It handles protocol communication, equipment adaptation, sampling, quality, offline persistence, and recovery forwarding. Drivers, equipment profiles, site points, and process-variable mappings are managed as separate layers.

### Platform

Platform manages R&D projects, experiments, variables, process data, quality outcomes, datasets, models, mechanisms, process windows, validation records, and process knowledge.

### Intelligent R&D engine

The engine performs quality checks, time-series feature computation, statistical analysis, experiment design, sequential optimization, model evaluation, mechanism fusion, and uncertainty estimation to provide evidence-backed next-step guidance.

### Ingot Chat

Ingot Chat works within an R&D project. It structures questions, retrieves evidence, invokes deterministic tools, drafts hypotheses and experiments, and links every result back to data, experiments, models, and knowledge sources.

## Measuring value

Ingot measures success through development outcomes:

- experiments required to reach target specifications;
- calendar time to find and validate a process window;
- material, equipment, and labor cost per project;
- valid-experiment ratio and recommendation adoption;
- process-window validation rate;
- reuse of process knowledge across future projects.

## Learn about Ingot

- [Project introduction](docs/index.en.md)
- [Product and system design](docs/design.en.md)
- [Rollout and validation](docs/rollout.en.md)
- [FAQ](docs/faq.en.md)

## Participate

Use Issues to share real process-development problems and product feedback. For code contributions, see the [contributing guide](CONTRIBUTING.en.md). Report security concerns through the [security policy](SECURITY.md).

Ingot is available under the [MIT License](LICENSE).
