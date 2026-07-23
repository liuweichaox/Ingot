# Local device server and acquisition sample

This sample runs a simulated continuous heat-treatment furnace and lets Ingot Edge poll it once per second. Raw device fields, stable codes, data types, and context mappings are configuration-driven; the acquisition runtime contains no heat-treatment-specific fields.

## Simulated data

- Device: `FURNACE-001`, continuously operating without a fixed production cycle.
- Recipes: two switchable recipes with `double`, `integer`, `boolean`, and `string` parameters.
- Signals: furnace temperature and setpoint, pressure, oxygen, fan speed, heater state, and operating mode.
- Events: `recipe.applied` when the observed recipe first appears or changes, and one atomic `process.sample` per poll.

## Run

Start the device server in terminal 1:

```powershell
node docs/samples/device-acquisition/device-server.mjs
```

Start Edge with acquisition enabled in terminal 2:

```powershell
$env:DOTNET_ENVIRONMENT='DeviceSimulator'
dotnet run --project src/edge/Ingot.Edge.ConnectorHost/Ingot.Edge.ConnectorHost.csproj --no-build
```

Before Edge starts shipping, publish the process data model, both recipes, and the time-window analysis plan to the local Platform:

```powershell
node docs/samples/device-acquisition/register-platform.mjs
```

Inspect the device snapshot, acquisition status, and locally persisted events:

```powershell
Invoke-RestMethod http://127.0.0.1:8100/api/v1/snapshot
Invoke-RestMethod http://127.0.0.1:8001/api/v1/acquisition/status
Invoke-RestMethod 'http://127.0.0.1:8001/api/v1/events?subjectId=FURNACE-001&limit=20'
```

Switching the recipe makes Edge emit a new `recipe.applied` event without a restart:

```powershell
Invoke-RestMethod -Method Put -ContentType 'application/json' `
  -Body '{"recipeId":"HT-SHAFT-900"}' `
  http://127.0.0.1:8100/api/v1/active-recipe
```

The Edge token bundled with the `DeviceSimulator` environment is local-development machine authentication, not a page user or access password. Production deployments must inject a separate rotatable machine token through secure configuration. Platform acquisition tasks now configure HTTP polling, MQTT, OPC UA, and Modbus TCP while emitting the same normalized event. Protocol passwords are stored only as edge environment-variable references. Platform shipping remains an Edge outbox responsibility.
