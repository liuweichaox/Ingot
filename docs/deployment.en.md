# Deployment and operations

> Status: **current operations guide**. Deployment keeps acquisition, business records, and engineering decisions reliable inside the factory. The public website and documentation site are outside the factory runtime.

## Recommended topology

```text
Field data sources                            Factory runtime
controls / instruments / vision / inspection / MES
          └─ Edge ConnectorHost ───→ Platform API
                                      ├─ PostgreSQL / TimescaleDB
                                      ├─ attachments and process knowledge
                                      ├─ Optimizer (independent stateless service)
                                      ├─ Agent / Chat (embedded Platform capability)
                                      └─ Platform Web (independent React frontend)
```

Platform API is the central business runtime. `Edge.Application`, `Edge.Infrastructure`, `Platform.Infrastructure`, and Agent are code libraries, not independent Compose services.

### Edge partitioning

Every Edge ConnectorHost has its own `EdgeId`, process or container, data volume, configuration cache, and lifecycle.

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
- `INGOT_EDGE_ID`: stable after installation and unique per Edge
- `INGOT_EDGE_TOKEN`
- `INGOT_CONNECTOR_TOKEN`
- `INGOT_CONNECTOR_LOCAL_TOKEN`
- `INGOT_ADMIN_PASSWORD`

Never commit `.env` or real equipment credentials. Inject device passwords and certificates through a site-approved secret-management method.

### Local model service

When Chat is enabled, the model service provides an OpenAI-compatible `/v1` interface. Configure `INGOT_CHAT_BASE_URL`, `INGOT_CHAT_FAST_MODEL`, `INGOT_CHAT_REASONING_MODEL`, and `OPENAI_API_KEY`. Platform enables a role only when its configured model ID is available.

The model service is not a startup dependency for acquisition, inspection, or numerical optimization. Content sent to it remains subject to authorized tools and business permissions.

## Start the application stack

```bash
docker compose -f docker-compose.app.yml up -d --build
```

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

Apply configuration at a process-safe boundary. For cyclic equipment, prefer switching between cycles and retain the old version on failure.

## Health and readiness

| Service | Check | Meaning |
|---|---|---|
| Platform | `/health` | central process and configured dependencies |
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

## Data and backup

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
