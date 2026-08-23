<div align="center">
  <a href="https://ingotstack.com/en/">
    <img src="apps/website/public/brand/ingot-lockup.svg" alt="Ingot" width="340">
  </a>

  <p><strong>Open-source Process Diagnosis & Optimization</strong></p>
  <p>Fewer wasted experiments. Faster routes to target process conditions.</p>

  [![CI](https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml/badge.svg)](https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml)
  [![License: MIT](https://img.shields.io/badge/license-MIT-E8AD56.svg)](LICENSE)
  [![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
  [![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB.svg)](https://www.python.org/)
  [![BoTorch](https://img.shields.io/badge/optimizer-BoTorch-5FD4C8.svg)](https://botorch.org/)

  [Website](https://ingotstack.com/en/) · [Documentation](https://docs.ingotstack.com/en) · [Quickstart](docs/getting-started.en.md) · [Report an issue](https://github.com/liuweichaox/Ingot/issues)

  [简体中文](README.md) · English
</div>

## Understand Ingot in three minutes

The demo follows one concrete problem: lens run `RUN-2026-0821-005` has a surface error of **0.48 μm**, above the **0.35 μm** limit, while the adjacent passing run measures **0.22 μm**. No database or Docker is required. Run these commands in two terminals:

```bash
node scripts/platform-demo.mjs
```

```bash
npm --prefix apps/platform ci
npm --prefix apps/platform run demo
```

Open `http://127.0.0.1:3001` and sign in with `demo / demo`. The workbench provides four steps: open the out-of-spec run, verify the reviewed quality result, compare it with passing runs, and inspect candidate causes, confounders, and the next validation experiment. A new user can follow the main path—understand the run, locate the important difference, and decide what to do next—in three minutes.

## Why Ingot exists

Ingot is built around one outcome:

> **Turn every real run into comparable, testable engineering evidence so process engineers can avoid unproductive experiments and reach target process conditions faster.**

Much process development still depends on personal memory, disconnected spreadsheets, and experiment sequences that cannot be reproduced. Even when equipment already produces data, production conditions, process curves, and quality outcomes often cannot be tied to the same real run, leaving computers unable to participate reliably in engineering decisions.

Ingot first establishes a trustworthy data loop, then helps engineers:

- identify the equipment, product, process specification, material, and tooling actually used for a run;
- connect actual settings, process trajectories, and inspection outcomes to that run;
- compare like-for-like runs and locate differences by variable, stage, or context;
- distinguish candidate causes, confounding factors, and insufficient evidence;
- turn engineering judgment into falsifiable, reviewable experiments;
- choose more valuable next experiments within safety boundaries;
- preserve validated conclusions, applicability, and failure conditions.

The computer organizes evidence, computes, and proposes. Process engineers frame the problem, review constraints, approve experiments, and make the final judgment.

## One complete loop

```text
Process configuration → Field integration → Production runs → Quality management → Diagnosis → Process R&D
        ↑                                                                                              ↓
        └──────── validated process specifications, operating regions, and knowledge return to production ────────┘
```

| Stage | Question answered |
|---|---|
| Process configuration | Which variables, units, objectives, quality rules, and safety boundaries must the platform represent? |
| Field integration | How do controls, instruments, vision, inspection, and business sources map to stable business semantics? |
| Production runs | Which conditions did this run use, and what actually happened during the process? |
| Quality management | Can inspection outcomes be linked to the same run and independently reviewed? |
| Process diagnosis | Is the data trustworthy, which differences deserve validation, and which remain confounded or unsupported? |
| Process R&D | Which next experiment is most valuable without crossing declared safety boundaries? |

Acquisition is not the destination, and an optimization algorithm is not the starting point. Trustworthy run facts are the common foundation for every analysis and recommendation.

## Select computation by the problem

Ingot does not force one “advanced algorithm” onto every question:

- data quality statistics measure coverage, missingness, and drift;
- robust statistics, matching, and stage analysis compare normal and abnormal runs;
- hierarchical summaries, variance components, or mixed-effects models assess equipment, material, and tooling context;
- controls, repetition, blocking, randomization, and interventions support causal decisions;
- Gaussian processes and constrained Bayesian optimization search expensive small-data parameter spaces;
- physical features or priors help when mechanisms are known and data are scarce;
- an LLM parses engineering questions, calls read-only tools, and organizes evidence explanations, but never generates numerical process settings directly.

See [Analysis and optimization](docs/optimization.en.md) for the detailed boundaries.

## Product components

- **Edge** connects control systems, instruments, gateways, and business sources, handling semantic mapping, execution-boundary detection, offline buffering, and replay.
- **Process Executions** organize continuous signals into traceable real runs and stage trajectories.
- **Manufacturing** records product, process specification, equipment, material, component, and tooling context.
- **Inspections** preserve quality objectives, safety outcomes, attachments, and human review.
- **Research** organizes problems, candidate causes, hypotheses, experiments, results, and operating regions.
- **Optimizer** performs reproducible numerical modeling, constraint checks, and sequential experiment recommendations.
- **Agent** helps engineers query, organize, and explain verified system facts.

These components share one evidence chain rather than creating conflicting parallel records.

## What works today

The repository implements the main path from field data to a next-experiment recommendation:

- link equipment, product, process specification, material, tooling, process trajectory, and inspection outcomes to the same real run;
- compare out-of-spec and passing runs on one screen, with important differences, missing data, and provenance visible;
- turn candidate causes into executable experiments with controls, repetitions, blocks, and safety boundaries;
- select among linear response surfaces, quadratic response surfaces, and Bayesian optimization from visible evidence, then recommend the most valuable next experiment;
- preserve inputs, versions, sources, constraints, and results so every recommendation can be reviewed and replayed.

Public replay has confirmed fewer additional queries than random search and maximin on selected physical-experiment datasets; a stable advantage over linear and quadratic response surfaces is still under evaluation. Full data, failed subgroups, confidence intervals, and decision rules live in one place: [Public-data experiment-efficiency validation](tools/public-validation/README.en.md). See [Scenario validation](docs/rollout.en.md) for real-pilot acceptance.

## Architecture

```mermaid
flowchart LR
    Sources["Controls / instruments / vision / inspection / MES"] --> Edge["Edge ConnectorHost\nmapping · execution boundaries · buffering"]
    Edge --> Platform["Platform API\nruns · context · inspection · R&D · evidence"]
    Platform --> Web["Platform Web\nengineering workbench"]
    Platform --> Optimizer["Optimizer\nstatistics · GP · constraints · experiment proposals"]
    Platform --> Agent["Agent\nquery · organize · explain"]
    Engineer["Process engineer"] --> Web
    Web --> Platform
    Optimizer --> Platform
    Agent --> Platform
```

Platform is the factory system of record. Optimizer is a stateless numerical service. Agent can access structured facts only through authorized tools. Edge and Platform keep independent processes, storage, and recovery even when deployed on one physical host.

## Quickstart

The complete Docker Compose stack requires only Git, Docker Engine or Docker Desktop, and Docker Compose v2. Source development additionally requires .NET SDK 10, Node.js 22.22+, and uv 0.11.32.

To inspect the UI and a complete synthetic workflow first, use the [simulated-data preview](docs/getting-started.en.md#simulated-data-preview). Use the full Compose stack below for a representative field pilot or production deployment.

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
docker compose -f docker-compose.app.yml up -d --build
```

The first build downloads .NET, Node, Python, PyTorch, and TimescaleDB images, so duration depends on network conditions. After the command exits, run `docker compose -f docker-compose.app.yml ps -a` and verify that `platform-migrate` exited successfully, the four HTTP/database services are `healthy`, and `platform-worker` remains `running`; an image download still in progress does not mean the application has started.

Then open:

```text
http://localhost:3000       Engineering workbench
http://localhost:8000/health
http://localhost:8000/openapi/v1.json
http://localhost:8100/ready
```

Local authentication uses `INGOT_ADMIN_USERNAME` and `INGOT_ADMIN_PASSWORD` from `.env`. If the administrator password is empty, Migrator writes the generated password to the `platform-migrate` log only when it bootstraps an empty user table. See [Getting started](docs/getting-started.en.md) and [Deployment](docs/deployment.en.md) for startup and troubleshooting details.

Complete one real or representative data loop before diagnosis and R&D: build a process configuration → connect field data → complete a run → link inspections → review data trust → compare runs → design a validation experiment.

See [Getting started](docs/getting-started.en.md).

## Development verification

```bash
dotnet restore Ingot.sln
dotnet build Ingot.sln
dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj
npm --prefix apps/platform ci
npm --prefix apps/platform test
uv sync --project optimizer --extra service --group dev --locked
```

Run the full CI gate with:

```bash
./scripts/verify.sh
```

Run the complete fixed public-manufacturing-data benchmark with:

```bash
./scripts/benchmark-public-validation.sh
```

This benchmark validates a reproducible software and method path; it does not replace shadow or controlled online validation on a real project. See [Public-data experiment-efficiency validation](tools/public-validation/README.en.md) for the current result and decision rules.

Verify the frozen v3 external-data evaluation protocol with:

```bash
./scripts/verify-public-validation-v3.sh
```

See the frozen [`protocol-v3.json`](tools/public-validation/protocol-v3.json) and retained [`latest-results-v3.json`](tools/public-validation/latest-results-v3.json). The evaluator rejects any algorithm, data, dependency, or protocol state that differs from the frozen fingerprint.

## Repository layout

```text
src/edge/          field acquisition and reliable upload
src/platform/      central API and business modules
src/agent/         model-assisted query and evidence explanation
src/shared/        domain models and shared contracts
optimizer/         numerical analysis and Bayesian optimization service
tests/             .NET core tests
apps/platform/     React process R&D workbench
apps/website/      official website
apps/docs-site/    documentation site
docs/              Chinese and English project documentation
deploy/            deployment assets
scripts/           verification and operations scripts
```

## Documentation

- [Documentation home](docs/index.en.md)
- [Getting started](docs/getting-started.en.md)
- [System design](docs/design.en.md)
- [Analysis and optimization](docs/optimization.en.md)
- [Public-data experiment-efficiency validation](tools/public-validation/README.en.md)
- [Mechanism knowledge design](docs/mechanism-knowledge.en.md)
- [Data integration](docs/data-connection.en.md)
- [Scenario validation](docs/rollout.en.md)
- [Roadmap](docs/project-plan.en.md)
- [Deployment](docs/deployment.en.md)
- [FAQ](docs/faq.en.md)
- [Brand guide](docs/brand.en.md)
- [Open-source dependencies](docs/open-source-dependencies.en.md)

## Roadmap

- [x] Real runs, actual conditions, process features, and inspections form traceable observations.
- [x] Candidate causes, hypotheses, experiments, and operating regions share one R&D record.
- [x] Constrained GP/BO recommendations, pending-point avoidance, and safe cold start.
- [x] Reproducible public-manufacturing-data benchmark with an explicit claim boundary.
- [ ] Freeze and run the external public physical-experiment evaluation, strong-baseline comparison, and mechanism-feature ablation.
- [ ] Publish leakage-free sequential replay on a real manufacturing history.
- [ ] Complete shadow recommendations and analyze engineer rejection reasons on a new project.
- [ ] Complete a controlled online experiment and publish preregistered results.
- [ ] Validate the core contracts on a second, materially different process.
- [ ] Expose read and propose capabilities through a model-independent agent protocol.
- [ ] Publish candidate schemas, validators, and reference implementations for the manufacturing evidence and experiment protocol.

The near term proves the historical evidence apparatus, the medium term opens agent protocols, and only the long term pursues an open specification. The roadmap follows real evidence and acceptance gates. See the [Roadmap](docs/project-plan.en.md) for sequencing.

## Contributing

Contributions are welcome across device adapters, statistics, experimental design, optimization algorithms, real replay, tests, documentation, and process knowledge. Start with the [Contributing guide](CONTRIBUTING.en.md), [Code of Conduct](CODE_OF_CONDUCT.md), and [Security policy](SECURITY.md).

Ingot is available under the [MIT License](LICENSE).
