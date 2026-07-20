# Security Policy / 安全策略

## 报告安全问题

请通过 GitHub 的[私有漏洞报告](https://github.com/liuweichaox/Ingot/security/advisories/new)提交安全问题，不要创建公开 Issue，也不要在报告中包含真实生产凭据、生产数据或可识别人员的信息。

报告应包含：

- 受影响的组件、版本或提交；
- 前置条件和最小复现步骤；
- 实际影响与可能的攻击路径；
- 已完成的缓解措施；
- 如适用，建议修复方向。

维护者会在 GitHub Security Advisory 中确认报告、协调修复和披露时间。修复发布前请保密。

## 支持范围

安全修复针对 `main` 分支当前版本。部署方负责使用受支持的 .NET、Node.js、PostgreSQL、容器运行时和模型 SDK，并执行 `./scripts/verify.sh` 与依赖审计。

## 核心安全边界

- Chat 不直接访问数据库、宿主文件系统、Shell 或设备控制接口，只能调用白名单中的只读数据工具。
- Chat 的接口、历史和运行按 Actor 隔离。
- 用户接入程序通过标准事件 API 提交生产数据，并独立运行在部署方选择的环境中。
- 生产环境强制 Token、模型、数据库连接和 CORS 配置校验。
- 密钥只通过环境变量或 Secret Store 提供。用户不得把真实密钥写入 Chat 问题、事件上下文或日志。

Central 的查询、边缘注册、诊断代理和 Webhook 管理接口当前没有统一 RBAC。生产部署必须把这些接口置于受信网络或认证网关之后，并对可注册的 Edge 地址和 Webhook 目标实施网络出口策略。

---

## Reporting a vulnerability

Use GitHub [private vulnerability reporting](https://github.com/liuweichaox/Ingot/security/advisories/new). Do not open a public issue, and never include real production credentials, production data, or personally identifiable information.

Include the affected component and revision, prerequisites, minimal reproduction, impact, attack path, mitigations already applied, and a proposed fix when available. Maintainers will acknowledge and coordinate remediation and disclosure through the GitHub Security Advisory. Keep the report confidential until a fix is released.

## Supported scope

Security fixes target the current `main` branch. Deployments must use supported .NET, Node.js, PostgreSQL, container runtime, and model SDK releases and run `./scripts/verify.sh` with dependency audits.

## Core boundaries

- Chat receives no direct database, host-filesystem, shell, or equipment-control access and calls only allowlisted read-only fact tools.
- Chat APIs, histories, and runs are isolated by Actor.
- User adapters submit production facts through the standard event API and run independently in deployment-selected environments.
- Production startup validates tokens, models, database connectivity, and CORS configuration.
- Supply secrets only through environment variables or a secret store. Never place real secrets in Chat questions, event context, or logs.

Central query, Edge registration, diagnostic proxy, and webhook-management endpoints do not currently share a unified RBAC layer. Production deployments must place them behind a trusted network or authenticated gateway and enforce egress policy for registered Edge addresses and webhook targets.
