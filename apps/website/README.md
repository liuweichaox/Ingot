# Ingot Website

Bilingual static website for Ingot. Public copy follows [`docs/brand.md`](../../docs/brand.md) and [`docs/brand.en.md`](../../docs/brand.en.md); this application must not invent another core value.

The home page must:

- use “Open-source Process Diagnosis & Optimization” as the product category and “From real runs to the next process specification.” as the lead line;
- present the default workflow as process-specification revision: diagnosis evidence supports an engineer-created next-version draft;
- lead with real data supporting process-engineer decisions;
- show acquisition, context, production runs, inspections, diagnosis, and next-version specification drafts as one evidence chain;
- preserve the boundary that production parameters are never changed automatically;
- explain that process knowledge is attached to a specification revision together with its rationale and evidence references;
- preserve the engineer's authority and the boundary between association and validated cause;
- distinguish implemented capability from historical replay, shadow evidence, and online validation;
- remain independent of a specific equipment model, material, or process;
- link documentation, source, quickstart, and contributing guidance;
- use canonical assets from `public/brand` and remain statically exportable.

```bash
npm ci
npm run build
npm test
npm run lint
```

Production output is written to `out/`.
