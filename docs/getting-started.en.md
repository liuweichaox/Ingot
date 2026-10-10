# Getting started

> Document status: **current operating guide**. This page covers local deployment, startup, and sign-in. See [Current status](status.en.md) for capability boundaries.

## Choose a path

| Objective | Path | Completion signal |
|---|---|---|
| Deploy Ingot | [Start the complete stack](#start-the-complete-stack) | Web, API, Worker, Optimizer, and database are healthy, and migration exits successfully; no external-system account or connection is required |
| Validate the current production-evidence workflow | [Recipe-optimization pilot guide](pilot.en.md) | Qualified production-run evidence and the first next-recipe recommendation |
| Prepare production | [Production architecture](production-architecture.en.md) → [Deployment](deployment.en.md) | The site independently passes security, recovery, capacity, and observation acceptance |
| Contribute code | [Contributing](https://github.com/liuweichaox/Ingot/blob/main/CONTRIBUTING.en.md) | `./scripts/verify.sh` passes locally |

See [Current status](status.en.md) for capability and validation maturity.

## Start the complete stack

You need Git, Docker Engine or Docker Desktop, and Docker Compose v2. The Compose path does not require .NET, Node.js, Python, or uv on the host.

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
```

Change the database passwords and administrator settings in `.env`. Replace every `change-this-` placeholder. Production uses randomly generated, distinct passwords and tokens.

The reference Compose requires `INGOT_SITE_ID`, `INGOT_EDGE_ID`, `INGOT_EDGE_TOKEN`, and `INGOT_CONNECTOR_LOCAL_TOKEN` for Platform bindings even when the optional connector is disabled. Keep the example local IDs for an initial evaluation, and replace both token placeholders with distinct random secrets of at least 24 characters. Configure `INGOT_CONNECTOR_TOKEN` only when enabling `connector-host`. These settings do not connect equipment.

On Windows PowerShell, use `Copy-Item .env.example .env` instead of `cp`. Keep `.env` local and exclude it from commits.

Validate the configuration, then start:

```bash
docker compose -f docker-compose.app.yml config --quiet
docker compose -f docker-compose.app.yml up -d --build
```

The first build downloads build/runtime images, the TimescaleDB image, and Python numerical packages including PyTorch. After the command finishes, inspect every container:

```bash
docker compose -f docker-compose.app.yml ps -a
```

Confirm at least that:

- `platform-migrate` exited successfully;
- `postgres`, `optimizer`, `platform-api`, and `platform-web` are `healthy`;
- `platform-worker` remains `healthy`;
- no container is restarting repeatedly.

Then open:

```text
http://localhost:3000       Engineering workbench
http://localhost:8000/health
http://localhost:8000/openapi/v1.json
http://localhost:8100/ready
```

Sign in with `INGOT_ADMIN_USERNAME` and `INGOT_ADMIN_PASSWORD` from `.env`. If the administrator password is empty, Migrator generates a random password only when the user table is empty:

```bash
docker compose -f docker-compose.app.yml logs platform-migrate
```

Changing `.env` later does not reset an existing account.

This deployment starts Ingot's own Web, API, database, optimizer, and background services without external business-system accounts or connections. Device and enterprise-system connectors are configured and enabled as needed. After sign-in, users manage process variables and recipe versions through process configuration. Each process run is an experiment; run and quality records jointly retain experiment facts. See [Current status](status.en.md) for capability boundaries and recommendation-data requirements.

## Common startup problems

If the page is unavailable, inspect status and recent logs first:

```bash
docker compose -f docker-compose.app.yml ps -a
docker compose -f docker-compose.app.yml logs --tail=200
```

`unexpected EOF`, `short read`, or pull timeouts usually indicate an interrupted image download. Running `up -d --build` again reuses completed layers. Do not delete data volumes as a first troubleshooting step. See [Deployment](deployment.en.md#start-and-stop) for more diagnostics.

## Verify the first session

1. Sign in and open process configuration. Confirm that you can access process variables and recipe versions.
2. Check `http://localhost:8002/health` for the Worker and retain the migration result and service status for troubleshooting.
3. Continue with the pilot guide using your own authorized data. A healthy deployment does not create production runs, inspection results, or qualified optimization observations.

All application ports in the reference Compose bind to `127.0.0.1`. Remote users need a configured ingress with TLS and authentication; changing a URL alone does not expose the service.

## Next steps

- To connect a set of real or representative recipe runs, continue with the [Recipe-optimization pilot guide](pilot.en.md).
- To understand identity, points, and mappings, read [Data integration](data-connection.en.md).
- To see which capabilities are actually validated, read [Current status](status.en.md).
- For long-running deployment, select basic R&D, field-connected, or high-availability requirements in [Production architecture](production-architecture.en.md) and complete the corresponding acceptance checks.
