# Ingot Platform Web

React/Vite workbench for process engineers.

Its primary workflow is:

```text
research objective
→ observation readiness
→ optimized experiment
→ engineering approval
→ cycle and inspection
→ result and next experiment
```

The UI uses business forms and does not create a separate optimization workflow or expose raw JSON editors.

```bash
npm ci
npm run dev
npm test
npm run lint
npm run build
```

Development URL: <http://localhost:3000>. The API base URL is configured by the Vite development proxy and production Nginx configuration.
