# Ingot Product Website

The bilingual public website for the Ingot manufacturing data collection and process analysis platform.

Public product terms are fixed:

- **Ingot Chat** is the main workspace for engineers: quick queries show production records, while combined analysis compares process, quality, and review findings against the same original records.
- **Standard event ingestion** is how teams bring in data. They implement source adaptation and submit `ProductionEvent` batches to Platform.
- **Connector Host** is an optional, team-operated local ingress and SQLite outbox for plant networks that need it.

```bash
npm install
npm run build
npm test
npm run lint
```

The site is statically exported. Its documentation links point to `https://docs.ingotstack.com`; public links lead to documentation and describe source adaptation as team-owned. The site states the production-record and field-control boundaries.
