# Development

## Product surfaces

- Central Web exposes fact pages and Chat only. Do not add an Agent code-generation surface to the browser.
- `/api/v1/chat/*` handles read-only analysis only, with `check_data_quality` and `get_cycle_trace`.
- `/api/v1/agent/*` handles connector code generation only and must validate `X-Ingot-Client: ingot-agent-desktop`.
- Chat and Agent create, history, read, stream, and cancel operations must be isolated by surface and Actor.

## Dependency direction

1. `Ingot.Contracts`: public Chat, Agent, connector, event, and inspection contracts;
2. `Ingot.Agent`: surfaces, workflow, plans, budgets, and verification;
3. `Ingot.Agent.Infrastructure`: models, SQLite store, and tools;
4. `Ingot.Connector.Builder`: Actor workspace, fixed container build/test, approval, and packaging;
5. `Ingot.Central.Infrastructure`: central facts and read-only Chat tools;
6. `Ingot.Connector.Host`: protocol-neutral normalized event ingress;
7. `desktop`: Tauri 2 desktop Agent client.

## Connector extension

Do not link equipment protocol SDKs or readers into the platform core. Ingot Agent writes a concrete protocol into a connector workspace. Shared changes belong in connector specifications, templates, deterministic transformation tests, and Builder fixed entries.

The default template must preserve:

```text
stdin/json-lines → stdout/production-event-json-lines
```

Connector acceptance covers parsing fixed workspace fixtures and simulated input, field mapping, units, event types, subjects, context, correlation IDs, and `ProductionEvent` validation. Agent and Builder do not connect to live data sources. Generated packages must not include production tokens, an HTTP submission client, or automatic deployment scripts.

## Desktop development

```bash
cd desktop
npm install
npm run desktop:dev
```

Every Central request must cross the Rust native boundary, add the fixed desktop-client identifier, constrain allowed Central URLs, and verify download SHA-256. The React layer must not hold unrestricted network or filesystem permissions.

## Gate

```bash
./scripts/verify.sh
```

Update bilingual documentation and tests with every change. See [Chat](chat.en.md) and [Ingot Agent desktop](desktop-agent.en.md).
