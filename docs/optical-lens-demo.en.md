# Optical Lens PLC Acquisition Demo

## Purpose

This demo uses an independent Modbus TCP PLC simulator to verify the full “field connector → platform events → process samples” path. The data is deterministic simulation data. It does not represent a real equipment process window and must not be used for production control.

## Start

When the full stack is already running, execute this command from the repository root:

```powershell
pwsh -NoProfile -File scripts/setup-optical-lens-demo.ps1 -SkipBuild
```

The script starts `optical-lens-plc` and `connector-host`, logs in to the platform, and creates or reuses:

- Modbus TCP at `optical-lens-plc:1502`, polling every 500 ms.
- A process data model with 19 items: 18 continuous process values and one stage value.
- A recipe with 20 control parameters.
- One lens-mold assembly with six tooling components.
- Production context `OL25-420-DEMO-001` for a 25 mm PMMA optical lens.
- An analysis plan bound to the same data model so process samples can enter the platform analysis path.

To observe a complete batch from the beginning, restart the simulator:

```powershell
docker compose -f docker-compose.app.yml --profile optical-lens-demo restart optical-lens-plc
```

The PLC waits 10 seconds and then runs one 300-second batch:

1. Preheat and vacuum pull: 0–20 seconds.
2. Heating ramp: 20–95 seconds.
3. Temperature hold and pressure hold: 95–220 seconds.
4. Cooling and demolding: 220–300 seconds.

After the batch, the service remains idle so connector stop and reconnect behavior can be inspected. Restart it to generate another batch.

## Verify

Health checks:

```powershell
Invoke-RestMethod http://127.0.0.1:9100/health
Invoke-RestMethod http://127.0.0.1:8001/health
```

The published task is available in Workbench under “Configuration & Integration → Acquisition Configuration”; use “Configuration & Integration → Edge Nodes” to inspect the collector status. “Field Operations → Production Runs” and “Process Insight → Process Analysis” show the collected evidence. The script never prints the administrator password or access token.

## Boundaries

The PLC simulator is for development, demonstration, and connector acceptance only. A real deployment still needs the device address, register map, units, ranges, timestamp rules, and a site safety review supplied by the deployer. The demo parameters, process window, and run results must not be treated as real optical-lens production evidence.
