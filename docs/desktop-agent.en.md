# Ingot Agent desktop

Ingot Agent is a downloadable desktop application used only for connector code generation. It writes real source from a structured source specification, executes governed build and fixture tests in a network-disabled environment, repairs code from bounded errors, and produces a downloadable SHA-256 ZIP after operator approval. Agent never connects to a live data source.

Ingot Agent does not answer production-fact questions. Central Web [Chat](chat.en.md) owns fact queries and problem finding.

## Download and technology

- Download: [latest GitHub Release](https://github.com/liuweichaox/Ingot/releases/latest)
- Desktop framework: Tauri 2
- Native boundary: Rust
- UI: React 19, TypeScript, and Vite
- Server-side model runtime: Microsoft Agent Framework and the OpenAI Responses API in Central
- Governed build: platform-fixed Docker build and test entries

Local development:

```bash
cd desktop
npm install
npm run desktop:dev
```

Release build:

```bash
cd desktop
npm run icons
npm run desktop:build
```

## Inputs

Provide these fields before starting a task:

- source type and protocol;
- endpoint and authentication-method description;
- input fields, types, units, and samples;
- sampling or trigger policy;
- expected `ProductionEvent` types, subjects, context, and correlation rules;
- build and test acceptance criteria.

Production credentials do not belong in prompts, source, or packages. The external runtime supplies them during deployment.

## Workflow

```text
structured connector requirement
  → create desktop Agent run
  → Actor-isolated source workspace
  → generate connector source and tests
  → fixed container build
  → fixed container tests
  → repair from bounded error output
  → operator source and result review
  → authorized Actor packaging approval
  → generate and verify SHA-256 ZIP
```

The desktop receives run events over SSE, resumes from the last event sequence after disconnect, and uses snapshot polling to recover state. Source browsing is read-only; the model can change files only through governed workspace tools.

## Agent API boundary

The desktop uses `/api/v1/agent/*` with these headers:

```text
X-Ingot-Client: ingot-agent-desktop
X-Ingot-Actor: <actor>
Authorization: Bearer <actor-token>
```

The Agent API supports only `standard` connector-code-generation runs. Create, history, read, SSE, and cancellation are isolated from Chat. A Chat token or Chat run cannot enter the Agent workflow.

Primary endpoints:

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/agent/capabilities` | Verify the desktop client, code workflow, and run limits |
| `POST /api/v1/agent/runs` | Create a connector code-generation run |
| `GET /api/v1/agent/runs` | List Agent history for the current Actor |
| `GET /api/v1/agent/runs/{runId}` | Read run, artifact, and workspace state |
| `GET /api/v1/agent/runs/{runId}/stream` | Subscribe to and resume SSE events |
| `POST /api/v1/agent/runs/{runId}:cancel` | Cancel a run |
| `GET /api/v1/connector-workspaces/{id}` | Read source and build/test status |
| `GET /api/v1/connector-workspaces/{id}/files` | List reviewable source files |
| `GET /api/v1/connector-workspaces/{id}/file?path=` | Read a workspace text file |
| `POST /api/v1/connector-workspaces/{id}:approve-package` | Approve packaging as an authorized Actor |
| `POST /api/v1/connector-workspaces/{id}:package` | Generate a SHA-256 ZIP |
| `GET /api/v1/connector-workspaces/{id}/package` | Download the ZIP and verify its SHA-256 `ETag` |

## Connector runtime contract

The default .NET template declares this contract in `connector.manifest.json`:

```text
input:  stdin/json-lines
output: stdout/production-event-json-lines
```

The connector reads source JSON one line at a time and emits normalized ProductionEvent JSON lines. The package contains no Connector Host token, HTTP submission client, retry queue, or production deployment configuration. The external deployment runtime must:

1. provide source JSONL on stdin;
2. read and validate ProductionEvent JSONL from stdout;
3. form `ProductionEvent[]` batches;
4. call `POST /api/v1/connector-events` with a separate Connector Token;
5. own process supervision, credentials, retries, logging, and upgrades.

## Security boundary

- The desktop is the only Agent user surface; Central Web exposes no code-generation interface;
- Agent handles only connector specification, source, build, test, repair, and packaging;
- Agent and Builder do not connect to data sources; tests use only fixed workspace fixtures and simulated input;
- the model cannot submit arbitrary shell, select a container image, change the working directory, or access arbitrary host paths;
- workspaces are Actor-isolated and reject absolute paths, traversal, internal metadata, and oversized content;
- fixed build/test children are network-disabled and use a read-only root filesystem, resource limits, dropped capabilities, and `no-new-privileges`;
- passing tests enter `awaiting-package-approval`; packaging requires an explicit authorized-Actor approval;
- download verifies the server SHA-256 and refuses mismatched content;
- Agent does not deploy connectors, modify production facts, write inspection results, or control equipment.

See [deployment](tutorial-deployment.en.md) for the build environment and [development](tutorial-development.en.md) for connector-template extension.
