# Ingot Website

Bilingual static website for Ingot. Public copy follows [`docs/brand.md`](../../docs/brand.md) and [`docs/brand.en.md`](../../docs/brand.en.md); this application must not invent another core value.

The home page must:

- use “Open-source Process Diagnosis & Optimization” as the product category and “From real runs to the next recipe.” as the lead line;
- present the default workflow as a real-run recommendation loop: diagnosis evidence supports an engineer-reviewed next recipe;
- lead with real data supporting process-engineer decisions;
- show acquisition, context, production runs, inspections, diagnosis, recommendations, and later outcomes as one evidence chain;
- preserve the boundary that production parameters are never changed automatically;
- explain that process knowledge is attached to an applicable project scope together with its rationale and evidence references;
- preserve the engineer's authority and the boundary between association and validated cause;
- distinguish implemented capability from historical replay, shadow evidence, and online evaluation;
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
