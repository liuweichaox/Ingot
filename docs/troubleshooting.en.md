# Troubleshooting

This page is for local installation and deployment operators. Identify the failing service before changing configuration or data. See [Getting started](getting-started.en.md) for installation and [Deployment](deployment.en.md) for recovery and upgrades.

## Collect diagnostics

Run these read-only commands from the repository root:

```bash
git rev-parse --short HEAD
docker compose version
docker compose -f docker-compose.app.yml ps -a
docker compose -f docker-compose.app.yml logs --tail=200 platform-migrate platform-api platform-worker optimizer platform-web
```

Record the failure time, URL, steps, response status, and service name. Logs may contain business content or a first administrator password; remove credentials, tokens, user and field identities before sharing. Do not publish full `docker compose config` output or `.env`.

## Compose configuration fails validation

**Symptom**: `config --quiet` or startup reports an unset variable.

**Action**: check required values against the current `.env.example`, especially the database password, site identity, Edge identity, and associated tokens. The default Compose API needs these bindings even without a field connector. Replace placeholders and validate again:

```bash
docker compose -f docker-compose.app.yml config --quiet
```

**Success condition**: the command exits successfully. This validates Compose configuration, not password strength, connectivity, or data integrity.

## Containers remain unhealthy

**Symptom**: a service is `unhealthy`, restarts repeatedly, or the page cannot connect.

1. Identify the service with `ps -a`. Check `postgres`, then `platform-migrate`; migration must exit successfully before API and Worker can start normally.
2. Inspect recent logs of the failing service, for example:

```bash
docker compose -f docker-compose.app.yml logs --tail=200 platform-migrate
docker compose -f docker-compose.app.yml logs --tail=200 platform-api
```

3. For image-download `unexpected EOF`, `short read`, or timeouts, check Docker network and image access, then retry `up -d --build`.
4. For database authentication failures, compare configuration with the identity used to initialize the existing volume. Changing environment variables does not change an existing database user's password.
5. For migration errors, retain logs and backups and follow the specific migration's old-data requirements. Do not edit historical migrations or delete volumes to bypass failure.

**Success condition**: migration exits with code 0, core long-running services pass health checks, and repeated restarts stop. See [Getting started](getting-started.en.md) for the full service checklist.

## Login or authorization fails

**Symptom**: login fails, an existing password does not match, or business requests return 401/403.

- First login uses the administrator identity configured at installation. Bootstrap runs only when the user table is empty; editing `.env` does not reset existing accounts.
- For 401, check the session, authentication mode, and OIDC issuer/audience. See [Deployment](deployment.en.md) for callbacks and allowed origins.
- For 403, check role and site authorization. Non-administrators need the corresponding site scope; some run-detail queries also require an explicit `siteId`.
- Do not enable development identity or weaken site filtering to repair production authorization.

**Success condition**: the identity can perform authorized site and role operations while unauthorized access remains rejected.

## Acquisition does not produce a complete run

**Symptom**: the node is online but expected runs, curves, or optimization observations are absent.

1. Check consistent `SiteId`, `EdgeId`, tokens, and equipment identity.
2. Check the applied published acquisition configuration and point, process-variable, unit, and timestamp mappings.
3. Distinguish event receipt, run-boundary assembly, process analysis, and quality review. Event acceptance does not establish completion of later stages.
4. Check Worker health and background-job errors, and whether identity, boundaries, required fields, or quality data excluded the run.

**Success condition**: one run identity traces actual parameters, curves, context, and valid quality outcomes. Follow [Data integration](data-connection.en.md); do not guess missing associations after the fact.

## Next-run correction cannot be generated

**Symptom**: data checks find no valid samples or recommendation generation stops.

At least three valid runs and two distinct actual recipes are minimum requirements. Also check the published recipe's adjustable variables, units, steps, and bounds; numeric quality characteristics covering the recipe; process analysis; actual parameters; and quality outcomes with required review. Insufficient coverage, conflicting constraints, and unmet method conditions also stop recommendations.

If a correction is not yet frozen, complete its decision and outcome first. Repair the evidence chain or collect more real runs when data is insufficient. Do not change admission thresholds to force a recommendation.

**Success condition**: data checks pass; the recommendation retains inputs and evidence scope and stays inside safety boundaries and observed coverage. See the [Pilot guide](pilot.en.md) and [Analysis and optimization](optimization.en.md).

## Assistant or semantic retrieval is unavailable

**Symptom**: natural-language answers fail or semantic retrieval does not return expected fragments.

Check Worker status, model-service configuration, and protocol. Knowledge retrieval additionally requires reviewed sources and fragments within the authorized site and applicability scope. Semantic embeddings are disabled by default; see [Deployment](deployment.en.md) for configuration and provider interface requirements.

Embedding failures fall back to keyword retrieval. If no fragment passes authorization, review, and scope gates, insufficient evidence is the correct result. Run and quality facts remain managed through their original business paths during model-service outages.

## Report a problem

Include version, redacted logs, reproduction steps, expected and actual results, and checks already attempted in an [Issue](https://github.com/liuweichaox/Ingot/issues). Do not attach real production data. Use the [private reporting channel](../SECURITY.md) for vulnerabilities.
