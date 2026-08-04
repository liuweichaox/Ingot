# Ingot Website

Bilingual static website for Ingot. Public copy follows [`docs/brand.md`](../../docs/brand.md) and [`docs/brand.en.md`](../../docs/brand.en.md); this application must not invent another core value.

The home page must:

- lead with real data supporting process-engineer decisions;
- show acquisition, context, cycles, inspections, diagnosis, experiments, and optimization as one evidence chain;
- explain that methods are selected by the question rather than presenting one algorithm as the product;
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
