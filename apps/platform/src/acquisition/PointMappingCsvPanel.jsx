// Import and export protocol-aware point mappings through a preview before applying changes.

import { useMemo, useRef, useState } from "react";
import { Alert, Button } from "../ui/components";
import {
  parsePointMappingCsv,
  pointMappingCsvTemplate,
  pointMappingsToCsv,
} from "./pointMappingCsv";

function downloadCsv(fileName, contents) {
  const blob = new Blob(["\uFEFF", contents], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function normalizeCode(value) {
  return String(value ?? "").trim().toLocaleLowerCase("en");
}

export function PointMappingCsvPanel({ descriptor, rows = [], options = [], form, readOnly, onApply, createBlankRow }) {
  const inputRef = useRef(null);
  const [selectedFile, setSelectedFile] = useState(null);
  const [fileError, setFileError] = useState("");
  const [mode, setMode] = useState("merge");
  const [successMessage, setSuccessMessage] = useState("");

  const preview = useMemo(() => {
    if (!selectedFile) return null;
    const parsed = parsePointMappingCsv(selectedFile.text, descriptor);
    const errors = [...parsed.errors];
    const targets = new Map(options.map(item => [normalizeCode(item.code), item]));
    const providedColumns = parsed.rows.map(row => Object.keys(row));
    const preparedRows = parsed.rows.map((row, index) => {
      const target = targets.get(normalizeCode(row.dataItemCode));
      const defaults = createBlankRow(descriptor);
      const values = { ...defaults, ...row };
      for (const column of providedColumns[index]) {
        if (values[column] === "") values[column] = defaults[column] ?? "";
      }
      if (!target) {
        errors.push(`第 ${index + 2} 行：平台变量 ${values.dataItemCode || "（空）"} 不在当前模型中。`);
        return values;
      }
      const prepared = {
        ...values,
        dataItemCode: target.code,
        required: !target.nullable,
      };
      const pointErrors = descriptor.validatePoint?.(prepared, form) || {};
      for (const [field, message] of Object.entries(pointErrors))
        errors.push(`第 ${index + 2} 行 ${field}：${message}`);
      return prepared;
    });
    return { ...parsed, rows: preparedRows, providedColumns, errors };
  }, [selectedFile, descriptor, options, form]);

  async function selectFile(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    setFileError("");
    setSuccessMessage("");
    setSelectedFile(null);
    if (file.size > 5 * 1024 * 1024) {
      setFileError("CSV 超过 5 MB，未读取。请拆分为多个点位清单。");
      return;
    }
    try {
      const text = new TextDecoder("utf-8", { fatal: true }).decode(await file.arrayBuffer());
      setSelectedFile({ name: file.name, text });
    } catch {
      setFileError("无法按 UTF-8 读取所选文件。请另存为 UTF-8 CSV 后重试。");
    }
  }

  function applyPreview() {
    if (readOnly || !preview || preview.errors.length > 0) return;
    if (mode === "replace" && !window.confirm("替换会删除当前表格中的全部映射，只保留 CSV 中的点位。确认继续？")) return;

    if (mode === "replace") {
      onApply?.(preview.rows);
    } else {
      const next = [...rows];
      for (const [rowIndex, imported] of preview.rows.entries()) {
        const target = normalizeCode(imported.dataItemCode);
        const existingIndex = next.findIndex(item => normalizeCode(item.dataItemCode) === target);
        const updates = Object.fromEntries(preview.providedColumns[rowIndex].map(column => [column, imported[column]]));
        if (existingIndex >= 0) next[existingIndex] = { ...next[existingIndex], ...updates, dataItemCode: imported.dataItemCode, required: imported.required };
        else next.push(imported);
      }
      onApply?.(next);
    }
    setSuccessMessage(`已应用 ${preview.rows.length} 个点位映射。仍需检查整张配置并重新探查。`);
    setSelectedFile(null);
  }

  const protocolName = String(descriptor.id || "protocol").replace(/[^a-z0-9-]/gi, "-").toLowerCase();
  const hasRows = rows.length > 0;

  return (
    <section aria-label="点位映射 CSV 导入导出" className="grid gap-3 rounded-lg border border-slate-200 bg-slate-50 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-xs font-semibold text-slate-800">点位清单 CSV</h3>
          <p className="mt-1 text-[11px] leading-5 text-slate-600">模板按当前驱动列出地址字段；导入先预览，设备地址仍按该驱动的规则校验。</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary"
            onClick={() => downloadCsv(`point-mapping-${protocolName}-template.csv`, pointMappingCsvTemplate(descriptor))}>
            下载模板
          </Button>
          <Button variant="secondary" disabled={!hasRows}
            onClick={() => downloadCsv(`point-mapping-${protocolName}.csv`, pointMappingsToCsv(rows, descriptor))}>
            导出 {rows.length} 个映射
          </Button>
          {!readOnly && (
            <>
              <input ref={inputRef} className="sr-only" type="file" accept=".csv,text/csv" onChange={selectFile} />
              <Button onClick={() => inputRef.current?.click()}>选择 CSV</Button>
            </>
          )}
        </div>
      </div>

      {fileError && <Alert tone="danger">{fileError}</Alert>}
      {successMessage && <p role="status" className="text-xs text-emerald-700">{successMessage}</p>}
      {preview && (
        <div className="grid gap-3 rounded-md border border-slate-200 bg-white p-3">
          <div>
            <p className="text-xs font-semibold text-slate-800">{selectedFile.name} · 导入预览</p>
            <p className="mt-1 text-[11px] text-slate-600">{preview.rows.length} 个映射 · {mode === "merge" ? "按平台变量合并或更新" : "替换当前表格中的全部映射"}</p>
          </div>
          <div className="flex flex-wrap gap-2" role="group" aria-label="点位 CSV 应用方式">
            <Button variant={mode === "merge" ? "primary" : "secondary"} aria-pressed={mode === "merge"}
              disabled={readOnly} onClick={() => setMode("merge")}>合并 / 更新</Button>
            <Button variant={mode === "replace" ? "primary" : "secondary"} aria-pressed={mode === "replace"}
              disabled={readOnly} onClick={() => setMode("replace")}>替换全部映射</Button>
          </div>
          {preview.errors.length > 0 ? (
            <Alert tone="danger" title={`有 ${preview.errors.length} 项需要修正`}>
              <ul className="list-disc space-y-1 pl-5">
                {preview.errors.slice(0, 10).map((error, index) => <li key={index}>{error}</li>)}
                {preview.errors.length > 10 && <li>另有 {preview.errors.length - 10} 项错误未展开。</li>}
              </ul>
            </Alert>
          ) : (
            <p className="text-xs text-emerald-700">CSV 结构、目标变量和驱动地址格式通过预检。</p>
          )}
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="secondary" disabled={readOnly} onClick={() => setSelectedFile(null)}>取消</Button>
            <Button variant="primary" disabled={readOnly || preview.errors.length > 0} onClick={applyPreview}>
              应用 {preview.rows.length} 个映射
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}
