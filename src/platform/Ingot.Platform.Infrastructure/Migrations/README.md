# 数据库迁移

PostgreSQL schema 和 TimescaleDB hypertable 拓扑全部由版本化 SQL 迁移管理，Store 启动时只验证所需拓扑，不执行 DDL。

## 规则

1. 脚本位于 `Migrations/sql/NNNN_name.sql`，编译为嵌入资源，按文件名升序执行。
2. **已应用的脚本不可修改**——runner 校验 checksum，漂移即拒绝启动。任何变更用新的编号脚本表达。
3. 每个脚本在单个事务中执行；`schema_version` 表记录版本、名称、checksum、时间。
4. 迁移只能由一次性 `Ingot.Platform.Migrator` 宿主执行；API 和 Worker 不执行迁移。
5. 禁止在迁移中编写无 WHERE 的全表数据修复；数据订正必须限定范围并写明理由。
6. TimescaleDB 扩展、hypertable 转换以及未来的压缩策略同样只能通过新增迁移表达。

## 配置

- Compose 中 `platform-api` 与 `platform-worker` 必须等待 `platform-migrate` 成功退出。
- 非 Compose 启动时先运行 `dotnet run --project src/platform/Ingot.Platform.Migrator`。

## 对已有数据库

`0001_baseline.sql` 与旧初始化器 DDL 逐字一致且幂等：对已被旧版本初始化过的库，首次执行等价于收编（全部语句为 no-op 或既有幂等修复），随后 `schema_version` 开始记账。

## 验证记录

2026-07-24：baseline 在 PostgreSQL 16.13 上连续执行两轮均成功（幂等），建表 48 张（TimescaleDB 扩展语句在纯 PG 验证中跳过，与生产 timescaledb 镜像行为一致性由 CI compose smoke 覆盖）。
