# Design

## Chat design

```text
question and page context
  → typed query plan
  → authorization, range, and tool-allowlist validation
  → check_data_quality / get_cycle_trace
  → number and evidence verification
  → streamed answer
```

The Chat model handles language interpretation and response composition. Deterministic Central code owns data queries, authorization, tool execution, number checks, and evidence verification. Chat runs persist with `surface=chat` and are accessible only through `/api/v1/chat/*`.

## Ingot Agent design

```text
structured connector requirement
  → typed code plan
  → Actor workspace tools
  → fixed build/test
  → bounded repair
  → awaiting-package-approval
  → operator approval
  → SHA-256 ZIP
```

The Tauri 2 desktop calls Agent endpoints through a Rust network boundary. The server accepts only `X-Ingot-Client: ingot-agent-desktop`. `Ingot.Agent` supplies a provider-neutral workflow. `Ingot.Agent.Infrastructure` adapts models, SQLite, and tools. `Ingot.Connector.Builder` owns workspaces, fixed container entries, the approval gate, and content-addressed packaging.

The model cannot select host paths, shell commands, images, or working directories. Agent and Builder do not connect to data sources; network-disabled tests read only fixed workspace fixtures and simulated input. Bounded build/test errors can repair only current-workspace code. Passing tests never package automatically.

## Connector contract

The default template uses stdin JSONL input and stdout ProductionEvent JSONL output. The external runtime owns batching, Connector Token use, submission to Connector Host, process supervision, and upgrades. Connector Host validates events, commits them to SQLite, and ships them through an outbox.

## Isolation and recovery

- Chat and Agent create, list, read, SSE, cancellation, and history are mutually isolated.
- Every run uses Actor authorization; Agent source and packages add workspace boundaries.
- SSE uses monotonic event sequences and resumes with `Last-Event-ID`.
- After service restart, interrupted runs move to an explicit terminal state rather than silently continuing writes.
- Package download recomputes SHA-256, which the desktop verifies before saving.

See [Chat](chat.en.md) and [Ingot Agent desktop](desktop-agent.en.md).
