# Security Policy / 安全策略

## 报告安全问题

请通过 GitHub 的[私有漏洞报告](https://github.com/liuweichaox/Ingot/security/advisories/new)提交安全问题，不要创建公开 Issue，也不要在报告中包含真实生产凭据、生产数据或可识别人员的信息。

报告应包含受影响的组件与版本、前置条件、最小复现、实际影响、可能的攻击路径和已采取的缓解措施。维护者会通过 GitHub Security Advisory 协调修复与披露。

## 支持范围

安全修复针对 `main` 分支当前版本。部署方应使用受支持的 .NET、Node.js、PostgreSQL 和容器运行时版本，并执行仓库的完整验证与依赖审计。

## 核心安全边界

- 用户、租户、研发项目、实验数据、分析运行和知识条目必须按授权范围隔离。
- AI 只能调用经过注册和授权的工具；数据读取、算法执行和结果写入均经过确定性校验。
- AI 不直接访问数据库、宿主文件系统、Shell 或设备控制接口。
- 实验建议、参数发布和工艺窗口确认由有权限的工艺工程师审批。
- Edge 采集节点使用独立、可轮换的机器身份，并只获得完成采集与可靠上送所需的权限。
- 设备连接信息和协议凭据保存在边缘安全配置中，平台只保存受控引用。
- 实验数据、实时过程数据、模型、提示、分析结果和导出文件按敏感业务数据处理。
- 密钥仅通过环境变量或 Secret Store 提供，不写入代码、文档、事件、问题或日志。
- 日志和追踪保留审计所需的标识、版本和状态，同时避免记录完整提示、原始敏感数据和凭据。
- Webhook、设备地址、文件导入和外部模型连接必须限制目标、内容类型、大小、超时和网络出口。

尚未具备统一授权保护的管理接口必须放在受信网络或认证网关之后，直至相应授权边界完成并通过测试。

---

## Reporting a vulnerability

Use GitHub [private vulnerability reporting](https://github.com/liuweichaox/Ingot/security/advisories/new). Do not open a public issue or include real production credentials, production data, or personally identifiable information.

Include the affected component and revision, prerequisites, a minimal reproduction, impact, attack path, and mitigations already applied. Maintainers will coordinate remediation and disclosure through GitHub Security Advisory.

## Supported scope

Security fixes target the current `main` branch. Deployments should use supported .NET, Node.js, PostgreSQL, and container runtime versions and run the repository's complete verification and dependency audits.

## Core boundaries

- Authorization isolates users, tenants, R&D projects, experimental data, analysis runs, and knowledge entries.
- AI calls only registered and authorized tools; deterministic code validates data access, algorithm execution, and result persistence.
- AI has no direct database, host-filesystem, shell, or equipment-control access.
- Authorized process engineers approve experiments, parameter releases, and process-window confirmation.
- Edge acquisition nodes use separate, rotatable machine identities with least-privilege ingestion permissions.
- Device connection details and protocol credentials remain in secure edge configuration; the platform stores controlled references.
- Experimental data, real-time process data, models, prompts, analysis results, and exports are treated as sensitive business data.
- Secrets are supplied through environment variables or a secret store and never stored in code, documentation, events, questions, or logs.
- Logs and traces retain identifiers, versions, and states needed for audit without recording full prompts, raw sensitive data, or credentials.
- Webhooks, device addresses, file imports, and external model connections enforce destination, content-type, size, timeout, and network-egress restrictions.

Management endpoints that do not yet have unified authorization must remain behind a trusted network or authenticated gateway until the authorization boundary is implemented and tested.
