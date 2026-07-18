# Ingot 文档

Ingot 将不同来源的数据保存为标准生产事实，并在 Central Web 中提供 **Ingot Chat** 对话查询。使用方自行实现数据源适配，将 `ProductionEvent` 或 `InspectionRecord` 提交到公开 API；Ingot 不内置现场设备协议。

## 开始使用

1. [快速开始](tutorial-getting-started.md)
2. [事件接入](rfc-production-events.md)
3. [Ingot Chat](chat.md)
4. [部署](tutorial-deployment.md)
5. [配置](tutorial-configuration.md)

## Ingot Chat

- [Ingot Chat](chat.md)：Central Web 中的只读生产事实对话、证据与 HTTP API。

## 事件接入

- [生产事件规范](rfc-production-events.md)：`ProductionEvent` 批次、认证、去重、查询和扩展规则。
- [快速开始](tutorial-getting-started.md)：从启动 Central 到提交第一批事件。

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

## 参考资料

- [生产事件规范](rfc-production-events.md)
- [品牌与标识](brand.md)
- [安全策略](../SECURITY.md)
