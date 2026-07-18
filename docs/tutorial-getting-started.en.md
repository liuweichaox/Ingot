# Getting started

## Start Central and Connector Host

Prerequisites: .NET 10, Node.js 22.13+, Docker Engine 26+, and Docker Compose.

```bash
export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml up -d --build
```

Open `http://localhost:3000`. Central Web provides events, inspections, logs, metrics, and Chat. Connector Host accepts normalized events at `http://localhost:8001`.

## Use Chat

Chat is disabled by default. Configure `Chat:Enabled`, models, Actor tokens, and Actor data scopes, then recreate Central API. Open **Chat** in Central Web and enter a question with optional asset or cycle context.

```text
What happened during this cycle, and is its data complete?
```

Chat calls only `check_data_quality` and `get_cycle_trace`, then returns tool activity, findings, limitations, and evidence. See [Chat](chat.en.md) for the complete interface.

## Download Ingot Agent

Download Ingot Agent Desktop from the [latest GitHub Release](https://github.com/liuweichaox/Ingot/releases/latest). On first launch, enter:

1. Central API URL;
2. Actor ID;
3. Actor token.

The desktop verifies Agent capabilities before creating a structured connector-code task. It presents SSE progress, source, and network-disabled container build/fixture-test output. Agent does not connect to data sources. After tests pass, an authorized Actor reviews and approves packaging. The desktop generates the ZIP, verifies the server SHA-256, and downloads it.

See [Ingot Agent desktop](desktop-agent.en.md) for the full workflow. Central Web does not expose Agent code generation.

## Verify normalized event ingress

An external connector transforms source data into `ProductionEvent[]` and submits it to `POST http://localhost:8001/api/v1/connector-events` with `INGOT_CONNECTOR_TOKEN`. The default Agent template only transforms stdin JSONL into stdout ProductionEvent JSONL. The external runtime owns batching, authentication, and submission.

## Complete gate

```bash
./scripts/verify.sh
```
