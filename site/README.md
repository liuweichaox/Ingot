# Ingot Product Website

The bilingual public website for the Ingot production-facts and process-investigation platform.

Public product terms are fixed:

- **Ingot Chat** is the main workspace for engineers: everyday questions establish facts, while deeper investigation brings process, quality, and challenge perspectives to the same verified evidence.
- **Standard event ingestion** is how teams bring in data. They implement source adaptation and submit `ProductionEvent` batches to Central.
- **Connector Host** is an optional, team-operated local ingress and SQLite outbox for plant networks that need it.

```bash
npm install
npm run build
npm test
npm run lint
```

The site is statically exported. Its documentation links point to `https://docs.ingotstack.com`; public links lead to documentation and describe source adaptation as team-owned. The site states the production-fact and field-control boundaries.
