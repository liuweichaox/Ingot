# Production Setup and Tooling Context

Ingot keeps the production cycle as the join point for time-series samples, phases, vision inspection, and manual inspection. MES work orders and manufacturing batches are not core Ingot entities. Information that affects process analysis and remains valid for a time interval is captured as production setup.

## Data entities

- Component type: the configurable classification dictionary used by the component ledger and by tooling-position compatibility rules.
- Tooling type: versioned configuration of assembly positions and the component types accepted by each position. The platform core does not understand industry-specific names such as an upper mold core.
- Tooling component: a replaceable and reusable physical part with a stable component ID, component type, and serial number. A component is not intrinsically bound to a tooling type or assembly position.
- Tooling assembly: the stable mold identifier used by operators.
- Assembly revision: the point where components are assigned to configured positions, producing an immutable member snapshot. Replacing any component creates the next revision.
- Tooling installation: the physical installed-to-removed interval of an assembly revision on a machine. A machine can have only one active installation, and a physical component cannot belong to active tooling on multiple machines.
- Production context: the product, recipe version, and installation effective on a machine. It may carry MES references but is not work-order management.

When a production-operation start event such as `cycle.started` or `run.started` is ingested, the platform resolves the unique context by machine and event time and freezes product, recipe, installation, tooling, and assembly-revision references into an immutable operation snapshot. That snapshot is persisted by `correlationId` and propagated to subsequent events across upload batches and service restarts. Dynamic device fields such as `workpiece_id`, phase, and controller step remain available. If no valid setup can be resolved, the event receives `context_capture_status=configuration_missing` and the operation record reports a data issue.

## MES boundary

- With MES: the integration writes production context with `source=mes`; operators do not enter the same order, batch, or recipe selection again.
- Without MES: operators record installation or removal only when the physical tooling changes. Product, recipe, or batch changes create only a production changeover. Subsequent cycles inherit the active state and never create per-cycle installation records.
- External order, batch, and material-lot references are optional query metadata and do not control Ingot lifecycles.

## API

- `GET|POST /api/v1/tooling-component-types`
- `GET|POST /api/v1/tooling-types`
- `GET|POST /api/v1/tooling-components`
- `GET|POST /api/v1/tooling-assemblies`
- `GET /api/v1/tooling-assemblies/revisions`
- `GET|POST /api/v1/tooling-assemblies/{moldId}/revisions`
- `GET|POST /api/v1/tooling-installations`
- `POST /api/v1/tooling-installations/{installationId}:remove`
- `GET|POST /api/v1/production-contexts`
- `GET /api/v1/production-contexts/current/{machineId}`
- `POST /api/v1/production-contexts/{contextId}:close`

Reads use unified platform authentication and the quality-read permission. Tooling, installation, and production-context writes require process-engineer or platform-administrator permission. Development keeps the existing unified development identity and introduces no separate username or access password.

The optical-glass molding example defines only two component types: mold core and mold holder. Upper core, lower core, upper holder, and lower holder are assembly positions rather than four component types. Other industries can configure their own component types and positions.

## Pages and lifecycle closure

- Tooling configuration: component types → component ledger → tooling types → tooling assemblies.
- Shop-floor operation: tooling installation/removal → production changeover.
- Lifecycle coupling: removing tooling atomically closes its active production context without deleting historical references. A product or recipe change with unchanged tooling does not create another installation.
- Automatic capture: a production changeover is not a work order or batch. It states which product, recipe, and installed tooling become effective on a machine. New cycles inherit and freeze those references.
- Analysis feedback: cycles, quality inspection, and historical comparison use the same frozen context so process parameters, tooling composition, and quality outcomes remain joinable.
