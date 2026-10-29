// Edit and validate protocol-specific device points and their process-variable mappings.
import { Badge, Button, Card, Field, Input, Select } from "../../ui/components";
import {
  ADDRESSING,
  MELSEC_DEVICES,
  MELSEC_MC3E_DEVICES,
  MODBUS_AREAS,
  isModbusBitArea,
  melsecAddressHint,
  melsecDevice,
  melsecMc3eDevice,
  melsecWireAddress,
  modbusArea,
  normalizeSnmpOid,
  registerWordCount,
} from "../protocolRegistry";
import { PointMappingCsvPanel } from "../PointMappingCsvPanel";

export function PointMappingPanel({
  title,
  description,
  descriptor,
  rows,
  options,
  errors,
  errorPrefix,
  probe,
  form,
  readOnly,
  onChange,
}) {
  const addRow = () => onChange([...rows, blankRow(descriptor)]);
  const updateRow = (index, patch) =>
    onChange(rows.map((item, rowIndex) => (rowIndex === index ? { ...item, ...patch } : item)));
  const removeRow = index => onChange(rows.filter((_item, rowIndex) => rowIndex !== index));

  return (
    <Card
      title={title}
      description={description}
      actions={!readOnly ? <Button onClick={addRow}>添加点位</Button> : undefined}
    >
      {errors[errorPrefix] && (
        <p className="mb-3 rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{errors[errorPrefix]}</p>
      )}
      <div className="grid gap-4">
        <PointMappingCsvPanel
          descriptor={descriptor}
          rows={rows}
          options={options}
          form={form}
          readOnly={readOnly}
          createBlankRow={blankRow}
          onApply={onChange}
        />
        {rows.length === 0 && <p className="text-sm text-slate-500">还没有配置点位。</p>}
        {rows.map((row, index) => (
          <PointRow
            key={index}
            row={row}
            index={index}
            descriptor={descriptor}
            options={options}
            errors={errors}
            path={`${errorPrefix}[${index}]`}
            preview={probe?.mappings?.find(item => item.dataItemCode === row.dataItemCode)}
            probe={probe}
            form={form}
            readOnly={readOnly}
            removable={rows.length > 1}
            onChange={patch => updateRow(index, patch)}
            onRemove={() => removeRow(index)}
          />
        ))}
      </div>
    </Card>
  );
}

export function blankRow(descriptor) {
  return {
    dataItemCode: "",
    sourcePath: "",
    required: true,
    sourceDataType: descriptor.defaultDataType || (descriptor.dataTypes.includes("auto") ? "auto" : "int16"),
    sourceByteLength: 16,
    scale: 1,
    offset: 0,
    qualityPath: "",
    acceptedQualityValues: "",
    missingValueBehavior: "inherit",
    defaultValue: "",
    modbusArea: "holding-register",
    modbusAddress: "",
    modbusQuantity: 1,
    byteOrder: "big-endian",
    wordOrder: "high-low",
    melsecDevice: "D",
    melsecAddress: "",
    bitIndex: "",
    topic: "",
  };
}

function PointRow({
  row, descriptor, options, errors, path, preview, probe, form, readOnly, removable, onChange, onRemove,
}) {
  const definition = options.find(item => item.code === row.dataItemCode);
  const listId = `${path}-points`;
  const error = field => errors[`${path}.${field}`];
  const mc3eFamily = form.melsecMc3E?.family;
  const mc3eFamilyRange = mc3eFamily === "iq-r"
    ? "iQ-R 的帧地址上限为 0xFFFFFFFF"
    : ["q-l", "iq-f"].includes(mc3eFamily) ? `${mc3eFamily === "iq-f" ? "iQ-F" : "Q/L"} 的帧地址上限为 0xFFFFFF` : "选择 PLC 系列后显示该系列的帧地址上限";

  return (
    <div className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-3">
      <Field label="平台变量" error={error("dataItemCode")}>
        <Select
          value={row.dataItemCode}
          disabled={readOnly}
          onChange={event => {
            const selected = options.find(item => item.code === event.target.value);
            onChange({
              dataItemCode: event.target.value,
              required: selected ? !selected.nullable : row.required,
            });
          }}
        >
          <option value="">请选择</option>
          {options.map(item => (
            <option key={item.code} value={item.code}>{item.displayName || item.code}</option>
          ))}
        </Select>
      </Field>

      {descriptor.addressing === ADDRESSING.modbusRegister && (
        <ModbusAddressFields row={row} form={form} descriptor={descriptor} error={error} readOnly={readOnly} onChange={onChange} />
      )}
      {descriptor.addressing === ADDRESSING.melsecDevice && (
        <MelsecAddressFields row={row} error={error} readOnly={readOnly} onChange={onChange} />
      )}
      {[ADDRESSING.jsonPath, ADDRESSING.mtConnectDataItem, ADDRESSING.nodeId, ADDRESSING.siemensS7Address, ADDRESSING.ethernetIpTag, ADDRESSING.omronFinsAddress, ADDRESSING.melsecMc3EDevice, ADDRESSING.bacnetObject, ADDRESSING.snmpOid].includes(descriptor.addressing) && (
        <Field
          label={descriptor.pointLabel}
          hint={descriptor.addressing === ADDRESSING.siemensS7Address
            ? "填写 PLC 绝对地址，例如 DB1.DBW0、DB1.DBX0.0、MW10；S7 不扫描未知地址。"
            : descriptor.addressing === ADDRESSING.ethernetIpTag
              ? "填写控制器符号标签，例如 Program:MainProgram.Temperature、Motor.Speed 或 Values[0]；只回读已填写的标签。"
              : descriptor.addressing === ADDRESSING.omronFinsAddress
                ? "填写已知内存区地址，例如 DM:100 或 CIO:100.3；数据类型由下方选择，也可写成 DM:100:int16。"
                : descriptor.addressing === ADDRESSING.melsecMc3EDevice
                  ? "填写二进制 MC 3E 点位选择器并带类型后缀，例如 D:100:int16、D:100.3:boolean 或 X:1A:boolean。X/Y/B/W 用十六进制，M/L/D 用十进制。"
                  : descriptor.addressing === ADDRESSING.mtConnectDataItem
                    ? "使用 /probe 返回的 DataItem ID，例如 xact 或 avail；运行时只请求已配置 ID。"
                    : descriptor.addressing === ADDRESSING.bacnetObject
                      ? "从指定 Device Instance 的对象目录选择 object-type:instance，例如 analog-input:1；读取对象 present-value 标量。"
                      : descriptor.addressing === ADDRESSING.snmpOid
                        ? "填写数字 OID，例如 1.3.6.1.2.1.1.5.0；从 browseRootOid 子树浏览并拖拽点位。只读 GET，不支持 MIB 符号名称。"
            : probe ? "可从右侧读取到的设备点位中选择。" : "验证连接后可直接从设备点位中选择。"}
          error={error("sourcePath")}
        >
          <Input
            list={listId}
            value={row.sourcePath}
            disabled={readOnly}
            placeholder={descriptor.addressing === ADDRESSING.nodeId
              ? "ns=2;s=Machine.Temperature"
              : descriptor.addressing === ADDRESSING.siemensS7Address ? "DB1.DBW0"
                : descriptor.addressing === ADDRESSING.ethernetIpTag ? "Program:MainProgram.Temperature"
                  : descriptor.addressing === ADDRESSING.omronFinsAddress ? "DM:100"
                    : descriptor.addressing === ADDRESSING.melsecMc3EDevice ? "D:100:int16"
                      : descriptor.addressing === ADDRESSING.mtConnectDataItem ? "xact"
                        : descriptor.addressing === ADDRESSING.bacnetObject ? "analog-input:1"
                          : descriptor.addressing === ADDRESSING.snmpOid ? "1.3.6.1.2.1.1.5.0" : "sensors.temperature"}
            onChange={event => {
              let sourcePath = event.target.value;
              if (descriptor.addressing === ADDRESSING.snmpOid)
                sourcePath = normalizeSnmpOid(sourcePath) || sourcePath;
              const [deviceCode = ""] = sourcePath.split(":");
              const device = descriptor.addressing === ADDRESSING.melsecMc3EDevice ? melsecMc3eDevice(deviceCode) : null;
              if (descriptor.addressing === ADDRESSING.melsecMc3EDevice &&
                  /^[A-Za-z]+:[0-9A-Fa-f]+(?:\.\d{1,2})?(?::[A-Za-z0-9]+)?$/.test(sourcePath))
                sourcePath = canonicalizeMc3eSelector(sourcePath, row.sourceDataType);
              const canonicalType = sourcePath.split(":")[2]?.toLowerCase();
              onChange({
                sourcePath,
                ...(canonicalType && descriptor.dataTypes.includes(canonicalType)
                  ? { sourceDataType: canonicalType }
                  : device?.bit ? { sourceDataType: "boolean" } : {}),
              });
            }}
          />
          <datalist id={listId}>
            {descriptor.addressing === ADDRESSING.melsecMc3EDevice && [
              ["D:100:int16", "D 字软元件 · 十进制"], ["D:100.3:boolean", "D 字内位"],
              ["W:1A:uint16", "W 字软元件 · 十六进制"], ["X:1A:boolean", "X 位软元件 · 十六进制"],
            ].map(([value, label]) => <option key={`example-${value}`} value={value}>{label}</option>)}
            {(probe?.points || []).map(point => (
              <option key={`probe-${point.path}`} value={point.path}>{point.name}</option>
            ))}
          </datalist>
          {descriptor.addressing === ADDRESSING.melsecMc3EDevice && (
            <p className="mt-1 text-xs text-slate-500">
              支持区：{MELSEC_MC3E_DEVICES.map(device => `${device.code}（${device.description}，${device.radix === 16 ? "十六进制" : "十进制"}${device.bit ? "，boolean" : ""}）`).join("、")}。
              {mc3eFamilyRange}；这是 3E 二进制选择器，A 兼容 1E 的 X/Y 八进制规则不适用。
            </p>
          )}
        </Field>
      )}

      {[ADDRESSING.modbusRegister, ADDRESSING.melsecDevice, ADDRESSING.siemensS7Address, ADDRESSING.ethernetIpTag, ADDRESSING.omronFinsAddress, ADDRESSING.melsecMc3EDevice, ADDRESSING.bacnetObject, ADDRESSING.snmpOid].includes(descriptor.addressing) && (
        <DataTypeField row={row} descriptor={descriptor} error={error} readOnly={readOnly} onChange={onChange} />
      )}

      {descriptor.addressing === ADDRESSING.omronFinsAddress && row.sourceDataType !== "boolean" && registerWordCount(row.sourceDataType) > 1 && (
        <Field label="多字字序" hint="PLC 将 32/64 位值拆分到多个连续字时，选择高字或低字在前。" error={error("wordOrder")}>
          <Select value={row.wordOrder || "high-low"} disabled={readOnly}
            onChange={event => onChange({ wordOrder: event.target.value })}>
            <option value="high-low">高字在前</option>
            <option value="low-high">低字在前</option>
          </Select>
        </Field>
      )}

      {descriptor.addressing === ADDRESSING.siemensS7Address && row.sourceDataType === "string" && (
        <Field label="STRING 最大长度（字符）" hint="按 PLC DB 中 S7 STRING 的长度字节填写；单点最多读取 200 字节。" error={error("sourceByteLength")}>
          <Input type="number" min="1" max="198" value={row.sourceByteLength} disabled={readOnly}
            onChange={event => onChange({ sourceByteLength: event.target.value })} />
        </Field>
      )}

      {descriptor.capabilities.perTopicMapping && (
        <Field label="来源主题" hint="留空表示接受任意主题的报文。" error={error("topic")}>
          <Select value={row.topic || ""} disabled={readOnly} onChange={event => onChange({ topic: event.target.value })}>
            <option value="">任意主题</option>
            {(form.mqtt?.topics || [])
              .filter(item => (item.topic || "").trim())
              .map(item => <option key={item.topic} value={item.topic}>{item.topic}</option>)}
          </Select>
        </Field>
      )}

      <Field label="换算倍率" hint="设备原始值 × 倍率 + 偏移 = 平台值。" error={error("scale")}>
        <Input type="number" step="any" value={row.scale} disabled={readOnly}
          onChange={event => onChange({ scale: event.target.value })} />
      </Field>
      <Field label="设备值单位" hint={definition?.unit ? `平台目标单位：${definition.unit}` : "无量纲可留空。"} error={error("sourceUnit")}>
        <Input value={row.sourceUnit || ""} disabled={readOnly}
          placeholder={definition?.unit || "例如 °C、MPa、mm"}
          onChange={event => onChange({ sourceUnit: event.target.value })} />
      </Field>
      <Field label="换算偏移" error={error("offset")}>
        <Input type="number" step="any" value={row.offset} disabled={readOnly}
          onChange={event => onChange({ offset: event.target.value })} />
      </Field>

      <Field label="质量字段"
        hint={descriptor.id === "ethernet-ip" ? "EtherNet/IP 当前不支持质量标签；如有旧值，请清空后保存。"
          : descriptor.addressing === ADDRESSING.melsecMc3EDevice ? "填写带类型后缀的 MC 3E 选择器，例如 M:100:boolean 或 D:100:int16。"
            : descriptor.addressing === ADDRESSING.mtConnectDataItem ? "MTConnect observation 自带质量状态，首期不支持额外质量 DataItem 映射。"
              : descriptor.addressing === ADDRESSING.snmpOid ? "SNMP OID 点位不提供独立质量路径。"
            : "可选；例如 OPC UA 状态或报文中的 quality。"}
        error={error("qualityPath")}>
        <Input value={row.qualityPath || ""}
          disabled={readOnly || descriptor.addressing === ADDRESSING.nodeId ||
            descriptor.addressing === ADDRESSING.mtConnectDataItem ||
            descriptor.addressing === ADDRESSING.snmpOid ||
            (descriptor.id === "ethernet-ip" && !(row.qualityPath || "").trim())}
          placeholder={descriptor.addressing === ADDRESSING.nodeId ? "$status"
            : descriptor.addressing === ADDRESSING.melsecMc3EDevice ? "M:100:boolean" : undefined}
          onChange={event => onChange({ qualityPath: event.target.value })} />
      </Field>
      <Field label="允许的质量值"
        hint={descriptor.id === "ethernet-ip" ? "EtherNet/IP 当前不支持质量过滤；如有旧值，请清空后保存。"
          : descriptor.addressing === ADDRESSING.mtConnectDataItem ? "由 MTConnect observation 自带的质量状态决定；首期不提供过滤规则。"
            : descriptor.addressing === ADDRESSING.snmpOid ? "SNMP OID 点位不提供独立质量状态过滤。"
            : "多个值用逗号分隔；配置后其他质量值会被拒绝。"}
        error={error("acceptedQualityValues")}>
        <Input value={row.acceptedQualityValues || ""}
          disabled={readOnly || descriptor.addressing === ADDRESSING.mtConnectDataItem || descriptor.addressing === ADDRESSING.snmpOid ||
            (descriptor.id === "ethernet-ip" && !(row.acceptedQualityValues || "").trim())}
          placeholder="Good, Valid"
          onChange={event => onChange({ acceptedQualityValues: event.target.value })} />
      </Field>
      <Field label="缺失处理" error={error("missingValueBehavior")}>
        <Select value={row.missingValueBehavior || "inherit"} disabled={readOnly}
          onChange={event => onChange({ missingValueBehavior: event.target.value })}>
          <option value="inherit">按必需/可选属性</option>
          <option value="reject">拒绝整条快照</option>
          <option value="omit">省略该值</option>
          <option value="use-default">使用默认值</option>
        </Select>
      </Field>
      {row.missingValueBehavior === "use-default" && (
        <Field label="默认值" error={error("defaultValue")}>
          <Input value={row.defaultValue ?? ""} disabled={readOnly}
            onChange={event => onChange({ defaultValue: event.target.value })} />
        </Field>
      )}

      <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
        <p className="text-xs text-slate-500">平台目标</p>
        <p>{definition
          ? `${definition.dataType} · ${definition.unit || "无单位"} · ${definition.nullable ? "允许缺失" : "周期必需"}`
          : "请先选择平台变量"}</p>
      </div>

      {!readOnly && removable && (
        <Button variant="ghost" className="justify-self-start text-rose-700" onClick={onRemove}>移除</Button>
      )}

      {preview && (
        <div className="rounded-lg bg-slate-50 p-3 text-sm md:col-span-2 xl:col-span-3">
          <div className="grid gap-2 sm:grid-cols-4">
            <Readout label="原始值" value={preview.rawValue ?? "未读取"} />
            <Readout label="换算值" value={preview.convertedValue ?? "—"} />
            <Readout label="设备类型" value={preview.dataType || "—"} />
            <Readout label="设备单位" value={preview.sourceUnit || "—"} />
            <Readout label="目标单位" value={preview.targetUnit || "—"} />
          </div>
          {preview.error && <p className="mt-2 text-rose-700">{preview.error}</p>}
        </div>
      )}
    </div>
  );
}

function Readout({ label, value }) {
  return (
    <div>
      <p className="text-xs text-slate-500">{label}</p>
      <p className="font-medium text-slate-800">{value}</p>
    </div>
  );
}

function DataTypeField({ row, descriptor, error, readOnly, onChange }) {
  const bitArea = descriptor.addressing === ADDRESSING.modbusRegister && isModbusBitArea(row.modbusArea);
  const mc3eArea = descriptor.addressing === ADDRESSING.melsecMc3EDevice
    ? melsecMc3eDevice(String(row.sourcePath || "").split(":")[0])
    : null;
  const types = bitArea || mc3eArea?.bit ? ["boolean"] : descriptor.dataTypes;
  return (
    <Field
      label="设备数据类型"
      hint={bitArea || mc3eArea?.bit ? "位软元件只支持 boolean；MC 3E 字软元件读取 boolean 时在选择器中填写 .0-.15 位偏移。"
        : [ADDRESSING.mtConnectDataItem, ADDRESSING.bacnetObject, ADDRESSING.snmpOid].includes(descriptor.addressing)
          ? descriptor.addressing === ADDRESSING.bacnetObject
            ? "对象目录读取的 present-value 标量按目标变量类型自动解析。"
            : descriptor.addressing === ADDRESSING.snmpOid
              ? "auto 按平台目标变量类型解析 SNMP 标量；Counter32/64 保留为累计值，不计算速率。"
              : "Agent 返回 XML 文本；按平台目标变量类型解析成标量。"
          : undefined}
      error={error("sourceDataType")}
    >
      <Select
        value={row.sourceDataType}
        disabled={readOnly || bitArea || mc3eArea?.bit}
        onChange={event => {
          const next = event.target.value;
          onChange({
            sourceDataType: next,
            ...(descriptor.addressing === ADDRESSING.melsecMc3EDevice
              ? { sourcePath: withMelsecMc3eType(row.sourcePath, next) }
              : {}),
            bitIndex: next === "boolean" ? row.bitIndex : "",
            modbusQuantity: next === "string"
              ? Math.max(1, Math.ceil((Number(row.sourceByteLength) || 1) / 2))
              : registerWordCount(next),
          });
        }}
      >
        {types.map(value => (
          <option key={value} value={value}>{value === "auto"
          ? [ADDRESSING.mtConnectDataItem, ADDRESSING.bacnetObject, ADDRESSING.snmpOid].includes(descriptor.addressing)
            ? "按平台目标变量类型解析" : "按样本识别（不推荐）"
            : value}</option>
        ))}
      </Select>
    </Field>
  );
}

function withMelsecMc3eType(sourcePath, dataType) {
  const selector = String(sourcePath || "").trim();
  return canonicalizeMc3eSelector(selector, dataType);
}

function canonicalizeMc3eSelector(sourcePath, fallbackType) {
  const selector = String(sourcePath || "").trim();
  const match = /^([A-Za-z]+):([0-9A-Fa-f]+)(?:\.(\d{1,2}))?(?::([A-Za-z0-9]+))?$/i.exec(selector);
  if (!match) return selector;
  const [, rawCode, rawAddress, bit, rawType] = match;
  const device = melsecMc3eDevice(rawCode);
  if (!device) return selector;
  const address = device.radix === 16 ? rawAddress.toUpperCase() : rawAddress;
  const dataType = rawType?.toLowerCase() || (device.bit ? "boolean" : fallbackType);
  return `${device.code}:${address}${bit === undefined ? "" : `.${bit}`}:${dataType}`;
}

function ModbusAddressFields({ row, form, descriptor, error, readOnly, onChange }) {
  const area = modbusArea(row.modbusArea);
  const oneBased = form[descriptor.section]?.addressBase === "one-based";
  const words = row.sourceDataType === "string"
    ? Math.max(1, Math.ceil((Number(row.sourceByteLength) || 1) / 2))
    : registerWordCount(row.sourceDataType);
  const needsBit = !area?.bit && row.sourceDataType === "boolean";
  return (
    <>
      <Field label="寄存器区" error={error("modbusArea")}>
        <Select
          value={row.modbusArea}
          disabled={readOnly}
          onChange={event => {
            const next = event.target.value;
            onChange({
              modbusArea: next,
              sourceDataType: isModbusBitArea(next) ? "boolean" : (row.sourceDataType === "boolean" ? "int16" : row.sourceDataType),
              bitIndex: isModbusBitArea(next) ? "" : row.bitIndex,
            });
          }}
        >
          {MODBUS_AREAS.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
        </Select>
      </Field>
      <Field
        label="地址"
        hint={oneBased ? "按手册地址（从 1 开始）填写，发送前自动减 1。" : "按线缆地址（从 0 开始）填写。"}
        error={error("modbusAddress")}
      >
        <Input
          type="number"
          min={oneBased ? 1 : 0}
          value={row.modbusAddress}
          disabled={readOnly}
          onChange={event => onChange({ modbusAddress: event.target.value })}
        />
      </Field>
      {needsBit && (
        <Field label="位偏移" hint="从该寄存器的第几位取布尔值（0 是最低位）。" error={error("bitIndex")}>
          <Input type="number" min="0" max="15" value={row.bitIndex} disabled={readOnly}
            onChange={event => onChange({ bitIndex: event.target.value })} />
        </Field>
      )}
      {row.sourceDataType === "string" && (
        <Field label="文本长度（字节）" error={error("sourceByteLength")}>
          <Input type="number" min="1" max="128" value={row.sourceByteLength} disabled={readOnly}
            onChange={event => onChange({
              sourceByteLength: event.target.value,
              modbusQuantity: Math.max(1, Math.ceil((Number(event.target.value) || 1) / 2)),
            })} />
        </Field>
      )}
      {!area?.bit && row.sourceDataType !== "boolean" && (
        <Field label="字节序" hint="单个 16 位寄存器内两个字节的顺序。" error={error("byteOrder")}>
          <Select value={row.byteOrder} disabled={readOnly} onChange={event => onChange({ byteOrder: event.target.value })}>
            <option value="big-endian">高字节在前（AB）</option>
            <option value="little-endian">低字节在前（BA）</option>
          </Select>
        </Field>
      )}
      {!area?.bit && words > 1 && (
        <Field label="字序" hint="跨多个寄存器时的顺序。" error={error("wordOrder")}>
          <Select value={row.wordOrder} disabled={readOnly} onChange={event => onChange({ wordOrder: event.target.value })}>
            <option value="high-low">高字在前（ABCD）</option>
            <option value="low-high">低字在前（CDAB）</option>
          </Select>
        </Field>
      )}
      <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
        <p className="text-xs text-slate-500">占用</p>
        <p>{area?.bit ? "1 个位地址" : `${words} 个寄存器`}</p>
      </div>
    </>
  );
}

function MelsecAddressFields({ row, error, readOnly, onChange }) {
  const device = melsecDevice(row.melsecDevice);
  const wire = melsecWireAddress(row.melsecDevice, row.melsecAddress);
  const needsBit = device && !device.bit && row.sourceDataType === "boolean";
  const bitRead = device?.bit && row.sourceDataType === "boolean";
  const packed = device?.bit && row.sourceDataType !== "boolean";
  return (
    <>
      <Field label="软元件" error={error("melsecDevice")}>
        <Select
          value={row.melsecDevice}
          disabled={readOnly}
          onChange={event => onChange({ melsecDevice: event.target.value, bitIndex: "" })}
        >
          {MELSEC_DEVICES.map(item => (
            <option key={item.code} value={item.code}>
              {item.code} · {item.description}{item.bit ? "（位）" : ""}
            </option>
          ))}
        </Select>
      </Field>
      <Field label="软元件编号" hint={melsecAddressHint(row.melsecDevice)} error={error("melsecAddress")}>
        <Input
          value={row.melsecAddress}
          disabled={readOnly}
          placeholder={device?.radix === 8 ? "例如 17（八进制）" : device?.radix === 16 ? "例如 1A（十六进制）" : "例如 100"}
          onChange={event => onChange({ melsecAddress: event.target.value })}
        />
      </Field>
      {needsBit && (
        <Field label="位偏移" hint="从该字软元件的第几位取布尔值（0 是最低位）。" error={error("bitIndex")}>
          <Input type="number" min="0" max="15" value={row.bitIndex} disabled={readOnly}
            onChange={event => onChange({ bitIndex: event.target.value })} />
        </Field>
      )}
      {row.sourceDataType === "string" && (
        <Field label="文本长度（字节）" error={error("sourceByteLength")}>
          <Input type="number" min="1" max="128" value={row.sourceByteLength} disabled={readOnly}
            onChange={event => onChange({ sourceByteLength: event.target.value })} />
        </Field>
      )}
      <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
        <p className="text-xs text-slate-500">读取方式</p>
        <p className="flex flex-wrap items-center gap-1.5">
          {bitRead && <Badge tone="info">位单位批量读</Badge>}
          {packed && <Badge tone="warning">按字打包 16 点</Badge>}
          {!device?.bit && <Badge tone="neutral">字单位批量读</Badge>}
        </p>
        {device && device.radix !== 10 && wire !== null && (
          <p className="mt-1 text-xs text-slate-500">
            {device.code}{row.melsecAddress} → 软元件号 {wire}
          </p>
        )}
      </div>
    </>
  );
}
