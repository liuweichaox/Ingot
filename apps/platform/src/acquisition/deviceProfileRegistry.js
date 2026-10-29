// Defines common source-device categories separately from data ownership and communication drivers.

export const deviceProfileOptions = [
  { value: "plc", label: "PLC / 可编程控制器", drivers: ["siemens-s7", "melsec-a1e", "melsec-mc-3e", "omron-fins-tcp", "omron-fins-udp", "ethernet-ip", "modbus-tcp", "modbus-rtu", "modbus-ascii", "opc-ua", "mqtt", "http-polling"] },
  { value: "cnc", label: "CNC / 数控机床", drivers: ["mtconnect", "opc-ua", "modbus-tcp", "modbus-rtu", "modbus-ascii", "mqtt", "http-polling"] },
  { value: "industrial-robot", label: "工业机器人", drivers: ["ethernet-ip", "opc-ua", "modbus-tcp", "modbus-rtu", "modbus-ascii", "mqtt", "http-polling"] },
  { value: "dcs", label: "DCS / 分布式控制系统", drivers: ["opc-ua", "modbus-tcp", "modbus-rtu", "modbus-ascii", "mqtt", "http-polling"] },
  { value: "hmi-panel", label: "HMI / 操作面板", drivers: ["opc-ua", "modbus-tcp", "mqtt", "http-polling"] },
  { value: "building-automation", label: "楼宇自动化设备", drivers: ["bacnet-ip", "bacnet-mstp", "modbus-tcp", "modbus-rtu", "modbus-ascii", "opc-ua", "mqtt", "http-polling"] },
  { value: "remote-io", label: "远程 I/O / 现场总线模块", drivers: ["modbus-rtu", "modbus-ascii", "modbus-tcp", "opc-ua", "mqtt", "http-polling"] },
  { value: "drive", label: "变频器 / 伺服驱动器", drivers: ["modbus-rtu", "modbus-ascii", "modbus-tcp", "opc-ua", "mqtt", "http-polling"] },
  { value: "sensor-transmitter", label: "传感器 / 变送器", drivers: ["modbus-rtu", "modbus-ascii", "modbus-tcp", "mqtt", "opc-ua", "http-polling"] },
  { value: "process-instrument", label: "温度 / 压力 / 流量仪表", drivers: ["modbus-rtu", "modbus-ascii", "modbus-tcp", "opc-ua", "mqtt", "http-polling"] },
  { value: "measurement-instrument", label: "检测 / 测量仪器", drivers: ["modbus-rtu", "modbus-ascii", "modbus-tcp", "opc-ua", "http-polling", "mqtt"] },
  { value: "weighing-instrument", label: "工业称重 / 计量设备", drivers: ["modbus-rtu", "modbus-ascii", "modbus-tcp", "opc-ua", "mqtt", "http-polling"] },
  { value: "energy-meter", label: "电能表 / 能源仪表", drivers: ["modbus-rtu", "modbus-ascii", "modbus-tcp", "mqtt", "opc-ua", "http-polling"] },
  { value: "power-asset", label: "配电 / UPS 设备", drivers: ["snmp", "modbus-tcp", "modbus-rtu", "modbus-ascii", "opc-ua", "mqtt", "http-polling"] },
  { value: "machine-vision", label: "机器视觉 / 检测设备", drivers: ["ethernet-ip", "opc-ua", "http-polling", "mqtt", "modbus-tcp"] },
  { value: "barcode-rfid", label: "条码 / RFID 追溯设备", drivers: ["ethernet-ip", "mqtt", "http-polling", "modbus-tcp", "opc-ua"] },
  { value: "environmental-monitor", label: "环境监测设备", drivers: ["modbus-rtu", "modbus-ascii", "modbus-tcp", "mqtt", "opc-ua", "http-polling"] },
  { value: "industrial-computer", label: "工控机 / 边缘计算机", drivers: ["snmp", "http-polling", "mqtt", "opc-ua", "modbus-tcp", "modbus-rtu", "modbus-ascii"] },
  { value: "industrial-gateway", label: "工业网关 / 数据采集器", drivers: ["snmp", "mqtt", "http-polling", "opc-ua", "modbus-tcp", "modbus-rtu", "modbus-ascii"] },
  { value: "network-infrastructure", label: "网络与基础设施设备", drivers: ["snmp", "http-polling", "mqtt", "opc-ua"] },
  { value: "scada-mes", label: "SCADA / MES / 上位系统", drivers: ["http-polling", "mqtt", "opc-ua", "modbus-tcp"] },
  { value: "other", label: "其他设备", drivers: ["http-polling", "mqtt", "opc-ua", "modbus-tcp", "modbus-rtu", "modbus-ascii", "bacnet-ip", "bacnet-mstp", "siemens-s7", "melsec-a1e", "melsec-mc-3e", "omron-fins-tcp", "omron-fins-udp", "ethernet-ip"] },
];

const optionsByKind = new Map(deviceProfileOptions.map(option => [option.value, option]));

export function deviceProfileOption(kind) {
  return optionsByKind.get(kind) || optionsByKind.get("other");
}
