# Frequently Asked Questions

## Is Ingot a data-acquisition system?

No. Acquisition is foundational; the product goal is to recommend the next process experiment from real evidence.

## Why do the docs mention FX3U optical-lens molding?

It is one real validation scenario, not Ingot's product positioning or capability boundary. Ingot addresses any discrete or process operation that can form a loop of actual conditions, process evidence, and inspected outcomes; a new scenario supplies its data sources, variables, objectives, constraints, and features.

## Does another machine require another optimizer?

Usually not. A machine needs protocol, point, cycle, and variable mappings. A process needs objectives, constraints, features, and optional priors. The optimizer protocol stays stable.

## Why not ask an LLM for the recipe?

LLMs lack calibrated numerical uncertainty and reliable constrained optimization. GP/BoTorch chooses settings; an LLM may assist explanation and structuring.

## Why are actual recipes required?

Equipment can introduce bias, clipping, and dynamics. When a `recipe:` or `signal:` mapping is explicit, missing actual data excludes the run instead of contaminating training.

## Why model process traces?

The same setpoint may produce different heating rates, overshoot, pressure hold, and cooling. Quality depends on the realized process, so the two-stage surrogate includes trajectory features.

## How does cold start work?

Without safety outcomes, use a Sobol space-filling design. With safety outcomes, begin from a verified safe baseline and explore nearby. The BoTorch GP engine starts after three valid observations.

## Can it recommend a batch?

Yes. The API supports 1–8 points and accounts for `X_pending`. The UI defaults to one run because the primary KPI is experiment count; parallel equipment can use larger batches.

## Does a recommendation write directly to a field control system?

No. It becomes a normal experiment for engineering review. Automatic writeback is a higher-risk control capability requiring separate safety engineering.

## Does optimizer failure stop acquisition?

No. Platform and Edge continue collecting and inspecting; only new recommendations are unavailable.

## Has Ingot already proven fewer experiments?

Not publicly. The code loop and automated tests exist, but historical replay and controlled prospective campaigns must establish impact.

## Does an internal deployment need permissions?

Permissions are not the current capability priority. Internal deployments should still replace sample secrets, limit database and equipment-network exposure, back up data, and require experiment approval.
