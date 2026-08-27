# Roadmap

> Status: **v2 strategy baseline plus rolling roadmap**. Sections 1–5 fix the long-term direction, strategic thesis, and stable boundaries. Sections 6–9 evolve with real data, engineer feedback, external adoption, and acceptance results. Progress follows evidence gates, never feature count or calendar time alone.

This document distinguishes current capabilities from long-term objectives. Near-term work validates the trustworthiness of the data chain, recommendations, and review process. Automation and open protocols advance only after the preceding evidence stage passes.

## 1. Long-term position

Ingot's core value remains unchanged:

> **Turn every real recipe run into optimization evidence and continuously recommend the next recipe within safety boundaries and observed coverage.**

Ingot keeps its current public category. *Optimization* means turning real recipe runs into evidence and recommending the next recipe inside explicit objectives, safety boundaries, and observed coverage; it does not mean automatic control or demonstrated real-factory benefit:

> **Open-source Process Diagnosis & Optimization.**

Its long-term direction is to become:

> **A trustworthy decision and validation operating system for manufacturing processes.**

Foundation models and agents provide replaceable language parsing, evidence retrieval, and tool orchestration. Ingot keeps engineers and agents on the same trusted facts, scientific validation, and controlled-action discipline. Its durable advantage is not one model, but:

- reliable identity across real runs, actual conditions, trajectories, quality outcomes, and field context;
- evidence, methods, versions, uncertainty, and applicability for every judgment;
- formal state from candidate cause through falsifiable experiment, approval, execution, and result materialization;
- controlled actions that can be replayed, audited, denied, stopped, and rolled back;
- validated process knowledge reusable across products, equipment, and scenarios.

The governing principle is:

> **Every conclusion has provenance. Every recommendation has boundaries. Every action has approval, fallback, and an outcome.**

This is a strategic north star, not a claim that these product benefits have already been demonstrated. [Brand guide](brand.en.md) remains authoritative for public claims.

## 2. One product spine

Ingot centers on a complete process-decision case, not a chat session, algorithm, or device point:

```text
engineering problem
→ trustworthy evidence bundle
→ real recipe runs and optimization observations
→ next-recipe recommendation with evidence boundaries
→ engineer confirmation through the existing production flow
→ actual recipe run
→ quality outcome and side effects
→ conclusion boundary
→ reusable process knowledge
```

When causal proof, extrapolation, or operating-region confirmation is required, candidate causes branch into a separate controlled-validation plan, approval, execution, and result state machine. That branch is not a prerequisite for daily recipe optimization.

The long-term capability chain is:

```text
trusted acquisition → run and quality evidence → optimization observations
→ next recipe → engineer confirmation → knowledge reuse
```

This is a dependency chain, not menu order. Do not perform strong analysis with untrusted data, claim causes from observational evidence, enter shadow mode before replay, or enter controlled action before shadow and safety evidence pass.

Industry, equipment, validation data, and effect results stay out of the public repository. Deployers evaluate scenario applicability and business benefit with their own data; those results are not prerequisites for repository code completion.

## 3. Three-horizon strategy

### Near term: complete a reliable natural-run optimization loop

The near-term scope is limited to capabilities the repository can own:

- run identity, actual values, quality outcomes, and context are unique, complete, and traceable;
- normal production runs automatically become optimization observations without experiment setup or recipe reclassification;
- method selection reads visible evidence only and explains admission or fallback;
- missingness, mismatches, unauthorized access, and insufficient evidence are rejected explicitly;
- inputs, policies, models, seeds, outputs, and reviews are reproducible.

Deployers own scenario effects, process safety, and realized benefit. The repository provides an optional [scenario-validation method](rollout.en.md) but stores none of its data or results.

### Medium term: become a model-independent process capability substrate

Expose R&D capabilities through agent-callable protocols without exposing internal CRUD APIs directly to models. Use two layers:

```text
any conforming model / agent
↓
interoperability adapters such as MCP, OpenAPI, and SDKs
↓
Ingot process capability protocol and control plane
↓
evidence, experiment, approval, execution, and rollback state machines
```

MCP and similar protocols standardize discovery, description, and invocation. Ingot's domain protocol and Platform state machines enforce evidence discipline, permissions, approval, idempotency, replay, and safety. Expose capabilities by risk:

| Level | Agent capability | System guarantee |
|---|---|---|
| Read | query runs, evidence, quality, context, and applicability | project isolation, citations, versions, least privilege |
| Propose | create investigation drafts, candidate hypotheses, and experiment proposals | draft only, with input snapshot and rationale |
| Commit | submit approval, freeze a plan, and sign an execution version | no self-approval; no rewriting after outcomes appear |
| Execute | invoke allow-listed, time-bounded, scoped, reversible actions | policy checks, human authorization, device confirmation, stop, and rollback |

An agent may not approve its own proposal or bypass Platform to reach a database or device. The medium-term value is established through engineering guarantees rather than model-capability claims:

> **Every model that investigates or acts through Ingot must cite evidence, pass state and permission gates, and leave a verifiable execution receipt.**

### Long term: establish an open evidence and experiment specification for manufacturing intelligence

Open the cross-product semantics and validation contracts, not customer process content or current database tables. Candidate specification objects include:

- run-event envelopes and provenance;
- evidence bundles and content-addressing rules;
- experiment contracts, preregistration, and version freezes;
- agent recommendations, human decisions, and rejection reasons;
- execution receipts, actual-value confirmation, stop, and rollback;
- validation reports and machine-readable conclusion boundaries;
- validated operating regions with applicability, failure, and drift conditions.

The specification must include schemas, compatibility rules, reference implementations, adapters, conformance validators, standard fixtures, signature and provenance rules, extension namespaces, and an open change process.

Before a `1.0` release, require:

1. two materially different real process scenarios using the core semantics unchanged;
2. at least two external teams implementing or validating without private Ingot code;
3. public review of compatibility, conformance, and security boundaries;
4. governance independent of one customer's data, one model, or one vendor interface.

Until external adoption and independent implementations exist, a specification candidate must not be described as an industry standard. The accurate near-term name is:

> **An evidence and experiment protocol for manufacturing intelligence.**

Network effects come from equipment, agents, validators, scenario packages, and enterprise systems using one verifiable contract, not from centralizing customers' raw process data.

## 4. Product and commercial boundary

Ingot serves costly, small-data manufacturing recipe optimization with measurable quality objectives and safety boundaries. Process, quality, equipment, and R&D teams collaborate inside one company, with factory-local or hybrid deployment by default.

The core platform carries stable concepts:

- equipment, connections, process executions, and stages;
- products, process specifications, materials, components, tooling, and lot context;
- actual settings, trajectories, versioned features, and data quality;
- quality objectives, safety constraints, inspections, and human review;
- engineering problems, candidate causes, counterevidence, hypotheses, experiments, and evidence;
- analysis strategies, numerical recommendations, stopping, operating regions, and knowledge applicability;
- human and agent identities, roles, approvals, audit, and provenance.

Scenario differences belong in versioned configuration: variables, units, mappings, run boundaries, stages, quality plans, constraints, context, experiment policies, and optional mechanism knowledge.

Ingot does not expand into a general MES, SCADA, interlock, scheduler, data lake, or unattended controller. It sits above those systems to manage process evidence, next-recipe recommendations, optional controlled validation, and knowledge closure.

Recommended open and commercial boundaries:

- **Open**: Edge, core schemas, evidence protocol, basic connectors, replay, and conformance tools.
- **Enterprise**: organization governance, multi-site operation, SSO, durable audit, agent evaluation operations, controlled execution, certified scenario packages, and engineering support.

Commercial value should be measured by site, line, R&D workspace, or durable decision workflow, not conversations or token use.

## 5. Long-term architecture and safety invariants

The diagram below is a long-term logical capability and control map, not the current deployment topology. See [System design](design.en.md#system-map) and [Production architecture](production-architecture.en.md) for current processes, database, and file-volume relationships.

```mermaid
flowchart LR
    Sources["controls / instruments / vision / inspection / MES"] --> Edge["Edge\nacquisition · mapping · buffering · future controlled action gateway"]
    Edge --> Platform["Platform\nformal facts · state machines · permissions · audit"]
    Platform --> Analysis["deterministic analysis\nquality · comparison · statistics"]
    Platform --> Optimizer["Optimizer\nrecipe recommendations · constrained optimization"]
    Platform --> Agent["Agent\nlanguage parsing · evidence retrieval · tool orchestration"]
    Platform --> Web["engineering workbench"]
    Engineer["Process engineer"] --> Web
    Web --> Platform
    Agent -. "authorized capabilities only" .-> Platform
    Platform -. "approved structured actions" .-> Edge
```

Stable decisions:

- **Platform** is the sole formal record for runs, context, inspections, recipe recommendations, controlled validations, evidence, approvals, agent proposals, and knowledge.
- **Edge** provides trusted acquisition, offline buffering, and replay. It may later host a controlled action gateway but never replaces PLC, DCS, or safety interlocks.
- **Optimizer** is stateless business-wise and performs reproducible statistics, constraints, DOE, and numerical optimization. It cannot approve experiments or control equipment.
- **Agent** is a replaceable language-parsing, evidence-retrieval, and tool-orchestration layer. Conversation context and model memory are not formal business state.
- **Web** does not maintain parallel business state that conflicts with Platform.
- Data, features, policies, models, tools, and schemas are versioned and replayable; critical evidence uses content hashes or signatures.
- Acquisition, inspection, and formal records do not depend on Optimizer, Agent, or an external model being available.
- Agents never receive arbitrary device-write permission; they submit structured intent or invoke allow-listed actions.

Every agent behavior affecting business or equipment records identity, model and policy version, tools and permissions, evidence snapshot, proposal and uncertainty, constraint checks, approval scope, device confirmation, outcomes, side effects, stop, and rollback.

Protocol adapters, database topology, algorithms, model providers, and page layouts may evolve. Stable-boundary changes require an ADR.

## 6. Product maturity and promotion gates

| Level | What an engineer or agent can do | What the system must prove |
|---|---|---|
| L0 connected | see equipment, instrument, and inspection data | raw values, units, time, and provenance are explicit |
| L1 trusted run | find actual conditions, trajectory, context, and outcome | no silent loss; missingness and versions are visible |
| L2 comparable | find a qualified baseline and first deviation | matching, coverage, and confounding are explicit |
| L3 diagnosable | receive candidates, evidence, counterevidence, and validation advice | correlation is not sold as cause; correct refusal works |
| L4 validation-ready | when needed, turn a candidate into reviewable, falsifiable controlled validation | controls, repetition, blocks, safety, and stopping are explicit |
| L5 optimizable | receive a next-recipe recommendation from real recipe runs | data admission, leakage-free replay, calibrated uncertainty, zero known safety violations |
| L6 actionable | execute one reversible action inside approved scope | shadow evidence, permission, actual confirmation, and rollback drills pass |
| L7 reusable | reuse conclusions on a new product, machine, or scenario | applicability, failure, drift, and negative transfer are visible |
| L8 interoperable | external systems independently implement and validate the protocol | multi-scenario use, compatibility, conformance, and governance pass |

No level may use a higher-level demonstration to bypass lower-level evidence. Autonomy grows only inside validated operating regions and automatically degrades on drift, missing evidence, model anomalies, communication failure, or human intervention.

## 7. Delivery and validation model

Maintain four mutually constraining work lines:

- **Product**: trusted data → real-run observations → next recipe → engineer confirmation → outcome feedback → reuse; causal or extrapolation questions branch into controlled validation.
- **Scientific validation**: historical replay, shadow validation, controlled online validation, and cross-scenario transfer advance independently.
- **Engineering assurance**: real-database tests, recovery exercises, performance baselines, security, and observability.
- **Protocol ecosystem**: schema stability, reference implementations, conformance tests, external implementations, and governance.

Do not collapse the work lines into one global maturity state:

| Work line | Independent evidence artifact | Permitted conclusion only |
|---|---|---|
| Historical replay | frozen dataset, sequential traces, baselines, gates, review hash | leakage-free, reproducible performance against preregistered baselines on existing history |
| Shadow validation | recommendation snapshot, independent engineer choice, actual outcome, rejection reasons, calibration report | applicability, executability, and calibration on a new project |
| Controlled online | per-run approval, rollback drill, actual settings, outcomes, stop records | safe prospective value inside declared boundaries |
| Protocol interoperability | independent implementation, compatibility matrix, conformance report, security review | correct exchange and validation without private Ingot code |

Each work line preregisters data, baselines, measures, threshold versions, acceptance, and falsification separately. An API existing means infrastructure exists; it does not mean the work line passed.

## 8. Current priorities and rolling batches

The current priority is tightening implemented software paths rather than accumulating scenario-validation rounds in the repository. Deployers manage validation data and effect results; the repository keeps core code, contracts, tests, and documentation simple and consistent.

| Priority | Work | Definition of done |
|---|---|---|
| P0 | run identity, actual values, context, inspection linkage, and quality validity | every analysis record uniquely reaches a real run and valid outcome |
| P0 | observation assembly, algorithm selection, and recommendation fallback | identical inputs reproduce; insufficient evidence selects a simpler method |
| P1 | process-decision case and deterministic diagnosis contract | evidence, candidates, counterevidence, hypotheses, experiments, and conclusions share one formal spine |
| P1 | agent adversarial tests | unsupported inference, identity mismatch, overreach, and incorrect tool use are detectable |
| P2 | read and propose agent protocols | multiple models use the same schemas and cannot bypass state machines |
| P2 | single-step controlled action protocol | allow lists, approval, actual confirmation, stop, and rollback drills pass |
| P3 | specification candidate | core contracts remain stable and at least one external implementation exists |

Current rolling batches:

1. **Trusted identity and quality chain**: tighten rejection of site-isolation, clock, missing-actual-value, and incorrect-inspection-linkage failures.
2. **Natural-run optimization**: keep automatic observation assembly, algorithm selection, recommendation explanation, and result materialization on one path.
3. **Process-decision case**: fix data quality, baseline, first deviation, candidates, counterevidence, confounding, missingness, and controlled-validation proposal structure.
4. **Agent adversarial tests**: expand correct refusal, citation coverage, permission, and tool-call tests.
5. **Protocolized read and propose capabilities**: derive stable domain tools from internal APIs and provide MCP, OpenAPI, or SDK adapters.
6. **Controlled-action preparation**: action ledger, authorization tokens, policy checks, device confirmation, stop, and rollback.
7. **Specification candidate**: publish stable candidate schemas, validators, and reference implementations.

These batches are the current sequence, not an immutable product definition. Reorder them after each completed batch according to evidence.

## 9. Measures, governance, and falsification

Every roadmap review answers five groups of questions.

### Is data more trustworthy?

- complete-run and actual-setting, feature, context, and inspection coverage;
- linkage failures and unit, clock, configuration-version, and provenance anomalies;
- Edge backlog, recovery, duplicates, and disorder;
- successful historical recomputation, replay, and interpretation.

### Is diagnosis more useful?

- time from anomaly to first executable hypothesis;
- engineer usefulness rating;
- evidence citation, correct refusal, and unsupported causal claims;
- candidates supported, rejected, or left inconclusive by experiments.

### Are experiments and actions more effective?

- valid experiments to attain and repeatedly confirm specification;
- recommendation acceptance, modification, rejection reasons, and actual-setting deviation;
- calibration, reproduction, post-stop outcomes, and rollback success;
- material, equipment, inspection, and calendar time;
- unauthorized actions and known safety-boundary violations always equal zero.

### Is an agent worthy of trust?

- tool choice, parameters, citations, and final judgment evaluated separately;
- regression after model, prompt, tool, or schema version changes;
- correct refusal under missing evidence, identity conflicts, and inadequate permission;
- reasons and later outcomes for human acceptance, modification, and rejection.

### Is the protocol being adopted?

- external implementations, connectors, validators, and compatible versions;
- conformance pass rate and interoperability failure causes;
- time from connecting a new system to the first conforming evidence bundle;
- proportion validated without private Ingot code.

Governance discipline:

- manage core value, evidence principles, and stable component boundaries as normative baselines;
- record stable architecture and breaking protocol changes with ADRs;
- preregister data, baselines, measures, acceptance, and falsification for every phase;
- every feature must improve data trust, diagnosis, experiment value, action safety, or interoperability;
- downgrade, repair, or stop methods that do not beat applicable simple baselines;
- produce separate controlled internal reports for replay, shadow, and controlled online validation, and a public conformance report for protocol interoperability; never substitute one for another;
- never measure success by pages, conversations, agent count, model size, or token consumption;
- stronger foundation models never relax provenance, permission, approval, safety, or causal-validation requirements.
