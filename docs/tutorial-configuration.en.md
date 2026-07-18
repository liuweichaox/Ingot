# Configuration

Central uses environment variables and protected configuration storage. Never commit passwords, tokens, or model keys to the repository.

## Required configuration

```json
{
  "Urls": "http://0.0.0.0:8000",
  "ConnectionStrings": {
    "Events": "Host=postgres;Database=ingot;Username=ingot;Password=<secret>"
  },
  "EventIngest": {
    "RequireToken": true,
    "EdgeTokens": {
      "EDGE-001": "<secret>"
    }
  },
  "InspectionSubmission": {
    "RequireToken": true,
    "ActorTokens": {
      "OPERATOR-001": "<strong-secret>"
    }
  },
  "Cors": {
    "AllowedOrigins": ["https://central.example.com"]
  },
  "Chat": {
    "Enabled": true,
    "RequireToken": true,
    "Provider": "OpenAI",
    "FastModel": "<fast-model>",
    "ReasoningModel": "<reasoning-model>",
    "ActorTokens": {
      "operator": "<strong-secret>"
    },
    "MaxToolCalls": 8,
    "MaxRunSeconds": 60
  }
}
```

Every `EventIngest:EdgeTokens` key must match the batch request `edgeId`. Give every source-adaptation runtime an independent, rotatable token.

## Chat data scope and production enablement

```json
{
  "ChatDataAccess": {
    "Actors": {
      "operator": { "AllowAll": true, "EdgeIds": [] }
    }
  }
}
```

Chat registers only `check_data_quality` and `get_cycle_trace`. Every Actor needs an explicit fact-data scope. The production Compose example gives `operator` `AllowAll=true`; restricted deployments can list explicit `EdgeIds` for an Actor in protected configuration.

## Environment variables

```bash
ConnectionStrings__Events='Host=postgres;Database=ingot;Username=ingot;Password=<secret>'
EventIngest__RequireToken=true
EventIngest__EdgeTokens__EDGE-001='<secret>'
InspectionSubmission__RequireToken=true
InspectionSubmission__ActorTokens__OPERATOR-001='<strong-secret>'
Cors__AllowedOrigins__0='https://central.example.com'
Chat__Enabled=true
Chat__RequireToken=true
Chat__Provider=OpenAI
Chat__FastModel='<fast-model>'
Chat__ReasoningModel='<reasoning-model>'
OPENAI_API_KEY='<secret>'
Chat__ActorTokens__operator='<strong-secret>'
ChatDataAccess__Actors__operator__AllowAll=true
```

With Docker Compose, use these production variable names:

```bash
INGOT_CHAT_ENABLED=true
INGOT_CHAT_PROVIDER=OpenAI
INGOT_CHAT_FAST_MODEL='<fast-model>'
INGOT_CHAT_REASONING_MODEL='<reasoning-model>'
OPENAI_API_KEY='<secret>'
INGOT_CHAT_OPERATOR_TOKEN='<strong-secret>'
INGOT_CHAT_OPERATOR_ALLOW_ALL=true
```

The production validator requires event- and inspection-submission tokens, a CORS origin, `OpenAI` as the Chat provider, both Fast and Reasoning models, `OPENAI_API_KEY`, a Chat Actor token, and a data scope for every Actor. Model keys are read only from a secret store or environment variables. Logs do not record complete questions, answers, or sensitive tool parameters by default.

## Runtime limits

- `Chat:MaxToolCalls`: maximum read-only tool calls per conversation;
- `Chat:MaxRunSeconds`: maximum duration of one conversation;
- event batches: 1–500 events each;
- event source: must start with `edge/{edgeId}/`;
- when Chat is disabled or its authentication is not configured, Central event, query, and inspection paths remain available.

See [deployment](tutorial-deployment.en.md) and the [production event specification](rfc-production-events.en.md).
