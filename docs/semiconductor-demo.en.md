# Semiconductor Wafer Etch Acquisition Demo

## Purpose

This demo uses an independent Modbus TCP simulator to show how the collector reads synthetic wafer-etch process data and links process samples with a run boundary, recipe version, and production context. All readings, recipe values, and results are demonstration data. They do not represent an equipment model, a real process window, or production guidance.

## Start

When the full stack is already running, execute this command from the repository root:

```powershell
pwsh -NoProfile -File scripts/setup-semiconductor-demo.ps1 -SkipBuild
```

To restart the simulator, wait for a new complete run, and verify its run boundaries and valid samples:

```powershell
pwsh -NoProfile -File scripts/setup-semiconductor-demo.ps1 -SkipBuild -WaitForBatch
```

The script starts `semiconductor-etch-simulator` and `connector-host`, logs in to the platform, and creates or reuses:

- A Modbus TCP simulator endpoint at `semiconductor-etch-simulator:1502`, polled every 500 ms.
- A process data model with 19 synthetic items: 18 process values and one stage value.
- A recipe with 20 synthetic demonstration parameters.
- One wafer-etch equipment record and a demo process-kit record.
- Production context `WAFER-ETCH-DEMO-001` and a process-analysis plan.

To manually observe a simulated run from the beginning, restart the simulator:

```powershell
docker compose -f docker-compose.app.yml -f docker-compose.demo.local.yml --profile semiconductor-demo restart semiconductor-etch-simulator
```

The simulator waits 10 seconds, then runs one 300-second batch:

1. Wafer load and pump-down: 0–25 seconds.
2. Gas stabilization and plasma start: 25–55 seconds.
3. Etch stage: 55–240 seconds.
4. Purge, shutdown, and unload: 240–300 seconds.

These stages and durations only make the demo traces easier to inspect. After a batch, the service remains idle; restart it to begin again.

## Verify

Health checks:

```powershell
Invoke-RestMethod http://127.0.0.1:9110/health
Invoke-RestMethod http://127.0.0.1:8003/health
```

In the platform, view the published task under “Configuration & Integration → Acquisition Configuration” and collector status under “Configuration & Integration → Edge Nodes”. “Field Operations → Production Runs” and “Process Insight → Process Analysis” show the demonstration data. `-WaitForBatch` records the current event cursor, restarts the isolated simulator, and accepts only a start and completion event from the same run after that restart; it then confirms complete run boundaries and at least one signal with valid samples. The script never prints the administrator password or access token.

## Configuration boundary

This demo configures one simulator endpoint and one acquisition task to illustrate the relationship among connection settings, mappings, probing, and run data. Actual projects should create data sources and tasks according to their real network, communication interfaces, equipment assets, and logical subjects. The system does not assume a controller count, equipment hierarchy, or workstation layout.

Pressure, power, gas-flow, and temperature fields in the demo model are synthetic signals. A real deployment must configure drivers, point addresses, data types, units, and timestamps from its own equipment documentation and field validation. Manage process windows and safety limits in mechanism knowledge and process control, outside acquisition-task mappings. Do not treat this demo as real wafer-process evidence or a process recipe.
