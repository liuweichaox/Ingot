// Edit and validate protocol-specific device points and their process-variable mappings.
import { Fragment, useEffect, useMemo, useRef, useState } from "react";
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
import { hasPointAddress, validateIngestionTask } from "../ingestionTaskForm";

const MAPPING_PAGE_SIZE = 50;
const EMPTY_ROW_ERRORS = [];

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
  focusIncompleteRevision = 0,
  focusRowRequest = null,
  onFocusRowComplete,
}) {
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [currentPage, setCurrentPage] = useState(0);
  const [selectedRows, setSelectedRows] = useState(() => new Set());
  const [expandedIndex, setExpandedIndex] = useState(null);
  const [copyMessage, setCopyMessage] = useState("");
  const [pendingFocusRow, setPendingFocusRow] = useState(null);
  const [pendingIncompleteFocusRevision, setPendingIncompleteFocusRevision] = useState(0);
  const [focusedRowIndex, setFocusedRowIndex] = useState(null);
  const pageAfterFilterReset = useRef(null);
  const clearFiltersAndSetPage = page => {
    const targetPage = Math.max(0, page);
    if (search || statusFilter !== "all") {
      pageAfterFilterReset.current = targetPage;
      setSearch("");
      setStatusFilter("all");
    } else {
      setCurrentPage(targetPage);
    }
  };
  const addRow = () => {
    clearFiltersAndSetPage(Math.floor(rows.length / MAPPING_PAGE_SIZE));
    onChange([...rows, blankRow(descriptor)]);
  };
  const updateRow = (index, patch) =>
    onChange(rows.map((item, rowIndex) => (rowIndex === index ? { ...item, ...patch } : item)));
  const removeRow = index => {
    onChange(rows.filter((_item, rowIndex) => rowIndex !== index));
    setSelectedRows(current => new Set([...current].filter(rowIndex => rowIndex !== index)
      .map(rowIndex => rowIndex > index ? rowIndex - 1 : rowIndex)));
    setExpandedIndex(null);
  };
  const addressKey = descriptor.addressing === ADDRESSING.modbusRegister ? "modbusAddress"
    : descriptor.addressing === ADDRESSING.melsecDevice ? "melsecAddress" : "sourcePath";
  const currentValidationErrors = useMemo(
    () => validateIngestionTask(form, { dataItems: options }).errors,
    [form, options],
  );
  const statusErrors = useMemo(
    () => ({ ...currentValidationErrors, ...(errors || {}) }),
    [currentValidationErrors, errors],
  );
  const optionsByCode = useMemo(() => new Map(options.map(item => [item.code, item])), [options]);
  const mappedCodes = useMemo(() => new Set(rows.map(row => row.dataItemCode)), [rows]);
  const mqttTopics = useMemo(() => new Set((form.mqtt?.topics || [])
    .map(item => String(item.topic || "").trim())), [form.mqtt?.topics]);
  const rowErrorsByIndex = useMemo(() => {
    const rowPathPrefix = `${errorPrefix}[`;
    const groupedErrors = new Map();
    for (const [path, message] of Object.entries(statusErrors)) {
      if (!path.startsWith(rowPathPrefix)) continue;
      const indexEnd = path.indexOf("].", rowPathPrefix.length);
      if (indexEnd < 0) continue;
      const index = Number(path.slice(rowPathPrefix.length, indexEnd));
      if (!Number.isInteger(index) || index < 0) continue;
      const rowErrorList = groupedErrors.get(index);
      if (rowErrorList) rowErrorList.push(message);
      else groupedErrors.set(index, [message]);
    }
    return groupedErrors;
  }, [errorPrefix, statusErrors]);
  const sourceLabel = row => descriptor.addressing === ADDRESSING.modbusRegister
    ? `${row.modbusArea || "寄存器"} ${row.modbusAddress ?? ""}${row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`}`.trim()
    : descriptor.addressing === ADDRESSING.melsecDevice
      ? `${row.melsecDevice || "D"}${row.melsecAddress ?? ""}${row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`}`
      : String(row.sourcePath || "").trim();
  const rowErrors = index => rowErrorsByIndex.get(index) || EMPTY_ROW_ERRORS;
  const rowComplete = index => {
    const row = rows[index];
    const definition = optionsByCode.get(row?.dataItemCode);
    const topic = String(row?.topic || "").trim();
    const topicValid = !descriptor.capabilities.perTopicMapping || !topic
      || mqttTopics.has(topic);
    const sourceUnit = String(row?.sourceUnit || "").trim();
    const unitValid = !definition?.unit || (sourceUnit
      && (sourceUnit === definition.unit || Number(row.scale) !== 1 || Number(row.offset) !== 0));
    return Boolean(row?.dataItemCode && optionsByCode.has(row.dataItemCode)
      && hasPointAddress(row, descriptor) && rowErrors(index).length === 0
      && Object.keys(descriptor.validatePoint(row, form) || {}).length === 0 && topicValid && unitValid);
  };
  const missingRequired = useMemo(() => options.filter(item => !item.nullable
    && !mappedCodes.has(item.code)), [options, mappedCodes]);
  const query = search.trim().toLowerCase();
  const visibleRows = useMemo(() => rows.map((row, index) => ({ row, index, complete: rowComplete(index) }))
    .filter(({ row, index, complete }) => {
      if (statusFilter === "mapped" && !complete) return false;
      if (statusFilter === "needs-attention" && complete) return false;
      if (!query) return true;
      const target = optionsByCode.get(row.dataItemCode);
      const searchable = [sourceLabel(row), row.dataItemCode, target?.displayName, row.sourceUnit,
        row.sourceDataType, row.topic, ...rowErrors(index)].join(" ").toLowerCase();
      return searchable.includes(query);
    }), [rows, statusFilter, query, optionsByCode, rowErrorsByIndex, mqttTopics, form, descriptor]);
  const filteredIndexes = visibleRows.map(item => item.index);
  const selectedCount = filteredIndexes.filter(index => selectedRows.has(index)).length;
  const allFilteredSelected = filteredIndexes.length > 0 && selectedCount === filteredIndexes.length;
  const pageCount = Math.max(1, Math.ceil(visibleRows.length / MAPPING_PAGE_SIZE));
  const pageStartIndex = currentPage * MAPPING_PAGE_SIZE;
  const pageEndIndex = Math.min(pageStartIndex + MAPPING_PAGE_SIZE, visibleRows.length);
  const pageRows = visibleRows.slice(pageStartIndex, pageEndIndex);

  useEffect(() => {
    if (!focusIncompleteRevision) return;
    setSearch("");
    setStatusFilter("needs-attention");
    setExpandedIndex(null);
    setPendingIncompleteFocusRevision(focusIncompleteRevision);
  }, [focusIncompleteRevision]);

  useEffect(() => {
    if (!focusRowRequest) return;
    setSearch("");
    setStatusFilter("all");
    setPendingFocusRow(focusRowRequest);
  }, [focusRowRequest]);

  useEffect(() => {
    const targetPage = pageAfterFilterReset.current;
    pageAfterFilterReset.current = null;
    setCurrentPage(targetPage ?? 0);
  }, [search, statusFilter]);

  useEffect(() => {
    setCurrentPage(page => Math.min(page, pageCount - 1));
  }, [pageCount]);

  useEffect(() => {
    if (!pendingIncompleteFocusRevision || pendingFocusRow || search || statusFilter !== "needs-attention") return;
    if (!visibleRows.length) {
      setPendingIncompleteFocusRevision(0);
      return;
    }
    setPendingFocusRow({ index: visibleRows[0].index, revision: pendingIncompleteFocusRevision });
    setPendingIncompleteFocusRevision(0);
  }, [pendingFocusRow, pendingIncompleteFocusRevision, search, statusFilter, visibleRows]);

  useEffect(() => {
    if (!pendingFocusRow) return undefined;
    const targetPosition = visibleRows.findIndex(item => item.index === pendingFocusRow.index);
    if (targetPosition < 0) {
      setPendingFocusRow(null);
      if (pendingFocusRow.revision !== undefined) onFocusRowComplete?.(pendingFocusRow.revision);
      return undefined;
    }
    const targetPage = Math.floor(targetPosition / MAPPING_PAGE_SIZE);
    if (targetPage !== currentPage) {
      setCurrentPage(targetPage);
      return undefined;
    }
    const frame = window.requestAnimationFrame(() => {
      const row = document.getElementById(`acq-point-mapping-row-${pendingFocusRow.index}`);
      if (row) {
        row.scrollIntoView({ block: "center", behavior: "smooth" });
        const field = document.getElementById(`acq-point-mapping-target-${pendingFocusRow.index}`);
        (field && !field.disabled ? field : row).focus({ preventScroll: true });
        setFocusedRowIndex(pendingFocusRow.index);
      }
      setPendingFocusRow(null);
      if (pendingFocusRow.revision !== undefined) onFocusRowComplete?.(pendingFocusRow.revision);
    });
    return () => window.cancelAnimationFrame(frame);
  }, [currentPage, pendingFocusRow, visibleRows, onFocusRowComplete]);

  useEffect(() => setSelectedRows(new Set()), [search, statusFilter]);
  useEffect(() => setFocusedRowIndex(null), [search, statusFilter]);

  function toggleFilteredSelection() {
    setSelectedRows(current => {
      const next = new Set(current);
      if (allFilteredSelected) filteredIndexes.forEach(index => next.delete(index));
      else filteredIndexes.forEach(index => next.add(index));
      return next;
    });
  }

  function removeSelectedRows() {
    onChange(rows.filter((_row, index) => !selectedRows.has(index)));
    setSelectedRows(new Set());
    setExpandedIndex(null);
  }

  async function copySelectedRows() {
    const selected = rows.map((row, index) => ({ row, index })).filter(item => selectedRows.has(item.index));
    if (!selected.length) return;
    const text = [
      ["平台变量", "设备地址", "设备单位", "数据类型", "倍率", "偏移", "主题"].join("\t"),
      ...selected.map(({ row }) => [row.dataItemCode, sourceLabel(row), row.sourceUnit || "", row.sourceDataType || "",
        row.scale ?? "", row.offset ?? "", row.topic || ""].join("\t")),
    ].join("\n");
    try {
      await navigator.clipboard.writeText(text);
      setCopyMessage(`已复制 ${selected.length} 行，可粘贴到电子表格。`);
    } catch {
      setCopyMessage("无法访问剪贴板；可使用下方 CSV 导出全部映射。 ");
    }
  }

  function addMissingRequiredRows() {
    const firstAddedRowIndex = rows.length;
    onChange([...rows, ...missingRequired.map(item => ({
      ...blankRow(descriptor), dataItemCode: item.code, required: true,
    }))]);
    setStatusFilter("needs-attention");
    setSearch("");
    setExpandedIndex(null);
    setPendingFocusRow({ index: firstAddedRowIndex });
  }

  return (
    <Card
      title={title}
      description={description}
      actions={!readOnly ? <div className="flex flex-wrap gap-2"><Button onClick={addRow}>添加点位</Button>
        {missingRequired.length > 0 && <Button onClick={addMissingRequiredRows}>添加缺失的 {missingRequired.length} 个必需变量</Button>}
      </div> : undefined}
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
          onApply={nextRows => {
            onChange(nextRows);
            setSearch("");
            setStatusFilter("all");
            setCurrentPage(0);
            setSelectedRows(new Set());
            setExpandedIndex(null);
          }}
        />
        {missingRequired.length > 0 && <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
          尚缺 {missingRequired.length} 个运行必需变量：{missingRequired.map(item => item.displayName || item.code).join("、")}。这些变量尚未进入下方表格。
        </p>}
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div className="grid min-w-64 flex-1 gap-2 sm:grid-cols-[minmax(14rem,1fr)_12rem]">
            <Field label="搜索映射">
              <Input value={search} onChange={event => setSearch(event.target.value)} placeholder="地址、变量、单位、主题或错误" />
            </Field>
            <Field label="状态">
              <Select value={statusFilter} onChange={event => setStatusFilter(event.target.value)}>
                <option value="all">全部</option><option value="mapped">映射完整</option><option value="needs-attention">需补全或修正</option>
              </Select>
            </Field>
          </div>
          <div className="flex flex-wrap items-center gap-2 text-xs text-slate-500">
            <span>已筛选 {visibleRows.length}/{rows.length} 行 · 已选 {selectedRows.size} 行（可跨页）</span>
            <Button variant="ghost" disabled={!filteredIndexes.length} onClick={toggleFilteredSelection}>
              {allFilteredSelected ? "取消选择筛选结果" : `选择全部筛选结果（${filteredIndexes.length} 行）`}
            </Button>
            <Button variant="ghost" disabled={!selectedRows.size} onClick={() => void copySelectedRows()}>复制所选 TSV</Button>
            {!readOnly && <Button variant="ghost" className="text-rose-700" disabled={!selectedRows.size} onClick={removeSelectedRows}>删除所选 {selectedRows.size || ""}</Button>}
          </div>
        </div>
        {copyMessage && <p role="status" className="text-xs text-slate-500">{copyMessage}</p>}
        {rows.length === 0 && <p className="text-sm text-slate-500">还没有配置点位。</p>}
        {rows.length > 0 && <div className="overflow-x-auto rounded-lg border border-slate-200">
          <table className="w-full min-w-[900px] border-collapse text-left text-sm">
            <thead className="bg-slate-50 text-xs text-slate-500">
              <tr><th className="w-9 px-3 py-2"><span className="sr-only">选择行</span></th><th className="px-3 py-2">平台变量</th>
                <th className="px-3 py-2">设备来源</th><th className="w-36 px-3 py-2">设备单位</th><th className="w-24 px-3 py-2">倍率</th>
                <th className="w-24 px-3 py-2">偏移</th><th className="w-36 px-3 py-2">状态与详情</th></tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {pageRows.map(({ row, index, complete }) => {
                const rowErrorMessages = rowErrors(index);
                const definition = optionsByCode.get(row.dataItemCode);
                return <Fragment key={index}>
                  <tr id={`acq-point-mapping-row-${index}`} tabIndex={-1}
                    className={`align-top ${focusedRowIndex === index ? "bg-blue-50 ring-2 ring-inset ring-blue-400" : ""}`}>
                    <td className="px-3 py-2"><input aria-label={`选择第 ${index + 1} 行`} type="checkbox" checked={selectedRows.has(index)}
                      onChange={event => setSelectedRows(current => { const next = new Set(current); if (event.target.checked) next.add(index); else next.delete(index); return next; })} /></td>
                    <td className="min-w-48 px-3 py-2">
                      <Select id={`acq-point-mapping-target-${index}`} value={row.dataItemCode} disabled={readOnly} aria-label={`第 ${index + 1} 行平台变量`}
                        className="min-w-44" onChange={event => {
                          const selected = optionsByCode.get(event.target.value);
                          updateRow(index, { dataItemCode: event.target.value, required: selected ? !selected.nullable : row.required });
                        }}>
                        <option value="">请选择变量</option>{options.map(item => <option key={item.code} value={item.code}>{item.displayName || item.code}</option>)}
                      </Select>
                      {rowErrorMessages.some(message => message.includes("平台数据项") || message.includes("数据项只能")) &&
                        <p className="mt-1 text-xs text-rose-700">{rowErrorMessages.find(message => message.includes("平台数据项") || message.includes("数据项只能"))}</p>}
                    </td>
                    <td className="min-w-52 px-3 py-2">
                      <Input value={addressKey === "sourcePath" ? row.sourcePath || "" : row[addressKey] ?? ""} disabled={readOnly}
                        aria-label={`第 ${index + 1} 行设备来源`} placeholder={descriptor.pointLabel}
                        onChange={event => updateRow(index, { [addressKey]: event.target.value })} />
                      {rowErrorMessages.some(message => /路径|地址|来源|OID|标签|选择器|范围/.test(message)) &&
                        <p className="mt-1 text-xs text-rose-700">{rowErrorMessages.find(message => /路径|地址|来源|OID|标签|选择器|范围/.test(message))}</p>}
                    </td>
                    <td className="px-3 py-2"><Input value={row.sourceUnit || ""} disabled={readOnly} aria-label={`第 ${index + 1} 行设备单位`}
                      placeholder={definition?.unit || "单位"} onChange={event => updateRow(index, { sourceUnit: event.target.value })} />
                      {definition?.unit && !String(row.sourceUnit || "").trim() && <p className="mt-1 text-xs text-amber-700">发布前需填写 {definition.unit}</p>}
                      {definition?.unit && row.sourceUnit?.trim() && row.sourceUnit.trim() !== definition.unit && Number(row.scale) === 1 && Number(row.offset) === 0 &&
                        <p className="mt-1 text-xs text-amber-700">单位不同，请配置倍率或偏移</p>}</td>
                    <td className="px-3 py-2"><Input type="number" step="any" value={row.scale} disabled={readOnly} aria-label={`第 ${index + 1} 行倍率`}
                      onChange={event => updateRow(index, { scale: event.target.value })} /></td>
                    <td className="px-3 py-2"><Input type="number" step="any" value={row.offset} disabled={readOnly} aria-label={`第 ${index + 1} 行偏移`}
                      onChange={event => updateRow(index, { offset: event.target.value })} /></td>
                    <td className="px-3 py-2">
                      <div className="flex items-center gap-2"><Badge tone={complete ? "success" : "warning"}>{complete ? "映射完整" : rowErrorMessages.length ? `${rowErrorMessages.length} 项错误` : "待补全"}</Badge>
                        <Button variant="ghost" onClick={() => setExpandedIndex(current => current === index ? null : index)}>
                          {expandedIndex === index ? "收起" : "详情"}
                        </Button></div>
                      {!complete && rowErrorMessages.length > 0 && <p className="mt-1 line-clamp-2 text-xs text-rose-700">{rowErrorMessages[0]}</p>}
                    </td>
                  </tr>
                  {expandedIndex === index && <tr><td colSpan={7} className="bg-slate-50 p-3">
                    <PointRow row={row} index={index} descriptor={descriptor} options={options} errors={statusErrors}
                      path={`${errorPrefix}[${index}]`} preview={probe?.mappings?.find(item => item.dataItemCode === row.dataItemCode)}
                      probe={probe} form={form} readOnly={readOnly} removable={rows.length > 1}
                      onChange={patch => updateRow(index, patch)} onRemove={() => removeRow(index)} />
                  </td></tr>}
                </Fragment>;
              })}
              {visibleRows.length === 0 && <tr><td colSpan={7} className="px-3 py-8 text-center text-sm text-slate-500">没有符合筛选条件的映射。</td></tr>}
            </tbody>
          </table>
        </div>}
        {visibleRows.length > 0 && <nav className="flex flex-wrap items-center justify-between gap-2" aria-label="点位映射表分页">
          <span role="status" aria-live="polite" className="text-xs text-slate-600">
            每页 50 条 · 第 {currentPage + 1} / {pageCount} 页 · 显示 {pageStartIndex + 1}–{pageEndIndex} / {visibleRows.length} 条筛选结果
          </span>
          <div className="flex items-center gap-2">
            <Button variant="ghost" aria-label="点位映射：上一页" disabled={currentPage === 0}
              onClick={() => setCurrentPage(page => Math.max(0, page - 1))}>上一页</Button>
            <Button variant="ghost" aria-label="点位映射：下一页" disabled={currentPage >= pageCount - 1}
              onClick={() => setCurrentPage(page => Math.min(pageCount - 1, page + 1))}>下一页</Button>
          </div>
        </nav>}
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
