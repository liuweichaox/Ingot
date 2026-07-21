# Configuration

Platform uses environment variables and protected configuration storage. Never commit passwords, tokens, or model keys to the repository.

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
  "Cors": {
    "AllowedOrigins": ["https://platform.example.com"]
  },
  "Chat": {
    "Enabled": true,
    "Provider": "OpenAI",
    "FastModel": "<fast-model>",
    "ReasoningModel": "<reasoning-model>",
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
    "Users": {
      "operator": { "AllowAll": true, "EdgeIds": [] }
    }
  }
}
```

Chat registers only `check_data_quality` and `get_cycle_trace`. Every platform-authenticated user needs an explicit record-data scope. Chat has no separate username or password; production establishes `HttpContext.User` through platform authentication middleware. The production Compose example gives `operator` `AllowAll=true`; restricted deployments can list explicit `EdgeIds` for a user in protected configuration.

## Environment variables

```bash
ConnectionStrings__Events='Host=postgres;Database=ingot;Username=ingot;Password=<secret>'
EventIngest__RequireToken=true
EventIngest__EdgeTokens__EDGE-001='<secret>'
Cors__AllowedOrigins__0='https://platform.example.com'
Chat__Enabled=true
Chat__Provider=OpenAI
Chat__FastModel='<fast-model>'
Chat__ReasoningModel='<reasoning-model>'
OPENAI_API_KEY='<secret>'
ChatDataAccess__Users__operator__AllowAll=true
```

With Docker Compose, use these production variable names:

```bash
INGOT_CHAT_ENABLED=true
INGOT_CHAT_PROVIDER=OpenAI
INGOT_CHAT_FAST_MODEL='<fast-model>'
INGOT_CHAT_REASONING_MODEL='<reasoning-model>'
OPENAI_API_KEY='<secret>'
INGOT_CHAT_OPERATOR_ALLOW_ALL=true
```

The production validator requires an event-ingestion token, a CORS origin, `OpenAI` as the Chat provider, both Fast and Reasoning models, `OPENAI_API_KEY`, and at least one platform-user data scope. Inspection submission, visual review, and original-image access use the platform identity and roles directly; there is no inspection-specific username or access token. Model keys are read only from a secret store or environment variables. Logs do not record complete questions, answers, or sensitive tool parameters by default.

## Runtime limits

- `Chat:MaxToolCalls`: maximum read-only tool calls per conversation;
- `Chat:MaxRunSeconds`: maximum duration of one conversation;
- event batches: 1–500 events each;
- event source: must start with `edge/{edgeId}/`;
- when Chat is disabled or its authentication is not configured, Platform event, query, and inspection paths remain available.

See [deployment](tutorial-deployment.en.md) and the [production event specification](rfc-production-events.en.md).
