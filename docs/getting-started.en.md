# Install and Run a First Experiment

## 1. Prepare the environment

The recommended path is Docker Compose.

Requirements:

- Git;
- Docker Engine or Docker Desktop;
- Docker Compose v2.

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
```

Set database, Edge token, and admin secrets in `.env`, then run:

```bash
docker compose -f docker-compose.app.yml up -d --build
```

Open:

```text
http://localhost:3000       process R&D workbench
http://localhost:8000/health
http://localhost:8100/ready
```

## 2. Create an optical-molding campaign

Define at least:

| Type | Example |
|---|---|
| Control | holding temperature, 480–550 °C |
| Objective | form error, minimize, weight 1 |
| Safety outcome | crack rate ≤ 0.05, minimum probability 0.95 |
| Actual source | `recipe:holding-temperature` |
| Objective source | `inspection:form-error` |
| Safety source | `inspection:crack-rate` |

Variable codes, inspection characteristic codes, and units must remain stable.

The bundled optical-lens molding simulator uses a separate demo contract. Its
14 acquired values include an integer stage number on every sample, plus
upper/lower mold infrared temperature, current, voltage, power, pressure,
grating position, servo speed, vacuum, and servo position. Its 12 recipe
parameters cover HEAT/WORK/HOST positions,
temperature settings and limits, nitrogen temperature, preheat delay, and
pressure settings and limits. The simulated device exposes an FX3U-ENET-ADP
A-compatible MC 1E binary endpoint on port 5551. Its data-source profile uses
`melsec-a1e` to read scaled D registers rather than HTTP JSON. Moving to a real
PLC keeps the register map and changes only the PLC address and site MC port.
The process data model does not maintain a
separate stage catalog; cycle analysis segments traces from the acquired stage
number. This contract validates the local closed loop; it does not represent
real device addresses or a production process window.
The stage number is used only for trace alignment and stage-level feature
calculation, not for production-cycle completeness. A cycle is complete only
when the same `CorrelationId` has both `cycle.started` and `cycle.completed`;
variable-duration, repeated, or skipped stage numbers do not make the cycle
incomplete.

After creating the project, choose “start R&D,” then choose “propose the first hypothesis.” The current R&D flow requires at least one hypothesis tied to a controllable variable; the intelligent experiment-design action appears only when the project is no longer a draft and a hypothesis exists. Historical runs can also be imported to build observation and diagnosis evidence.

## 3. Wire a real run

One identifier crosses three boundaries:

```text
experiment RunKey
    = run or cycle CorrelationId (when present)
    = inspection OperationRunId
```

Let Platform generate the RunKey, then have a field adapter write it to a control-system correlation field or map it to a MES order, barcode, sample ID, or other run identifier; select or scan the same value during inspection.

## 4. Establish a safe baseline

When safety outcome constraints exist, cold start requires at least one inspected baseline inside every safety boundary. The optimizer will not invent an arbitrary recipe without safe evidence.

The baseline needs:

- a completed run or cycle record;
- actual run settings and conditions;
- usable process features;
- every objective outcome;
- every safety outcome.

## 5. Generate the next run

In a project with an R&D hypothesis, choose “design the next experiment.” The product UI defaults to two distinct conditions, two replicates per condition, and two execution blocks with rotated order. The result includes:

- recommended settings;
- means and 95% intervals;
- safety outcome predictions;
- combined feasibility;
- rationale;
- observation count, process-feature count, and model version.

Repeating the action before the current batch finishes returns the existing experiment.

## 6. Execute and feed back

After engineering approval:

1. choose “dispatch and start” to create an ordered, equipment-neutral execution package;
2. let a PLC/MES/recipe integration or operator apply the recommended settings;
3. associate the RunKey with the field run identifier;
4. execute the process run;
5. complete quality and safety inspections.

Once all planned runs have valid observations, Platform materializes the result, closes the experiment, and may create a candidate setting backed by repeats of the same condition. In validation, choose “design independent validation,” complete at least three repeats across two execution blocks, and let Platform verify actual settings, quality objectives, and safety constraints. A different engineer may then approve and release it. A continuous range requires separate boundary and interaction experiments; repeats at one point cannot approve the whole range.

## 7. Troubleshooting

Use experiment readiness to inspect exclusions:

- RunKey and run ID do not match;
- the run or cycle is incomplete;
- process data is unavailable or has no features;
- actual run settings are missing;
- inspection result or unit is missing;
- safety outcome is incomplete.

Do not hide missing actual data by entering planned values.
