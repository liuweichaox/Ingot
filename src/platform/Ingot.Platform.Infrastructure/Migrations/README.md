# 数据库迁移

PostgreSQL schema 和 TimescaleDB hypertable 拓扑全部由版本化 SQL 迁移管理，Store 启动时只验证所需拓扑，不执行 DDL。

## 规则

1. 脚本位于 `Migrations/sql/NNNN_name.sql`，编译为嵌入资源，按文件名升序执行。
2. `0001_baseline.sql` 是当前产品的全量新装基线；投入使用后的任何变更必须新增编号脚本。
3. **已应用的脚本不可修改**——runner 校验 checksum，漂移即拒绝启动。
4. 每个脚本在单个事务中执行；`schema_version` 表记录版本、名称、checksum、时间。
5. 迁移只能由一次性 `Ingot.Platform.Migrator` 宿主执行；API 和 Worker 不执行迁移。
6. 禁止在迁移中编写无 WHERE 的全表数据修复；数据订正必须限定范围并写明理由。
7. TimescaleDB 扩展、hypertable 转换以及未来的压缩策略同样只能通过新增迁移表达。

## 配置

- Compose 中 `platform-api` 与 `platform-worker` 必须等待 `platform-migrate` 成功退出。
- 非 Compose 启动时先运行 `dotnet run --project src/platform/Ingot.Platform.Migrator`。

## 安装边界

当前基线只支持空数据库新装。开发阶段生成过的数据库不属于支持范围，应删除后由 Migrator 重新创建；系统不提供旧 schema 的识别、重命名或数据转换路径。

## 验证记录

2026-08-19：将开发期 37 步演进链压缩为当前 schema 基线，并在 TimescaleDB 2.28.3 / PostgreSQL 17.10 空库执行成功；导出结构与压缩前最终结构一致（仅约束名规范化）。
