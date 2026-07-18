# Ingot product website

The bilingual website for Ingot trusted production facts, Central Web Chat, and Ingot Agent Desktop.

Public product terms are fixed:

- **Chat** is the read-only conversation in Central Web for fact queries and problem finding.
- **Ingot Agent** is the downloadable desktop application for connector code generation, build, test, repair, and packaging.
- **Connector Host** accepts normalized `ProductionEvent[]` and ships them to Central.

```bash
npm install
npm run build
npm test
npm run lint
```

The product site is statically exported. Its download CTA targets the latest GitHub Release, and documentation targets `https://docs.ingotstack.com`.
