# Ingot Chat

Ingot Chat is the main way engineers use Ingot: it helps people query recorded production facts, check data quality, and locate cycle-data problems. Ingot is the trusted production-facts and process-investigation platform; Chat is its conversation and investigation workspace. It reads production records only and preserves field-equipment state.

## Capabilities

- Interpret natural-language questions with optional asset or cycle page context;
- produce a governed typed query plan;
- call `check_data_quality` for event completeness, missing context, freshness, and available range;
- call `get_cycle_trace` for an ordered `correlationId`-scoped cycle event chain;
- stream plan, read-only tool activity, answer, limitations, and evidence references;
- retain Actor-scoped history with cancellation and SSE resume.

The available tools are `check_data_quality` and `get_cycle_trace`.

## Deeper investigation: bounded multi-agent collaboration

Everyday questions use `standard` mode. For a complex question that benefits from several perspectives, explicitly enable `deep` investigation mode.

- The **process role** reviews cycles, state changes, and parameter differences.
- The **quality role** reviews inspection outcomes, sample scope, and quality associations.
- The **challenge role** looks for data gaps, confounders, and alternative explanations.
- All roles can read only the verified tool results from this run; they cannot access a database, network, or equipment themselves.
- The default limit is 3 rounds and 9 turns, with hard limits of 5 rounds and 15 turns. At least two first-round roles must succeed before a candidate explanation is returned.
- The result includes supporting evidence, challenges, and limitations. It can describe only a candidate explanation, never a confirmed root cause or causation.

This is not an unconstrained group chat. It is a read-only investigation workflow with fixed roles, turn limits, evidence scope, and stop conditions. An engineer always makes the final judgement.

## Execution flow

```text
question and page context
  → intent and time-range parsing
  → authorization, tool, and data-range validation
  → read-only fact tools
  → number, unit, and evidence verification
  → answer, limitations, and fact references
```

The model interprets language and composes the response. Deterministic code owns queries, permissions, range limits, tool execution, and evidence verification. Chat uses governed fact tools and does not generate SQL, Flux, or scripts.

## Enable in production

Production Compose leaves Chat disabled. Configure the model, model key, Actor token, and data scope together when enabling it:

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true
export INGOT_CHAT_ENABLE_DEEP_INVESTIGATION=true
```

Central Web and the HTTP API use Actor `operator` with `INGOT_CHAT_OPERATOR_TOKEN`. Production deployments configure access for the fact scope required by each Actor.

## Use Chat

1. Open Central Web and select **Chat**.
2. Enter a question such as “What happened during this cycle, and is its data complete?”
3. Optionally select an asset or cycle and supply page context.
4. Review read-only tool activity, findings, limitations, and evidence.
5. Follow evidence references back to production events or cycle facts.

## HTTP API

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/chat/capabilities` | Discover availability, modes, read-only tools, models, and run limits |
| `POST /api/v1/chat/runs` | Create a Chat run |
| `GET /api/v1/chat/runs` | Page through history for the current Actor |
| `GET /api/v1/chat/runs/{runId}` | Read snapshot, plan, tools, evidence, and answer |
| `GET /api/v1/chat/runs/{runId}/stream` | Stream SSE events and resume with `Last-Event-ID` |
| `POST /api/v1/chat/runs/{runId}:cancel` | Cancel a run |

```bash
curl -X POST http://localhost:8000/api/v1/chat/runs \
  -H "Content-Type: application/json" \
  -H "X-Ingot-Actor: operator" \
  -H "Authorization: Bearer ${INGOT_CHAT_OPERATOR_TOKEN}" \
  -d '{
    "question": "What happened during this cycle, and is its data complete?",
    "pageContext": { "kind": "cycle", "id": "CYCLE-001" },
    "mode": "deep"
  }'
```

## Security boundary

- The read-only allowlist contains only registered fact-query tools;
- every tool inherits the current Actor's data scope;
- arbitrary SQL, scripts, shell, filesystem access, and open network access are prohibited;
- instruction-like text in tool results remains untrusted data and cannot change policy;
- answer numbers must come from tool results, and key findings require resolvable evidence;
- insufficient, conflicting, or low-quality data produces explicit limitations rather than a definitive conclusion;
- Chat cannot change configuration, events, inspection records, or equipment state.

See [configuration](tutorial-configuration.en.md) and the [production event specification](rfc-production-events.en.md).
