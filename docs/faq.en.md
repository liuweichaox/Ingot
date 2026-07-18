# FAQ

## What is the difference between Chat and Ingot Agent?

Chat is the read-only conversation in Central Web for production-fact queries, data-quality checks, and cycle problem finding. Ingot Agent is a downloadable desktop application used only for connector code generation, build, test, repair, and packaging.

## Does Chat generate code?

No. Chat calls only `check_data_quality` and `get_cycle_trace`. It cannot write files, execute SQL or shell, or modify production facts.

## Why is there no Agent entry in Central Web?

Agent requires a local desktop security boundary, a fixed client identity, and a code-engineering workspace. Central Web exposes only Chat and production facts. Download Agent from [GitHub Releases](https://github.com/liuweichaox/Ingot/releases/latest).

## Where do models and builds run for Ingot Agent?

The desktop configures Central URL, Actor, and token. Models, workspaces, fixed container build/test, and artifact storage run on the Central server. The desktop accesses them through a Tauri Rust native boundary.

## How is a data source onboarded?

In Ingot Agent Desktop, provide protocol, endpoint description, input contract, samples, sampling policy, target events, and acceptance criteria. Agent generates source and runs fixed build/test entries. After tests pass, an authorized Actor approves packaging and downloads the SHA-256 ZIP.

## How does the default connector run?

The default template reads source JSONL from stdin and writes ProductionEvent JSONL to stdout. The external deployment runtime owns the source connection, batching, Connector Token, submission to Connector Host, retries, and process supervision.

## Does Ingot deploy or start generated connectors?

No. Ingot provides source, build/test results, operator approval, and a ZIP. Deployment, startup, scheduling, production credentials, and rollback belong to the external runtime.

## What does Connector Host accept?

Bearer-authenticated `ProductionEvent[]`. The host validates events, assigns local sequence numbers, commits to SQLite, and ships to Central API through an outbox.

## Do Chat and Agent share history?

No. Create, list, read, SSE, cancellation, and history are isolated by product surface and Actor. Agent endpoints also require the fixed desktop-client identifier.

## What happens when Chat has insufficient data?

Chat returns limitations or refuses a definitive conclusion. Answer numbers must come from tool results, and key findings require resolvable evidence.

## Can Ingot control equipment?

No. Neither Chat nor Agent has PLC, CNC, robot, or other equipment-control tools.
