# Ingot documentation site

The static bilingual documentation application for `docs.ingotstack.com`. It reads the repository-level `docs/` Markdown files during the build; documentation content is not copied into the application.

## Local development

Requires Node.js `>=22.13.0` and execution from `docs-site/`.

```bash
npm ci
npm run dev
```

The root route redirects to `/zh`. Chinese pages use `/zh/...`; English pages use `/en/...`.

## Validation

```bash
npm run build
npm test
npm run lint
```

The validation suite checks static routes, the search index, internal documentation links, canonical repository links, and shared brand assets.

## Main files

- `lib/docs.ts` — Markdown discovery, navigation, link conversion, and search metadata
- `app/[lang]/[[...slug]]/page.tsx` — localized static documentation pages
- `app/sitemap.ts` — sitemap generation
- `tests/` — export, links, routes, search, and brand assertions
- `public/brand/` — synchronized official brand assets
