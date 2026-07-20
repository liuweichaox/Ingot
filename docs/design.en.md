# Design

## Ingot Chat design

```text
question and page context
  → typed query plan
  → authorization, scope, and tool-allowlist validation
  → check_data_quality / get_cycle_trace
  → number and evidence verification
  → streamed answer
```

The Chat model interprets language and composes responses. Platform deterministic code owns data query, permissions, tool execution, number validation, and evidence validation. Chat runs are Actor-isolated and available only under `/api/v1/chat/*`.

## Event-ingestion design

Teams map any source to `ProductionEvent` batches and call `/api/v1/events:batch`. Platform deterministically:

1. validates the Edge token, `edgeId`, batch size, and event fields;
2. validates source prefixes, event IDs, and sequence uniqueness;
3. handles duplicate submission by `eventId` and `(edgeId, seq)`;
4. persists platform facts and returns `ackSeq`;
5. exposes query, SSE, and correlation-ID cycle fact chains.

Teams choose source protocols, local buffering, retry timing, and process supervision. The standard contract keeps those implementation details decoupled from Platform.

## Isolation and recovery

- Chat creation, history, reads, SSE, and cancellation are Actor-authorized;
- SSE uses monotonic event sequences; clients resume with `Last-Event-ID`;
- interrupted Chat runs enter an explicit terminal state after service restart;
- event batches are validated as a unit; callers should retain submitted sequences and use `ackSeq` for retries;
- Chat never holds source credentials or calls field networks or equipment interfaces.

See [Ingot Chat](chat.en.md) and the [production event specification](rfc-production-events.en.md).
