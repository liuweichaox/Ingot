# Ingot Documentation Site

Static bilingual documentation generated from paired Markdown files in `docs/`.

`lib/docs.ts` and `scripts/prepare-content.mjs` contain the explicit public-document allowlist. Every public page needs both Chinese and English versions.

```bash
npm ci
npm run build
npm test
npm run lint
```

The build produces navigation, search data, sitemap, robots, and static pages in `out/`. Tests verify language alternates, internal links, assets, and retired product terminology.
