# Development

## Local verification

```bash
./scripts/verify.sh
```

This gate builds the .NET projects, runs tests, builds Platform Web, the product site, and the documentation site, then checks formatting, architecture, and product scope.

## Development principles

- Update contracts before Platform API, fact services, Web, and documentation;
- event ingestion accepts strongly typed `ProductionEvent` and `InspectionRecord` only, never arbitrary SQL or scripts from callers;
- Chat statistics, authorization, tool execution, and evidence validation remain deterministic;
- every new tool is read-only by default and needs data-scope, result-size, and timeout limits;
- public copy uses Ingot Chat and does not expose internal implementation terminology;
- when a public interface changes, update bilingual documentation, the product site, and static-site tests together.

## Event-contract changes

1. Assess whether the event type, version, and fields remain backward compatible;
2. update strong typed contracts and validation in `Ingot.Contracts`;
3. add matching API, storage, and integration tests;
4. record semantics in the [production event specification](rfc-production-events.en.md);
5. validate with de-identified samples that Chat still produces correct limitations and evidence.

## Documentation and website

`docs/` is the bilingual content source. `apps/docs-site/` reads Markdown at build time and generates navigation, a search index, and static pages. `apps/website/` is the product website. After a change, run at least:

```bash
npm --prefix apps/docs-site run build
npm --prefix apps/docs-site test
npm --prefix apps/website run build
npm --prefix apps/website test
```

See [architecture](architecture.en.md), [design](design.en.md), and the [contribution guide](../CONTRIBUTING.en.md).
