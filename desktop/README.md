# Ingot Agent Desktop

Ingot Agent is the downloadable desktop application for governed production-data connector code generation. It submits a structured connector requirement to Central, streams the bounded generation workflow, and exposes generated source, fixed build/test results, human packaging approval, and SHA-256-verified ZIP download.

It is not the Central Web analysis interface and does not provide production-data question answering, event search, dashboards, or device control.

## Capabilities

- Configure the Central API address, Actor, and bearer token.
- Verify that Central exposes the connector workspace workflow.
- Create a standard Agent run from a complete connector requirement.
- Review and reopen the current Actor's recent code-generation runs.
- Stream typed run and tool events with polling recovery.
- Inspect run-scoped specification and package artifacts.
- Browse generated workspace source in read-only mode.
- Review fixed container build and test output.
- Record explicit packaging approval with an authorized Actor.
- Create and download a content-addressed ZIP.
- Verify the complete ZIP against the server-provided SHA-256 before writing it to disk.

The native network layer permits only the required Agent, artifact, and connector-workspace routes and identifies every request with `X-Ingot-Client: ingot-agent-desktop`. It cannot call event mutation, configuration, inspection, or device-control APIs. The access token remains in process memory and is never written to browser storage.

## Requirements

- Node.js 22.13 or newer
- Rust 1.77.2 or newer
- Tauri 2 platform prerequisites for the target operating system
- An Ingot Central deployment with `Agent:Enabled=true`, connector workspace tools enabled, an Actor token, and a packaging approver

## Development

```bash
npm install
npm run lint
npm test
npm run build
npm run desktop:dev
```

The browser-only Vite preview cannot execute native networking or package download commands; use `npm run desktop:dev` for an end-to-end workflow.

## Release build

Generate native icon assets after changing the canonical SVG, then build the signed platform package:

```bash
npm run icons
npm run desktop:build
```

Release signing, notarization, and update publication are supplied through the deployment environment. Credentials must not be stored in this directory or in Tauri configuration.

## Project structure

```text
desktop/
├── src/                         React workflow UI
│   └── lib/native.ts            typed Tauri command boundary
├── src-tauri/
│   ├── src/lib.rs               API allowlist, SSE, SHA-256 download
│   ├── capabilities/            minimum window permissions
│   └── icons/source-icon.svg    canonical application icon
├── package.json                 frontend and desktop scripts
└── vite.config.ts               build and test configuration
```

## Security boundary

- No arbitrary URL path, HTTP method, shell command, filesystem read, or script execution is exposed to the webview.
- API credentials are attached only by the Rust network boundary and never included in emitted events.
- Source browsing uses Central's Actor-scoped read-only workspace endpoints.
- Build and test commands are selected by Central; the desktop application cannot submit commands.
- Package approval and package creation remain separate authenticated actions.
- ZIP bytes are limited to 256 MiB, verified before writing, and persisted by an atomic same-directory temporary-file rename.
- Connector deployment and production network access remain external operator-controlled steps.
