# FAQ

## Does Ingot connect directly to equipment?

No. Teams implement source adaptation, map equipment, instrument, or business-system data to `ProductionEvent`, and call `POST /api/v1/events:batch`. This lets each plant choose the language and runtime that meet its safety, network, and operations requirements.

For a local SQLite outbox, a team may deploy `Ingot.Edge.ConnectorHost` and submit events to it; it is an optional local ingress, while teams own adaptation implementation and runtime operation.

## What must a source-adaptation program do?

Use a stable `edgeId`, locally increasing `seq`, globally unique `eventId`, and a `source` that starts with `edge/{edgeId}/`. Retain unacknowledged batches and use `ackSeq` for retries; never put secrets or large objects in `context`.

## What can Chat do?

Chat queries recorded production records, checks data completeness, returns cycle event chains, and displays related records. It does not write events, inspection records, configuration, or equipment; it cannot execute arbitrary SQL, scripts, or open network requests.

## How do I enable Chat in production?

Default Compose keeps Chat disabled. Enable it with `INGOT_CHAT_ENABLED=true`, `INGOT_CHAT_PROVIDER=OpenAI`, Fast and Reasoning models, `OPENAI_API_KEY`, `INGOT_CHAT_OPERATOR_TOKEN`, and `INGOT_CHAT_OPERATOR_ALLOW_ALL`. Platform Web and the Chat API use user `operator` with the Chat user token. See [configuration](tutorial-configuration.en.md) for the complete configuration.

## Can Chat confirm root cause?

Chat shows saved production records, missing information, and next checks, while clearly separating parameter correlation from confirmed causes.

## How is data access scoped?

Configure allowed `EdgeIds` for every Chat user. Event ingestion uses an independent token matching the `edgeId`. Production deployments should rotate tokens and avoid global access.

## What happens when an event is submitted twice?

Platform detects duplicates by `eventId` and `(edgeId, seq)`. Callers should still retain local acknowledgment state and avoid mixing unacknowledged and new events into an unordered batch.

## Does the platform control equipment?

No. PLCs, CNCs, robots, safety interlocks, equipment authentication, and plant operations remain with existing field systems.

See the [production event specification](rfc-production-events.en.md), [configuration](tutorial-configuration.en.md), and [Ingot Chat](chat.en.md).
