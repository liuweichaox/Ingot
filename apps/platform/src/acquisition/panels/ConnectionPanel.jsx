import { Button, Card, Field, Input, Select, Textarea } from "../../ui/components";
import { mqttTopicError } from "../protocolRegistry";

/**
 * 由协议描述符驱动的连接参数表单。
 * 界面不再为每个协议写一段 if-else：字段来自 descriptor.fields，
 * 分组、显示条件、校验提示都由描述符声明。
 */
export function ConnectionPanel({ descriptor, connection, errors, readOnly, onChange }) {
  const update = (name, value) => onChange({ ...connection, [name]: value });
  const groups = [];
  descriptor.fields.forEach(field => {
    if (field.when && !field.when(connection)) return;
    const name = field.group || "";
    const existing = groups.find(item => item.name === name);
    if (existing) existing.fields.push(field);
    else groups.push({ name, fields: [field] });
  });

  return (
    <Card title="连接参数" description={descriptor.summary}>
      <div className="grid gap-5">
        {groups.map(group => (
          <section key={group.name || "基础"} className="grid gap-3">
            {group.name && (
              <div className="flex items-center gap-3 pt-1">
                <h3 className="shrink-0 text-xs font-semibold tracking-wide text-slate-600">{group.name}</h3>
                <span className="h-px flex-1 bg-slate-200" aria-hidden="true" />
              </div>
            )}
            <div className={`grid items-start gap-4 ${group.fields.length === 4 || group.fields.length === 8
              ? "md:grid-cols-2"
              : "md:grid-cols-2 xl:grid-cols-3"}`}>
              {group.fields.map(field => (
                <ConnectionField
                  key={field.name}
                  field={field}
                  connection={connection}
                  error={errors[`${descriptor.section}.${field.name}`]}
                  readOnly={readOnly}
                  onChange={update}
                />
              ))}
            </div>
          </section>
        ))}
        {descriptor.id === "mqtt" && (
          <TopicEditor
            topics={connection.topics || []}
            errors={errors}
            section={descriptor.section}
            readOnly={readOnly}
            onChange={value => update("topics", value)}
          />
        )}
      </div>
    </Card>
  );
}

function ConnectionField({ field, connection, error, readOnly, onChange }) {
  const label = typeof field.label === "function" ? field.label(connection) : field.label;
  const value = connection[field.name];

  if (field.type === "checkbox") {
    return (
      <Field label={label} className={field.tone === "warning" ? "text-amber-700" : undefined}>
        <span className={`flex h-10 items-center gap-2 rounded-lg border px-3 font-normal ${readOnly
          ? "cursor-not-allowed border-slate-200 bg-slate-50 text-slate-600"
          : field.tone === "warning"
            ? "border-amber-300 bg-amber-50/60 text-amber-800"
            : "border-slate-300 bg-white text-slate-700"}`}>
          <input
            type="checkbox"
            checked={Boolean(value)}
            disabled={readOnly}
            onChange={event => onChange(field.name, event.target.checked)}
          />
          {value ? "已启用" : "未启用"}
        </span>
      </Field>
    );
  }

  return (
    <Field label={label} hint={field.hint} error={error}>
      {field.type === "select" ? (
        <Select value={value ?? ""} disabled={readOnly} onChange={event => onChange(field.name, event.target.value)}>
          {field.options.map(([optionValue, optionLabel]) => (
            <option key={optionValue} value={optionValue}>{optionLabel}</option>
          ))}
        </Select>
      ) : field.type === "textarea" ? (
        <Textarea value={value ?? ""} disabled={readOnly} placeholder={field.placeholder}
          onChange={event => onChange(field.name, event.target.value)} />
      ) : (
        <Input
          type={field.type === "number" ? "number" : "text"}
          min={field.min}
          max={field.max}
          value={value ?? ""}
          placeholder={field.placeholder}
          disabled={readOnly}
          onChange={event => onChange(field.name, event.target.value)}
        />
      )}
    </Field>
  );
}

/**
 * MQTT 订阅主题。每个主题可以单独指定报文根路径，
 * 配合点位上的"来源主题"实现多主题各自携带一部分字段。
 */
function TopicEditor({ topics, errors, section, readOnly, onChange }) {
  const update = (index, patch) =>
    onChange(topics.map((item, rowIndex) => (rowIndex === index ? { ...item, ...patch } : item)));

  return (
    <div className="grid gap-3">
      <div className="flex items-center justify-between">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">订阅主题</p>
          <p className="text-xs text-slate-500">支持 + 和 # 通配符；+ 必须独占一层，# 只能在最后一层。</p>
        </div>
        {!readOnly && (
          <Button onClick={() => onChange([...topics, { channel: "", topic: "", qos: 0, payloadRoot: "", topicVariables: "" }])}>添加主题</Button>
        )}
      </div>
      {errors[`${section}.topics`] && (
        <p className="text-xs text-rose-600">{errors[`${section}.topics`]}</p>
      )}
      {topics.map((item, index) => {
        const inlineError = errors[`${section}.topics[${index}].topic`] ||
          ((item.topic || "").trim() ? mqttTopicError(item.topic.trim()) : "");
        return (
          <div key={index} className="grid gap-2 rounded-xl border border-slate-200 p-3 md:grid-cols-2 xl:grid-cols-[1fr_2fr_6rem_1.3fr_1.3fr_auto]">
            <Field label={index === 0 ? "通道代码" : undefined} error={errors[`${section}.topics[${index}].channel`]}>
              <Input value={item.channel || ""} disabled={readOnly} placeholder="telemetry"
                aria-label={`主题通道 ${index + 1}`}
                onChange={event => update(index, { channel: event.target.value })} />
            </Field>
            <Field label={index === 0 ? "主题过滤器" : undefined} error={inlineError}>
              <Input
                value={item.topic}
                disabled={readOnly}
                placeholder="plant/line1/press01/telemetry"
                aria-label={`MQTT 主题 ${index + 1}`}
                onChange={event => update(index, { topic: event.target.value })}
              />
            </Field>
            <Field label={index === 0 ? "QoS" : undefined}>
              <Select
                value={item.qos}
                disabled={readOnly}
                aria-label={`QoS ${index + 1}`}
                onChange={event => update(index, { qos: Number(event.target.value) })}
              >
                <option value="0">0</option>
                <option value="1">1</option>
                <option value="2">2</option>
              </Select>
            </Field>
            <Field
              label={index === 0 ? "报文根路径" : undefined}
              hint={index === 0 ? "网关把数据包在信封里时填写，例如 payload。留空表示报文根即数据。" : undefined}
            >
              <Input
                value={item.payloadRoot || ""}
                disabled={readOnly}
                placeholder="留空表示报文根"
                aria-label={`报文根路径 ${index + 1}`}
                onChange={event => update(index, { payloadRoot: event.target.value })}
              />
            </Field>
            <Field label={index === 0 ? "主题变量" : undefined}
              hint={index === 0 ? "name:层级，多个用逗号分隔；映射时用 $topic.name。" : undefined}
              error={errors[`${section}.topics[${index}].topicVariables`]}>
              <Input value={item.topicVariables || ""} disabled={readOnly} placeholder="equipment:1"
                aria-label={`主题变量 ${index + 1}`}
                onChange={event => update(index, { topicVariables: event.target.value })} />
            </Field>
            {!readOnly && topics.length > 1 && (
              <Button
                variant="ghost"
                className="self-end text-rose-700"
                onClick={() => onChange(topics.filter((_item, rowIndex) => rowIndex !== index))}
              >
                移除
              </Button>
            )}
          </div>
        );
      })}
    </div>
  );
}
