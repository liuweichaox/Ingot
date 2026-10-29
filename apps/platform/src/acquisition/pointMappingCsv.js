// Parse and export protocol-aware point-mapping CSV without applying partial or malformed imports.

import { ADDRESSING } from "./protocolRegistry";

const MAXIMUM_CSV_BYTES = 5 * 1024 * 1024;
const MAXIMUM_DATA_ROWS = 10_000;

const COMMON_COLUMNS = [
  "dataItemCode",
  "sourceDataType",
  "sourceByteLength",
  "sourceUnit",
  "scale",
  "offset",
  "missingValueBehavior",
  "defaultValue",
  "qualityPath",
  "acceptedQualityValues",
];

const NUMERIC_COLUMNS = new Set([
  "sourceByteLength", "scale", "offset", "modbusAddress", "bitIndex",
]);

const TEXT_COLUMNS = new Set([
  "dataItemCode", "sourcePath", "sourceDataType", "sourceUnit", "missingValueBehavior",
  "defaultValue", "qualityPath", "acceptedQualityValues", "modbusArea", "byteOrder", "wordOrder", "melsecDevice", "melsecAddress", "topic",
]);

function addressColumns(descriptor) {
  if (descriptor.addressing === ADDRESSING.modbusRegister)
    return ["modbusArea", "modbusAddress", "bitIndex", "byteOrder", "wordOrder"];
  if (descriptor.addressing === ADDRESSING.melsecDevice)
    return ["melsecDevice", "melsecAddress", "bitIndex"];
  return ["sourcePath"];
}

function requiredAddressColumns(descriptor) {
  if (descriptor.addressing === ADDRESSING.modbusRegister) return ["modbusArea", "modbusAddress"];
  if (descriptor.addressing === ADDRESSING.melsecDevice) return ["melsecDevice", "melsecAddress"];
  return ["sourcePath"];
}

function usesWordOrder(descriptor) {
  return ["modbus-tcp", "modbus-rtu", "modbus-ascii", "melsec-mc-3e", "omron-fins-tcp", "omron-fins-udp"].includes(descriptor.id);
}

export function pointMappingCsvColumns(descriptor) {
  const columns = [...COMMON_COLUMNS.slice(0, 1), ...addressColumns(descriptor)];
  if (!columns.includes("sourceDataType")) columns.push("sourceDataType");
  if (!columns.includes("sourceByteLength")) columns.push("sourceByteLength");
  if (usesWordOrder(descriptor) && !columns.includes("wordOrder")) columns.push("wordOrder");
  if (descriptor.capabilities?.perTopicMapping) columns.push("topic");
  for (const column of COMMON_COLUMNS.slice(2))
    if (!columns.includes(column)) columns.push(column);
  return columns;
}

function serializeCell(value, protectFormula = false) {
  let text = String(value ?? "");
  if (protectFormula) {
    const leading = text.match(/^\s*/)?.[0] || "";
    const content = text.slice(leading.length);
    if (/^[=+\-@]/.test(content) || content.startsWith("'")) text = `${leading}'${content}`;
  }
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function csvRowsToText(rows, columns) {
  return rows.map((row, rowIndex) => row.map((value, columnIndex) =>
    serializeCell(value, rowIndex > 0 && TEXT_COLUMNS.has(columns[columnIndex]))).join(",")).join("\r\n");
}

export function pointMappingCsvTemplate(descriptor) {
  const columns = pointMappingCsvColumns(descriptor);
  return `${csvRowsToText([columns], columns)}\r\n`;
}

export function pointMappingsToCsv(rows, descriptor) {
  const columns = pointMappingCsvColumns(descriptor);
  const body = (rows || []).map(row => columns.map(column => {
    const value = row?.[column];
    return column === "acceptedQualityValues" && Array.isArray(value) ? value.join(",") : value;
  }));
  return `${csvRowsToText([columns, ...body], columns)}\r\n`;
}

function parseCsvRows(source) {
  const rows = [];
  let cells = [];
  let cell = "";
  let inQuotes = false;
  let afterQuote = false;
  let physicalLine = 1;
  let rowStartLine = 1;

  function finishCell() {
    cells.push(cell);
    cell = "";
    afterQuote = false;
  }

  function finishRow() {
    finishCell();
    if (cells.some(value => value.trim() !== "")) {
      rows.push({ line: rowStartLine, cells });
      if (rows.length > MAXIMUM_DATA_ROWS + 1)
        throw new Error(`CSV 超过 ${MAXIMUM_DATA_ROWS} 行点位上限；请拆分文件。`);
    }
    cells = [];
    rowStartLine = physicalLine + 1;
  }

  for (let index = 0; index < source.length; index += 1) {
    const character = source[index];
    if (inQuotes) {
      if (character === '"') {
        if (source[index + 1] === '"') {
          cell += '"';
          index += 1;
        } else {
          inQuotes = false;
          afterQuote = true;
        }
      } else if (character === "\r" || character === "\n") {
        if (character === "\r" && source[index + 1] === "\n") index += 1;
        cell += "\n";
        physicalLine += 1;
      } else {
        cell += character;
      }
      continue;
    }

    if (afterQuote && character !== "," && character !== "\r" && character !== "\n" && character !== " " && character !== "\t")
      throw new Error(`CSV 第 ${physicalLine} 行：引号结束后只能有逗号或换行。`);
    if (character === '"') {
      if (cell.trim() !== "" || afterQuote)
        throw new Error(`CSV 第 ${physicalLine} 行：字段中的引号格式无效。`);
      inQuotes = true;
    } else if (character === ",") {
      finishCell();
    } else if (character === "\r" || character === "\n") {
      if (character === "\r" && source[index + 1] === "\n") index += 1;
      finishRow();
      physicalLine += 1;
      rowStartLine = physicalLine;
    } else if (afterQuote && (character === " " || character === "\t")) {
      // Ignore spreadsheet padding after a quoted cell.
    } else {
      cell += character;
    }
  }

  if (inQuotes) throw new Error(`CSV 第 ${rowStartLine} 行：文本结束时仍有未闭合的引号。`);
  if (cell !== "" || cells.length > 0 || afterQuote) finishRow();
  return rows;
}

function decodeFormulaCell(value) {
  const text = String(value ?? "");
  const leading = text.match(/^\s*/)?.[0].length || 0;
  if (text[leading] !== "'") return text;
  const afterPrefix = text.slice(leading + 1);
  if (afterPrefix.startsWith("'") || /^[\s]*[=+\-@]/.test(afterPrefix))
    return `${text.slice(0, leading)}${afterPrefix}`;
  return text;
}

function utf8ByteLength(value) {
  if (typeof TextEncoder !== "undefined") return new TextEncoder().encode(value).byteLength;
  return value.length;
}

function validateNumeric(value, column, line, errors) {
  if (!value.trim()) return;
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) errors.push(`第 ${line} 行：${column} 必须是有限数字。`);
  if (column === "sourceByteLength" && Number.isFinite(parsed) && (!Number.isInteger(parsed) || parsed < 1))
    errors.push(`第 ${line} 行：sourceByteLength 必须是正整数。`);
  if (column === "bitIndex" && Number.isFinite(parsed) && (!Number.isInteger(parsed) || parsed < 0))
    errors.push(`第 ${line} 行：bitIndex 必须是非负整数。`);
}

export function parsePointMappingCsv(text, descriptor) {
  const source = String(text ?? "").replace(/^\uFEFF/, "");
  const errors = [];
  if (utf8ByteLength(source) > MAXIMUM_CSV_BYTES)
    return { errors: ["CSV 文件超过 5 MB，未读取；请拆分文件后重试。"], rows: [] };

  let parsedRows;
  try {
    parsedRows = parseCsvRows(source);
  } catch (error) {
    return { errors: [error.message], rows: [] };
  }
  if (parsedRows.length === 0)
    return { errors: ["CSV 文件为空。"], rows: [] };

  const columns = pointMappingCsvColumns(descriptor);
  const canonicalByName = new Map(columns.map(column => [column.toLowerCase(), column]));
  const rawHeader = parsedRows[0].cells;
  const header = rawHeader.map(value => String(value ?? "").trim().toLowerCase());
  const headerIndexes = new Map();
  header.forEach((name, index) => {
    if (!name) errors.push(`CSV 第 1 行第 ${index + 1} 列缺少列名。`);
    if (headerIndexes.has(name)) errors.push(`CSV 表头重复：${name || `第 ${index + 1} 列`}。`);
    if (name && !canonicalByName.has(name)) errors.push(`CSV 含不支持的列：${name}。`);
    headerIndexes.set(name, index);
  });
  const requiredHeaders = ["dataItemCode", ...requiredAddressColumns(descriptor)];
  for (const column of requiredHeaders) {
    if (!headerIndexes.has(column.toLowerCase())) errors.push(`CSV 缺少必需列 ${column}。`);
  }
  if (errors.length > 0) return { errors, rows: [] };

  const dataRows = parsedRows.slice(1);
  if (dataRows.length === 0)
    return { errors: ["CSV 只有表头，没有点位数据。"], rows: [] };

  const rows = [];
  const seenTargets = new Map();
  const supportedTypes = new Set((descriptor.dataTypes || []).map(value => String(value).toLowerCase()));
  for (const parsed of dataRows) {
    if (parsed.cells.length !== header.length) {
      errors.push(`第 ${parsed.line} 行：字段数为 ${parsed.cells.length}，表头列数为 ${header.length}。`);
      continue;
    }
    const row = {};
    const values = new Map();
    for (const [headerName, index] of headerIndexes) {
      const column = canonicalByName.get(headerName);
      if (!column) continue;
      let value = String(parsed.cells[index] ?? "");
      if (TEXT_COLUMNS.has(column)) value = decodeFormulaCell(value);
      value = value.trim();
      values.set(column, value);
    }

    const dataItemCode = values.get("dataItemCode") || "";
    if (!dataItemCode) {
      errors.push(`第 ${parsed.line} 行：dataItemCode 不能为空。`);
      continue;
    }
    const targetKey = dataItemCode.toLocaleLowerCase("en");
    if (seenTargets.has(targetKey)) {
      errors.push(`第 ${parsed.line} 行：工艺变量 ${dataItemCode} 重复（CSV 第 ${seenTargets.get(targetKey)} 行）。`);
      continue;
    }
    seenTargets.set(targetKey, parsed.line);

    for (const column of columns) {
      if (!values.has(column)) continue;
      const value = values.get(column);
      if (!value) {
        row[column] = "";
        continue;
      }
      if (NUMERIC_COLUMNS.has(column)) validateNumeric(value, column, parsed.line, errors);
      if (column === "sourceDataType") {
        const normalized = value.toLowerCase();
        if (supportedTypes.size > 0 && !supportedTypes.has(normalized)) {
          errors.push(`第 ${parsed.line} 行：${column} ${value} 不受 ${descriptor.label} 支持。`);
          continue;
        }
        row[column] = normalized;
      } else if (column === "melsecDevice") {
        row[column] = value.toUpperCase();
      } else {
        row[column] = value;
      }
    }

    for (const column of requiredAddressColumns(descriptor)) {
      if (!values.get(column)) errors.push(`第 ${parsed.line} 行：${column} 不能为空。`);
    }
    rows.push(row);
  }

  return errors.length > 0 ? { errors, rows: [] } : { errors: [], rows };
}
