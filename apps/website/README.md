# Ingot Website

Bilingual static website for Ingot. Copy lives in `app/IngotSite.tsx`; `app/(zh)/page.tsx` and `app/en/page.tsx` are thin locale wrappers. Public copy follows [`docs/brand.md`](../../docs/brand.md) and [`docs/brand.en.md`](../../docs/brand.en.md); this application must not invent another core value.

The home page requirements come from these sources:

| Requirement | Source of truth |
|---|---|
| Category, lead line, and public commitments | [`docs/brand.md`](../../docs/brand.md), [`docs/brand.en.md`](../../docs/brand.en.md) |
| Workflow, engineer authority, and claim boundaries | [`docs/design.md`](../../docs/design.md), [`docs/optimization.md`](../../docs/optimization.md) |
| Implemented vs planned capability | [`docs/status.md`](../../docs/status.md), [`docs/project-plan.md`](../../docs/project-plan.md) |
| Machine-checked copy and boundary rules | [`tests/rendered-html.test.mjs`](tests/rendered-html.test.mjs) |
| Brand assets and labeled workflow illustrations | `public/brand`, [`docs/brand.md`](../../docs/brand.md) |

Keep the page independent of a specific equipment model, material, or process, and never present simulated or historical evidence as realized production benefit.

```bash
cd apps/website
npm ci
npm run dev        # local preview
npm run lint
npm test           # builds, then checks rendered HTML and product boundaries
```

Production output is written to `out/` and deployed with `./deploy/deploy.sh site`; see [deploy/README.md](../../deploy/README.md).
