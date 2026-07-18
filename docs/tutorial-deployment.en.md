# Deployment

## Central and Connector Host

The repository provides a single-host Docker Compose baseline:

```bash
export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker pull mcr.microsoft.com/dotnet/sdk:10.0
docker compose -f docker-compose.app.yml up -d --build
```

| Service | Responsibility |
|---|---|
| `central-web` | production-fact pages and Chat |
| `central-api` | Chat, desktop Agent, fact, inspection, webhook, and SSE APIs |
| `connector-host` | protocol-neutral normalized event ingress and shop-floor outbox |
| `postgres` | central production facts |

The default ports bind only to the host loopback interface. Public deployment requires an authenticated gateway, TLS, rate limits, and an explicit ingress allowlist.

## Chat deployment

Chat runs in Central API; Central Web only provides its user interface. Production configures the Chat provider, models, Actor tokens, and data scopes. Chat reads through central fact services and receives no database credentials, filesystem access, or equipment-network access.

## Ingot Agent deployment

Users download Ingot Agent Desktop from [GitHub Releases](https://github.com/liuweichaox/Ingot/releases/latest). The desktop stores Central URL, Actor, and token and uses a Rust native boundary for desktop-only Agent API requests.

Agent models, SQLite run storage, source workspaces, Builder, and package directories run on the Central side:

- `Data/agent.db`: Chat/Agent runs, SSE events, and artifact metadata;
- `Data/connector-workspaces`: Actor-isolated source, command results, and packaging-approval metadata;
- `Data/connector-packages`: tested and approved content-addressed ZIPs.

Central API uses the Docker socket to start constrained build children. The socket is a privileged operations boundary and must be exposed only to the trusted Central API. Production should use a dedicated host, rootless daemon, or equivalent governed build service. The model cannot access the socket or supply Docker arguments.

Fixed build/test children use `--network none`, `--pull never`, a read-only root filesystem, resource limits, dropped capabilities, and `no-new-privileges`. Pre-pull and audit the SDK image. Production can pin `INGOT_CONNECTOR_BUILDER_IMAGE` to a digest.

## Connector Host data

- `Data/connector-host/events.db`: shop-floor events and outbox;
- `Data/connector-host/context.db`: business context;
- `ingot-postgres-data`: central fact PostgreSQL volume.

The Connector Host outbox defaults to 500,000 records. At `Events:MaxBacklogRows`, it drops the oldest unshipped events and emits `diagnostic.backlog_dropped` plus `event_backlog_dropped_total`. Production monitoring must cover both.

## External connectors

Generated packages use a stdin JSONL → stdout ProductionEvent JSONL contract and contain no production credentials, HTTP submission client, or process supervision. The external deployment environment must:

- run and restart the connector;
- provide source and Connector Host credentials;
- form batches and call `POST /api/v1/connector-events`;
- own logs, upgrades, rollback, and network policy.

Ingot does not deploy, start, or schedule connectors and does not control equipment.

## Release gate

Retention, backup, and recovery must cover source, run records, approval metadata, packages, SQLite files, and PostgreSQL volumes. Run before release:

```bash
./scripts/verify.sh
```

The website and docs site deploy independently with `deploy/compose.yml` and `deploy/deploy.sh`; see [`deploy/README.md`](../deploy/README.md).
