# Open-source Dependencies

Ingot uses open-source components for equipment connectivity, business workflows, numerical optimization, and public sites. Project files and lockfiles are authoritative for exact versions.

| Area | Primary components | Licenses |
|---|---|---|
| Numerical optimization | PyTorch, GPyTorch, BoTorch, NumPy, SciPy | BSD / Apache-2.0 |
| Equipment acquisition | MQTTnet, OPC Foundation UA .NET Standard, NModbus | MIT |
| Platform | .NET / ASP.NET Core, Npgsql, SQLitePCLRaw | MIT |
| Frontend | React, Vite, Headless UI, Plotly.js | MIT |
| Website and docs | Next.js, remark/rehype, Tailwind CSS | MIT |
| Data import | ClosedXML, PdfPig, MatFileHandler | MIT / Apache-2.0 |
| Database and time series | PostgreSQL, TimescaleDB | PostgreSQL / Apache-2.0 |

New runtime dependencies must:

- use an acceptable open-source license;
- pin or constrain versions;
- enter dependency audit and build validation;
- preserve license obligations in images or releases;
- avoid making a proprietary cloud service mandatory for the core loop.
