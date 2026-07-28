# 开源依赖

Ingot 使用开源组件完成设备连接、业务平台、数值优化和公开站点。精确版本以项目文件和 lockfile 为准。

| 领域 | 主要组件 | 许可证 |
|---|---|---|
| 数值优化 | PyTorch、GPyTorch、BoTorch、NumPy、SciPy | BSD / Apache-2.0 |
| 设备采集 | MQTTnet、OPC Foundation UA .NET Standard、NModbus | MIT |
| 平台 | .NET / ASP.NET Core、Npgsql、SQLitePCLRaw | MIT |
| 前端 | React、Vite、Headless UI、Plotly.js | MIT |
| 官网与文档 | Next.js、remark/rehype、Tailwind CSS | MIT |
| 数据导入 | ClosedXML、PdfPig、MatFileHandler | MIT / Apache-2.0 |
| 数据库与时序 | PostgreSQL、TimescaleDB | PostgreSQL / Apache-2.0 |

新增运行期依赖必须：

- 有可接受的开源许可证；
- 固定或受控版本范围；
- 进入依赖审计和构建验证；
- 在镜像或发布产物中保留许可证要求；
- 不把云端专有服务变成核心闭环的必要条件。
