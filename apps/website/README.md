# Ingot Website

Bilingual static website for Ingot. Public copy follows [`docs/brand.md`](../../docs/brand.md) and [`docs/brand.en.md`](../../docs/brand.en.md); this application must not invent another core value.

The home page must:

- use “Open-source Process R&D and Optimization System” as the product category and “From process data to evidence-based R&D decisions.” as the lead line;
- describe recipe versions, experiment records, quality analysis, process diagnosis, and engineer-reviewed recipe recommendations;
- lead with real data supporting process-engineer decisions;
- explain the current run-evidence recommendation path and its later outcomes; retain experiment-input limitations in the current-status section;
- preserve the boundary that production parameters are never changed automatically;
- explain that process knowledge is attached to an applicable site and recipe scope together with its rationale and evidence references;
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
