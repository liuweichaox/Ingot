# 开源依赖

Ingot 的设备采集、文档解析和科研数据导入只引入开放源代码组件：

| 组件 | 用途 | 版本 | 许可证 | 上游 |
| --- | --- | --- | --- | --- |
| MQTTnet | MQTT 3.1.1/5.0 客户端、TLS 和订阅 | 5.2.0.1603 | MIT | <https://github.com/dotnet/MQTTnet> |
| OPC Foundation UA .NET Standard | OPC UA 客户端、发现、会话和订阅 | 1.5.378.156 | MIT | <https://github.com/OPCFoundation/UA-.NETStandard> |
| NModbus | Modbus TCP 主站 | 3.0.83 | MIT | <https://github.com/NModbus/NModbus> |
| Aedes | 模拟设备 MQTT Broker | 1.1.1 | MIT | <https://github.com/moscajs/aedes> |
| MQTT.js | 模拟设备 MQTT 发布客户端 | 5.15.2 | MIT | <https://github.com/mqttjs/MQTT.js> |
| node-opcua | 模拟设备 OPC UA Server | 2.175.2 | MIT | <https://github.com/node-opcua/node-opcua> |
| modbus-serial | 模拟设备 Modbus TCP Server | 8.0.25 | ISC | <https://github.com/yaacov/node-modbus-serial> |
| ClosedXML | XLSX/XLSM 解析与单元格引用定位 | 0.105.0 | MIT | <https://github.com/ClosedXML/ClosedXML> |
| PdfPig | PDF 文本解析与页码定位 | 0.1.14 | Apache-2.0 | <https://github.com/UglyToad/PdfPig> |
| MatFileHandler | MATLAB Level 5 MAT 科研数据读取 | 1.3.0 | MIT | <https://github.com/mahalex/MatFileHandler> |

版本固定在项目文件、NuGet 资产和模拟器 `package-lock.json` 中。协议实现不依赖商业版连接器或闭源 SDK。
