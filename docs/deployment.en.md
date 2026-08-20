# Deployment

> Status: **current operations guide**. Deployment keeps acquisition, business records, and engineering decisions reliable inside the factory. The public website and documentation site are outside the factory runtime.

This document describes the repository's current runnable form. See [Production architecture](production-architecture.en.md) for target requirements covering replicas, PITR, site production cells, and controlled action. A deployment that has not passed those admission gates must not claim the corresponding production level.

## Recommended topology

```text
Field data sources                            Factory runtime
controls / instruments / vision / inspection / MES
          └─ Edge ConnectorHost ───→ Platform API
                                      ├─ Platform Worker (durable jobs)
                                      ├─ PostgreSQL / TimescaleDB
                                      ├─ attachments and process knowledge
                                      ├─ Optimizer (independent stateless service)
                                      ├─ Agent / Chat (embedded Platform capability)
                                      └─ Platform Web (independent React frontend)
```

Platform API handles requests and business transactions. Platform Worker handles knowledge extraction, analysis backfills, experiment materialization, and retention jobs. They coordinate through PostgreSQL job leases rather than process-local queues. `Edge.Application`, `Edge.Infrastructure`, `Platform.Infrastructure`, and Agent are code libraries, not independent Compose services.

The bundled Compose file is a single-API reference topology, not an HA claim. Agent runs now share PostgreSQL with business evidence, so they no longer prevent an external orchestrator from scaling API replicas behind a load balancer. A production multi-replica deployment must still provide ingress load balancing, PostgreSQL HA, and capacity acceptance.

### Edge partitioning

Every Edge ConnectorHost has an explicit `SiteId` assignment plus its own `EdgeId`, process or container, data volume, configuration cache, and lifecycle. `SiteId` is the production-cell boundary. Platform binds the Edge token, `EdgeId`, and `SiteId`, so even an Edge holding the correct token cannot write into another site.

Production reads also fail closed on `SiteId`. The OIDC issuer must emit one or more `ingot:site` claims for non-administrator identities; local accounts receive site assignments through `POST /api/v1/users/{userId}:set-site-access`. A `platform.admin` may run cross-site administrative lists, but execution detail, analysis, and curve reads still require an explicit `siteId` to avoid resolving a same-named execution in another production cell.

- Equipment on the same OT network that may stop together can share one Edge.
- Separate Edge instances serve different VLANs, security zones, or physically isolated networks.
- Critical equipment and high-event or high-backlog sources use dedicated Edge instances.
- Shared power, host, switch, network path, and maintenance window define failure domains.
- Acceptable acquisition loss and recovery time determine instance count.

A small site may place Edge and Platform on one physical server while keeping processes, storage, health, upgrades, and recovery independent.

The website and docs use `deploy/compose.yml` separately and never enter the factory application's failure domain.

## Environment configuration

```bash
cp .env.example .env
```

Change at least:

- `INGOT_POSTGRES_PASSWORD`
- `INGOT_SITE_ID`: owning production cell; do not change casually after production admission
- `INGOT_EDGE_ID`: stable after installation and unique per Edge
- `INGOT_EDGE_TOKEN`
- `INGOT_CONNECTOR_TOKEN`
- `INGOT_CONNECTOR_LOCAL_TOKEN`
- `INGOT_EDGE_DIAGNOSTICS_BASE_URL`: the trusted, deployment-pinned Edge diagnostics API URL; reported node metadata cannot override it
- `INGOT_ADMIN_PASSWORD`

Never commit `.env` or real equipment credentials. Inject device passwords and certificates through a site-approved secret-management method.

### Local model service

When Chat is enabled, the model service provides an OpenAI-compatible `/v1` interface. Configure `INGOT_CHAT_BASE_URL`, `INGOT_CHAT_FAST_MODEL`, `INGOT_CHAT_REASONING_MODEL`, and `OPENAI_API_KEY`. Platform enables a role only when its configured model ID is available.

The model service is not a startup dependency for acquisition, inspection, or numerical optimization. Content sent to it remains subject to authorized tools and business permissions.

## Start and stop

Validate required environment variables and Compose structure first:

```bash
docker compose -f docker-compose.app.yml config --quiet
```

Build and start the five core services:

```bash
docker compose -f docker-compose.app.yml up -d --build
```

Common lifecycle commands:

```bash
docker compose -f docker-compose.app.yml ps -a
docker compose -f docker-compose.app.yml logs --tail=200
docker compose -f docker-compose.app.yml restart platform-api
docker compose -f docker-compose.app.yml down
```

`down` removes containers and networks but retains named volumes by default. Do not add `--volumes` without a backup and an explicit reset decision. After source changes, use `up -d --build`; after `.env`-only changes, use `up -d` to recreate affected containers.

The first build downloads large SDK, PyTorch, and database images. Startup is complete only after `platform-migrate` exits successfully, the four HTTP/database services are `healthy`, and `platform-worker` remains `running`.

| Symptom | Check first | Common cause and response |
|---|---|---|
| `ps -a` shows no containers | final build output | build is still running or was interrupted; rerun `up -d --build` |
| `unexpected EOF` or `short read` | image download layer | network interruption left an incomplete layer; retry and Docker reuses complete layers |
| Web is absent while API is healthy | `logs platform-web` | frontend build or Nginx configuration failed |
| API repeatedly restarts | `logs platform-migrate`, `logs platform-api`, and `logs postgres` | database password, migration, directory permission, or production configuration validation failed |
| Optimizer is unhealthy | `logs optimizer` and `/ready` | numerical Python dependencies are incomplete or failed to load |
| Login password is unknown | `logs platform-api` | a random password is logged only when the first administrator is seeded with an empty configured password; existing accounts are not reset by editing `.env` |
| Port is already in use | `lsof -nP -iTCP:3000 -iTCP:8000 -iTCP:8100 -sTCP:LISTEN` | stop the conflicting process or deliberately change the Compose port mapping |

Use `docker compose -f docker-compose.app.yml logs --tail=200 <service>` for one service. Preserve the full error during diagnosis instead of deleting containers, images, or volumes first.

For an independent field connector:

```bash
docker compose -f docker-compose.app.yml --profile connector-host up -d --build
```

Before connecting real equipment, configure the target Edge, connection, and mappings in Platform. Example addresses, passwords, and process ranges are not production defaults.

## Acquisition configuration rules

Platform publishes versioned acquisition configuration by `EdgeId`. Edge pulls and validates it locally and stores the last successful version in `Data/acquisition-deployments.json`.

- During a Platform outage, Edge continues with the last successful version.
- A version that fails connection, point, or conversion validation never replaces the old version.
- An explicit zero-configuration publication stops the corresponding acquisition and reports state.
- Production never silently enables an unversioned local fallback.
- HTTP/MQTT may read a sample payload; OPC UA may browse nodes.
- Modbus TCP and MELSEC read only explicitly configured addresses and never blind-scan.
- Real-value validation runs before and during publication.

Apply configuration at a process-safe boundary. For cyclic equipment, prefer switching between process executions and retain the old version on failure.

## Health and readiness

| Service | Check | Meaning |
|---|---|---|
| Platform | `/health` | central process and configured dependencies |
| Platform Worker | container state and job-heartbeat metrics | durable job processor remains active |
| Optimizer | `/health` | HTTP process is alive |
| Optimizer | `/ready` | PyTorch, GPyTorch, and BoTorch runtime is usable |
| ConnectorHost | `/health` | field process and configured dependencies |
| Web | `/health` | frontend service state |
| PostgreSQL | `pg_isready` | database accepts connections |

Platform starts without Optimizer. An Optimizer outage pauses new numerical recommendations while acquisition, run records, and inspections continue. Agent or model failure also leaves formal business records available.

## Observability

Production monitoring includes:

- Edge heartbeat, desired/applied configuration versions, and errors;
- connection state, sample time, and backlog per equipment;
- event duplicates, disorder, maximum gaps, and replay delay;
- run completeness and actual-setting, context, and inspection linkage coverage;
- PostgreSQL connectivity, disk, migration, and slow transactions;
- Optimizer readiness, failures, and computation time;
- Agent tool failures, unavailable models, and authorization denials.

Alerts identify an actionable object such as an Edge, equipment, configuration version, or run rather than only “system error.”

The repository provides an optional minimum monitoring profile with Prometheus, Alertmanager, a PostgreSQL exporter, and a provisioned Grafana dashboard:

```bash
docker compose -f docker-compose.app.yml \
  --profile connector-host --profile monitoring up -d --build
```

Grafana, Prometheus, and Alertmanager bind only to local ports `3001`, `9090`, and `9093`. Before enabling the profile:

- edit `deploy/observability/edge-targets.yml` with the real target, `SiteId`, and `EdgeId` for every independently operated Edge;
- set a unique `INGOT_GRAFANA_ADMIN_PASSWORD`;
- replace the default with a site-owned `INGOT_ALERTMANAGER_CONFIG_PATH` connected to a tested notification route;
- select `INGOT_PROMETHEUS_RETENTION` from capacity and data-classification requirements, and monitor Prometheus storage itself.

The checked-in Alertmanager receiver deliberately sends no external notification and cannot prove that alert delivery works. This profile closes the gap between exported metrics and an actual collector/dashboard, but it remains a single-host reference topology and does not make Compose highly available.

## Data and backup

The application-consistent backup script briefly stops Platform API and Worker, creates a logical PostgreSQL dump, archives inspection and process-knowledge volumes, writes a SHA-256 manifest, and then restores the previously running writers:

```bash
./scripts/backup-app.sh
./scripts/check-backup.sh deploy/backups 24
```

Restore replaces the current PostgreSQL database and all four file volumes, so it requires an explicit confirmation flag. If restore fails, writers remain stopped rather than producing records against a partial recovery:

```bash
./scripts/restore-app.sh --confirm-replace-all-data deploy/backups/app-YYYYMMDDTHHMMSSZ
```

The backup uses `pg_dump --format=custom`; it supports logical restore and migration validation but is not PITR. Sites with a smaller RPO must additionally configure PostgreSQL base backups, continuous WAL archiving, off-host retention, and regular point-in-time recovery exercises. Backup directories contain business records and attachments and require production-equivalent access control.

Back up at least:

- PostgreSQL data;
- inspection attachments;
- process-knowledge files;
- Edge local event databases until upload is confirmed;
- the last successful Edge acquisition configuration;
- required certificates, secret references, and recovery instructions.

A recovery exercise verifies more than service startup:

- run, context, and inspection linkage remains intact;
- experiments, evidence, and reviews are readable;
- Edge backlog replays without duplicates;
- historical observations rebuild under their original versions;
- a known project reproduces the same analytical input hash.

## Upgrade

1. Read `CHANGELOG.md` and identify data-model or configuration changes.
2. Back up the database, attachments, and Edge configuration cache.
3. Run migrations and replay in a test environment.
4. Run `scripts/verify.sh`.
5. Upgrade Platform and database dependencies.
6. Upgrade Edge instances in batches and confirm the old configuration remains available.
7. Check backlog recovery, duplicate events, and configuration convergence.
8. Regress run assembly, comparison, and recommendation on a known project.

## Minimum security set

- Do not expose PostgreSQL, Optimizer, or ConnectorHost to unnecessary networks.
- Replace every example password, token, and certificate.
- Give each Edge an independent identity and least-privilege token.
- Restrict the equipment network to required addresses and protocols.
- Apply access control to attachments, knowledge, and backup directories.
- Separate roles for quality entry and review.
- Require engineer approval before an experiment enters field execution.
- Keep equipment interlocks and field safety independent of model recommendations.
- Report security issues privately under `SECURITY.md`.

## Production acceptance

Before go-live, exercise Platform outage, Edge restart, network loss, bad configuration publication, database recovery, unavailable Optimizer, and unavailable model service. Prove that acquisition and formal records degrade or recover as designed.

The RPO, RTO, offline window, backlog age, peak load, and observation period in `.env.example` are deployment declarations, not acceptance evidence. After site exercises, load those targets and provide measured values plus stable evidence identifiers:

```bash
set -a; . ./.env; set +a
export INGOT_MEASURED_RPO_MINUTES=10
export INGOT_MEASURED_RTO_MINUTES=45
export INGOT_MEASURED_EDGE_OFFLINE_HOURS=24
export INGOT_MEASURED_BACKLOG_AGE_SECONDS=600
export INGOT_MEASURED_CAPACITY_EVENT_RATE_PER_SECOND=2000
export INGOT_MEASURED_CAPACITY_SAMPLE_POINTS_PER_SECOND=30000
export INGOT_OBSERVED_CONTINUOUS_HOURS=168
export INGOT_BACKUP_EVIDENCE=backup-20260820-001
export INGOT_PITR_DRILL_ID=pitr-20260820-001
export INGOT_FAILURE_DRILL_ID=failure-20260820-001
export INGOT_DATABASE_HA_EVIDENCE=database-ha-20260820-001
export INGOT_FILE_RECOVERY_EVIDENCE=file-recovery-20260820-001
export INGOT_EDGE_REPLAY_EVIDENCE=edge-replay-20260820-001
export INGOT_DETERMINISM_EVIDENCE=determinism-20260820-001
export INGOT_SITE_ISOLATION_EVIDENCE=site-isolation-20260820-001
export INGOT_RUNBOOK_EVIDENCE=runbook-review-20260820-001
export INGOT_MONITORING_EVIDENCE=grafana-snapshot-20260820-001
export INGOT_ALERT_ROUTING_EVIDENCE=alert-route-20260820-001
export INGOT_ACCEPTANCE_REVIEWER=quality-owner
./scripts/verify-production-acceptance.sh artifacts/production-acceptance.txt
```

The script checks thresholds, refuses to overwrite an existing artifact, and writes a SHA-256 checksum; failed results are retained too. It records declarations, measurements, and evidence references. It neither validates the referenced evidence nor performs PITR or capacity tests by itself.

An isolated Compose environment can automatically exercise Optimizer, Worker, and API process outages. The script restores stopped services and writes a checksummed artifact:

```bash
INGOT_DRILL_ENVIRONMENT=isolated \
  ./scripts/drill-compose-failures.sh --confirm-isolated-environment \
  artifacts/compose-failure-drill.txt
```

The script refuses to run without the isolation marker and never stops PostgreSQL. Network partitions, Edge power-loss replay, database HA/PITR, bad configuration, model-service failure, and post-recovery data integrity still require site-specific exercises. A single passing script is never sufficient for production admission.
