# Ingot Product Information Architecture

Ingot organizes its interface around understanding operations, finding problems, analyzing processes, and maintaining semantics. It is not organized around database tables, APIs, or internal services. The platform does not duplicate MES scheduling, inventory, personnel, or work-order execution, and it does not present raw time-series points without operating context.

## Design principles

1. **Work first**: The product opens on the workbench with active operations, pending inspections, data issues, and ingress health. AI is a first-class capability, but it is not the home page.
2. **Object first**: Equipment and logical operating objects are the entry point to data. Their current context, operating records, events, quality, and relationships are available without repeatedly entering identifiers.
3. **Immutable operating context**: A record captures the product, recipe, tooling, and quality-plan references effective at that time. Historical analysis does not depend on later master-data edits.
4. **Quality is first-class data**: Manual inspections, vision results, measurements, original images, and reviews share the analytical context with process data.
5. **Cyclic and continuous operation coexist**: Cyclic equipment is organized by production cycle. Continuously running equipment uses time windows, state segments, or event segments. Both share object, event, and analysis semantics.
6. **Centralized configuration, clean operations**: Daily workspaces do not expose configuration forms. Creation and maintenance start from a register, version catalog, or task context and provide create, maintain, retire, and conditionally allowed delete paths.
7. **State replaces instructions**: Titles, fields, status, empty states, and valid actions communicate meaning. Success feedback is transient; persistent notices are reserved for errors, blockers, and risks.
8. **Operations are isolated**: Platform metrics and logs belong to administration. Raw metric endpoints are not part of the normal product navigation.

## Primary navigation

- Workbench
- AI Assistant
- Operations & Traceability
  - Operating Records
  - Production Events
  - Production Changeover
  - Tooling Installation
- Quality Management
  - Quality Tasks
  - Quality Analysis
  - Inspection Definitions
  - Quality Plans
- Analysis Center
  - Historical Comparison
  - Data Health
  - Analysis Plans
- Data Assets
  - Object Catalog
  - Process Data Models
  - Recipe Versions
  - Data Ingress
- Tooling Management
  - Component Types
  - Component Register
  - Tooling Types
  - Tooling Assemblies
- Administration
  - Platform Metrics
  - Runtime Logs

Navigation follows the proven two-level pattern used by mature industrial data platforms. The top menu switches among Workbench, AI, Operations, Quality, Analysis, Data Assets, Tooling, and Administration; the left sidebar contains only entries in the active domain. Workbench and AI use the full workspace without a sidebar. Page identity stays in the workspace header and global search stays in the top bar. This preserves the strength of TDengine IDMP's global-menu-plus-context-sidebar model without copying database-oriented terminology into Ingot.

## Page patterns

- **Workbench**: Key state, actionable items, current production context, and recent operating records.
- **Object catalog**: Object list on the left and contextual detail on the right; records, quality, events, and relationships are not isolated destinations.
- **Query workspace**: Compact filters, result grid, and a detail drawer.
- **Task workspace**: Queue-first layout with processing forms in a drawer.
- **Configuration workspace**: Object/version catalog on the left and detail/editor on the right. Creation and maintenance do not permanently occupy the primary browsing area.
- **Analysis workspace**: Scope and alignment controls above full-width charts and findings.
- **Historical comparison**: The user selects a product series, chooses multiple operating records, and designates a baseline from that selection. A signal matrix presents cross-record differences before contextual record details. The product must not replace user selection with an automatic “latest N” rule.
