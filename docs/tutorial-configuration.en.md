# Configuration

Configuration is separated across Chat, Ingot Agent, Connector Builder, Connector Host, and central fact services. The desktop stores only Central URL, Actor, and token. Models, workspaces, and Builder run in the Central deployment.

## Chat

```json
{
  "Chat": {
    "Enabled": false,
    "Provider": "Deterministic",
    "FastModel": "deterministic-v1",
    "ReasoningModel": "deterministic-v1",
    "MaxToolCalls": 8,
    "MaxRunSeconds": 60,
    "RequireToken": true,
    "ActorTokens": {},
    "ModelPricing": {}
  },
  "ChatDataAccess": {
    "Actors": {
      "operator": { "AllowAll": false, "EdgeIds": ["EDGE-01"] }
    }
  }
}
```

Chat registers only `check_data_quality` and `get_cycle_trace`. Every Actor needs an explicit fact-data scope. Prefer specific `EdgeIds` in production; reserve `AllowAll=true` for trusted global roles.

## Ingot Agent

```json
{
  "Agent": {
    "Enabled": false,
    "DatabasePath": "Data/agent.db",
    "Provider": "Deterministic",
    "FastModel": "deterministic-v1",
    "ReasoningModel": "deterministic-v1",
    "MaxToolCalls": 24,
    "MaxIterations": 8,
    "MaxRunSeconds": 300,
    "RequireToken": true,
    "ActorTokens": {},
    "PackagingApprovers": ["operator"],
    "ModelPricing": {}
  }
}
```

Agent accepts only `standard` connector-code-generation runs from Ingot Agent Desktop. `PackagingApprovers` must reference configured Actors; only those Actors can open the manual packaging gate.

When Chat or Agent is enabled in production, use the `OpenAI` provider, configured model names, and `OPENAI_API_KEY`. `Deterministic` is for development and automated tests only. Supply Actor tokens, model keys, and connector keys through environment variables or a secret store.

Usage comes from provider input/output token fields. `estimatedCost` is calculated only when every called model has `ModelPricing` in one currency; otherwise it is `null`.

## Connector Builder

```json
{
  "ConnectorBuilder": {
    "WorkspaceRoot": "Data/connector-workspaces",
    "ArtifactRoot": "Data/connector-packages",
    "ContainerCommand": "docker",
    "ContainerWorkspaceVolume": "",
    "DotnetSdkImage": "mcr.microsoft.com/dotnet/sdk:10.0",
    "CommandTimeoutSeconds": 120,
    "MaxFileBytes": 524288,
    "MaxWorkspaceFiles": 256,
    "MaxWorkspaceBytes": 8388608,
    "MaxOutputCharacters": 32000
  }
}
```

Builder executes platform-fixed build and test entries only in network-disabled child containers. Test input comes only from fixed workspace fixtures and simulated data. Agent and Builder do not connect to data sources. The model cannot select commands, images, host paths, or working directories. Workspaces are Actor-isolated and limited to 512 KiB per file, 256 visible files, and 8 MiB of source.

## Connector Host

```json
{
  "ConnectorHost": { "MaxBatchSize": 1000 },
  "Context": { "DatabasePath": "Data/context.db" },
  "Events": {
    "DatabasePath": "Data/events.db",
    "MaxBacklogRows": 500000
  }
}
```

Connector Host accepts normalized `ProductionEvent[]`. At the outbox cap it drops the oldest unshipped records and emits `diagnostic.backlog_dropped` plus a drop metric.

## Environment variables

.NET hierarchical configuration uses double underscores:

```text
Chat__Enabled=true
Chat__Provider=OpenAI
Chat__FastModel=<model>
Chat__ReasoningModel=<model>
Chat__ActorTokens__operator=<secret-store-reference>
Agent__Enabled=true
Agent__Provider=OpenAI
Agent__FastModel=<model>
Agent__ReasoningModel=<model>
Agent__ActorTokens__operator=<secret-store-reference>
Agent__PackagingApprovers__0=operator
ChatDataAccess__Actors__operator__AllowAll=false
ChatDataAccess__Actors__operator__EdgeIds__0=EDGE-01
OPENAI_API_KEY=<secret-store-reference>
ConnectorBuilder__DotnetSdkImage=mcr.microsoft.com/dotnet/sdk:10.0
ConnectorHost__IngestToken=<secret-store-reference>
Edge__EventIngestToken=<secret-store-reference>
ConnectionStrings__Events=<secret-store-reference>
```

Production also requires CORS, database, event-ingress, and inspection-submission credentials. See [deployment](tutorial-deployment.en.md).
