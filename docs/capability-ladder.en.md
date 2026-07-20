# Ingot Analysis Capability Ladder

This document defines the long-term stages from usable production records to process optimization recommendations. A level is assigned to a specific process problem, not to an entire factory. The same deployment may reach L3 for mold-life analysis and only L1 for another quality problem.

## Data scope

- Analysis uses only core parameters that directly describe the process or quality result, such as temperature, pressure, speed, feed, position, time, and mold or tool condition.
- Equipment, workpiece, production-run, and process-stage IDs only connect records correctly; they are not process parameters.
- Do not introduce plant-management fields that are unavailable or unrelated to the process. Shift, organization, and individual performance are not default analysis dimensions.
- Do not fill in missing fields. When a core parameter is unavailable, state what is missing and remain at the level supported by the available records.
- Configure only the parameters required by each process instead of defining one fixed field list for every factory.

## Capability levels

| Level | Stage goal | What the system can do |
|---|---|---|
| **L0 Usable production records** | Core parameters are connected to the correct process objects | Check missing, late, interrupted, and unit-conflicting records; connect equipment, workpieces, production runs, and process stages; distinguish machine capture, manual entry, and system estimates |
| **L1 Visible production process** | Explain what happened during production | Show stage duration, core-parameter trends and distributions, abnormal runs, and differences between process versions or equipment |
| **L2 Parameter relationships** | Narrow the investigation range | Analyze relationships between core parameters and quality results, controlled comparisons, change magnitude, priority parameters to check, and other core parameters changing at the same time |
| **L3 Quality risk prediction** | Warn before a quality problem occurs | Predict defect risk, drift, mold or tool life, and risk windows while stating prediction error and applicable range |
| **L4 Confirmed parameter impact** | Confirm which parameter changes produce an effect | Analyze recorded parameter changes, controlled before-and-after comparisons, DOE, and plant trials; report effect direction, size, and applicable process range |
| **L5 Process optimization recommendations** | Provide an optimization proposal for engineering review | Recommend parameter ranges within quality, cycle-time, energy, equipment, and safety constraints; state expected improvement, risk, and recovery conditions |

## Promotion rules

- A level depends on sufficient and stable records for the process problem, not merely on whether a software function exists.
- L2 lists possible causes and priority parameters to check; it does not present a root cause as confirmed.
- L3 continuously checks prediction error. If performance becomes unstable, return to L2 and show parameter relationships only.
- L4 requires controlled changes, DOE, plant trials, or clearly bounded historical adjustments. Ordinary historical relationships remain at L2.
- L5 returns a parameter range rather than a single value detached from plant constraints. Engineers approve recommendations and execute them through existing plant systems. Ingot does not control equipment.

## Applying the ladder to a new factory

For a new factory, first organize the available core parameters, units, production runs, process stages, and quality results. A process engineer confirms the mapping once, and Ingot stores it as reusable configuration. New processes should rely on configuration instead of changes to shared code. When key parameters are not connected, the system clearly lowers the analysis level it can provide.

This document describes the long-term destination. See the [implementation roadmap](roadmap.md) for current priorities and sequencing.
