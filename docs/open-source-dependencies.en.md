# Open-source dependencies

> Status: **rolling dependency overview**. Exact versions, transitive dependencies, and licenses are determined by project files, lockfiles, image manifests, and automated audit results.

Dependency selection is based on the engineering problem, license compatibility, maintainability, and local-deployment requirements. Popularity does not justify fixing a technology as an irreplaceable product boundary.

| Capability | Main components | Typical licenses |
|---|---|---|
| .NET platform and services | .NET, ASP.NET Core, Npgsql, SQLitePCLRaw | MIT / PostgreSQL |
| Field protocols and acquisition | MQTTnet, OPC Foundation UA .NET Standard, NModbus | MIT |
| Numerical computation and optimization | Python, PyTorch, GPyTorch, BoTorch, NumPy, SciPy | PSF / BSD / Apache-2.0 |
| Product frontend | React, Vite, Headless UI, Plotly.js, oidc-client-ts | MIT / Apache-2.0 |
| Website and documentation | Next.js, remark, rehype, Tailwind CSS | MIT |
| Data import | ClosedXML, PdfPig, MatFileHandler | MIT / Apache-2.0 |
| Data, time-series, and knowledge-retrieval storage | PostgreSQL, TimescaleDB, pgvector (with PostgreSQL's `pg_trgm` extension) | PostgreSQL / Apache-2.0 |

## Introduction requirements

Every new runtime dependency must:

- directly improve data trust, engineering judgment, recipe-decision quality, or system reliability;
- have a project-compatible open-source license;
- use a pinned version or controlled range;
- enter build, vulnerability, license, and supply-chain audits;
- preserve required license notices in images and releases;
- run locally in the factory or have a local replacement that keeps the core loop intact;
- avoid making a proprietary cloud service mandatory for acquisition, records, inspections, or numerical analysis.

## Change and audit

- Review lockfile changes with the code that uses the dependency.
- Run full verification and relevant historical replay before a major-version upgrade.
- Remove dependencies that are no longer used.
- Resolve license or maintenance-status changes before release.
- Publish a generated SBOM or dependency inventory rather than treating this page as the release manifest.

## Version sources

To reproduce a release, read dependency files at the corresponding commit rather than inferring exact versions from component names on this page:

- .NET: project `.csproj` files and `Directory.Build.props`.
- JavaScript: each app's `package.json` and `package-lock.json`; use `npm ci` for locked installation.
- Python: `optimizer/pyproject.toml` and `optimizer/uv.lock`; use the `--locked` commands in the contribution guide.
- Containers: service Dockerfiles, Compose definitions, and the image identities actually deployed.

Typical licenses here are navigation aids, not a substitute for checking each version, subcomponent, and distribution method. In particular, check the actual license of database-extension features and build artifacts. Validate dependency changes through [Contributing](../CONTRIBUTING.en.md) and report vulnerabilities privately through the [Security policy](../SECURITY.md).
