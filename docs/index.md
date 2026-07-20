# Ingot 文档

Ingot 是制造数据采集与工艺分析平台：它将不同来源的重要数据整理成统一格式的生产记录，并在 Platform Web 中提供 **Ingot Chat** 作为工程师的主要入口。使用方自行实现数据接入，将 `ProductionEvent` 或 `InspectionRecord` 提交到公开 API；Ingot 不内置现场设备协议。

## 开始使用

1. [快速开始](tutorial-getting-started.md)
2. [生产记录接入](rfc-production-events.md)
3. [Ingot Chat](chat.md)
4. [部署](tutorial-deployment.md)
5. [配置](tutorial-configuration.md)

## Ingot Chat

- [Ingot Chat](chat.md)：日常记录问答，以及工艺、质量和复核角色参与的有界综合分析；所有结果都回链到相关记录。

## 生产记录接入

- [生产事件规范](rfc-production-events.md)：`ProductionEvent` 批次、认证、去重、查询和扩展规则。
- [快速开始](tutorial-getting-started.md)：从启动 Platform 到提交第一批事件。

## 部署运维

- [部署](tutorial-deployment.md)
- [配置](tutorial-configuration.md)
- [常见问题](faq.md)

## 架构开发

- [宏观架构](architecture.md)
- [设计](design.md)
- [模块](modules.md)
- [开发指南](tutorial-development.md)
- [贡献指南](../CONTRIBUTING.md)
- [产品用语](product-language.md)

## 参考资料

- [生产事件规范](rfc-production-events.md)
- [品牌与标识](brand.md)
- [安全策略](../SECURITY.md)
