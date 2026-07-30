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

Choose “generate optimized experiment.” The product UI defaults to two distinct conditions, two replicates per condition, and two execution blocks with rotated order. The result includes:

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

Once all planned runs have valid observations, Platform materializes the result, closes the experiment, and creates an evidence-backed candidate window when possible. Window maturity then progresses through replay review, laboratory replicate validation, and production release.

## 7. Troubleshooting

Use experiment readiness to inspect exclusions:

- RunKey and run ID do not match;
- the run or cycle is incomplete;
- process data is unavailable or has no features;
- actual run settings are missing;
- inspection result or unit is missing;
- safety outcome is incomplete.

Do not hide missing actual data by entering planned values.
