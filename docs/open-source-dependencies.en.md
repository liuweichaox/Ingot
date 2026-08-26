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
| Data and time-series storage | PostgreSQL, TimescaleDB | PostgreSQL / Apache-2.0 |

## Introduction requirements

Every new runtime dependency must:

- directly improve data trust, engineering judgment, experiment efficiency, or system reliability;
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
