# Deployment

This guide deploys Platform Web, Platform API, and PostgreSQL. Teams deploy source adaptation in the network and runtime appropriate to the plant and submit records to Platform through the standard event API.

## Pre-deployment checks

- Prepare separate secrets for PostgreSQL, event ingestion, and Chat;
- if deploying optional `Ingot.Edge.ConnectorHost`, prepare an independent local-ingress token;
- create an independent event token for every `edgeId`;
- configure the model, model key, independent token, and required record scope for every Chat user;
- place Platform API and the database on a controlled network and use TLS in production;
- ensure source-adaptation programs do not put device credentials, raw large objects, or sensitive text into event context.

## Docker Compose

```bash
export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"

docker compose -f docker-compose.app.yml up -d --build
docker compose -f docker-compose.app.yml ps
```

Check the services:

```bash
curl http://localhost:8000/health
```

The default Compose stack starts PostgreSQL, Platform API, and Platform Web, with Chat disabled.

## Enable Chat in production

Production Chat requires this complete configuration before it is enabled:

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true

docker compose -f docker-compose.app.yml up -d --build

curl http://localhost:8000/api/v1/chat/capabilities
```

This Compose configuration grants the platform identity `operator` access to all records. Development supplies this local identity on the server; production must configure unified authentication and the actual data scope required by each role.

## Source ingestion

Source-adaptation programs own:

1. reading equipment, instruments, or business systems;
2. mapping stable event types, subjects, context, and units;
3. retaining local sequence state and unacknowledged batches;
4. submitting events with `POST /api/v1/events:batch` and an Edge token;
5. retrying unacknowledged events from `ackSeq`;
6. monitoring authorization failures, latency, and duplicate rate.

`Ingot.Edge.ConnectorHost` is an optional path: a team may deploy it in the plant for a local SQLite outbox and submit `ProductionEvent[]` to it; the Host ships batches to Platform with at-least-once delivery. Default Compose does not start the Host. Enable this path when needed:

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

Direct Platform batches and Host ingress use different tokens, so choose, monitor, and operate one path for each network boundary.

Platform requires no specific language, process model, or plant operating system. See the [production event specification](rfc-production-events.en.md) for full fields and retry handling rules.

## Backup and upgrade

- Back up PostgreSQL according to organizational recovery objectives;
- run `./scripts/verify.sh` and validate Compose configuration before an upgrade;
- validate compatibility in an isolated environment with real but de-identified event batches;
- increase `eventTypeVersion` or add an event type for incompatible meaning changes;
- pass Chat evaluation before changing model or prompt versions and retain run, tool, and related records audit records.

## Operating boundary

Ingot provides record storage, query, and conversation. Device protocols, plant safety, network isolation, credential rotation, source-adaptation availability, and equipment control remain deployment and field-system responsibilities.
