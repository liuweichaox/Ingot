# Ingot Documentation Site

Static bilingual documentation generated from paired Markdown files in [`docs/index.en.md`](../../docs/index.en.md). The site exports to `out/`; serving it needs only a static file server, with no application server or database.

## Develop and verify

Use Node.js 22.22 or later and the committed lockfile.

```bash
npm ci
npm run dev
npm run lint
npm test
```

`npm test` builds the static site and checks routes, language alternates, the approved public catalogue (currently 40 indexed pages), local links, canonical brand assets, and product boundaries. `npm run build` generates a production export without running the tests.

## Content and navigation

- Edit paired `docs/<slug>.md` and `docs/<slug>.en.md` sources. Follow the [documentation guide](../../docs/documentation-guide.en.md).
- Register public pages in `lib/public-docs.json`, the shared allowlist for rendering and search. Internal engineering documents remain repository references.
- Assign each public slug to one group in `lib/docs.ts`: Getting started, Guides, Concepts & architecture, or Reference. Keep existing slugs stable.
- `scripts/prepare-content.mjs` copies canonical brand assets and indexes the entire public document text. Search filters by language, matches all query terms, and ranks title matches first.
- `app/[lang]/[[...slug]]/page.tsx` renders navigation, reading paths, the article, its table of contents, adjacent pages, and source links.

The structure draws on [OpenTelemetry documentation](https://opentelemetry.io/docs/) and [Gitea documentation](https://docs.gitea.com/). Ingot's claims and terminology follow [the brand reference](../../docs/brand.en.md).

## Publish

Deploy with `./deploy/deploy.sh docs` (see [deploy/README.md](../../deploy/README.md)). Before publishing, check both language homepages, search, mobile navigation, and a nested page. Never edit generated files in `out/` or `public/search-index.json` by hand.
