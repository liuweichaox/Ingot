# 数据库迁移

自 v2.2 起，PostgreSQL schema 由版本化 SQL 迁移管理，不再依赖各 Store 启动期 DDL（初始化器暂保留一个过渡期，其 `CREATE ... IF NOT EXISTS` 与基线逐字一致，属无害冗余，后续批次移除）。

## 规则

1. 脚本位于 `Migrations/sql/NNNN_name.sql`，编译为嵌入资源，按文件名升序执行。
2. **已应用的脚本不可修改**——runner 校验 checksum，漂移即拒绝启动。任何变更用新的编号脚本表达。
3. 每个脚本在单个事务中执行；`schema_version` 表记录版本、名称、checksum、时间。
4. 多实例并发启动由 `pg_advisory_lock` 串行化。
5. 禁止在迁移中编写无 WHERE 的全表数据修复；数据订正必须限定范围并写明理由。
6. TimescaleDB 扩展、hypertable 转换与压缩/保留策略仍由
   `PostgresPlatformEventStore` / `PostgresTimeSeriesStore` 的既有幂等逻辑负责（它们是配置驱动的运行参数，不是 schema）。

## 配置

- PostgreSQL schema 始终由版本化迁移管理；Store 初始化仅负责配置驱动的 Timescale 拓扑、存储目录和派生数据校正。

## 对已有数据库

`0001_baseline.sql` 与旧初始化器 DDL 逐字一致且幂等：对已被旧版本初始化过的库，首次执行等价于收编（全部语句为 no-op 或既有幂等修复），随后 `schema_version` 开始记账。

## 验证记录

2026-07-24：baseline 在 PostgreSQL 16.13 上连续执行两轮均成功（幂等），建表 48 张（TimescaleDB 扩展语句在纯 PG 验证中跳过，与生产 timescaledb 镜像行为一致性由 CI compose smoke 覆盖）。
