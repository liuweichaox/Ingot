# Ingot Documentation Site

Static bilingual documentation generated from paired Markdown files in `docs/`.

`lib/docs.ts` and `scripts/prepare-content.mjs` contain the explicit public-document allowlist. Every public page requires Chinese and English versions with equivalent headings and claims. Product language follows `docs/brand.md`; technical pages may describe current strategy but cannot redefine the core value.

```bash
npm ci
npm run build
npm test
npm run lint
```

The build produces navigation, search data, sitemap, robots, and static pages in `out/`. Tests verify language alternates, internal links, canonical assets, product-language boundaries, and retired terminology.
