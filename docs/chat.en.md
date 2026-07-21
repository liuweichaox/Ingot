# Ingot Chat

Ingot Chat is the main way engineers use Ingot: it helps people query production records, check whether a run is complete, compare runs, and review possible causes. It reads saved records only and never changes field equipment or production data.

## Capabilities

- Interpret natural-language questions with an optional equipment ID or production run ID;
- produce a governed typed query plan;
- call `check_data_quality` for event completeness, missing context, freshness, and available range;
- call `get_cycle_trace` for an ordered `correlationId`-scoped cycle event chain;
- stream the query steps, answer, missing information, and links to related production records;
- retain user-scoped history with cancellation and SSE resume.

The available tools are `check_data_quality` and `get_cycle_trace`.

## Combined analysis: bounded multi-agent collaboration

Quick queries use `quick` mode. For a complex question that benefits from several perspectives, explicitly enable `combined` mode.

- The **process role** reviews cycles, state changes, and parameter differences.
- The **quality role** reviews inspection outcomes, sample scope, and quality associations.
- The **review role** looks for missing data, mixed influences, and other possible explanations.
- All roles use only the saved production records returned for this run; they cannot access a database, network, or equipment themselves.
- The default limit is 3 rounds and 9 turns, with hard limits of 5 rounds and 15 turns. At least two first-round perspectives must finish before possible causes are returned for review.
- The result includes matching production records, conflicting conditions, and missing data. It lists possible causes for engineering review and never presents them as confirmed root causes.

This is not an unconstrained group chat. It is a read-only combined analysis with fixed perspectives, turn limits, production-record scope, and stop conditions. An engineer always makes the final judgement.

## Execution flow

```text
question and selected equipment or production run
  → intent and time-range parsing
  → authorization, tool, and data-range validation
  → read-only record tools
  → number, unit, and original-record checks
  → answer, missing information, and record links
```

The model interprets language and composes the response. Application code owns queries, permissions, range limits, and checks that results link to the original records. Chat uses read-only record queries and does not generate SQL, Flux, or scripts.

## Enable in production

Production Compose leaves Chat disabled. Configure the model, model key, and platform-user data scope together when enabling it:

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true
export INGOT_CHAT_ENABLE_COMBINED_ANALYSIS=true
```

Development uses the server-owned local platform identity `operator`. Production must integrate unified authentication and configure the record scope required by each platform user.

## Use Chat

1. Open Platform Web and select **Chat**.
2. Enter a question such as “What happened during this cycle, and is its data complete?”
3. Optionally select an asset or cycle and supply page context.
4. Review read-only tool activity, findings, limitations, and related records.
5. Follow related records references back to production events or cycle records.

## HTTP API

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/chat/capabilities` | Discover availability, modes, read-only tools, models, and run limits |
| `POST /api/v1/chat/runs` | Create a Chat run |
| `GET /api/v1/chat/runs` | Page through history for the current user |
| `GET /api/v1/chat/runs/{runId}` | Read snapshot, plan, tools, related records, and answer |
| `GET /api/v1/chat/runs/{runId}/stream` | Stream SSE events and resume with `Last-Event-ID` |
| `POST /api/v1/chat/runs/{runId}:cancel` | Cancel a run |

```bash
curl -X POST http://localhost:8000/api/v1/chat/runs \
  -H "Content-Type: application/json" \
  -d '{
    "question": "What happened during this cycle, and is its data complete?",
    "pageContext": { "kind": "cycle", "id": "CYCLE-001" },
    "mode": "combined"
  }'
```

Development uses the server-owned local `operator` identity. Production must establish the user principal through platform authentication middleware; the Chat API does not accept a client-asserted user or a Chat-specific password.

## Security boundary

- The read-only allowlist contains only registered record-query tools;
- production trusts only the platform-authenticated principal, not `X-Ingot-User` or a Chat-specific bearer password;
- every tool inherits the current user's data scope;
- arbitrary SQL, scripts, shell, filesystem access, and open network access are prohibited;
- instruction-like text in tool results remains untrusted data and cannot change policy;
- answer numbers must come from tool results, and key findings require resolvable related records;
- insufficient, conflicting, or low-quality data produces explicit limitations rather than a definitive conclusion;
- Chat cannot change configuration, events, inspection records, or equipment state.

See [configuration](tutorial-configuration.en.md) and the [production event specification](rfc-production-events.en.md).
