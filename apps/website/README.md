# Ingot Website

Bilingual static website for Ingot. Public copy follows [`docs/brand.md`](../../docs/brand.md) and [`docs/brand.en.md`](../../docs/brand.en.md); this application must not invent another core value.

The home page must:

- use “Open-source Process R&D and Optimization System” as the product category and “From process data to evidence-based R&D decisions.” as the lead line;
- describe recipe versions, experiment records, quality analysis, process diagnosis, and engineer-reviewed recipe recommendations;
- lead with real data supporting process-engineer decisions;
- explain that each run is an experiment and run/quality records retain measured facts;
- describe corrections on the current recipe version, linkage of an eligible completed same-version run started after the decision, significant-change revision drafts, and outcomes frozen from actual evidence;
- preserve the boundary that production parameters are never changed automatically;
- explain that mechanism claims belong to a site and recipe, while document sources belong to a site; recommendations freeze the knowledge versions they use;
- preserve the engineer's authority and the boundary between association and validated cause;
- distinguish implemented capability from historical replay, shadow evidence, and online evaluation;
- remain independent of a specific equipment model, material, or process;
- link documentation, source, quickstart, pilot, troubleshooting, and contributing guidance;
- use canonical assets from `public/brand` and remain statically exportable.
- use explicitly labeled workflow illustrations rather than outdated product screenshots or fabricated outcome figures;
- link current status and deployment acceptance, and require replacing all `.env` placeholders before starting Compose.

```bash
cd apps/website
npm ci
npm run build
npm test
npm run lint
```

Production output is written to `out/`.
