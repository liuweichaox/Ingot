# Getting started

> Status: **current operating guide**. The goal is not to run an optimizer immediately, but to complete one traceable record linking actual conditions, process behavior, and inspection results.

## Choose a path

You do not need to complete every step before seeing the system. Start with the path that matches your goal:

| Goal | Recommended path | Boundary |
|---|---|---|
| Evaluate the UI and workflow | Use the [simulated-data preview](#simulated-data-preview) below | Validates software flow; real process benefit is measured from field execution |
| Reproduce the optimization method without a real factory | Use the [public-data offline validation](#public-data-offline-validation) below | Validates data checks, categorical isolation, historical-pool replay, and baseline comparison; factory benefit is accepted separately using that factory's experiment count and elapsed time |
| Prepare a controlled pilot | Start with [Prepare the environment](#1-prepare-the-environment) and complete one representative run | Requires real or representative field data |
| Prepare production deployment | Read [Production architecture](production-architecture.en.md), then execute the acceptance steps in [Deployment](deployment.en.md) | Requires independent backup, failure, capacity, alert-delivery, and continuous-observation evidence |
| Contribute code | Install the repository and run `./scripts/verify.sh` | Applies to code contribution, not field acceptance |

### Simulated-data preview

This path requires Node.js 22.22+ only; it does not require a database, equipment, or Docker. Install the frontend dependencies once:

```bash
npm --prefix apps/platform ci
```

Start the synthetic API and frontend in separate terminals:

```bash
# Terminal 1: synthetic business API
node scripts/platform-demo.mjs

# Terminal 2: frontend connected to the synthetic API
npm --prefix apps/platform run demo
```

Open `http://127.0.0.1:3001`. Use `demo / demo` for the engineer workflow or `admin / admin12345` for system administration and the controlled-pilot gate. The service covers process configuration, field integration, production runs, inspections, diagnosis, experiments, and multiple data states, but every record is synthetic. Press `Ctrl+C` in both terminals when finished.

### Public-data experiment-efficiency validation

This path requires Git and uv 0.12.5 but no database, equipment, or factory data. It checks three explicitly licensed and SHA-256-verified reaction, formulation, and equipment-process datasets together with the frozen protocol and complete result:

```bash
./scripts/verify-optimizer-acceptance.sh
```

The retained frozen result belongs to the previous candidate. It found passing settings faster than random trial and error, but on Alkox and P3HT it failed to use the simpler, more effective stable trend and therefore ran extra experiments. Automatic method selection failed acceptance. The algorithm has since changed; this check confirms only that the old data and result remain reproducible and does not count them as current performance. See [`acceptance-results.json`](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/acceptance-results.json) and [Public-data experiment-efficiency validation](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md) for formal decision rules, licenses, and subgroup results. Whether another factory receives the same benefit is accepted separately under the same protocol using that factory's executed experiment count, success rate, and elapsed time.

## 1. Prepare the environment

Docker Compose is the recommended way to start the full system. You need Git, Docker Engine or Docker Desktop, and Docker Compose v2. The Compose path does not require .NET, Node.js, Python, or uv on the host.

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
```

Change the database password, Edge upload token, and administrator password in `.env`. Replace every `change-this-` placeholder; production uses independently generated random passwords and tokens.

Validate the Compose configuration, then start:

```bash
docker compose -f docker-compose.app.yml config --quiet
docker compose -f docker-compose.app.yml up -d --build
```

The first build downloads .NET, Node, Python, PyTorch, and TimescaleDB images and may take several minutes. After `up` exits successfully, inspect container state:

```bash
docker compose -f docker-compose.app.yml ps
```

`postgres`, `optimizer`, `platform-api`, and `platform-web` should all report `healthy`. Then check:

```text
http://localhost:3000       Engineering workbench
http://localhost:8000/health
http://localhost:8100/ready
```

Open `http://localhost:3000` and sign in with `INGOT_ADMIN_USERNAME` and `INGOT_ADMIN_PASSWORD` from `.env`. If `INGOT_ADMIN_PASSWORD` is empty, the system generates a random password only during the first migration bootstrap with an empty user table. Find it in the Migrator log:

```bash
docker compose -f docker-compose.app.yml logs platform-migrate
```

The Migrator bootstraps the first administrator only when the user table is empty. Changing the administrator password in `.env` later does not reset an existing account.

If the page is unavailable, run these two commands before rebuilding repeatedly or deleting volumes:

```bash
docker compose -f docker-compose.app.yml ps -a
docker compose -f docker-compose.app.yml logs --tail=200
```

No containers means the build has not completed or was interrupted. `unexpected EOF`, `short read`, or pull timeout usually means an incomplete image download; rerun `up -d --build` to reuse completed layers. See [Deployment](deployment.en.md#start-and-stop) for more diagnostics.

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
- ingestion tasks and source-point mappings;
- run boundaries, stages, and process features;
- inspection definitions and quality plan;
- process specification variables, allowed ranges, objectives, and constraints;
- context fields required for analysis or recorded when available.

Publish a process-configuration version before assigning it to an R&D project. Once execution starts, freeze the configuration and context policy so historical observations remain interpretable. The UI consistently uses “process configuration”; the API and code contracts retain the technical name `ScenarioPackage`.

After creating an R&D project, complete “Phase 0: preregistration and data baseline” in the project workspace. Freeze the data scope, inclusion and exclusion, comparison baselines, primary and guardrail measures, stop and falsification conditions, and the actual time and steps of the engineer's current workflow. The system calculates and freezes a data-reliability snapshot for the same scope. A different project member must review the preregistration before “Start research” becomes available.

## 4. Connect data sources

Open Process configuration → Configuration overview and check the readiness of data standards, field integration, analysis rules, quality rules, and configuration publishing. Connect continuous process sources under Field integration → Data source configuration:

1. Register the edge node and equipment identity.
2. Select a protocol and enter connection details.
3. Map raw points to stable process variables.
4. Probe and read real values through the target Edge.
5. Check data type, scale, offset, and unit.
6. Publish the configuration and confirm the Edge actually applied it.
7. Configure inspection entry, instruments, or quality-system sources.

Do not put PLC, instrument, or gateway addresses in an R&D project. Field integration owns source addresses and protocols; research references stable business codes.

The system supports reusable starting points. A precision-molding example can prefill a new process data dictionary, and a tooling-structure example can prefill upper insert, lower insert, and mold-frame roles. These examples only seed the structure; they must be adapted to the plant's actual equipment, instruments, vision systems, or MES fields, then connection-tested and version-published. Once the first device of a kind is verified, versioned task templates, data-source instances, and task bindings can be extracted for batch onboarding.

See [Data integration](data-connection.en.md) for protocol semantics.

## 5. Establish manufacturing context

For the current equipment, confirm:

- product or process object;
- published process specification version;
- installed tooling and assembly revision;
- material lot, components, calibration, or maintenance facts required by the scenario.

These facts freeze into a snapshot when the run starts. Do not wait for a quality problem and then manually guess which tooling assembly was probably used.

## 6. Complete one run

A stable identity must span the research plan, field execution, and inspection record:

```text
R&D ExecutionKey ←→ Platform ExecutionId
```

The two values may be identical or deterministically mapped, but the relationship must exist before execution and remain traceable. An MES work order, barcode, or instrument sample ID may carry the mapping; an equipment register is only an external reference and does not replace the Edge-generated `ExecutionId`.

After the run, open Production runs → Run records and confirm in the run detail that:

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

The platform currently accepts only PNG, JPEG, TIFF, or PDF inspection attachments whose signature matches the extension. An attachment is uploaded under the authorized `SiteId`, and every download rechecks role and site permission.

The result must link to the same real run. If linkage is not unique, retain a pending state instead of guessing by time proximity.

## 8. Review data trust

In Process diagnosis → Data trust and the project's Experiment data readiness view, review:

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

Open Process diagnosis → Diagnosis workbench. Select a run that missed its objective and a qualified historical or conforming baseline with explicit matching conditions. The system can help inspect:

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

You can first choose a classical DOE method and preview the complete run table. A preview only helps generate and check a plan: it neither approves the experiment nor sends settings to equipment. The engineer may edit runs, add controls, and submit once the checklist passes.

The engineer approves execution. Once all runs and inspections are complete, the system calculates the result from source data and marks the hypothesis supported, rejected, or inconclusive.

## 11. Enter optimization when appropriate

When the project has trustworthy observations, explicit controlled variables, and a safe baseline, it can generate the next experiment set. The system shows settings, objective intervals, safety outcomes, joint feasibility, data scope, model version, and rationale.

To use mechanism knowledge, open “Process R&D → Research assets,” select the project, upload a source, and complete claim review and activation. Select variables by name; their units come from the project. Select product, equipment, tooling, and process-specification applicability from project context. Claims that match the current project context may narrow hard bounds or participate in soft ranking. Actual usage is not shown in a separate Mechanism tab; it appears in the “Prediction and trust” column of the project's Experiment table, and only when an optimization experiment actually used a claim.

Pending points prevent duplicate recommendations while a batch remains incomplete. One successful point is only a candidate setting; a operating region requires independent repetition, boundary or interaction validation, and review by another engineer.
