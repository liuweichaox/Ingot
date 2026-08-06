# Installation and the first data loop

> Status: **current operating guide**. The goal is not to run an optimizer immediately, but to complete one traceable record linking actual conditions, process behavior, and inspection results.

## 1. Prepare the environment

Docker Compose is the recommended way to start the full system. You need Git, Docker Engine or Docker Desktop, and Docker Compose v2.

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
```

Change the database password, Edge upload token, and administrator password in `.env`, then start:

```bash
docker compose -f docker-compose.app.yml up -d --build
```

Check:

```text
http://localhost:3000       Process R&D workbench
http://localhost:8000/health
http://localhost:8100/ready
```

Simulated data are suitable for evaluating documentation and software flow. Product value must ultimately be validated with real or representative field runs.

## 2. Define the engineering problem first

Start with one bounded problem rather than handing an entire factory to a model. At minimum, define:

| Item | Question |
|---|---|
| Industrial object | Which product, process, or R&D object? |
| Equipment scope | Which machine or comparable group? |
| Run boundary | Where does one run start and end? |
| Controlled variables | What can the engineer actually change? |
| Quality objective | Which inspection determines success, in which direction, specification, and unit? |
| Safety boundary | Which parameter or outcome must never cross its boundary? |
| Context | Which material, tooling, lot, calibration, or maintenance facts require traceability? |

Variables, inspection characteristics, and units use stable codes. Display names may change; historical semantics must not drift casually.

## 3. Build a process configuration

A process configuration versions the process's data and analysis rules together:

- process data model and standard units;
- acquisition profile and equipment-point mapping;
- run boundaries, stages, and process features;
- inspection definitions and quality plan;
- recipe variables, allowed ranges, objectives, and constraints;
- context fields required for analysis or recorded when available.

Publish a process-configuration version before assigning it to an R&D project. Once execution starts, freeze the configuration and context policy so historical observations remain interpretable. The UI consistently uses “process configuration”; the API and code contracts retain the technical name `ScenarioPackage`.

## 4. Connect data sources

Under Data and connectivity:

1. Register the edge node and equipment identity.
2. Select a protocol and enter connection details.
3. Map raw points to stable process variables.
4. Probe and read real values through the target Edge.
5. Check data type, scale, offset, and unit.
6. Publish the configuration and confirm the Edge actually applied it.
7. Configure inspection entry, instruments, or quality-system sources.

Do not put PLC addresses in an R&D project. Equipment connectivity owns addresses and protocols; research references stable business codes.

See [Equipment and data connection](data-connection.en.md) for protocol semantics.

## 5. Establish manufacturing context

For the current equipment, confirm:

- product or process object;
- published recipe version;
- installed tooling and assembly revision;
- material lot, components, calibration, or maintenance facts required by the scenario.

These facts freeze into a snapshot when the run starts. Do not wait for a quality problem and then manually guess which mold was probably used.

## 6. Complete one run

A stable identity must span the research plan, field cycle, and inspection record:

```text
R&D RunKey ←→ field CorrelationId ←→ Platform OperationRunId
```

They may share one value or use a deterministic mapping, but the relationship must exist before execution and remain traceable. An MES work order, barcode, instrument sample ID, or equipment register can carry the mapping.

After the run, confirm in cycle detail that:

- start and completion events are present;
- actual parameters were collected;
- process curves and stages are available;
- run context resolved successfully;
- configuration and provenance versions are explicit.

## 7. Link inspection results

Enter or receive quality and safety results for the run. Each result includes at least:

- inspection characteristic code and version;
- value, disposition, and unit;
- sample or run identity;
- time, provenance, and required attachments;
- review state when applicable.

The result must link to the same real run. If linkage is not unique, retain a pending state instead of guessing by time proximity.

## 8. Review data trust

In Data quality and the project's Experiment data readiness view, review:

- run completeness;
- actual-setting coverage;
- process-feature coverage;
- run-to-inspection linkage;
- context required for analysis;
- unit, time, configuration-version, and provenance anomalies;
- the specific reason for every excluded run.

Common reasons include incomplete runs, missing actual settings, unavailable process data, missing inspections, unit mismatches, or missing context configuration. Never hide these with planned values or manual guesses.

At this point the system has moved from “data were collected” to “data can support engineering judgment.”

## 9. Compare runs and form candidates

Select a run that missed its objective and a qualified historical or conforming baseline with explicit matching conditions. The system can help inspect:

- the stage where deviation first appeared;
- planned-versus-actual settings;
- the largest trajectory-feature differences;
- coverage and confounding across material, tooling, equipment, or time;
- candidates that the next experiment could identify.

Observational analysis forms candidate causes only. The engineer reviews candidates, counterevidence, field limits, and missing data before creating an R&D hypothesis.

## 10. Design a validation experiment

An experiment draft defines at least:

- the candidate cause being tested;
- controlled variables and candidate conditions;
- controls, repetitions, blocks, and run order;
- objective, minimum meaningful effect, and safety boundaries;
- stopping and fallback conditions.

The engineer approves execution. Once all runs and inspections are complete, the system calculates the result from source data and marks the hypothesis supported, rejected, or inconclusive.

## 11. Enter optimization when appropriate

When the project has trustworthy observations, explicit controlled variables, and a safe baseline, it can generate the next experiment set. The system shows settings, objective intervals, safety outcomes, joint feasibility, data scope, model version, and rationale.

Pending points prevent duplicate recommendations while a batch remains incomplete. One successful point is only a candidate setting; a process window requires independent repetition, boundary or interaction validation, and review by another engineer.

## 12. Demo scenario

The optical-lens molding simulator validates the complete data path; it is not a real equipment address map or production process window. It exposes a Mitsubishi A-compatible MC 1E binary interface together with stage, temperature, pressure, position, and recipe values. A real machine requires fresh validation of addresses, ranges, units, run boundaries, and safety conditions.

Simulation can prove that the software path runs. It cannot prove shorter development time in a real process.
