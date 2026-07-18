# Ingot Product Website

The bilingual website for Ingot trusted production facts, standard event ingestion, and Ingot Chat.

Public product terms are fixed:

- **Ingot Chat** is the only user-facing AI conversation in Central Web. It queries recorded facts and presents evidence.
- **Standard event ingestion** is how teams bring in data. They implement source adaptation and submit `ProductionEvent` batches to Central.
- **Connector Host** is an optional, team-operated local ingress and SQLite outbox for plant networks that need it.

```bash
npm install
npm run build
npm test
npm run lint
```

The site is statically exported. Its documentation links point to `https://docs.ingotstack.com`; public links lead to documentation and describe source adaptation as team-owned. The site states the production-fact and field-control boundaries.
