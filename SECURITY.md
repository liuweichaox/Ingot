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

- Chat 不直接访问数据库、宿主文件系统、Shell 或设备控制接口，只能调用白名单中的只读事实工具。
- Agent 只接受固定桌面客户端发起的连接器代码生成；源码写入仅能通过 Actor 隔离工作区白名单工具。
- Chat 与 Agent 的接口、历史和运行按产品面及 Actor 隔离；制品写入受类型、Actor、大小、路径和版本控制。
- Connector Builder 只在禁网、只读源码和受限资源的 Docker 子容器中执行固定构建/测试入口；测试通过后仍需操作者批准当前修订，平台不部署或启动连接器。
- Agent 和 Connector Builder 不连接数据源；固定构建/测试子容器始终禁网，测试输入仅来自受控工作区样本。
- 生产环境强制 Token、模型、数据库连接和 CORS 配置校验。
- 密钥只通过环境变量或 Secret Store 提供。用户不得把真实密钥写入问题、连接器规格或其他 Agent 制品；制品内容按提交值保存，当前不提供通用自动脱敏保证。

Central 的查询、边缘注册、诊断代理和 Webhook 管理接口当前没有统一 RBAC。生产部署必须把这些接口置于受信网络或认证网关之后，并对可注册的 Edge 地址和 Webhook 目标实施网络出口策略。

单机 Compose 为 Connector Builder 挂载只读 Docker socket。Docker API 本身是高权限边界，因此 Central 必须运行在专用主机或独立 Docker daemon 上，限制宿主登录与 socket 访问，并预先拉取平台配置的受信 SDK 镜像；正式发布应固定到审核过的 digest，不得把 Docker API 暴露到网络。

---

## Reporting a vulnerability

Use GitHub [private vulnerability reporting](https://github.com/liuweichaox/Ingot/security/advisories/new). Do not open a public issue, and never include real production credentials, production data, or personally identifiable information.

Include the affected component and revision, prerequisites, minimal reproduction, impact, attack path, mitigations already applied, and a proposed fix when available. Maintainers will acknowledge and coordinate remediation and disclosure through the GitHub Security Advisory. Keep the report confidential until a fix is released.

## Supported scope

Security fixes target the current `main` branch. Deployments must use supported .NET, Node.js, PostgreSQL, container runtime, and model SDK releases and run `./scripts/verify.sh` with dependency audits.

## Core boundaries

- Chat receives no direct database, host-filesystem, shell, or equipment-control access and calls only allowlisted read-only fact tools.
- Agent accepts connector code-generation requests only from the fixed desktop client; source writes are limited to allowlisted, Actor-scoped workspace tools.
- Chat and Agent APIs, histories, and runs are isolated by product surface and Actor. Artifact writes are constrained by type, Actor, size, path, and version controls.
- Connector Builder executes only fixed build/test entries inside a network-disabled, resource-constrained Docker child container with read-only source. Successful tests still require operator approval of the current revision, and the platform does not deploy or start connectors.
- Agent and Connector Builder do not connect to data sources. Fixed build/test children are always network-disabled and consume only governed workspace fixtures.
- Production startup validates tokens, models, database connectivity, and CORS configuration.
- Supply secrets only through environment variables or a secret store. Never place real secrets in questions, connector specifications, or other Agent artifacts; artifact content is retained as submitted and is not covered by a general automatic-redaction guarantee.

Central query, Edge registration, diagnostic proxy, and webhook-management endpoints do not currently share a unified RBAC layer. Production deployments must place them behind a trusted network or authenticated gateway and enforce egress policy for registered Edge addresses and webhook targets.

The single-host Compose baseline mounts a read-only Docker socket for Connector Builder. The Docker API is still a privileged boundary: run Central on a dedicated host or isolated Docker daemon, restrict host and socket access, pre-pull the platform-configured trusted SDK image, pin an audited digest for releases, and never expose the Docker API over the network.
