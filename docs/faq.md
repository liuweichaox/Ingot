# 常见问题

## Chat 和 Ingot Agent 有什么区别？

Chat 是 Central Web 中的只读对话，用于查询生产事实、检查数据质量和查找周期问题。Ingot Agent 是下载安装的桌面软件，只用于连接器代码生成、构建、测试、修复和打包。

## Chat 会生成代码吗？

不会。Chat 只调用 `check_data_quality` 与 `get_cycle_trace`，不写文件、不执行 SQL 或 Shell，也不修改生产事实。

## 为什么 Central Web 没有 Agent 入口？

Agent 需要本地桌面安全边界、明确客户端身份和代码工程工作台。Central Web 只提供 Chat 与生产事实界面。下载 Agent 请访问 [GitHub Releases](https://github.com/liuweichaox/Ingot/releases/latest)。

## Ingot Agent 在哪里运行模型和构建？

桌面应用配置 Central URL、Actor 和 Token。模型运行、工作区、固定容器构建/测试和制品存储位于 Central 服务端；桌面端通过 Tauri Rust 原生边界访问。

## 如何接入数据源？

在 Ingot Agent Desktop 中填写协议、端点说明、输入契约、样本、采样策略、目标事件和验收条件。Agent 生成源码并执行固定构建/测试；测试通过后由授权 Actor 批准打包并下载 SHA-256 ZIP。

## 默认连接器如何运行？

默认模板从 stdin 读取源 JSONL，并向 stdout 输出 ProductionEvent JSONL。外部部署运行时负责数据源连接、批处理、Connector Token、向 Connector Host 提交、重试和进程监管。

## Ingot 会部署或启动生成的连接器吗？

不会。Ingot 提供源码、构建/测试结果、人工批准和 ZIP。部署、启动、调度、生产凭据和回滚属于外部运行环境。

## Connector Host 接收什么？

带 Bearer Token 的 `ProductionEvent[]`。Host 校验事件、分配本地顺序号、写入 SQLite，并通过 outbox 上报 Central API。

## Chat 与 Agent 的历史是否互通？

不互通。两者按产品面和 Actor 隔离创建、列表、读取、SSE、取消和历史。Agent API 还要求固定桌面客户端标识。

## 数据不足时 Chat 会怎样？

Chat 返回限制条件或拒绝确定性结论。回答数字必须来自工具结果，关键发现必须带可解析证据。

## Ingot 能控制设备吗？

不能。Chat 和 Agent 都没有 PLC、CNC、机器人或其他设备控制工具。
