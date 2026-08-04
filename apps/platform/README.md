# Ingot Platform Web

React/Vite workbench for process engineers. It presents one evidence chain from real production data to engineering decisions; it does not define a separate product value or keep browser-local business truth.

The visible domains are:

- Global overview;
- Cycles;
- Manufacturing context;
- Inspections;
- Process R&D;
- Data and connectivity;
- Identity and system.

The working path is:

```text
define the process
→ connect and qualify data
→ inspect a real run
→ compare evidence
→ form and validate a candidate cause
→ review a next experiment
→ preserve the result and its scope
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
