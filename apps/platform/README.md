# Ingot Platform Web

React/Vite workbench for Ingot, an Open-source Process R&D and Optimization System. It presents one evidence chain from real run data to engineering decisions; business state lives in the Platform API, never in the browser. Field connectors are enabled as needed; the current recommendation workflow still requires qualified run and quality evidence.

The visible domains are:

- Workbench, plus the separate Ingot Chat entry;
- Field Operations;
- Process Insight;
- Recipe Optimization;
- Configuration & Integration;
- System Administration.

The current field-evidence workflow is:

```text
connect field sources
→ define process semantics
→ inspect a real run
→ review quality and data trust
→ compare evidence
→ generate a next recipe recommendation
→ record the engineer's decision
→ link the subsequent production run and freeze its quality outcome
```

The UI uses business forms, shows missingness and provenance, and does not expose raw JSON editors as normal product workflows. Numerical recommendations require engineer review before execution.

Prerequisites: Node.js 22.22+ and a running Platform API (default `http://127.0.0.1:8000`).

```bash
cd apps/platform
npm ci
npm run dev        # development server on port 3000
npm run lint
npm test           # node:test plus Vitest rendering tests
npm run build
```

Development URL: <http://localhost:3000>. The Vite dev server proxies to `INGOT_PLATFORM_API_TARGET` (default `http://127.0.0.1:8000`); in production, Nginx proxies `/api` and `/health` to `platform-api:8000`.

Product principles live in [`docs/brand.en.md`](../../docs/brand.en.md) and architecture boundaries in [`docs/design.en.md`](../../docs/design.en.md).
