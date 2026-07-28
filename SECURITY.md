# Security Policy / 安全策略

## Private reporting / 私密报告

请通过 GitHub [Private Vulnerability Reporting](https://github.com/liuweichaox/Ingot/security/advisories/new) 报告漏洞，不要创建公开 Issue。

Report vulnerabilities through GitHub [Private Vulnerability Reporting](https://github.com/liuweichaox/Ingot/security/advisories/new), not a public Issue.

报告中请包含受影响版本、前置条件、最小复现、影响和建议缓解。不要附带真实工厂凭据、设备地址、生产数据或个人信息。

Include affected revision, prerequisites, minimal reproduction, impact, and mitigation. Do not include real factory credentials, equipment addresses, production data, or personal information.

## Supported version / 支持版本

安全修复针对 `main` 当前版本。正式版本发布后，本节将列出仍受支持的版本范围。

Security fixes currently target the latest `main`. Supported release ranges will be listed after stable releases begin.

## Deployment baseline / 部署基线

即使部署在工厂内部网络，也应：

- 更换 `.env.example` 中的全部示例凭据；
- 不向非必要网段暴露 PostgreSQL、Optimizer 或 Connector；
- 为每个 Edge 使用独立、可轮换的上送令牌；
- 备份数据库、检验附件和 Edge 待上送日志；
- 不在日志、Issue 或导出文件中保存密钥；
- 在真实实验执行前保留工程师审核。

Even inside a factory network:

- replace every sample secret;
- do not expose PostgreSQL, Optimizer, or Connector beyond required networks;
- use separate, rotatable Edge ingestion tokens;
- back up database, attachments, and unshipped Edge logs;
- keep secrets out of logs, Issues, and exports;
- retain engineering review before real experiment execution.

## Scope

Examples of security-sensitive issues include authentication bypass, cross-project data exposure, unsafe file handling, SSRF, secret leakage, forged Edge ingestion, experiment-tampering, and any path that could cause unreviewed equipment control.
