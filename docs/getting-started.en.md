# Getting started

> Document status: **current operating guide**. This page provides a synthetic-workflow tour and instructions for starting the complete local stack. Requirements for a real pilot are defined in the [Controlled pilot guide](pilot.en.md).

## Choose a path

| Objective | Path | Completion signal |
|---|---|---|
| Evaluate the product workflow | [Five-minute synthetic tour](#five-minute-synthetic-tour) | Complete the nonconforming-run comparison and experiment-decision workflow |
| Run the complete system locally | [Start the complete stack](#start-the-complete-stack) | Web, API, Optimizer, and database are healthy |
| Prepare a real project | [Controlled pilot guide](pilot.en.md) | Produce the first trustworthy run evidence and validation experiment |
| Prepare production | [Production architecture](production-architecture.en.md) → [Deployment](deployment.en.md) | The site independently passes security, recovery, capacity, and observation acceptance |
| Contribute code | [Contributing](https://github.com/liuweichaox/Ingot/blob/main/CONTRIBUTING.en.md) | `./scripts/verify.sh` passes locally |

See [Current status](status.en.md) for capability and validation maturity.

## Five-minute synthetic tour

This path requires Node.js 22.22+ but no database, equipment, or Docker.

Install frontend dependencies on the first run:

```bash
npm --prefix apps/platform ci
```

Start the synthetic business API and frontend in two terminals:

```bash
# Terminal 1
node scripts/platform-demo.mjs
```

```bash
# Terminal 2
npm --prefix apps/platform run demo
```

Open `http://127.0.0.1:3001`:

- `demo / demo`: tour the engineering workflow;
- `admin / admin12345`: inspect system administration and pilot-acceptance entry points.

The workbench guides the user through opening a nonconforming run, reviewing an approved inspection, choosing a conforming baseline, comparing actual conditions and trajectories, and inspecting candidate causes, confounders, and the next validation experiment. All data are synthetic. The demo validates pages and workflow, not real process benefit.

Press `Ctrl+C` in both terminals when finished.

## Start the complete stack

You need Git, Docker Engine or Docker Desktop, and Docker Compose v2. The Compose path does not require .NET, Node.js, Python, or uv on the host.

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
```

Change the database passwords, Edge delivery token, and administrator settings in `.env`. Replace every `change-this-` placeholder. Production uses randomly generated, distinct passwords and tokens.

Validate the configuration, then start:

```bash
docker compose -f docker-compose.app.yml config --quiet
docker compose -f docker-compose.app.yml up -d --build
```

The first build downloads .NET, Node, Python, PyTorch, and TimescaleDB images. After the command finishes, inspect every container:

```bash
docker compose -f docker-compose.app.yml ps -a
```

Confirm at least that:

- `platform-migrate` exited successfully;
- `postgres`, `optimizer`, `platform-api`, and `platform-web` are `healthy`;
- `platform-worker` and `connector-host` remain `healthy`;
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

## Common startup problems

If the page is unavailable, inspect status and recent logs first:

```bash
docker compose -f docker-compose.app.yml ps -a
docker compose -f docker-compose.app.yml logs --tail=200
```

`unexpected EOF`, `short read`, or pull timeouts usually indicate an interrupted image download. Running `up -d --build` again reuses completed layers. Do not delete data volumes as a first troubleshooting step. See [Deployment](deployment.en.md#start-and-stop) for more diagnostics.

## Next steps

- To connect one real or representative run, continue with the [Controlled pilot guide](pilot.en.md).
- To understand identity, points, and mappings, read [Data integration](data-connection.en.md).
- To see which capabilities are actually validated, read [Current status](status.en.md).
- To reproduce public method results, read [Optimizer experiment-efficiency validation](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md).
- To deploy in production, complete the site acceptance defined by [Production architecture](production-architecture.en.md).
