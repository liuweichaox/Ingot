# Engineering and theoretical foundation

This page provides methods for Ingot design, development, and review. Tailor the methods to the scenario and risk. Standards conformance, site suitability, and process safety each require their own verification evidence.

[System design](design.en.md) records the current architecture for routine development and architecture review. Start reviews with actual requirements and risks, and use engineering methods and evidence to evaluate the existing design and its assumptions. Use [current status](status.en.md), the implementation, and tests to check actual behavior and identify claims that still need verification.

## Systems engineering: goals, boundaries, and validation

Use the lifecycle processes of [ISO/IEC/IEEE 15288](https://www.iso.org/standard/81702.html) and practices in the [NASA Systems Engineering Handbook](https://www.nasa.gov/reference/2-0-fundamentals-of-systems-engineering/) as references. The V model helps relate requirements decomposition to verification: start with an engineer's scenario, define system boundaries, requirements, design, and tests, then validate whether actual use addresses the need. The standard provides a process framework; using these references does not constitute certification.

Systems thinking and cybernetics help analyze feedback among equipment, acquisition, inspection, software, and people. Ingot supports engineer decisions; subsequent real runs, inspections, and reviews provide feedback on a recommendation. The system does not automatically send recommendations to equipment. Ingot does not replace adjacent systems or site control; see [system design](design.en.md) for the boundaries.

For consequential changes, record the scenario, constraints, design choices, and verification approach, and distinguish implemented behavior, design goals, and assumptions requiring site confirmation. Verification checks specified requirements; validation checks fitness for the intended use.

## Process, data, and decisions

- **Process engineering and mechanism knowledge**: Consider units, dimensions, equipment and process context, and verified relationships and constraints. Knowledge has sources and scope; model output is not proof of site safety.
- **Measurement and data quality**: Assess sources, measurement methods, units, timing, completeness, associations, and conflicts for the intended use. Admission of optimization observations should have explicit grounds and rejection reasons; see the [data model](data-model.en.md) and [analysis and optimization](optimization.en.md).
- **Design of experiments**: Consider controls, replication, factors, interactions, and confounders such as material lots or equipment changes. The approach must fit actual costs and site conditions.
- **Statistical and causal inference**: Distinguish recorded facts, statistical associations, explanatory hypotheses, and causally supported conclusions. State sample limitations, methodological assumptions, and uncertainty. Both experiments and observational studies require scrutiny of assumptions and applicability.
- **Optimization and decision theory**: Select methods for the objectives, data, and constraints, and explain the basis and limits of candidate proposals. Observed coverage, a model's domain of applicability, and site-confirmed safety limits are distinct; observed coverage alone does not establish safety.

These methods support engineering judgment; they do not guarantee benefits or eliminate risk. See [analysis and optimization](optimization.en.md) for method and data boundaries.

## Safety, reliability, and human factors

Use methods such as FMEA, fault-tree analysis, or [STPA](https://www.mit.stamp-institute.com/handbooks) to assess risks such as incorrect units, mismatched runs and quality results, stale constraints, insufficient evidence, and misuse of recommendations. Record potential consequences, triggers, controls, residual risks, and verification evidence. Engineer confirmation and the absence of automatic equipment delivery are important boundaries, but do not replace equipment-side protections or site procedures.

Use reliability concepts such as fault domains, persistence, retries, idempotency, monitoring, backups, and recovery exercises to define behavior during network or service failures. Deployment scenarios determine recovery objectives; see [production architecture](production-architecture.en.md) for current deployment capabilities and outstanding validation.

Information security draws on least privilege, defense in depth, and the [NIST SSDF](https://csrc.nist.gov/pubs/sp/800/218/final); AI use draws on the [NIST AI RMF](https://www.nist.gov/itl/ai-risk-management-framework). Examine authorization scope, read-only tools, source citations, conflicting material, and insufficient evidence. Human factors design should help engineers inspect the basis and limits of an answer and use feedback to assess clarity; no single screen density is assumed to suit every task.

## Software and data engineering

- **Domain-driven design**: Establish shared language for runs, quality results, observations, recipe versions, recommendations, and engineer decisions. Define boundaries from business rules and consistency needs; domain boundaries need not correspond to service boundaries.
- **Data modeling, state machines, and lineage**: Define state transitions, permissions, versions, and retention of history, supporting traceability from decisions to evidence. Choose immutability and correction mechanisms for each record's business needs; do not assume all data can be fully replayed.
- **Distributed systems**: Analyze disconnects, retransmission, duplication, ordering, and failure handling between Edge and Platform, as well as compute-task communication boundaries. The [System Design Primer](https://github.com/donnemartin/system-design-primer) is an introductory reference for technical topics; assess specific guarantees against implementation, authoritative sources, and tests.
- **Evolutionary architecture and contracts**: Keep dependency direction and data ownership clear; document key interfaces and cross-language contracts. Use the [C4 model](https://c4model.com/) for architecture views and architecture decision records for choices and tradeoffs. See [system design](design.en.md) for existing boundaries.
- **Verification and testing**: Select rule, authorization, contract, fault, property, or historical replay tests according to risk. Tests demonstrate covered behavior; alone they do not establish site safety or correctness for every algorithm input.

## Application and maintenance

Start with the path from run and quality evidence through optimization observations, recommendations, engineer decisions, and real outcomes. Review requirements, sources, business rules, risks, permissions, and verification evidence along that path. Record the scenario, assumptions, alternatives, tradeoffs, and reconsideration criteria for consequential design decisions; tailor the process for small changes rather than applying every method.

Distinguish product requirements, safety and authorization invariants, revisable design choices, and unverified assumptions. For identified gaps, compare alternatives against the existing implementation and specify whether code tests, deployment exercises, or site data are needed. When implementing an improvement, update the affected design, implementation, tests, and architecture gates together. Verify compatibility and invariants during the transition. See [Contributing](../CONTRIBUTING.en.md#verification-and-documentation) for public claims and bilingual documentation checks.
