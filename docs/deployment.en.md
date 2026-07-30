# Deployment and Operations

## Recommended topology

```text
field data sources                         factory server
control systems / instruments / vision / inspection / MES
                └─ Edge ConnectorHost ─────→ Platform API (modular monolith)
                                             ├─ PostgreSQL / TimescaleDB
                                             ├─ attachments + process knowledge
                                             ├─ embedded Agent / Chat / data tools
                                             ├─ Optimizer (separate stateless service)
                                             └─ Platform Web (standalone React frontend)
```

Deploy Edge ConnectorHost near equipment. Platform API, database, Optimizer, and Platform Web may share one factory server through Compose or be separated behind the same contracts. `Edge.Application`, `Edge.Infrastructure`, `Platform.Infrastructure`, and Agent are code-level libraries, not separate Compose services.

Website and Docs are not part of the factory runtime. Deploy them separately in the public-site topology defined by `deploy/compose.yml`.

## Configuration

```bash
cp .env.example .env
```

Change at least:

- `INGOT_POSTGRES_PASSWORD`
- `INGOT_EDGE_TOKEN`
- `INGOT_ADMIN_PASSWORD`

The repository ignores `.env`; never commit real credentials.

## Start

```bash
docker compose -f docker-compose.app.yml up -d --build
```

Enable the field connector profile with:

```bash
docker compose -f docker-compose.app.yml --profile connector-host up -d --build
```

Configure addresses or interfaces, tokens, and mappings before connecting a real data source.

## Health

| Service | Check |
|---|---|
| Platform | `/health` |
| Optimizer | `/ready` |
| Connector | `/health` |
| Web | `/health` |
| PostgreSQL | `pg_isready` |

Optimizer `/health` only indicates that its HTTP process is alive; Optimizer `/ready` verifies the PyTorch, GPyTorch, and BoTorch runtime. Platform, Connector, and Web health checks cover their own process and configured dependencies. Platform starts without Optimizer; acquisition and inspection continue while new recommendations remain unavailable.

## Data and backup

Back up at least:

- PostgreSQL volume;
- inspection attachments;
- process-knowledge files;
- Edge event databases until forwarding is confirmed.

A restore drill must verify experiment, cycle, inspection, and evidence relationships—not merely database startup.

## Upgrade

1. back up database and attachments;
2. read `CHANGELOG.md`;
3. run migrations in a test environment;
4. execute `scripts/verify.sh`;
5. update central services;
6. confirm Edge backlog recovery;
7. regress observation assembly and suggestion on a known campaign.

## Minimum internal-network boundaries

Capability first does not remove basic operational boundaries:

- do not expose PostgreSQL, Optimizer, or Connector beyond required networks;
- replace sample secrets;
- use separate Edge ingestion tokens;
- back up and restrict attachment storage;
- require engineering review before experiment execution.
