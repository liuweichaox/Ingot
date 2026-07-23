# Configuration Creation and Maintenance Closure

Ingot follows one platform-wide UI rule: whenever users can create a data entity manually, the same business area must also expose its query and maintenance path. A creation form must never be an isolated entry point.

Maintenance behavior follows data semantics:

- Master data (component types, components, and tooling identities) provides lists, details, maintenance actions, and inactive or retired states. Stable identifiers are locked during maintenance.
- Versioned configuration (process data models, recipes, analysis plans, inspection definitions, quality plans, tooling types, and tooling assembly revisions) preserves history. Drafts can be edited and deleted. Published versions are immutable and can only be retired or used as the basis for a new version.
- Shop-floor facts (tooling installations, production contexts, inspection records, attachments, and reviews) are append-only. The platform provides history, details, interval closure, review, and audit actions without overwriting facts that already occurred.
- Device events and samples enter through ingestion APIs and remain queryable through event, cycle, and data-quality pages. They have no manual edit path.

Page acceptance must verify that a newly created entity can be found, opened, maintained according to its semantics, and retained by historical references.

Production UI starts from blank forms or saved versions. It does not expose “load sample” or “load demo data” actions. Industry examples belong only in developer documentation and standalone import tooling; they must never masquerade as production master data.

Layout follows the task instead of centering every form. Versioned configuration uses a version catalog on the left and a detail/editor workspace on the right. Master-data areas remain list-first with explicit create, edit, and delete actions. Shop-floor entry starts from a pending task and opens in a drawer or another focused work area. Ingot brand locations use the project-owned mark, while generic actions retain clear functional icons.

Business pages do not expose persistent refresh buttons. Query pages update through explicit query criteria, successful saves synchronize their views automatically, and dynamic node, metrics, log, and event pages use automatic updates or pausable live tracking with visible update state.

Normal pages do not display instructional alert banners. Titles, fields, grouping, state, and empty states carry the product semantics. Successful operations use transient feedback; persistent alerts are reserved for errors, blockers, or risks that require user action.

The visual platform-metrics page uses `/platform-metrics`. `/metrics` remains exclusively the Prometheus raw-metrics endpoint so that a product page never conflicts with a machine-facing route.
