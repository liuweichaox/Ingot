# Mechanism knowledge design

> Document status: **incremental implementation**. The claim kernel, relational storage, workbench, evidence-promotion lifecycle, constraint ranking, knowledge-snapshot gates, and recommendation usage trace are implemented. Bayesian priors and mechanism-residual fusion remain later phases.

## 1. Goal

Mechanism knowledge turns engineering experience, process material, and experimental conclusions into sourced, scoped, reviewable, falsifiable, and versioned engineering assets. When evidence permits, those assets improve candidate causes, experiment design, and next-experiment recommendations.

It does not create a second business spine. Knowledge follows the existing Ingot evidence loop:

```text
raw source → locatable fragment → mechanism-claim draft → engineer review
           → hypothesis and experiment → supporting or opposing evidence
           → formal knowledge version → recommendation constraint, prior, or explanation
           → observed result → further revision
```

Without mechanism knowledge, acquisition, run reconstruction, inspection linkage, comparison, statistical analysis, DOE, and data-driven recommendations inside approved bounds continue to work. Mechanism knowledge is an optional enhancement, not a startup prerequisite.

## 2. Non-goals

This capability does not:

- treat a retrieved document passage as a validated process conclusion;
- allow a language model to generate final numerical process settings;
- replace PLC, DCS, equipment interlocks, or field safety rules with a mechanism model;
- approve knowledge, experiments, or recommendations automatically;
- hide formal knowledge inside model parameters or treat model memory as business state;
- bypass data admission, experiment validation, or shadow validation because a physical explanation exists;
- silently generalize experience from one machine, material, or product to another context.

## 3. Current foundation and gaps

The current code already provides:

- `KnowledgeSource`: original files, hashes, project scope, and review status;
- `KnowledgeRecord`: fragments with page, worksheet, cell, or region citations;
- extraction extension points for PDF, Excel, CSV, text, images, and related sources;
- `ResearchHypothesis`: variables, expected effects, confounders, applicability, and supporting or opposing evidence;
- `MechanismModelVersion`: an executable, versioned, auditable affine mechanism model;
- `MechanismFusionDefinition`: calibration, post-processing, mechanism-as-feature, and ensemble fusion;
- one project spine for hypotheses, experiments, results, operating regions, knowledge claims, and independent review;
- Agent retrieval restricted to human-reviewed project knowledge.
- relational `MechanismClaim` versions, variables, applicability, constraints, evidence, reviews, and conflict records;
- the mechanism-knowledge workbench: asynchronous source extraction, claim entry, independent review, conflict registration, and independent resolution;
- relational storage for knowledge sources, context, fragments, structured values, and citation locations, with no JSONB payload on the formal read/write path;
- project-scoped evidence and content-hash validation, plus the recommendation knowledge-usage table.
- relational research hypotheses for causal chains, temporal features, interactions, failure conditions, and falsification conditions;
- the `reviewed → supported → validated → active → retired` lifecycle plus a formal `falsified` terminal path from every promoted state; preregistered effects and confidence intervals decide support or falsification, with a recorded human evaluation;
- hard-bound and soft-range ranking from context-matched, conflict-free `active` claims only;
- one frozen mechanism-knowledge snapshot across optimization, replay, shadow evidence, and controlled-online admission; stale experiments cannot be approved or started;
- experiment-page explanation of the claim, version, use type, and content hash actually applied.

The main gaps are:

1. the workbench does not yet provide model-assisted semantic-extraction drafts;
2. executable mechanism-model transitions still need the same evidence-promotion rules;
3. Bayesian priors, mechanism features, and residual models are not yet connected to recommendations;
4. snapshot-specific replay, shadow, and online gates are implemented; paired knowledge-versus-data-only effectiveness reports and long-horizon calibration metrics remain.

## 4. Three recommendation modes

Platform computes a capability profile before every recommendation. Optimizer does not infer the mode itself.

### 4.1 Data-only mode `data-only`

Admission requires:

- run and inspection data pass analysis admission;
- hard parameter bounds, equipment limits, and engineering safety ranges are explicit;
- sufficient historical observations, a safe baseline, or an approved initial DOE exists.

Like-for-like comparison, DOE, candidate ranking, interpolation inside approved bounds, and shadow recommendations are allowed. Cross-context transfer and extrapolation beyond the approved range are denied by default. Explanations describe data evidence without claiming a physical cause.

### 4.2 Knowledge-assisted mode `knowledge-assisted`

In addition to data-only admission, active mechanism claims match the current product, material, equipment, tooling, and process stage and have no open conflict. Claims may:

- narrow the candidate space;
- add hard or soft constraints;
- provide directional, threshold, interaction, or temporal expectations;
- rank candidate causes;
- select more falsifiable experiments;
- provide cited recommendation explanations.

Qualitative knowledge must not be silently converted into a precise numerical prior.

### 4.3 Mechanism-fused mode `mechanism-fused`

In addition to knowledge-assisted admission, a validated and active executable mechanism model and fusion definition match the current context. They may provide mechanism features, calibration, residual learning, post-processing, or controlled ensembles.

If the mechanism context does not match, inputs exceed validity ranges, a version is stale, or validation is insufficient, the system degrades instead of forcing execution.

## 5. Core concepts

### 5.1 `KnowledgeSource`

An immutable original file and its metadata. Sources include documents, spreadsheets, images, field notes, experiment retrospectives, and engineer explanations. The file remains in controlled object or file storage; Platform stores its hash, media type, project scope, and audit history.

### 5.2 `KnowledgeFragment`

A deterministically extracted, locatable passage. A fragment preserves page, worksheet, cell range, image region, extractor version, confidence, and content hash. It is evidence, not a conclusion.

### 5.3 `MechanismClaim`

A structured engineering statement about a variable relationship, threshold, interaction, temporal response, constraint, or failure mode. A claim states its applicability and falsification condition and cites at least one source fragment or system evidence record.

The first release limits claim types to:

| Type | Meaning | May support |
|---|---|---|
| `qualitative` | qualitative causal chain | candidate causes, explanations, experiment drafts |
| `monotonic` | monotonic increase or decrease | candidate ranking, soft priors |
| `threshold` | behavior changes across a threshold | candidate space, experiment boundaries |
| `interaction` | joint effect of two or more variables | interaction experiment design |
| `temporal` | delay, stage, or trajectory signature | trace diagnosis, stage features |
| `constraint` | reviewed engineering constraint | hard or soft constraints |
| `failure-mode` | failure mode and observable signature | stopping, refusal, diagnosis |
| `executable-model` | reference to an executable model | mechanism fusion |

### 5.4 `MechanismModelVersion`

A deterministic model supported by one or more reviewed claims. The existing affine model remains the first controlled implementation. Equations, lookup tables, state-space models, or external simulation may be added only as allowlisted model types; arbitrary uploaded executable code is not supported.

### 5.5 `RecommendationKnowledgeUsage`

A frozen record of the claim versions, model versions, usage types, and content hashes used by one recommendation. Later knowledge changes do not rewrite historical recommendations.

## 6. Relational data model

Original files stay in object storage. Controlled JSON may hold high-variation, non-critical display metadata. Identity, status, variables, applicability, constraints, evidence, and review are relational business fields.

### 6.1 Sources and fragments

```sql
knowledge_sources(
  source_id uuid primary key,
  project_id uuid not null,
  title text not null,
  source_kind text not null,
  status text not null,
  storage_ref text not null,
  sha256 text not null unique,
  media_type text not null,
  file_name text not null,
  size_bytes bigint not null,
  extraction_status text not null,
  extractor_version text,
  uploaded_by text not null,
  uploaded_at timestamptz not null,
  reviewed_by text,
  reviewed_at timestamptz
)
```

```sql
knowledge_fragments(
  fragment_id uuid primary key,
  source_id uuid not null references knowledge_sources,
  category text not null,
  content text not null,
  page_number integer,
  sheet_name text,
  cell_range text,
  region text,
  content_hash text not null,
  extraction_method text not null,
  extractor_version text not null,
  extraction_confidence double precision,
  human_reviewed boolean not null,
  reviewed_by text,
  reviewed_at timestamptz
)
```

### 6.2 Claims and versions

```sql
mechanism_claims(
  claim_id uuid primary key,
  project_id uuid not null,
  current_version integer not null,
  status text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null
)
```

```sql
mechanism_claim_versions(
  claim_id uuid not null references mechanism_claims,
  version integer not null,
  name text not null,
  mechanism_type text not null,
  statement text not null,
  expected_signature text,
  falsification_condition text not null,
  evidence_level text not null,
  created_by text not null,
  created_at timestamptz not null,
  reviewed_by text,
  reviewed_at timestamptz,
  content_hash text not null,
  primary key(claim_id, version)
)
```

### 6.3 Variables, applicability, and constraints

```sql
mechanism_claim_variables(
  claim_id uuid not null,
  claim_version integer not null,
  variable_code text not null,
  variable_role text not null,
  direction text,
  delay_ms bigint,
  unit text not null,
  primary key(claim_id, claim_version, variable_code, variable_role)
)
```

`variable_code` references a stable code from the process data model, control parameters, inspection characteristics, or research-project variables. The mechanism module never duplicates variable definitions.

```sql
mechanism_claim_applicability(
  claim_id uuid not null,
  claim_version integer not null,
  dimension_code text not null,
  dimension_value text not null,
  primary key(claim_id, claim_version, dimension_code, dimension_value)
)
```

Initial applicability dimensions include product family, product, material, equipment, tooling, scenario package, process specification, and stage. Empty applicability does not mean globally applicable; it means incomplete scope and is ineligible for recommendation use.

```sql
mechanism_claim_constraints(
  constraint_id uuid primary key,
  claim_id uuid not null,
  claim_version integer not null,
  variable_code text not null,
  constraint_kind text not null,
  minimum double precision,
  maximum double precision,
  unit text not null,
  severity text not null
)
```

### 6.4 Evidence, review, and usage

```sql
mechanism_claim_evidence(
  evidence_link_id uuid primary key,
  claim_id uuid not null,
  claim_version integer not null,
  evidence_kind text not null,
  reference_id text not null,
  polarity text not null,
  content_hash text not null,
  created_at timestamptz not null
)
```

```sql
mechanism_claim_reviews(
  review_id uuid primary key,
  claim_id uuid not null,
  claim_version integer not null,
  decision text not null,
  reviewer_id text not null,
  comment text,
  reviewed_at timestamptz not null
)
```

```sql
recommendation_knowledge_usage(
  recommendation_id uuid not null,
  claim_id uuid not null,
  claim_version integer not null,
  usage_type text not null,
  content_hash text not null,
  primary key(recommendation_id, claim_id, claim_version, usage_type)
)
```

## 7. State machines and governance

### 7.1 Knowledge source

```text
uploaded → indexed → reviewed → retired
    └──────────────────────────→ retired
```

A source enters `reviewed` only after all formal fragments have completed human review.

### 7.2 Mechanism-claim version

```text
draft → reviewed → supported → validated → active → retired
  └ rejected       └───────────────┴────→ falsified
```

- `draft`: machine-extracted or manually entered; cannot affect recommendations;
- `reviewed`: structure, variables, units, source, and applicability are reviewed;
- `supported`: at least one admissible intervention supports the claim, but no stable operating region is established;
- `validated`: preregistered repetition, blocking, boundaries, or interactions satisfy the validation rule;
- `active`: approved for recommendation use in declared contexts;
- `rejected`: the draft failed structural review;
- `falsified`: a formal experiment's confidence interval fails the preregistered effect; retained for audit and immediately removed from recommendations;
- `retired`: superseded or no longer applicable.

Creators cannot review their own claims or mechanism models. A reviewer does not edit the reviewed version; they approve, reject, or request a successor version. Conflicting claims may coexist and must not be overwritten by last-write-wins behavior.

## 8. Document recognition and semantic extraction

The pipeline has three independent layers so one model never reads, interprets, and approves the same content.

### 8.1 Deterministic structure extraction

Born-digital PDF, Excel, CSV, Markdown, and text prefer deterministic parsers. They preserve text, numbers, units, table structure, pages, and cell locations. The same file and extractor version must produce the same fragment hashes.

### 8.2 OCR and layout recognition

Scans, field photos, complex tables, and handwritten annotations use a replaceable OCR or Document AI adapter. Output includes regions, reading order, and field-level confidence. Low-confidence numbers and units require human confirmation.

### 8.3 Mechanism semantic extraction

A language model receives only located fragments and produces a fixed-schema `MechanismClaimDraft`. Every field cites source fragments. Unknown fields remain `unknown`; the model does not guess.

Recommended boundaries:

```csharp
public interface IDocumentStructureExtractor;
public interface IMechanismClaimExtractor;
public interface IMechanismClaimReviewService;
public interface IApplicableMechanismKnowledgeProvider;
```

Model providers, OCR engines, and deployment choices are adapters. Formal schemas, review state machines, and evidence citations belong to Platform.

## 9. Service boundaries

### Platform

- stores sources, fragments, claims, versions, reviews, and evidence;
- validates variables, units, context, and permissions;
- computes the recommendation capability profile;
- freezes knowledge versions used by each recommendation;
- links experiment results as supporting, opposing, or validation evidence;
- decides degradation, pause, and stop behavior.

### Extraction Worker

- runs document extraction, OCR, and semantic draft generation;
- cannot promote a draft to reviewed knowledge;
- reports extractor, model, prompt, and schema versions;
- may run inside the factory or through a controlled external service.

### Optimizer

- receives resolved bounds, constraints, priors, and mechanism features;
- does not query the knowledge store directly;
- does not decide whether knowledge is valid;
- remains reproducible from an input snapshot, policy version, and random seed.

### Agent

- retrieves authorized, reviewed knowledge;
- helps engineers draft claims, explain conflicts, and write experiment proposals;
- cannot activate knowledge or generate final numerical settings directly.

## 10. Recommendation capability profile

Before invoking Optimizer, Platform creates and freezes:

```csharp
public sealed record RecommendationCapabilityProfile
{
    public required string Mode { get; init; }
    public bool DataAdmissionPassed { get; init; }
    public bool AllowInterpolation { get; init; }
    public bool AllowExtrapolation { get; init; }
    public IReadOnlyList<ParameterBoundary> HardBoundaries { get; init; } = [];
    public IReadOnlyList<MechanismConstraint> SoftConstraints { get; init; } = [];
    public IReadOnlyList<MechanismClaimReference> ApplicableClaims { get; init; } = [];
    public IReadOnlyList<MechanismModelReference> ActiveModels { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
}
```

Admission order:

1. validate run, inspection, and context admission;
2. combine platform hard bounds and process-specification ranges outside equipment interlocks;
3. match active claims to the current project and context;
4. check claim conflicts, units, and versions;
5. match executable mechanism models;
6. select one of the three modes;
7. construct Optimizer input;
8. run deterministic boundary checks on Optimizer output;
9. persist the recommendation, knowledge-usage records, model versions, limitations, and hashes.

Conflicts, incomplete scope, or insufficient evidence exclude the affected knowledge or downgrade the mode. The system never silently chooses one conflicting claim.

## 11. Proposed APIs

### Sources and fragments

```text
POST /api/v1/research-projects/{projectId}/knowledge-sources
GET  /api/v1/research-projects/{projectId}/knowledge-sources
GET  /api/v1/knowledge-sources/{sourceId}
POST /api/v1/knowledge-sources/{sourceId}:extract
POST /api/v1/knowledge-fragments/{fragmentId}:review
```

### Mechanism claims

```text
POST /api/v1/research-projects/{projectId}/mechanism-claims
GET  /api/v1/research-projects/{projectId}/mechanism-claims
GET  /api/v1/mechanism-claims/{claimId}/versions/{version}
POST /api/v1/mechanism-claims/{claimId}/versions/{version}:review
POST /api/v1/mechanism-claims/{claimId}/versions/{version}:activate
POST /api/v1/mechanism-claims/{claimId}/versions/{version}:retire
GET  /api/v1/mechanism-claims:applicable
```

### Recommendation explanation

```text
GET /api/v1/recommendations/{recommendationId}/knowledge-usage
GET /api/v1/recommendations/{recommendationId}/capability-profile
```

Writes use structured requests and explicit authorization policies. State transitions use command endpoints rather than generic PUT replacement.

## 12. UI information architecture

### 12.1 Process R&D / Mechanism knowledge

Add a stable entry containing:

- **Sources**: upload, extraction, hash, status, and project scope;
- **Extraction review**: source and extraction side by side, with click-through to page or cell locations;
- **Mechanism claims**: variable mapping, direction, threshold, interaction, timing, applicability, and falsification;
- **Relationship view**: causes, mediators, outcomes, and failure modes;
- **Review queue**: pending review, conflicts, low confidence, and successor-version requests;
- **Usage and validation**: hypotheses, experiments, recommendations, and operating regions that reference a claim.

### 12.2 Research project

Add a Mechanism tab limited to knowledge accessible to the current project. A hypothesis may cite claims; experiment design shows what the experiment may support or falsify; completed results show evidence changes without mutating claims automatically.

### 12.3 Recommendation detail

Every recommendation shows:

- current mode;
- data range and sample coverage;
- claim and model versions used;
- usage type: constraint, prior, feature, or explanation;
- prediction, uncertainty, and feasibility probability;
- hard safety boundaries and platform limits;
- whether the point is inside observed data support;
- proposed validation and falsification conditions;
- engineer accept, modify, or reject reason.

## 13. Security, privacy, and deployment

- Raw process material remains inside the factory by default. External recognition requires explicit configuration and records provider, region, model, and data-processing policy.
- Secrets remain in server-side secret storage; the browser never calls a model provider directly.
- Uploads enforce type, size, malicious-content, and decompression boundaries.
- Extractors run in resource-limited processes and do not execute document macros, scripts, or arbitrary code.
- Every automatic extraction records model, prompt, schema, and extractor versions.
- Agent and Optimizer read only knowledge authorized for the current user, project, and scenario.
- Reviewed versions are immutable; changes create successors or retire old versions.
- Source deletion follows retention policy and cannot break evidence cited by published conclusions.

## 14. Implementation sequence and acceptance

### P0: claim kernel and relational storage

- add claim, variable, applicability, evidence, and review contracts;
- add relational migrations and store interfaces;
- enforce creator-reviewer separation;
- leave Optimizer behavior unchanged.

Acceptance: a claim can be created from source fragments, reviewed, rejected, versioned, and traced to source. Existing workflows continue without knowledge.

### P1: knowledge workbench and extraction adapters

- implement source upload, extraction review, variable mapping, and conflict UI;
- separate deterministic extraction, OCR, and semantic extraction interfaces;
- validate structured output and field-level citations.

Acceptance: an engineer can turn one representative document into one complete, reviewable, traceable claim. Incorrect numbers or units never enter formal knowledge automatically.

### P2: knowledge-assisted recommendations

- implement the capability profile;
- integrate hard bounds, forbidden combinations, candidate-space reduction, and explanation first;
- persist recommendation-knowledge usage;
- compare data-only and knowledge-assisted modes in shadow.

Acceptance: identical inputs replay identically; missing or stale knowledge causes deterministic degradation; recommendations never violate known hard bounds.

### P3: mechanism fusion

- integrate mechanism features, calibration, residual models, or ensembles;
- freeze model and fusion-definition versions;
- add calibration, drift, and mismatch stopping rules.

Acceptance: historical replay compares data-only, knowledge-assisted, mechanism-fused, and applicable simple baselines. Results support only preregistered claims and do not imply field benefit.

### P4: prospective validation

- freeze recommendations and independent engineer choices on a new project;
- record accept, modify, and reject reasons;
- compare executability, calibration, and ineffective-experiment rates;
- request controlled online testing only after shadow gates pass.

## 15. Evaluation metrics

### Extraction quality

- variable-mapping accuracy;
- number and unit accuracy;
- source-location completeness;
- acceptance, modification, and rejection rate for automatic drafts;
- correct escalation rate for low-confidence fields.

### Knowledge quality

- share of claims with applicability and falsification conditions;
- conflict-detection rate;
- independent-review coverage;
- completeness of supporting, opposing, and validation evidence;
- correct refusal under context shift.

### Recommendation value

- engineer accept, modify, and reject rates with reasons;
- out-of-range recommendations and known safety violations;
- prediction-interval coverage and feasibility calibration;
- experiment efficiency against DOE, random, historical order, and data-only mode;
- whether candidate-space reduction incorrectly removes truly feasible regions;
- negative-transfer detection across contexts.

## 16. Architecture invariants

1. Platform is the sole source of truth for formal knowledge, reviews, evidence, and recommendation usage.
2. Original sources, structured claims, executable models, and numerical recommendations are distinct assets.
3. The system operates without mechanism knowledge while making the degraded capability and claim boundary explicit.
4. Language models draft and explain; they do not approve knowledge or generate final numerical settings.
5. Formal knowledge has provenance, version, applicability, falsification conditions, and an independent reviewer.
6. Experimental results may support, oppose, or narrow knowledge, but never silently rewrite history.
7. Optimizer does not query the knowledge store; it consumes structured inputs frozen by Platform.
8. Equipment safety interlocks remain independent from models and platform constraints.

中文版：[mechanism-knowledge.md](mechanism-knowledge.md).
