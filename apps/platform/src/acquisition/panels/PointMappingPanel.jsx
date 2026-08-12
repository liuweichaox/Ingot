import { Badge, Button, Card, Field, Input, Select } from "../../ui/components";
import {
  ADDRESSING,
  MELSEC_DEVICES,
  MODBUS_AREAS,
  isModbusBitArea,
  melsecAddressHint,
  melsecDevice,
  melsecWireAddress,
  modbusArea,
  registerWordCount,
} from "../protocolRegistry";

/**
 * 协议专业点位编辑器。
 *
 * 每种寻址方式有自己的字段组合：Modbus 用寄存器区 + 地址 + 字节序，
 * MELSEC 用软元件 + 编号（含进制换算），文档类协议用路径 + 探查点位补全。
 * 不再让工程师在同一排输入框里面对与自己协议无关的字段。
 */
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
    sourceDataType: descriptor.dataTypes.includes("auto") ? "auto" : "int16",
    sourceByteLength: 16,
    scale: 1,
    offset: 0,
    qualityPath: "",
    acceptedQualityValues: "",
    minimum: "",
    maximum: "",
    outOfRangeBehavior: "reject",
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
        <ModbusAddressFields row={row} form={form} error={error} readOnly={readOnly} onChange={onChange} />
      )}
      {descriptor.addressing === ADDRESSING.melsecDevice && (
        <MelsecAddressFields row={row} error={error} readOnly={readOnly} onChange={onChange} />
      )}
      {[ADDRESSING.jsonPath, ADDRESSING.nodeId].includes(descriptor.addressing) && (
        <Field
          label={descriptor.pointLabel}
          hint={probe ? "可从右侧读取到的设备点位中选择。" : "验证连接后可直接从设备点位中选择。"}
          error={error("sourcePath")}
        >
          <Input
            list={listId}
            value={row.sourcePath}
            disabled={readOnly}
            placeholder={descriptor.addressing === ADDRESSING.nodeId ? "ns=2;s=Machine.Temperature" : "sensors.temperature"}
            onChange={event => onChange({ sourcePath: event.target.value })}
          />
          <datalist id={listId}>
            {(probe?.points || []).map(point => (
              <option key={point.path} value={point.path}>{point.name}</option>
            ))}
          </datalist>
        </Field>
      )}

      {[ADDRESSING.modbusRegister, ADDRESSING.melsecDevice].includes(descriptor.addressing) && (
        <DataTypeField row={row} descriptor={descriptor} error={error} readOnly={readOnly} onChange={onChange} />
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

      <Field label="质量字段" hint="可选；例如 OPC UA 状态或报文中的 quality。" error={error("qualityPath")}>
        <Input value={row.qualityPath || ""} disabled={readOnly || descriptor.addressing === ADDRESSING.nodeId}
          placeholder={descriptor.addressing === ADDRESSING.nodeId ? "$status" : undefined}
          onChange={event => onChange({ qualityPath: event.target.value })} />
      </Field>
      <Field label="允许的质量值" hint="多个值用逗号分隔；配置后其他质量值会被拒绝。" error={error("acceptedQualityValues")}>
        <Input value={row.acceptedQualityValues || ""} disabled={readOnly} placeholder="Good, Valid"
          onChange={event => onChange({ acceptedQualityValues: event.target.value })} />
      </Field>
      <Field label="有效下限" error={error("minimum")}>
        <Input type="number" step="any" value={row.minimum ?? ""} disabled={readOnly}
          onChange={event => onChange({ minimum: event.target.value })} />
      </Field>
      <Field label="有效上限" error={error("maximum")}>
        <Input type="number" step="any" value={row.maximum ?? ""} disabled={readOnly}
          onChange={event => onChange({ maximum: event.target.value })} />
      </Field>
      <Field label="越界处理" error={error("outOfRangeBehavior")}>
        <Select value={row.outOfRangeBehavior || "reject"} disabled={readOnly}
          onChange={event => onChange({ outOfRangeBehavior: event.target.value })}>
          <option value="reject">拒绝整条快照</option>
          <option value="clamp">限制到边界</option>
          <option value="omit">省略该值</option>
        </Select>
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
  const types = bitArea ? ["boolean"] : descriptor.dataTypes;
  return (
    <Field
      label="设备数据类型"
      hint={bitArea ? "位区读取结果固定为布尔值。" : undefined}
      error={error("sourceDataType")}
    >
      <Select
        value={row.sourceDataType}
        disabled={readOnly || bitArea}
        onChange={event => {
          const next = event.target.value;
          onChange({
            sourceDataType: next,
            bitIndex: next === "boolean" ? row.bitIndex : "",
            modbusQuantity: next === "string"
              ? Math.max(1, Math.ceil((Number(row.sourceByteLength) || 1) / 2))
              : registerWordCount(next),
          });
        }}
      >
        {types.map(value => (
          <option key={value} value={value}>{value === "auto" ? "按样本识别（不推荐）" : value}</option>
        ))}
      </Select>
    </Field>
  );
}

function ModbusAddressFields({ row, form, error, readOnly, onChange }) {
  const area = modbusArea(row.modbusArea);
  const oneBased = form.modbusTcp?.addressBase === "one-based";
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
