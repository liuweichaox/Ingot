# 开源依赖

> 文档状态：**滚动依赖概览**。精确版本、传递依赖和许可证以项目文件、lockfile、容器清单和自动审计结果为准。

Ingot 选择依赖的原则与产品思想一致：先解决工程问题，再选择适合的方法；不因为技术流行而把它变成不可替换的产品边界。

| 能力 | 主要组件 | 典型许可证 |
|---|---|---|
| .NET 平台与服务 | .NET、ASP.NET Core、Npgsql、SQLitePCLRaw | MIT / PostgreSQL |
| 现场协议与采集 | MQTTnet、OPC Foundation UA .NET Standard、NModbus | MIT |
| 数值计算与优化 | Python、PyTorch、GPyTorch、BoTorch、NumPy、SciPy | PSF / BSD / Apache-2.0 |
| 产品前端 | React、Vite、Headless UI、Plotly.js | MIT |
| 官网与文档站 | Next.js、remark、rehype、Tailwind CSS | MIT |
| 数据导入 | ClosedXML、PdfPig、MatFileHandler | MIT / Apache-2.0 |
| 数据与时序存储 | PostgreSQL、TimescaleDB | PostgreSQL / Apache-2.0 |

## 引入要求

新增运行期依赖必须：

- 直接服务于数据可信、工程判断、实验效率或系统可靠性；
- 具有与项目兼容的开源许可证；
- 固定版本或使用受控版本范围；
- 进入构建、漏洞、许可证和供应链审计；
- 在镜像和发布产物中保留许可证要求；
- 可以在厂内本地部署，或提供不破坏核心闭环的本地替代；
- 不把云端专有服务变成采集、记录、检验或数值分析的必要条件。

## 变更与审计

- lockfile 变更应与使用该依赖的代码在同一评审范围内；
- 主要版本升级先运行完整验证和相关历史回放；
- 不再使用的依赖及时移除；
- 许可证或维护状态变化必须在发布前处理；
- 对外发布时使用实际生成的 SBOM 或依赖清单，而不是仅依赖本页摘要。
