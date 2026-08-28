# System design

> Status: **v1 architecture baseline**. This document fixes product principles, business-record boundaries, and stable component responsibilities. Algorithms, default validation parameters, page layouts, and implementation sequence remain evolvable strategies.

This document defines component responsibilities, dependency direction, systems of record, runtime boundaries, and architecture constraints that may not be bypassed. Implementation changes are reviewed against this baseline and the automated architecture gates. See [Getting started](getting-started.en.md) for operating instructions.

## Design objective

Ingot's core value is fixed by the [Brand guide](brand.en.md): turn every real recipe run into optimization evidence and continuously recommend the next recipe within safety boundaries and observed coverage.

The architecture must therefore:

1. **Establish trustworthy facts first**: every analysis traces to a real run, actual conditions, process data, and quality outcomes.
2. **Support engineering judgment next**: show differences, evidence, counterevidence, confounding, and uncertainty rather than only a score.
3. **Let production naturally form optimization samples**: completed recipe runs automatically link actual parameters, process context, and quality outcomes without a separately created experiment.
4. **Select methods by the problem**: statistics, response surfaces, machine learning, Bayesian optimization, physical models, and controlled validation are replaceable tools.
5. **Keep engineers in control**: engineers define objectives and safety boundaries and confirm the next recipe; the system never dispatches it automatically.

The system is designed for one company on its factory network, shared by process, quality, equipment, and R&D teams.

## Product model

```text
Process configuration → Field integration → Production runs → Quality management → Process diagnosis → Recipe optimization
```

The first four steps follow business dependencies to organize field activity into trustworthy run facts. The last two use those facts to support engineering decisions. Process diagnosis explains an observed result; recipe optimization directly consumes normal production runs and recommends the next recipe within objectives, safety boundaries, and observed coverage. Controlled validation is used only for causal confirmation, extrapolation, or operating-region validation; it is not a prerequisite for daily optimization. Both read the same evidence.

The current Web information architecture balances the decision chain with frequent role-based tasks through seven business entries:

1. **Workbench**: prioritized quality tasks, run status, field status, and R&D progress;
2. **Field integration**: edge nodes, communication drivers, and mappings from multiple source fields to process variables;
3. **Process configuration**: configuration overview, data dictionaries, process specifications, analysis rules, quality configuration, tooling configuration, and configuration publishing;
4. **Production runs**: production preparation, tooling installation, run records, the object catalog, and run events;
5. **Quality management**: inspection tasks, independent review, quality records, and deviation analysis, with direct access for daily quality work;
6. **Process diagnosis**: the diagnosis overview, data quality, run comparison, and the analysis assistant; AI is an analysis method rather than a standalone business domain;
7. **Recipe optimization**: optimization tasks, real-run observations, next-recipe recommendations, optional controlled validation, and process knowledge.

After the workbench, the primary business entries follow “Field integration → Process configuration → Production runs → Quality management → Process diagnosis → Recipe optimization.” This navigation order prioritizes frequent role-specific work; it is not the business dependency order above. A new scenario still defines and publishes process semantics before mapping real sources to those semantics. Production runs also covers production preparation, collection, and traceability; Quality management covers inspection and quality-deviation work; and the complete data loop additionally depends on cross-entry evidence such as data trust and run context.

System administration has a separate entry for users, role permissions, platform status, runtime logs, and assistant evaluation, so it does not compete with business tasks. Secondary navigation places frequent daily tasks before setup and maintenance actions. Before the first production release, only canonical current URLs are retained; development-era page aliases are not preserved. After production release, URLs and data contracts follow controlled version-migration discipline.

Menus may change, but these business facts must not be hidden, duplicated into parallel records, or buried inside algorithm state.

## System map

```mermaid
flowchart LR
    Sources["controls / instruments / vision / inspection\nMES / QMS / LIMS when integrated"] --> Edge["Edge ConnectorHost\nprotocol mapping · run detection · acquisition replay"]
    EdgeStore[("local SQLite\noutbox · logs · configuration cache")] --- Edge
    Engineer["Process engineer"] --> Web["Platform Web\nstatic engineering workbench"]
    Web <--> Api
    Edge -->|events and heartbeat| Api
    Api -.->|configuration and diagnostics| Edge

    subgraph ApiProcess["Platform API process"]
        Api["Platform API\nformal business record · evidence assembly"]
        Analysis["deterministic analysis\nquality · comparison · features · statistics"]
        Agent["optional Agent\nread-only tools · evidence explanation"]
        Api --- Analysis
        Api -.-> Agent
    end

    Api <--> Optimizer["Optimizer\nrecipe recommendation · diagnosis · optional validation design"]
    Api <--> DB[("PostgreSQL 17\nTimescaleDB extension")]
    Api <--> Files[("persistent file volumes\nattachments · process knowledge · archives")]
    Worker["Platform Worker\nChat runs · projection · recompute · maintenance · result materialization · knowledge jobs"] <--> DB
    Worker -.-> Files
    Migrator["Platform Migrator\none-shot migration and first-user bootstrap"] --> DB
```

Code-project boundaries are not deployment boundaries. Current long-running units are Platform API, Platform Worker, independent Edge ConnectorHost instances, PostgreSQL/TimescaleDB, Optimizer, and Web; Migrator is a one-shot job completed before API and Worker start. Chat admission and queries run with Platform API, while Platform Worker executes durable deterministic-analysis and optional model-Agent jobs; Agent remains a code library rather than an independent service. Inspection attachments and process knowledge use persistent file volumes while metadata and formal state remain in PostgreSQL. A small site may share a physical server, while Edge and Platform retain independent processes, storage, identity, and recovery lifecycles.

This document fixes stable business boundaries. [Production architecture](production-architecture.en.md) defines the target topology for replicas, failure domains, data lifecycle, disaster recovery, and controlled action; [Deployment](deployment.en.md) defines current operating procedures. The target design must not be presented as a capability already delivered by the current Compose topology.

## Stable component responsibilities

### Edge

- Actively connect to controls, instruments, gateways, and approved business sources.
- Map vendor addresses, protocol types, and raw units to stable business codes.
- Detect run boundaries and report actual settings, process samples, events, and context provenance.
- Use a durable local queue for outages, platform restarts, and short backpressure.
- Pull versioned acquisition configuration, validate it locally, and report applied state.

Edge does not decide process causes, run product-level optimization, or become the formal record for experiments and quality outcomes.

Discrete run identity must remain traceable across ConnectorHost restarts. If the connector starts while equipment is already active and no recoverable shop-floor run identity exists, Edge marks the segment as incomplete instead of presenting it as a complete run from a normal start. A single event deterministically rejected by Platform is quarantined with a local audit record and must not block later valid events.

### Platform

- Store industrial objects, equipment, manufacturing context, runs, process executions, inspections, optimization tasks, recipe recommendations, controlled validations, evidence, and knowledge.
- Maintain versioned configuration, provenance, units, permissions, audit, and business state machines.
- Assemble the conditions, trajectory, and result of a real run into an immutable analytical observation.
- Execute data-quality, matching, comparison, feature, and reviewable statistical calculations.
- Admit inspection evidence to formal comparison and optimization only when it matches a published quality plan, has trusted identity, and satisfies independent-review requirements; versioned definitions determine non-numeric outcomes on the server.
- Preserve inputs sent to numerical services and their returned results.

Platform is the formal business system of record. A next-recipe recommendation is an independent append-only record and does not reuse an experiment identifier, run plan, or state machine. Controlled-validation state and formal conclusions must not exist only in Optimizer, Agent, or a browser.

Chat uses `ChatConversation` and ordered `ChatMessage` records as its formal user-facing conversation model. URLs, history lists, refresh recovery, idempotent sends, and deletion are scoped to a Conversation. A user message and its pending assistant message are created in one transaction, while `ClientMessageId` prevents network retries from duplicating messages. `AgentRun` is only the execution detail behind an assistant message and retains plans, tool calls, model usage, and stream events; it no longer defines conversation identity.

The production host stores Agent run snapshots and event streams in Platform PostgreSQL. Agent core still depends only on `IAgentRunStore` and does not reference Npgsql. After the message transaction commits, API only creates a queued run; an independent Platform Worker claims, renews, and executes it through a database lease. Restarting API does not lose an accepted answer task, and a later Worker instance can reclaim an expired lease after interruption. A continuing conversation assembles only bounded summaries, findings, and limitations from recent completed answers instead of feeding unbounded charts, proposals, or old record references back to the model; an old answer also cannot replace a production-data query required by the current question. A run admitted to golden-question evaluation freezes its complete snapshot and SHA-256 inside the same recovery boundary and can no longer be deleted as an ordinary conversation.

Platform separates policy from implementation by use case: `Platform.Application` owns database-independent ports plus multi-step rules and use cases for process research, acquisition configuration, manufacturing context, events, identity, insight, analytics, and inspection. `Platform.Infrastructure` implements PostgreSQL transactions, external services, background hosts, and cross-context adapters through module-specific composition entry points. New business controllers handle transport and authorization, then delegate business operations through Application use cases. A small set of operational adapters for Edge registration/diagnostics, identity, and runtime metrics still injects Infrastructure services directly into API; `ARCH-001` below constrains this gap, which must not expand into new business-write paths. Migrator bootstraps the first local user under a transactional lock, and Worker owns periodic maintenance. Concurrency idempotency and atomic writes remain enforced by database transactions and constraints rather than being lifted into in-memory rules. Process-research rules cannot read the inspection module directly; inspection, process-run, and configuration evidence enters research only through explicitly registered assembly adapters (currently `ResearchObservationAssembler`).

### Architecture debt register

| ID | Current gap | Owning boundary | Expansion prohibited | Exit criteria | Trigger |
| --- | --- | --- | --- | --- | --- |
| `ARCH-001` | A small set of API controllers for Edge registration/diagnostics, identity, and runtime metrics directly inject Infrastructure services | Platform composition root | No new business-write path and no domain rule in controllers | Add Application ports for the existing operational use cases; remove the corresponding Infrastructure namespace references from API; cover the rule in architecture checks | Before the first production pilot or before adding a second production maintainer, whichever comes first |

`ARCH-001` is not a permanent exception. Any feature change touching these controllers must reduce the registered scope or demonstrate in its change notes that the dependency surface did not expand.

### Optimizer

- Receive the complete optimization definition, valid real-run observations, pending recommendations, and a random seed.
- Execute reproducible numerical modeling, constraint checks, and candidate selection.
- Return predictions, uncertainty, feasibility, parameters, rationale, and model version.
- Remain free of business state, never access equipment directly, and never confirm or dispatch recipes.

### Agent

- Parse the engineer's question and current business context.
- Call authorized read-only or controlled business tools.
- Organize facts, cite sources, explain limits, and suggest next steps.
- Never compute or invent numerical process settings itself and never turn language probability into an engineering conclusion.

### Web

- Organize engineering work around business objects, runs, inspections, and optimization tasks.
- Present facts, data quality, evidence, and actionable steps together.
- Avoid browser-local business state that conflicts with Platform.

## Evidence spine

An analyzable run must answer:

```text
who / which equipment / which product
        + actual process specification and controlled conditions
        + process trajectory and stages
        + material, tooling, lot, and other context
        + quality and safety outcomes
        + provenance, time, units, and versions
```

Stable identifiers connect these facts:

- `ExecutionId`: the real run identity in Platform;
- `ExecutionId`: the correlation identity for field events or process executions;
- `ExecutionKey`: the association between an R&D experiment plan and real execution;
- `EquipmentId`, product/process object, and process specification version: minimum run identity;
- planned and actually applied process specifications remain separate, and comparable cohorts use the actual specification dimension declared by the analysis plan;
- content hash: the fixed analytical input and its provenance.

Identifiers may be mapped but never inferred after the fact. Unlinked runs remain visible with a reason rather than disappearing silently.

## Manufacturing context

Equipment, material, tooling, lot, calibration, and maintenance state may be important causes or may only be useful for traceability. The system does not assume their effect in advance.

Every run preserves an immutable context snapshot. Versioned scenario configuration classifies fields as:

- **required for analysis**: absence excludes the run from a specified analysis;
- **record when available**: retain for traceability, stratification, and later coverage assessment;
- **validated for modeling**: repeated data or experiments support use in diagnosis, optimization, or applicability scope.

The system must expose coverage, missingness, sample counts per level, and factor overlap. Data without overlap cannot pretend to separate equipment, mold, or material effects.

## From observation to cause

```text
comparable runs → differences and first deviation → candidate cause
                                                     ↓
                                  counterevidence / confounding / missing data
                                                     ↓
                                  falsifiable experiment → support / reject / inconclusive
```

- Observational analysis may report differences, associations, and candidate causes.
- Every candidate cites source runs, comparison baseline, analysis version, and limitations.
- Only controllable or blockable candidates can automatically become experiment drafts.
- Causal promotion requires appropriate controls, repetition, blocking, randomization, or intervention evidence.
- When identification conditions are absent, “insufficient evidence” is the correct result.

Specific statistics and models are described in [Analysis and optimization](optimization.en.md) and are not immutable architecture.

## From real runs to the next recipe

Daily recipe optimization consumes completed real production runs directly:

```text
actual recipe + process context + valid quality outcome
                         ↓
              optimization observation
                         ↓
 next-recipe recommendation inside safety and observed coverage
                         ↓
       engineer confirmation through the existing production flow
```

A production run does not become an experiment and requires no engineer reclassification. Once at least three valid runs covering two distinct actual recipes pass admission, the system may create one independent append-only recommendation. It retains the input snapshot, prediction, uncertainty, evidence scope, and rationale, but has no experiment identifier, run plan, experiment approval state, or equipment-dispatch command. A new recommendation requires a new input snapshot after another real run arrives.

Controlled validation is a separate decision used only to test a causal hypothesis, explore beyond the observed parameter envelope, or confirm a releasable operating region. It has its own plan, approval, execution, and result state machine; uses only actually controllable variables; declares hard bounds, stopping, and fallback conditions; and accounts for validation conditions whose outcomes are not yet known. One successful point remains only a candidate process setting. An operating region also requires independent confirmation, repeatability, boundary or interaction validation, and an explicit applicability scope.

## Consistency and replay

- The same analytical input produces a stable content hash.
- Data, features, analysis methods, models, and scenario configuration carry versions.
- Experiment results are calculated from source data rather than accepted as trustworthy self-assertions.
- Each experiment has at most one formal result.
- Concurrent requests and retries do not create duplicate business experiments.
- Historical data remain interpretable under their original versions and recomputable under new versions.
- Optimizer or Agent failure does not stop acquisition, run records, or inspections.

## Scenario replacement boundary

A new process scenario provides:

- industrial objects, equipment, and run boundaries;
- controlled variables, actual-value sources, units, and allowed ranges;
- process signals, stages, and versioned features;
- quality objectives, inspection mappings, and safety constraints;
- required and optional context fields;
- a safe baseline, default experiment policy, and optional mechanism knowledge.

It should not rewrite run identity, evidence relationships, experiment state machines, audit principles, or the stateless Optimizer protocol. Generality is supported only after a second, materially different real scenario works without changing those contracts.

## Agent capability and interoperability boundary

Agents do not depend directly on internal Platform CRUD and do not gain business permission merely because MCP, OpenAPI, or an SDK is used. The interoperability adapter handles discovery, schemas, and invocation. Platform continues to enforce project isolation, evidence citations, state transitions, approval, idempotency, audit, and rollback.

HTTP errors use `application/problem+json` with a stable `code`, request `traceId`, and standard `detail` field. Monotonically growing research histories use opaque cursors and bounded `limit` values. The project workspace returns only the newest page plus `nextCursors`; external implementations must not depend on unbounded arrays.

Capabilities open progressively by risk:

- **Read**: query authorized runs, evidence, quality, context, and applicability.
- **Propose**: create investigation, hypothesis, or experiment drafts without changing formal state.
- **Commit**: freeze a version and submit independent approval; creators cannot self-approve.
- **Execute**: invoke only allow-listed, time-bounded, scoped, stoppable, reversible actions.

Agents do not connect directly to devices or hold arbitrary write access. Platform records approved structured actions, a controlled integration or Edge gateway executes them, and actual confirmation and outcomes return to the formal record. See the [Roadmap](project-plan.en.md) for protocol objects, versions, and safety invariants.

## Evolvable strategies

The following record current choices but do not define the product core:

- Web navigation and page layout;
- protocol drivers and equipment templates;
- feature algorithms, statistical tests, and surrogate models;
- default repetitions, blocks, and stopping rules;
- GP variants, acquisition functions, physical priors, and transfer methods;
- LLM providers, model roles, and prompts;
- agent-protocol adapters such as MCP, OpenAPI, and SDKs;
- implementation sequence and priorities.

These strategies follow real data, engineer feedback, and field validation. Stable-boundary changes are recorded through ADRs.
