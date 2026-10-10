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

## Sources, Review, and Conflict Resolution

When ingesting a source, retain its original location, license or usage authorization, site, and process scope. Extraction only establishes that text was obtained. Reviewers still check units, conditions, citation locations, and content hashes against the original. Source and fragment review do not replace evidence review for a mechanism claim.

A claim should state variables and units, applicability dimensions, evidence level, expected signature, and falsification conditions, with supporting or refuting evidence links. Status and version retain review conclusions. Retrieval rank is not a reason to raise confidence, and engineering experience should not automatically be labeled validated.

When claims disagree, link both claim identifiers and versions and record the conflict kind and rationale. Resolution retains the responsible reviewer, time, and conclusion. Check differences in scope or measurement separately from a contradiction in the mechanism. New evidence requires a traceable update; deleting a version used by an old recommendation must not hide the difference.

## Knowledge Retrieval

Document knowledge reaches the analysis assistant through source ingestion and fragment extraction, human review, keyword and optional semantic indexing, hard site and applicability filtering, hybrid ranking, and fragment-level citation. Only reviewed sources and human-reviewed fragments are retrievable. Results retain the source record, page or sheet, source SHA, and content hash so an engineer can return to the original material and detect content changes.

The semantic index is rebuildable derived state. It does not change review status or become formal business evidence. Embeddings are disabled by default. When enabled, they reuse the protected model-service configuration and require an OpenAI-compatible `/embeddings` endpoint. If that service is unavailable or a query embedding fails, the system falls back to PostgreSQL keyword retrieval without relaxing authorization or review gates.

## Current Boundary

Index maintenance handles retrieval availability and rebuilding; source maintenance retains originals and hashes; review determines applicability; authorization and scope filtering determine visibility. Vector matches, keyword matches, and reindexing grant no access rights. When a citation cannot reach a valid source, inspect its source record and review state before inspecting the index or embedding service.

The current path supports sources, fragments, claims, applicability, hard constraints, conflicts, versions, recommendation-use traceability, and scope-controlled hybrid retrieval for the analysis assistant. Real-run decisions and quality outcomes progressively support or weaken knowledge. A retrieval match does not establish a claim, and the repository does not claim proof of a particular mechanism or field benefit.

## Related Documents

- [System design](design.en.md)
- [Analysis and optimization](optimization.en.md)
- [Data model overview](data-model.en.md)
