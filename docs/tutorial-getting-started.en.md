# Getting started

This tutorial starts Platform, submits a standard production-event batch, and uses Ingot Chat to query a cycle fact chain. Source adaptation is implemented by your team; map its output to the public event contract.

## 1. Prepare the environment

- .NET SDK 10
- Node.js 22.13 or later
- Docker Engine 26 or later with Docker Compose
- OpenSSL

## 2. Start Platform

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot

export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"

docker compose -f docker-compose.app.yml up -d --build
```

Wait for the health check:

```bash
curl http://localhost:8000/health
```

Open <http://localhost:3000> for Platform Web.

The default Compose stack starts PostgreSQL, Platform API, and Platform Web. If an adapter can reach Platform, use the direct batch API in this tutorial.

### Optional: enable Connector Host

When a plant network needs a local SQLite outbox, enable the Connector Host profile:

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

The Host listens on <http://localhost:8001>, accepts `ProductionEvent[]`, and sends standard batches to Platform with at-least-once delivery. The team deploys and operates this local ingress.

## 3. Submit the first production-event batch

Call Platform from your own source-adaptation process. This example sends one `cycle.started` event:

```bash
curl -X POST http://localhost:8000/api/v1/events:batch \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${INGOT_EDGE_TOKEN}" \
  -d '{
    "edgeId": "EDGE-001",
    "events": [{
      "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
      "eventType": "cycle.started",
      "eventTypeVersion": 1,
      "occurredAt": "2026-07-18T08:00:00Z",
      "recordedAt": "2026-07-18T08:00:00Z",
      "source": "edge/EDGE-001/demo/FURNACE-01",
      "subject": { "type": "asset", "id": "FURNACE-01" },
      "context": { "workpiece_id": "WP-001" },
      "data": {},
      "correlationId": "CYCLE-001",
      "seq": 1
    }]
  }'
```

`ackSeq` in the response means Platform has safely accepted, or recognized as a duplicate, the contiguous sequence. Retain local sequence state and retry only unacknowledged events.

## 4. Enable Chat in production

The default Compose stack keeps Chat disabled. The following configuration enables OpenAI, Actor `operator`, and full fact access:

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true

docker compose -f docker-compose.app.yml up -d --build
```

For production, replace broad access with the actual data scope required by each Actor. Platform Web and the Chat API use `operator` with `INGOT_CHAT_OPERATOR_TOKEN`.

## 5. Query events and use Chat

```bash
curl "http://localhost:8000/api/v1/events?edgeId=EDGE-001&correlationId=CYCLE-001"
```

Then open **Chat** in Platform Web and ask:

```text
What happened during this cycle, and is its data complete?
```

Chat returns read-only tool activity, limitations, and fact references. See the [production event specification](rfc-production-events.en.md) for all fields and [Ingot Chat](chat.en.md) for Chat behavior.
