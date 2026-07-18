# 开发指南

## 本地验证

```bash
./scripts/verify.sh
```

该门禁会构建 .NET 项目、运行测试、构建 Central Web、官网和文档站，并检查格式、架构与产品范围。

## 开发原则

- 先更新契约，再更新 Central API、事实服务、Web 和文档；
- 事件接入只接受强类型 `ProductionEvent` 与 `InspectionRecord`，不让调用方传递任意 SQL 或脚本；
- Chat 的统计、权限、工具执行与证据验证必须保持确定性；
- 任何新工具都默认只读，并需通过数据范围、结果大小和超时限制；
- 公共文字使用 Ingot Chat，不暴露内部实现术语；
- 新增或改变公开接口时，同步中英文文档、官网和静态文档站测试。

## 事件契约变更

1. 评估事件类型、版本和字段是否保持向后兼容；
2. 更新 `Ingot.Contracts` 中的强类型契约和验证；
3. 追加对应的 API、存储和集成测试；
4. 在[生产事件规范](rfc-production-events.md)中记录语义；
5. 使用脱敏样本验证 Chat 仍能给出正确限制与证据。

## 文档与官网

`docs/` 是中英文内容源。`docs-site/` 在构建时读取 Markdown 并生成导航、搜索索引和静态页面。`site/` 是官网。修改后至少执行：

```bash
npm --prefix docs-site run build
npm --prefix docs-site test
npm --prefix site run build
npm --prefix site test
```

参见[架构](architecture.md)、[设计](design.md)和[贡献指南](../CONTRIBUTING.md)。
