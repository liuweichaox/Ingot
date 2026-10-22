<!-- BEGIN:nextjs-agent-rules -->

# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->

## Ingot website rules

- Copy lives in `app/IngotSite.tsx`; keep the Chinese and English layouts in sync.
- Public claims follow `docs/brand.en.md` and the repository product-language checks.
- Run `npm test` before publishing; it builds the site and checks rendered HTML and product boundaries.
- Keep the page independent of a specific equipment model, material, or process.
