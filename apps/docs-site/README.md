# Ingot Documentation Site

The static documentation site for trusted production records, standard event ingestion, and Ingot Chat.

`docs-site` reads bilingual Markdown from `../docs` at build time, creates navigation and a local search index, and exports static pages for `docs.ingotstack.com`.

```bash
npm install
npm run build
npm test
npm run lint
```

Public navigation is organized around getting started, Ingot Chat, event ingestion, operations, architecture, and references. Source integration is implemented by users against the standard event API.
