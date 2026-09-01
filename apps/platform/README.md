# Ingot Platform Web

React/Vite workbench for process engineers. It presents one evidence chain from real production data to engineering decisions; it does not define a separate product value or keep browser-local business truth.

The visible domains are:

- Workbench;
- Field integration;
- Process configuration;
- Production runs;
- Quality management;
- Process diagnosis;
- Recipe optimization;
- System administration.

The working path is:

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

```bash
npm ci
npm run dev
npm test
npm run lint
npm run build
```

Development URL: <http://localhost:3000>. The API target comes from the Vite development proxy or production Nginx configuration.

Product principles live in [`docs/brand.en.md`](../../docs/brand.en.md) and architecture boundaries in [`docs/design.en.md`](../../docs/design.en.md).
