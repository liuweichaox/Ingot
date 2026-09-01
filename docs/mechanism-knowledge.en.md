# Mechanism Knowledge Design

> Document status: **architecture baseline**. Mechanism knowledge constrains and explains recommendations; it does not create a second R&D run workflow.

## Position

Mechanism knowledge stores engineering experience, process material, and source evidence as reviewable, versioned, scoped assets. It can explain a recommendation, exclude known unsafe combinations, or adjust candidate ranking; it cannot replace real production runs, quality outcomes, or an engineer decision.

## Relationship to the Recommendation Loop

```text
knowledge sources → claims and constraints → recommendation input snapshot
real runs and quality outcomes → optimization observation → next-recipe recommendation
engineer decision → actual-execution link → frozen outcome → evidence for knowledge updates
```

Each recommendation freezes the knowledge versions, source hashes, and applicability scope it used. An engineer's adoption, modification, or rejection reasons and the later quality outcome are the raw evidence used to strengthen or weaken knowledge.

## Rules

- Each claim has a source, applicability scope, version, review status, and conflict treatment.
- Hard constraints only reduce the candidate space; soft constraints only affect ranking and cannot manufacture a quality outcome.
- When inputs do not match, scope is exceeded, knowledge is stale, or conflicts remain unresolved, the system degrades to data-driven recommendation or stops recommending.
- Mechanism explanation states its evidence level and never presents association as a definitive root cause.
- A recommendation retains its exact knowledge snapshot; later knowledge edits never rewrite it.

## Current Boundary

The current path supports sources, fragments, claims, applicability, hard constraints, conflicts, versions, and recommendation-use traceability. Real-run decisions and quality outcomes progressively support or weaken knowledge; the repository does not claim proof of a particular mechanism or field benefit.

## Related Documents

- [System design](design.en.md)
- [Analysis and optimization](optimization.en.md)
- [Data model overview](data-model.en.md)
